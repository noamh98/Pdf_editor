using System.Collections.ObjectModel;
using PdfEditor.App.Services;
using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Core.Files;
using PdfEditor.Core.Localization;
using PdfEditor.Core.Ocr;
using PdfEditor.Core.Printing;
using PdfEditor.Core.Settings;
using PdfEditor.Ocr;

namespace PdfEditor.App.ViewModels;

/// <summary>
/// The application shell: which document is open, what the user can do to it, and what the status
/// bar shows while it happens.
/// </summary>
/// <remarks>
/// Every long operation runs on a task with a cancellation token owned by this view model, so the
/// UI thread is never blocked and the user can always cancel.
/// </remarks>
public sealed class MainWindowViewModel : ViewModelBase
{
    private static readonly IReadOnlyList<FileFilter> PdfFilter =
        [new FileFilter("PDF", ["pdf"])];

    private readonly AppServices _services;
    private DocumentViewModel? _document;
    private PropertiesViewModel? _properties;
    private CancellationTokenSource? _operation;
    private bool _isBusy;
    private string _busyText = string.Empty;
    private double _progress;
    private string? _statusMessage;
    private bool _statusIsError;
    private string? _searchText;
    private PrintPreviewViewModel? _printPreview;

    public MainWindowViewModel(AppServices services, IDialogService? dialogs = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        Dialogs = dialogs ?? new NullDialogService();

        Toolbox = new ToolboxViewModel();
        Toolbox.ActiveToolChanged += (_, _) => RaisePropertyChanged(nameof(ActiveToolLabel));

        OpenCommand = Async(OpenAsync);
        SaveCommand = Async(SaveAsync, () => Document is not null);
        SaveAsCommand = Async(SaveAsAsync, () => Document is not null);
        ExportCommand = Async(ExportAsync, () => Document is not null);
        PrintCommand = Async(ShowPrintPreviewAsync, () => Document is not null);
        MergeCommand = Async(MergeAsync);
        SplitCommand = Async(SplitAsync, () => Document is not null);
        RunOcrCommand = Async(RunOcrOnCurrentPageAsync, () => Document is not null);
        CloseDocumentCommand = Async(CloseDocumentAsync, () => Document is not null);

        UndoCommand = new RelayCommand(Undo, () => Document?.CanUndo == true);
        RedoCommand = new RelayCommand(Redo, () => Document?.CanRedo == true);
        DeleteSelectionCommand = new RelayCommand(DeleteSelection, () => Document?.IsSelectionEditable == true);
        DuplicateSelectionCommand = new RelayCommand(DuplicateSelection, () => Document?.IsSelectionEditable == true);
        CopySelectionCommand = new RelayCommand(CopySelection, () => Document?.HasSelection == true);
        PasteCommand = new RelayCommand(Paste, () => Document is not null && _clipboard is not null);
        ClearSelectionCommand = new RelayCommand(ClearSelection);

        ZoomInCommand = new RelayCommand(() => Document?.ZoomIn(), () => Document is not null);
        ZoomOutCommand = new RelayCommand(() => Document?.ZoomOut(), () => Document is not null);
        FitWidthCommand = new RelayCommand(() => Document?.ApplyZoomMode(ZoomMode.FitWidth), () => Document is not null);
        FitPageCommand = new RelayCommand(() => Document?.ApplyZoomMode(ZoomMode.FitPage), () => Document is not null);
        ActualSizeCommand = new RelayCommand(() => Document?.ApplyZoomMode(ZoomMode.Actual), () => Document is not null);

        NextPageCommand = new RelayCommand(() => GoToPage((Document?.CurrentPageIndex ?? 0) + 1),
            () => Document is not null && Document.CurrentPageIndex < Document.PageCount - 1);
        PreviousPageCommand = new RelayCommand(() => GoToPage((Document?.CurrentPageIndex ?? 0) - 1),
            () => Document is not null && Document.CurrentPageIndex > 0);

        CancelCommand = new RelayCommand(CancelOperation, () => IsBusy);
        ToggleThemeCommand = new RelayCommand(CycleTheme);
        ClearOcrCacheCommand = Async(ClearOcrCacheAsync);
    }

    public IDialogService Dialogs { get; set; }

    public AppServices Services => _services;

    public ToolboxViewModel Toolbox { get; }

