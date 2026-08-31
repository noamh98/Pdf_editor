using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PdfEditor.App.Services;
using PdfEditor.App.ViewModels;

namespace PdfEditor.App.Views;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;
    private bool _closeConfirmed;
    private DispatcherTimer? _autosaveTimer;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DragDrop.SetAllowDrop(this, true);

        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel is null) return;
            _viewModel.Dialogs = new DialogService(this);
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.ViewportWidth = Bounds.Width > 0 ? Bounds.Width : Width;
            SyncPanelColumns();
            _viewModel.ThemeChanged += (_, preference) =>
            {
                if (Avalonia.Application.Current is { } app) ThemeApplier.Apply(app, preference);
            };
            StartAutosave();
        };
    }

    /// <summary>
    /// The window drives autosave because it is the part of the pair with a lifetime: it is opened
    /// once and closed once, so the timer is guaranteed to stop.
    /// </summary>
    private void StartAutosave()
    {
        _autosaveTimer?.Stop();
        _autosaveTimer = null;
        if (_viewModel?.AutosaveInterval is not { } interval) return;

        _autosaveTimer = new DispatcherTimer { Interval = interval };
        _autosaveTimer.Tick += (_, _) => _ = _viewModel.AutosaveNowAsync();
        _autosaveTimer.Start();
    }

    /// <summary>
    /// Work stranded by a previous run is offered once the window exists, so the prompt has a
    /// parent to sit on rather than appearing before there is anything on screen.
    /// </summary>
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_viewModel is not null) await _viewModel.OfferRecoveryAsync();
    }

    /// <summary>
    /// The window's close button bypasses every command, so the unsaved-work prompt has to be
    /// hung off the closing event. The prompt is asynchronous and the event is not, so the first
    /// close is always cancelled and re-issued once the user has answered.
    /// </summary>
    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || e.Cancel || _viewModel is null) return;

        e.Cancel = true;
        if (await _viewModel.ConfirmCloseAsync())
        {
            _autosaveTimer?.Stop();
            _autosaveTimer = null;
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>
    /// The shell measures itself and hands the width to the view model, which owns every rule about
    /// what has to give way; the view only applies the result.
    /// </summary>
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_viewModel is not null) _viewModel.ViewportWidth = e.NewSize.Width;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainWindowViewModel.IsThumbnailRailVisible)
            or nameof(MainWindowViewModel.IsPropertiesDocked)
            or nameof(MainWindowViewModel.ThumbnailRailWidth)
            or nameof(MainWindowViewModel.PropertiesPanelWidth))
        {
            SyncPanelColumns();
        }
    }

    /// <summary>
    /// A hidden panel must give its column back, otherwise the grid keeps reserving the width. The
    /// column is restored to the width the current breakpoint asks for, which also lets a drag on
    /// the splitter survive until the window crosses into another size.
    /// </summary>
    private void SyncPanelColumns()
    {
        if (_viewModel is null) return;
        var grid = this.FindControl<Grid>("MainGrid");
        if (grid is null || grid.ColumnDefinitions.Count < 5) return;

        grid.ColumnDefinitions[0].Width = _viewModel.IsThumbnailRailVisible
            ? new GridLength(_viewModel.ThumbnailRailWidth)
            : new GridLength(0);
        grid.ColumnDefinitions[4].Width = _viewModel.IsPropertiesDocked
            ? new GridLength(_viewModel.PropertiesPanelWidth)
            : new GridLength(0);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnToolClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: EditorTool tool }) _viewModel?.Toolbox.Select(tool);
    }

    private void OnSearchHitClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: SearchHitViewModel hit }) _viewModel?.GoToPage(hit.PageIndex);
    }

    private void OnPageOperationChosen(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string name }
            && Enum.TryParse<PageOperation>(name, out var operation)
            && _viewModel?.PageOperations is { } operations)
        {
            operations.Operation = operation;
        }
    }

    /// <summary>A signature was positioned but has no image; the shell offers the library.</summary>
    private async void OnSignatureRequested(object? sender, PdfEditor.Core.Annotations.SignatureAnnotation e)
    {
        if (_viewModel is not null) await _viewModel.ChooseSignatureForAsync(e);
    }

    private void OnAlignmentChosen(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string name }
            && Enum.TryParse<PdfEditor.Core.Annotations.TextAlignment>(name, out var alignment))
        {
            _viewModel?.Properties?.ApplyAlignment(alignment);
        }
    }

    private void OnSwatchClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: ColorSwatch swatch }) _viewModel?.Properties?.ApplyColor(swatch.Color);
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = HasPdf(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = FirstPdfPath(e);
        if (path is null || _viewModel is null) return;
        e.Handled = true;
        await _viewModel.OpenAsync(path);
    }

    private static bool HasPdf(DragEventArgs e) => FirstPdfPath(e) is not null;

    private static string? FirstPdfPath(DragEventArgs e) => e.DataTransfer?
        .TryGetFiles()?
        .Select(f => f.TryGetLocalPath())
        .FirstOrDefault(p => p is not null && p.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Shortcuts must not fire while the user is typing into an annotation's text, so the window's
    /// key bindings are suppressed whenever a text input has focus.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F)
        {
            FocusSearch();
            e.Handled = true;
            return;
        }

        if (IsTextInputFocused() && !IsAlwaysAllowed(e))
        {
            e.Handled = false;
            return;
        }
        base.OnKeyDown(e);
    }

    /// <summary>Ctrl+F reaches whichever of the two search fields the current width is showing.</summary>
    internal void FocusSearch()
    {
        var box = _viewModel?.ShowSearchInCommandBar == false
            ? this.FindControl<TextBox>("CompactSearchBox")
            : this.FindControl<TextBox>("SearchBox");
        box?.Focus();
    }

    internal bool IsTextInputFocused() =>
        FocusManager?.GetFocusedElement() is TextBox { IsReadOnly: false };

    /// <summary>Save and print stay available even while editing text; editing keys do not.</summary>
    private static bool IsAlwaysAllowed(KeyEventArgs e) =>
        e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.S or Key.P or Key.O;
}
