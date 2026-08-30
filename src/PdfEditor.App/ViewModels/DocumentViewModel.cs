using System.Collections.ObjectModel;
using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Core.History;
using PdfEditor.Core.Localization;

namespace PdfEditor.App.ViewModels;

public enum ZoomMode { Custom, FitWidth, FitPage, Actual }

/// <summary>
/// One open document: its pages, its annotations, its edit history and its view state.
/// </summary>
public sealed class DocumentViewModel : ViewModelBase, IAsyncDisposable
{
    private static readonly double[] ZoomSteps =
        [0.10, 0.25, 0.33, 0.50, 0.67, 0.75, 1.00, 1.25, 1.50, 2.00, 3.00, 4.00, 6.00, 8.00];

    private readonly IPdfDocument _document;
    private double _zoom = 1.0;
    private ZoomMode _zoomMode = ZoomMode.Custom;
    private int _currentPageIndex;
    private Annotation? _selectedAnnotation;
    private string? _savedPath;
    private double _viewportWidth = 800;
    private double _viewportHeight = 600;

    public DocumentViewModel(IPdfDocument document, IReadOnlyList<Annotation> annotations)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _savedPath = document.SourcePath;

        Pages = new ObservableCollection<PageViewModel>(
            document.Pages.Select(info => new PageViewModel(document, info)));

        foreach (var annotation in annotations)
        {
            Annotations.Add(annotation);
            if (annotation.PageIndex >= 0 && annotation.PageIndex < Pages.Count)
                Pages[annotation.PageIndex].Annotations.Add(annotation);
        }

