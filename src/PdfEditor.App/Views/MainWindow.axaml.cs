using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using PdfEditor.App.Services;
using PdfEditor.App.ViewModels;

namespace PdfEditor.App.Views;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DragDrop.SetAllowDrop(this, true);

        DataContextChanged += (_, _) =>
        {
            _viewModel = DataContext as MainWindowViewModel;
            if (_viewModel is null) return;
            _viewModel.Dialogs = new DialogService(this);
            _viewModel.ThemeChanged += (_, preference) =>
            {
                if (Avalonia.Application.Current is { } app) ThemeApplier.Apply(app, preference);
            };
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnToolClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: EditorTool tool }) _viewModel?.Toolbox.Select(tool);
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
        if (IsTextInputFocused() && !IsAlwaysAllowed(e))
        {
            e.Handled = false;
            return;
        }
        base.OnKeyDown(e);
    }

    internal bool IsTextInputFocused() =>
        FocusManager?.GetFocusedElement() is TextBox { IsReadOnly: false };

    /// <summary>Save and print stay available even while editing text; editing keys do not.</summary>
    private static bool IsAlwaysAllowed(KeyEventArgs e) =>
        e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key is Key.S or Key.P or Key.O;
}