    public ObservableCollection<OcrSearchHit> SearchResults { get; } = [];

    // ---- commands ------------------------------------------------------------------------------
    public AsyncRelayCommand OpenCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand SaveAsCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }
    public AsyncRelayCommand PrintCommand { get; }
    public AsyncRelayCommand MergeCommand { get; }
    public AsyncRelayCommand SplitCommand { get; }
    public AsyncRelayCommand RunOcrCommand { get; }
    public AsyncRelayCommand CloseDocumentCommand { get; }
    public AsyncRelayCommand ClearOcrCacheCommand { get; }
    public RelayCommand UndoCommand { get; }
    public RelayCommand RedoCommand { get; }
    public RelayCommand DeleteSelectionCommand { get; }
    public RelayCommand DuplicateSelectionCommand { get; }
    public RelayCommand CopySelectionCommand { get; }
    public RelayCommand PasteCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand ZoomInCommand { get; }
    public RelayCommand ZoomOutCommand { get; }
    public RelayCommand FitWidthCommand { get; }
    public RelayCommand FitPageCommand { get; }
    public RelayCommand ActualSizeCommand { get; }
    public RelayCommand NextPageCommand { get; }
    public RelayCommand PreviousPageCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }

    // ---- state ----------------------------------------------------------------------------------
    public DocumentViewModel? Document
    {
        get => _document;
        private set
        {
            var previous = _document;
            if (!SetProperty(ref _document, value)) return;

            if (previous is not null) previous.SelectionChanged -= OnSelectionChanged;
            if (value is not null) value.SelectionChanged += OnSelectionChanged;

            Properties = value is null ? null : new PropertiesViewModel(value);
            RaiseAll(nameof(HasDocument), nameof(IsEmpty), nameof(DocumentTitle), nameof(WindowTitle));
            RefreshCommands();
        }
    }

    public PropertiesViewModel? Properties
    {
        get => _properties;
        private set => SetProperty(ref _properties, value);
    }

    public PrintPreviewViewModel? PrintPreview
    {
        get => _printPreview;
        private set
        {
            if (!SetProperty(ref _printPreview, value)) return;
            RaisePropertyChanged(nameof(IsPrintPreviewOpen));
        }
    }

    public bool IsPrintPreviewOpen => _printPreview is not null;

    public bool HasDocument => _document is not null;

    public bool IsEmpty => _document is null;

    public string DocumentTitle => _document?.DisplayName ?? Strings.EmptyStateTitle;

    public string WindowTitle => _document is null
        ? Strings.AppName
        : $"{_document.DisplayName}{(_document.IsDirty ? " •" : string.Empty)} — {Strings.AppName}";

    public string ActiveToolLabel => Toolbox.Tools.First(t => t.Tool == Toolbox.ActiveTool).Label;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            CancelCommand.RaiseCanExecuteChanged();
        }
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetProperty(ref _busyText, value);
    }

    public double Progress
    {
        get => _progress;
        private set => SetProperty(ref _progress, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (!SetProperty(ref _statusMessage, value)) return;
            RaisePropertyChanged(nameof(HasStatusMessage));
        }
    }

    public bool HasStatusMessage => !string.IsNullOrEmpty(_statusMessage);

    public bool StatusIsError
    {
        get => _statusIsError;
        private set => SetProperty(ref _statusIsError, value);
    }

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value)) return;
            RunSearch();
        }
    }

    public ThemePreference Theme
    {
        get => _services.Settings.Theme;
        set
        {
            if (_services.Settings.Theme == value) return;
            _services.Settings.Theme = value;
            RaiseAll(nameof(Theme), nameof(ThemeLabel));
            ThemeChanged?.Invoke(this, value);
            _ = _services.SaveSettingsAsync();
        }
    }

    public string ThemeLabel => Theme switch
    {
        ThemePreference.Light => Strings.ThemeLight,
        ThemePreference.Dark => Strings.ThemeDark,
        _ => Strings.ThemeSystem
    };

    public bool ReducedMotion => _services.Settings.ReducedMotion;

    public string OcrAvailabilityText => _services.Ocr.IsAvailable
        ? Strings.OcrAccuracyNotice
        : _services.Ocr.UnavailableReason ?? Strings.OcrNotAvailable;

    public event EventHandler<ThemePreference>? ThemeChanged;

    // ---- document lifecycle ----------------------------------------------------------------------
    public async Task OpenAsync()
    {
        var path = await Dialogs.PickOpenFileAsync(Strings.OpenFile, PdfFilter).ConfigureAwait(true);
        if (path is not null) await OpenAsync(path).ConfigureAwait(true);
    }

    public async Task OpenAsync(string path)
    {
        if (!await ConfirmDiscardChangesAsync().ConfigureAwait(true)) return;

        using var scope = BeginOperation(Strings.Loading);
        try
        {
            var document = await _services.Loader.OpenAsync(path, scope.Token).ConfigureAwait(true);
            var annotations = document.LoadAnnotations();

            await CloseCurrentAsync().ConfigureAwait(true);
            Document = new DocumentViewModel(document, annotations);
            Document.AnnotationsChanged += (_, _) => RefreshCommands();
            Document.ApplyZoomMode(ZoomMode.FitWidth);
            Document.StartThumbnails();

            if (document.IsProtected) ReportStatus(Strings.ProtectedDocument, isError: false);
            else ClearStatus();
        }
        catch (PdfOpenException e)
        {
            ReportStatus(ErrorMessages.ForOpenError(e.Error), isError: true);
        }
    }

    public async Task CloseDocumentAsync()
    {
        if (!await ConfirmDiscardChangesAsync().ConfigureAwait(true)) return;
        await CloseCurrentAsync().ConfigureAwait(true);
        Document = null;
        ClearStatus();
    }

    private async Task CloseCurrentAsync()
    {
        if (_document is null) return;
        var old = _document;
        _document = null;
        await old.DisposeAsync().ConfigureAwait(true);
    }

    // ---- saving ------------------------------------------------------------------------------------
    public async Task SaveAsync()
    {
        if (_document is null) return;
        if (string.IsNullOrEmpty(_document.FilePath))
        {
            await SaveAsAsync().ConfigureAwait(true);
            return;
        }
        await WriteAsync(_document.FilePath, SaveMode.Editable).ConfigureAwait(true);
    }

    public async Task SaveAsAsync()
    {
        if (_document is null) return;
        var suggested = _document.FilePath is { } path
            ? Path.GetFileName(path)
            : "document.pdf";
        var target = await Dialogs.PickSaveFileAsync(Strings.SaveAs, suggested, PdfFilter).ConfigureAwait(true);
        if (target is null) return;
        await WriteAsync(target, SaveMode.Editable).ConfigureAwait(true);
    }

    public async Task ExportAsync()
    {
        if (_document is null) return;

        var answer = await Dialogs.ShowMessageAsync(new MessageRequest(
            Strings.ExportWarningTitle, Strings.ExportWarningBody, MessageKind.Warning,
            PrimaryLabel: Strings.ExportFinalCopy, CancelLabel: Strings.Cancel)).ConfigureAwait(true);
        if (answer != MessageAnswer.Primary) return;

        var suggested = _document.FilePath is { } path
            ? SafeFileName.DeriveOutputName(path, "עותק סופי")
            : "עותק סופי.pdf";
        var target = await Dialogs.PickSaveFileAsync(Strings.ExportFinalCopy, suggested, PdfFilter)
            .ConfigureAwait(true);
        if (target is null) return;

        // Overwriting the source with a flattened copy destroys the editable original.
        if (_document.FilePath is { } source &&
            string.Equals(Path.GetFullPath(source), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
        {
            var confirm = await Dialogs.ShowMessageAsync(new MessageRequest(
                Strings.OverwriteSourceTitle, Strings.OverwriteSourceBody, MessageKind.Warning,
                PrimaryLabel: Strings.Confirm, CancelLabel: Strings.Cancel)).ConfigureAwait(true);
            if (confirm != MessageAnswer.Primary) return;
        }

        await WriteAsync(target, SaveMode.Flattened).ConfigureAwait(true);
    }

    private async Task WriteAsync(string target, SaveMode mode)
    {
        if (_document is null) return;
        using var scope = BeginOperation(Strings.SavingInProgress);
        try
        {
            await _services.Writer.SaveAsync(_document.Document,
                new SaveRequest(target, mode, [.. _document.Annotations]),
                new Progress<double>(p => Progress = p), scope.Token).ConfigureAwait(true);

            if (mode == SaveMode.Editable) _document.MarkSaved(target);
            RaisePropertyChanged(nameof(WindowTitle));
            ReportStatus(Strings.Saved, isError: false);
        }
        catch (IOException e)
        {
            ReportStatus(DescribeIoFailure(e), isError: true);
        }
        catch (UnauthorizedAccessException)
        {
            ReportStatus(Strings.ErrorTargetReadOnly, isError: true);
        }
    }

    private static string DescribeIoFailure(IOException e) =>
        e is { HResult: unchecked((int)0x80070070) } or { HResult: unchecked((int)0x80070027) }
            ? Strings.ErrorDiskFull
            : Strings.ErrorTargetReadOnly;

    // ---- document operations ------------------------------------------------------------------------
    public async Task MergeAsync()
    {
        var sources = await Dialogs.PickOpenFilesAsync(Strings.Merge, PdfFilter).ConfigureAwait(true);
        if (sources.Count < 2)
        {
            if (sources.Count == 1) ReportStatus("יש לבחור לפחות שני קבצים למיזוג.", isError: true);
            return;
        }

        var target = await Dialogs.PickSaveFileAsync(Strings.Merge, "מסמך ממוזג.pdf", PdfFilter)
            .ConfigureAwait(true);
        if (target is null) return;

        using var scope = BeginOperation(Strings.Merge);
        try
        {
            await _services.Assembler.MergeAsync(
                sources.Select(s => new MergeSource(s)).ToList(), target,
                new Progress<double>(p => Progress = p), scope.Token).ConfigureAwait(true);
            ReportStatus(Strings.Saved, isError: false);
        }
        catch (PdfOpenException e)
        {
            ReportStatus(ErrorMessages.ForOpenError(e.Error), isError: true);
        }
    }

    public async Task SplitAsync()
    {
        if (_document?.FilePath is not { } source) return;
        var folder = await Dialogs.PickFolderAsync(Strings.Split).ConfigureAwait(true);
        if (folder is null) return;

        using var scope = BeginOperation(Strings.Split);
        try
        {
            var files = await _services.Assembler.SplitAsync(
                new SplitRequest(source, folder, SplitMode.OnePerPage),
                new Progress<double>(p => Progress = p), scope.Token).ConfigureAwait(true);
            ReportStatus(ErrorMessages.Format("נוצרו {0} קבצים.", files.Count), isError: false);
        }
        catch (PdfOpenException e)
        {
            ReportStatus(ErrorMessages.ForOpenError(e.Error), isError: true);
        }
    }

    // ---- printing --------------------------------------------------------------------------------------
    public async Task ShowPrintPreviewAsync()
    {
        if (_document is null) return;
        using var scope = BeginOperation(Strings.PrintPreview);

        var pages = new List<PrintPageInfo>(_document.PageCount);
        for (int i = 0; i < _document.PageCount; i++)
        {
            scope.Token.ThrowIfCancellationRequested();
            var info = _document.Document.Pages[i];
            bool blank = await _document.Document.IsPageBlankAsync(i, scope.Token).ConfigureAwait(true);
            pages.Add(new PrintPageInfo(i, info.WidthPoints, info.HeightPoints, info.Rotation, blank));
            Progress = (double)(i + 1) / _document.PageCount;
        }

        var preview = new PrintPreviewViewModel(pages, _services.Settings.SeparateSheetsPerContentPageDefault);
        foreach (var printer in await _services.CreatePrintService(_document.Document)
                     .GetPrintersAsync(scope.Token).ConfigureAwait(true))
            preview.Printers.Add(printer);
        preview.SelectedPrinter = preview.Printers.FirstOrDefault(p => p.IsDefault) ?? preview.Printers.FirstOrDefault();

        PrintPreview = preview;
    }

    public void ClosePrintPreview() => PrintPreview = null;

    public async Task PrintAsync()
    {
        if (_document is null || _printPreview is null) return;
        var preview = _printPreview;
        if (preview.SelectedPrinter is null)
        {
            ReportStatus("לא נבחרה מדפסת.", isError: true);
            return;
        }

        using var scope = BeginOperation(Strings.Print);
        string? jobPath = null;
        try
        {
            jobPath = await _services.PrintJobBuilder.BuildAsync(
                _document.Document, preview.Sequence, _services.Paths.Temp, scope.Token).ConfigureAwait(true);
            _services.Janitor.Track(jobPath);

            await using var job = await _services.Loader.OpenAsync(jobPath, scope.Token).ConfigureAwait(true);
            var service = _services.CreatePrintService(job);
            var result = await service.PrintAsync(
                new PrintJobRequest(preview.SelectedPrinter.Name, jobPath, preview.Sequence, preview.Copies),
                new Progress<double>(p => Progress = p), scope.Token).ConfigureAwait(true);

            ReportStatus(result.Succeeded
                ? ErrorMessages.Format("נשלחו {0} עמודים להדפסה.", result.PagesSent)
                : result.ErrorMessage ?? Strings.ErrorUnknown, isError: !result.Succeeded);

            if (result.Succeeded)
            {
                _services.Settings.SeparateSheetsPerContentPageDefault = preview.SeparateSheetsPerContentPage;
                await _services.SaveSettingsAsync(scope.Token).ConfigureAwait(true);
                ClosePrintPreview();
            }
        }
        finally
        {
            if (jobPath is not null) _services.Janitor.Release(jobPath);
        }
    }

    // ---- OCR --------------------------------------------------------------------------------------------
    public async Task RunOcrOnCurrentPageAsync()
    {
        if (_document is null) return;
        if (!_services.Ocr.IsAvailable)
        {
            ReportStatus(_services.Ocr.UnavailableReason ?? Strings.OcrNotAvailable, isError: true);
            return;
        }

        using var scope = BeginOperation(Strings.Ocr);
        int pageIndex = _document.CurrentPageIndex;
        var result = await _services.Ocr.RecognizePageAsync(_document.Document, pageIndex,
            OcrLanguage.HebrewAndEnglish, _services.Settings.OcrRenderDpi,
            _services.Settings.OcrCacheEnabled, scope.Token).ConfigureAwait(true);

        ReportStatus(string.IsNullOrWhiteSpace(result.Text) ? Strings.OcrNoResults : Strings.OcrAccuracyNotice,
            isError: false);
        RunSearch();
    }

    public async Task ClearOcrCacheAsync()
    {
        int removed = await _services.Ocr.ClearCacheAsync().ConfigureAwait(true);
        ReportStatus(ErrorMessages.Format("נמחקו {0} רשומות מהמטמון.", removed), isError: false);
    }

    private void RunSearch()
    {
        SearchResults.Clear();
        if (string.IsNullOrWhiteSpace(_searchText)) return;
        foreach (var hit in _services.Ocr.Search(_searchText)) SearchResults.Add(hit);
        RaisePropertyChanged(nameof(SearchResults));
    }

    // ---- editing -------------------------------------------------------------------------------------------
    private Annotation? _clipboard;

    public void Undo()
    {
        _document?.History.Undo();
        RaisePropertyChanged(nameof(WindowTitle));
        RefreshCommands();
    }

    public void Redo()
    {
        _document?.History.Redo();
        RaisePropertyChanged(nameof(WindowTitle));
        RefreshCommands();
    }

    public void DeleteSelection()
    {
        if (_document?.SelectedAnnotation is { IsForeign: false } annotation)
            _document.RemoveAnnotation(annotation);
        RefreshCommands();
    }

    public void DuplicateSelection()
    {
        if (_document?.SelectedAnnotation is { IsForeign: false } annotation)
            _document.Duplicate(annotation);
        RefreshCommands();
    }

    public void CopySelection()
    {
        if (_document?.SelectedAnnotation is { } annotation) _clipboard = annotation.Clone();
        PasteCommand.RaiseCanExecuteChanged();
    }

    public void Paste()
    {
        if (_document is null || _clipboard is null) return;
        var copy = _clipboard.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.PageIndex = _document.CurrentPageIndex;
        copy.Rect = copy.Rect.Translate(16, -16);
        copy.Touch();
        _document.AddAnnotation(copy);
        RefreshCommands();
    }

    public void ClearSelection()
    {
        if (_document is not null) _document.SelectedAnnotation = null;
        Toolbox.Reset();
    }

    public void GoToPage(int pageIndex)
    {
        if (_document is null) return;
        _document.CurrentPageIndex = pageIndex;
        RefreshCommands();
    }

    private void OnSelectionChanged(object? sender, Annotation? annotation) => RefreshCommands();

    // ---- theme -----------------------------------------------------------------------------------------------
    private void CycleTheme() => Theme = Theme switch
    {
        ThemePreference.System => ThemePreference.Light,
        ThemePreference.Light => ThemePreference.Dark,
        _ => ThemePreference.System
    };

    // ---- operation plumbing -------------------------------------------------------------------------------------
    private AsyncRelayCommand Async(Func<Task> execute, Func<bool>? canExecute = null)
    {
        var command = new AsyncRelayCommand(execute, canExecute) { OnError = HandleCommandFailure };
        return command;
    }

    private void HandleCommandFailure(Exception exception)
    {
        CrashLog.Write(exception);
        ReportStatus(exception switch
        {
            PdfOpenException open => ErrorMessages.ForOpenError(open.Error),
            UnauthorizedAccessException => Strings.ErrorAccessDenied,
            IOException io => DescribeIoFailure(io),
            _ => Strings.ErrorUnknown
        }, isError: true);
    }

    private OperationScope BeginOperation(string text)
    {
        _operation?.Cancel();
        _operation?.Dispose();
        _operation = new CancellationTokenSource();

        BusyText = text;
        Progress = 0;
        IsBusy = true;
        return new OperationScope(this, _operation);
    }

    private void EndOperation()
    {
        IsBusy = false;
        Progress = 0;
        BusyText = string.Empty;
        RefreshCommands();
    }

    private void CancelOperation()
    {
        _operation?.Cancel();
        ReportStatus(Strings.OperationCancelled, isError: false);
    }

    public void ReportStatus(string message, bool isError)
    {
        StatusIsError = isError;
        StatusMessage = message;
    }

    public void ClearStatus() => StatusMessage = null;

    public void RefreshCommands()
    {
        SaveCommand.RaiseCanExecuteChanged();
        SaveAsCommand.RaiseCanExecuteChanged();
        ExportCommand.RaiseCanExecuteChanged();
        PrintCommand.RaiseCanExecuteChanged();
        SplitCommand.RaiseCanExecuteChanged();
        RunOcrCommand.RaiseCanExecuteChanged();
        CloseDocumentCommand.RaiseCanExecuteChanged();
        UndoCommand.RaiseCanExecuteChanged();
        RedoCommand.RaiseCanExecuteChanged();
        DeleteSelectionCommand.RaiseCanExecuteChanged();
        DuplicateSelectionCommand.RaiseCanExecuteChanged();
        CopySelectionCommand.RaiseCanExecuteChanged();
        PasteCommand.RaiseCanExecuteChanged();
        ZoomInCommand.RaiseCanExecuteChanged();
        ZoomOutCommand.RaiseCanExecuteChanged();
        FitWidthCommand.RaiseCanExecuteChanged();
        FitPageCommand.RaiseCanExecuteChanged();
        ActualSizeCommand.RaiseCanExecuteChanged();
        NextPageCommand.RaiseCanExecuteChanged();
        PreviousPageCommand.RaiseCanExecuteChanged();
        RaiseAll(nameof(WindowTitle));
    }

    private async Task<bool> ConfirmDiscardChangesAsync()
    {
        if (_document?.IsDirty != true) return true;

        var answer = await Dialogs.ShowMessageAsync(new MessageRequest(
            Strings.SaveBeforeClosingTitle, Strings.SaveBeforeClosingBody, MessageKind.Question,
            PrimaryLabel: Strings.Save, SecondaryLabel: Strings.DontSave, CancelLabel: Strings.Cancel))
            .ConfigureAwait(true);

        switch (answer)
        {
            case MessageAnswer.Primary:
                await SaveAsync().ConfigureAwait(true);
                return _document?.IsDirty != true;
            case MessageAnswer.Secondary:
                return true;
            default:
                return false;
        }
    }

    private sealed class OperationScope : IDisposable
    {
        private readonly MainWindowViewModel _owner;
        private readonly CancellationTokenSource _cts;

        public OperationScope(MainWindowViewModel owner, CancellationTokenSource cts)
        {
            _owner = owner;
            _cts = cts;
        }

        public CancellationToken Token => _cts.Token;

        public void Dispose() => _owner.EndOperation();
    }
}