        History.Changed += (_, _) => RaiseAll(nameof(CanUndo), nameof(CanRedo), nameof(IsDirty),
            nameof(UndoDescription), nameof(RedoDescription), nameof(SaveStateText));
    }

    public IPdfDocument Document => _document;

    public ObservableCollection<PageViewModel> Pages { get; }

    /// <summary>Every annotation in the document, ours and any read from the file.</summary>
    public ObservableCollection<Annotation> Annotations { get; } = [];

    public UndoRedoStack History { get; } = new();

    public string? FilePath => _savedPath;

    public string DisplayName => string.IsNullOrEmpty(_savedPath)
        ? Strings.EmptyStateTitle
        : Path.GetFileName(_savedPath);

    public int PageCount => Pages.Count;

    public bool IsProtected => _document.IsProtected;

    // ---- view state -------------------------------------------------------------------------
    public double Zoom
    {
        get => _zoom;
        private set
        {
            if (!SetProperty(ref _zoom, Math.Clamp(value, 0.05, 12.0))) return;
            foreach (var page in Pages) page.Scale = _zoom;
            RaisePropertyChanged(nameof(ZoomPercentText));
        }
    }

    public string ZoomPercentText => $"{Math.Round(_zoom * 100)}%";

    public ZoomMode ZoomMode
    {
        get => _zoomMode;
        private set => SetProperty(ref _zoomMode, value);
    }

    public int CurrentPageIndex
    {
        get => _currentPageIndex;
        set
        {
            int clamped = Math.Clamp(value, 0, Math.Max(0, Pages.Count - 1));
            if (!SetProperty(ref _currentPageIndex, clamped)) return;
            for (int i = 0; i < Pages.Count; i++) Pages[i].IsSelected = i == clamped;
            RaisePropertyChanged(nameof(PageIndicatorText));
        }
    }

    public string PageIndicatorText =>
        ErrorMessages.Format(Strings.PageOf, CurrentPageIndex + 1, Pages.Count);

    public Annotation? SelectedAnnotation
    {
        get => _selectedAnnotation;
        set
        {
            if (!SetProperty(ref _selectedAnnotation, value)) return;
            RaiseAll(nameof(HasSelection), nameof(IsSelectionEditable));
            SelectionChanged?.Invoke(this, value);
        }
    }

    public bool HasSelection => _selectedAnnotation is not null;

    /// <summary>Annotations written by other applications are shown but never edited.</summary>
    public bool IsSelectionEditable => _selectedAnnotation is { IsForeign: false };

    public event EventHandler<Annotation?>? SelectionChanged;

    // ---- history ----------------------------------------------------------------------------
    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;
    public bool IsDirty => History.IsDirty;
    public string? UndoDescription => History.NextUndoDescription;
    public string? RedoDescription => History.NextRedoDescription;
    public string SaveStateText => IsDirty ? Strings.Unsaved : Strings.Saved;

    public void MarkSaved(string path)
    {
        _savedPath = path;
        History.MarkSaved();
        RaiseAll(nameof(FilePath), nameof(DisplayName), nameof(IsDirty), nameof(SaveStateText));
    }

    // ---- annotation editing ------------------------------------------------------------------
    public void AddAnnotation(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        History.Execute(new DelegateAction(DescriptionFor(annotation.Kind),
            () => Insert(annotation),
            () => Remove(annotation)));
        SelectedAnnotation = annotation;
    }

    public void RemoveAnnotation(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (annotation.IsForeign) return;
        bool wasSelected = ReferenceEquals(_selectedAnnotation, annotation);
        History.Execute(new DelegateAction(Strings.Delete,
            () => { Remove(annotation); if (wasSelected) SelectedAnnotation = null; },
            () => Insert(annotation)));
    }

    /// <summary>
    /// Records a change already applied to <paramref name="annotation"/> so it can be undone,
    /// using a snapshot taken before the edit.
    /// </summary>
    public void RecordEdit(Annotation annotation, Annotation before, string description)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(before);
        var after = annotation.Clone();
        History.Push(new DelegateAction(description,
            () => CopyInto(after, annotation),
            () => CopyInto(before, annotation)));
    }

    public Annotation Duplicate(Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        var copy = annotation.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Rect = copy.Rect.Translate(12, -12);
        copy.Touch();
        AddAnnotation(copy);
        return copy;
    }

    private void Insert(Annotation annotation)
    {
        if (!Annotations.Contains(annotation)) Annotations.Add(annotation);
        var page = PageFor(annotation.PageIndex);
        if (page is not null && !page.Annotations.Contains(annotation)) page.Annotations.Add(annotation);
        AnnotationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Remove(Annotation annotation)
    {
        Annotations.Remove(annotation);
        PageFor(annotation.PageIndex)?.Annotations.Remove(annotation);
        AnnotationsChanged?.Invoke(this, EventArgs.Empty);
    }

    private PageViewModel? PageFor(int pageIndex) =>
        pageIndex >= 0 && pageIndex < Pages.Count ? Pages[pageIndex] : null;

    /// <summary>Copies mutable state from a snapshot back onto the live annotation.</summary>
    private void CopyInto(Annotation source, Annotation target)
    {
        target.Rect = source.Rect;
        target.Color = source.Color;
        target.LineWidth = source.LineWidth;
        target.Opacity = source.Opacity;
        target.Rotation = source.Rotation;
        target.ModifiedUtc = source.ModifiedUtc;

        switch (target)
        {
            case TextBoxAnnotation t when source is TextBoxAnnotation s:
                t.Text = s.Text;
                t.FontSize = s.FontSize;
                t.Bold = s.Bold;
                t.Italic = s.Italic;
                t.TextColor = s.TextColor;
                t.BackgroundColor = s.BackgroundColor;
                t.BorderColor = s.BorderColor;
                t.Alignment = s.Alignment;
                t.Direction = s.Direction;
                break;
            case ShapeAnnotation shape when source is ShapeAnnotation ss:
                shape.FillColor = ss.FillColor;
                shape.Start = ss.Start;
                shape.End = ss.End;
                break;
            case InkAnnotation ink when source is InkAnnotation si:
                ink.Strokes.Clear();
                foreach (var stroke in si.Strokes) ink.Strokes.Add([.. stroke]);
                break;
            case SignatureAnnotation sig when source is SignatureAnnotation ssig:
                sig.SignatureId = ssig.SignatureId;
                sig.ImagePng = ssig.ImagePng;
                break;
        }
        AnnotationsChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? AnnotationsChanged;

    private static string DescriptionFor(AnnotationKind kind) => kind switch
    {
        AnnotationKind.TextBox => Strings.ToolTextBox,
        AnnotationKind.Rectangle => Strings.ToolRectangle,
        AnnotationKind.Ellipse => Strings.ToolEllipse,
        AnnotationKind.Line => Strings.ToolLine,
        AnnotationKind.Arrow => Strings.ToolArrow,
        AnnotationKind.Ink => Strings.ToolInk,
        AnnotationKind.Highlight => Strings.ToolHighlight,
        AnnotationKind.CheckMark => Strings.ToolCheckMark,
        AnnotationKind.CrossMark => Strings.ToolCrossMark,
        _ => Strings.ToolSignature
    };

    // ---- zoom --------------------------------------------------------------------------------
    public void SetViewport(double width, double height)
    {
        _viewportWidth = Math.Max(1, width);
        _viewportHeight = Math.Max(1, height);
        if (_zoomMode is ZoomMode.FitWidth or ZoomMode.FitPage) ApplyZoomMode(_zoomMode);
    }

    public void SetZoom(double zoom)
    {
        ZoomMode = ZoomMode.Custom;
        Zoom = zoom;
    }

    public void ZoomIn() => SetZoom(ZoomSteps.FirstOrDefault(z => z > _zoom + 0.001, ZoomSteps[^1]));

    public void ZoomOut() => SetZoom(ZoomSteps.LastOrDefault(z => z < _zoom - 0.001, ZoomSteps[0]));

    public void ApplyZoomMode(ZoomMode mode)
    {
        ZoomMode = mode;
        var page = Pages.Count > 0 ? Pages[Math.Clamp(_currentPageIndex, 0, Pages.Count - 1)] : null;
        if (page is null) return;

        var (width, height) = page.Info.DisplaySize;
        const double margin = 48;

        Zoom = mode switch
        {
            ZoomMode.FitWidth => Math.Max(0.05, (_viewportWidth - margin) / width),
            ZoomMode.FitPage => Math.Max(0.05, Math.Min((_viewportWidth - margin) / width,
                                                        (_viewportHeight - margin) / height)),
            ZoomMode.Actual => 1.0,
            _ => _zoom
        };
    }

    /// <summary>
    /// Fills the page list's thumbnails in the background, one page at a time.
    /// </summary>
    /// <remarks>
    /// Sequential on purpose: the renderer is serialised per document anyway, and a single ordered
    /// queue keeps the pages the user is most likely to look at first rather than saturating the
    /// thread pool for a 500-page file.
    /// </remarks>
    public async Task BuildThumbnailsAsync(CancellationToken cancellationToken)
    {
        foreach (var page in Pages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await page.EnsureThumbnailAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Discards cached renders for a page after its content changed.</summary>
    public void InvalidatePage(int pageIndex)
    {
        if (pageIndex >= 0 && pageIndex < Pages.Count) Pages[pageIndex].Invalidate();
    }

    public async ValueTask DisposeAsync()
    {
        _thumbnails?.Cancel();
        _thumbnails?.Dispose();
        foreach (var page in Pages) page.Dispose();
        await _document.DisposeAsync().ConfigureAwait(false);
    }

    private CancellationTokenSource? _thumbnails;

    /// <summary>Starts the background thumbnail pass. Safe to call once per open document.</summary>
    public void StartThumbnails()
    {
        _thumbnails?.Cancel();
        _thumbnails?.Dispose();
        _thumbnails = new CancellationTokenSource();
        var token = _thumbnails.Token;
        _ = Task.Run(async () =>
        {
            try { await BuildThumbnailsAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }, token);
    }
}
