using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using PdfEditor.App.ViewModels;
using PdfEditor.Core.Annotations;
using AvaloniaColor = Avalonia.Media.Color;

namespace PdfEditor.App.Controls;

/// <summary>
/// The interactive surface for one page: it paints the rendered bitmap, overlays the annotations
/// that belong to the page, and turns pointer input into annotation edits.
/// </summary>
/// <remarks>
/// Screen coordinates run top-left with y downwards; PDF user space runs bottom-left with y
/// upwards. Every conversion goes through <see cref="ToPdf"/> and <see cref="ToScreen"/> so the
/// mapping exists in exactly one place.
/// </remarks>
public sealed class PageSurface : Control
{
    public static readonly StyledProperty<PageViewModel?> PageProperty =
        AvaloniaProperty.Register<PageSurface, PageViewModel?>(nameof(Page));

    public static readonly StyledProperty<DocumentViewModel?> DocumentProperty =
        AvaloniaProperty.Register<PageSurface, DocumentViewModel?>(nameof(Document));

    public static readonly StyledProperty<EditorTool> ActiveToolProperty =
        AvaloniaProperty.Register<PageSurface, EditorTool>(nameof(ActiveTool), EditorTool.Select);

    public static readonly StyledProperty<AnnotationColor> DrawColorProperty =
        AvaloniaProperty.Register<PageSurface, AnnotationColor>(nameof(DrawColor), AnnotationColor.Red);

    public static readonly StyledProperty<double> DrawLineWidthProperty =
        AvaloniaProperty.Register<PageSurface, double>(nameof(DrawLineWidth), 2.0);

    private const double HandleSize = 8;
    private const double MinimumDragToCreate = 4;

    private Point? _dragStart;
    private Point _dragCurrent;
    private Annotation? _dragTarget;
    private Annotation? _dragSnapshot;
    private PdfRect _dragOriginalRect;
    private List<PdfPoint>? _inkStroke;
    private DragMode _mode = DragMode.None;

    private enum DragMode { None, Create, Move, Resize, Ink }

    static PageSurface()
    {
        AffectsRender<PageSurface>(PageProperty, ActiveToolProperty);
        FocusableProperty.OverrideDefaultValue<PageSurface>(true);
    }

    public PageViewModel? Page
    {
        get => GetValue(PageProperty);
        set => SetValue(PageProperty, value);
    }

    public DocumentViewModel? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public EditorTool ActiveTool
    {
        get => GetValue(ActiveToolProperty);
        set => SetValue(ActiveToolProperty, value);
    }

    public AnnotationColor DrawColor
    {
        get => GetValue(DrawColorProperty);
        set => SetValue(DrawColorProperty, value);
    }

    public double DrawLineWidth
    {
        get => GetValue(DrawLineWidthProperty);
        set => SetValue(DrawLineWidthProperty, value);
    }

    /// <summary>Raised when a text annotation is created or double-clicked, so the shell can edit it.</summary>
    public event EventHandler<TextBoxAnnotation>? TextEditRequested;

    protected override Size MeasureOverride(Size availableSize) =>
        Page is null ? default : new Size(Page.DisplayWidth, Page.DisplayHeight);

    /// <summary>
    /// Rendering is driven by the visual tree. A virtualising panel only realises the pages the
    /// user can see, so attaching is the moment a page needs its bitmap, and detaching is the
    /// moment it can give it back.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(Page);
        RequestRender();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Subscribe(null);
        Page?.ReleaseBitmap();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PageProperty)
        {
            Subscribe(change.GetNewValue<PageViewModel?>());
            RequestRender();
        }
        else if (change.Property == DocumentProperty)
        {
            SubscribeDocument(change.GetNewValue<DocumentViewModel?>());
        }
    }

    private PageViewModel? _subscribedPage;
    private DocumentViewModel? _subscribedDocument;

    private void Subscribe(PageViewModel? page)
    {
        if (ReferenceEquals(_subscribedPage, page)) return;
        if (_subscribedPage is not null) _subscribedPage.PropertyChanged -= OnPagePropertyChanged;
        _subscribedPage = page;
        if (_subscribedPage is not null) _subscribedPage.PropertyChanged += OnPagePropertyChanged;
    }

    private void SubscribeDocument(DocumentViewModel? document)
    {
        if (ReferenceEquals(_subscribedDocument, document)) return;
        if (_subscribedDocument is not null)
        {
            _subscribedDocument.AnnotationsChanged -= OnAnnotationsChanged;
            _subscribedDocument.SelectionChanged -= OnSelectionChanged;
        }
        _subscribedDocument = document;
        if (_subscribedDocument is not null)
        {
            _subscribedDocument.AnnotationsChanged += OnAnnotationsChanged;
            _subscribedDocument.SelectionChanged += OnSelectionChanged;
        }
    }

    private void OnAnnotationsChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnSelectionChanged(object? sender, Annotation? e) => InvalidateVisual();

    private void OnPagePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PageViewModel.DisplayWidth) or nameof(PageViewModel.DisplayHeight))
        {
            InvalidateMeasure();
            RequestRender();
        }
        else if (e.PropertyName == nameof(PageViewModel.Bitmap))
        {
            InvalidateVisual();
        }
    }

    private void RequestRender()
    {
        var page = Page;
        if (page is null || !this.IsAttachedToVisualTree()) return;
        _ = page.EnsureRenderedAsync();
    }

    // ---- coordinate mapping -------------------------------------------------------------------
    private double Scale => Page?.Scale ?? 1.0;

    private double PageHeightPoints => Page?.Info.DisplaySize.Height ?? 0;

    private PdfPoint ToPdf(Point screen) =>
        new(screen.X / Scale, PageHeightPoints - screen.Y / Scale);

    private Point ToScreen(PdfPoint pdf) =>
        new(pdf.X * Scale, (PageHeightPoints - pdf.Y) * Scale);

    private Rect ToScreen(PdfRect rect) => new(
        rect.Left * Scale,
        (PageHeightPoints - rect.Top) * Scale,
        rect.Width * Scale,
        rect.Height * Scale);

    // ---- rendering ----------------------------------------------------------------------------
    public override void Render(DrawingContext context)
    {
        if (Page is null) return;
        var bounds = new Rect(0, 0, Page.DisplayWidth, Page.DisplayHeight);

        if (Page.Bitmap is Bitmap bitmap)
            context.DrawImage(bitmap, new Rect(bitmap.Size), bounds);
        else if (Page.Thumbnail is Bitmap placeholder)
            context.DrawImage(placeholder, new Rect(placeholder.Size), bounds);   // soft, until sharp
        else
            context.FillRectangle(Brushes.White, bounds);

        // The bitmap is rendered without annotations; the model supplies them, so an edit is
        // visible immediately without waiting for the page to be rasterised again.
        foreach (var annotation in Page.Annotations)
            AnnotationOverlay.Draw(context, annotation, ToScreen(annotation.Rect), Scale);

        // Live feedback for the shape being drawn right now.
        if (_mode == DragMode.Create && _dragStart is { } start)
            DrawPreview(context, start, _dragCurrent);
        if (_mode == DragMode.Ink && _inkStroke is { Count: > 1 })
            DrawInkPreview(context);

        if (Document?.SelectedAnnotation is { } selected && selected.PageIndex == Page.PageIndex)
            DrawSelection(context, selected);
    }

    private void DrawPreview(DrawingContext context, Point start, Point current)
    {
        var pen = new Pen(new SolidColorBrush(ToAvalonia(DrawColor)), Math.Max(1, DrawLineWidth * Scale));
        var rect = new Rect(start, current).Normalize();

        switch (ActiveTool)
        {
            case EditorTool.Ellipse:
                context.DrawEllipse(null, pen, rect.Center, rect.Width / 2, rect.Height / 2);
                break;
            case EditorTool.Line:
            case EditorTool.Arrow:
                context.DrawLine(pen, start, current);
                break;
            case EditorTool.Highlight:
                context.FillRectangle(new SolidColorBrush(ToAvalonia(DrawColor), 0.35), rect);
                break;
            default:
                context.DrawRectangle(null, pen, rect);
                break;
        }
    }

    private void DrawInkPreview(DrawingContext context)
    {
        if (_inkStroke is null) return;
        var pen = new Pen(new SolidColorBrush(ToAvalonia(DrawColor)), Math.Max(1, DrawLineWidth * Scale))
        {
            LineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        for (int i = 1; i < _inkStroke.Count; i++)
            context.DrawLine(pen, ToScreen(_inkStroke[i - 1]), ToScreen(_inkStroke[i]));
    }

    private void DrawSelection(DrawingContext context, Annotation annotation)
    {
        var rect = ToScreen(annotation.Rect);
        var accent = this.FindResource("AccentColor") is AvaloniaColor color
            ? color
            : AvaloniaColor.FromRgb(31, 78, 140);

        // A dashed outline plus corner handles, so selection does not rely on colour alone.
        var pen = new Pen(new SolidColorBrush(accent), 1.5)
        {
            DashStyle = new DashStyle([4, 3], 0)
        };
        context.DrawRectangle(null, pen, rect.Inflate(2));

        if (annotation.IsForeign) return;

        var handleBrush = new SolidColorBrush(accent);
        foreach (var handle in HandleRects(rect))
            context.DrawRectangle(handleBrush, null, handle);
    }

    private static IEnumerable<Rect> HandleRects(Rect rect)
    {
        double h = HandleSize;
        yield return new Rect(rect.X - h / 2, rect.Y - h / 2, h, h);
        yield return new Rect(rect.Right - h / 2, rect.Y - h / 2, h, h);
        yield return new Rect(rect.X - h / 2, rect.Bottom - h / 2, h, h);
        yield return new Rect(rect.Right - h / 2, rect.Bottom - h / 2, h, h);
    }

    private static AvaloniaColor ToAvalonia(AnnotationColor color) =>
        AvaloniaColor.FromArgb(color.A, color.R, color.G, color.B);

    // ---- input ----------------------------------------------------------------------------------
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Page is null || Document is null) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        Focus();
        Document.CurrentPageIndex = Page.PageIndex;
        if (e.ClickCount >= 2 && ActiveTool == EditorTool.Select)
        {
            BeginSelectOrMove(e.GetPosition(this));
            RequestTextEdit();
            e.Handled = true;
            return;
        }
        var position = e.GetPosition(this);
        _dragStart = position;
        _dragCurrent = position;

        if (ActiveTool == EditorTool.Select)
        {
            BeginSelectOrMove(position);
        }
        else if (ActiveTool == EditorTool.Ink)
        {
            _mode = DragMode.Ink;
            _inkStroke = [ToPdf(position)];
        }
        else
        {
            _mode = DragMode.Create;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    private void BeginSelectOrMove(Point position)
    {
        var pdf = ToPdf(position);
        var hit = Page!.Annotations
            .Where(a => a.Rect.Contains(pdf))
            .OrderBy(a => a.Rect.Width * a.Rect.Height)   // prefer the smallest shape under the cursor
            .FirstOrDefault();

        Document!.SelectedAnnotation = hit;
        if (hit is null || hit.IsForeign)
        {
            _mode = DragMode.None;
            return;
        }

        var screenRect = ToScreen(hit.Rect);
        _dragTarget = hit;
        _dragSnapshot = hit.Clone();
        _dragOriginalRect = hit.Rect;
        _mode = HandleRects(screenRect).Any(h => h.Inflate(3).Contains(position))
            ? DragMode.Resize
            : DragMode.Move;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_mode == DragMode.None || _dragStart is null) return;

        _dragCurrent = e.GetPosition(this);

        switch (_mode)
        {
            case DragMode.Ink:
                _inkStroke?.Add(ToPdf(_dragCurrent));
                break;
            case DragMode.Move when _dragTarget is not null:
                {
                    var delta = ToPdf(_dragCurrent);
                    var origin = ToPdf(_dragStart.Value);
                    _dragTarget.Rect = _dragOriginalRect.Translate(delta.X - origin.X, delta.Y - origin.Y);
                    break;
                }
            case DragMode.Resize when _dragTarget is not null:
                {
                    var corner = ToPdf(_dragCurrent);
                    _dragTarget.Rect = PdfRect.FromCorners(
                        _dragOriginalRect.Left, _dragOriginalRect.Bottom, corner.X, corner.Y);
                    break;
                }
        }
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_mode == DragMode.None || _dragStart is null || Page is null || Document is null)
        {
            Reset();
            return;
        }

        var start = _dragStart.Value;
        var end = e.GetPosition(this);

        switch (_mode)
        {
            case DragMode.Create:
                CreateAnnotation(start, end);
                break;
            case DragMode.Ink:
                CommitInk();
                break;
            case DragMode.Move:
            case DragMode.Resize:
                CommitTransform();
                break;
        }

        e.Pointer.Capture(null);
        Reset();
        InvalidateVisual();
    }

    private void CreateAnnotation(Point start, Point end)
    {
        var kind = ToolboxViewModel.KindFor(ActiveTool);
        if (kind is null) return;

        var a = ToPdf(start);
        var b = ToPdf(end);
        bool isClick = Math.Abs(end.X - start.X) < MinimumDragToCreate &&
                       Math.Abs(end.Y - start.Y) < MinimumDragToCreate;

        // A click without a drag places a sensibly sized default, which is what a user expects
        // from a stamp-like tool.
        var rect = isClick
            ? DefaultRectFor(kind.Value, a)
            : PdfRect.FromCorners(a.X, a.Y, b.X, b.Y);

        if (rect.Width < 2 || rect.Height < 2) return;

        Annotation annotation = kind.Value switch
        {
            // Plain text, not a sticky note: the common job is filling a form in — a name, an ID
            // number, a date — and that has to look like it belongs on the page. No background and
            // no border, so what lands in the PDF is the typed characters and nothing else. The
            // editor still shows an outline for an empty box; see AnnotationOverlay.
            AnnotationKind.TextBox => new TextBoxAnnotation
            {
                Text = string.Empty,
                TextColor = DrawColor,
                BorderColor = null,
                BackgroundColor = null
            },
            AnnotationKind.CheckMark or AnnotationKind.CrossMark => new MarkAnnotation(kind.Value),
            AnnotationKind.Signature => new SignatureAnnotation(),
            AnnotationKind.Ink => new InkAnnotation(),
            _ => new ShapeAnnotation(kind.Value) { Start = a, End = b }
        };

        annotation.PageIndex = Page!.PageIndex;
        annotation.Rect = rect;
        if (annotation is not MarkAnnotation) annotation.Color = DrawColor;
        annotation.LineWidth = DrawLineWidth;

        Document!.AddAnnotation(annotation);
        if (annotation is TextBoxAnnotation text) TextEditRequested?.Invoke(this, text);
    }

    private static PdfRect DefaultRectFor(AnnotationKind kind, PdfPoint at) => kind switch
    {
        AnnotationKind.CheckMark or AnnotationKind.CrossMark => new PdfRect(at.X - 12, at.Y - 12, 24, 24),
        AnnotationKind.TextBox => new PdfRect(at.X - 90, at.Y - 22, 180, 44),
        AnnotationKind.Signature => new PdfRect(at.X - 70, at.Y - 20, 140, 40),
        _ => new PdfRect(at.X - 40, at.Y - 25, 80, 50)
    };

    private void CommitInk()
    {
        if (_inkStroke is not { Count: > 1 } || Page is null || Document is null) return;

        var ink = new InkAnnotation
        {
            PageIndex = Page.PageIndex,
            Color = DrawColor,
            LineWidth = DrawLineWidth
        };
        ink.Strokes.Add([.. _inkStroke]);
        ink.RecalculateBounds();
        Document.AddAnnotation(ink);
    }

    private void CommitTransform()
    {
        if (_dragTarget is null || _dragSnapshot is null || Document is null) return;
        if (_dragTarget.Rect.Equals(_dragOriginalRect)) return;

        _dragTarget.Touch();
        Document.RecordEdit(_dragTarget, _dragSnapshot,
            _mode == DragMode.Move ? "הזזה" : "שינוי גודל");
    }

    private void Reset()
    {
        _mode = DragMode.None;
        _dragStart = null;
        _dragTarget = null;
        _dragSnapshot = null;
        _inkStroke = null;
    }

    /// <summary>Double-clicking a text annotation opens it for editing.</summary>
    public void RequestTextEdit()
    {
        if (Document?.SelectedAnnotation is TextBoxAnnotation text) TextEditRequested?.Invoke(this, text);
    }
}
