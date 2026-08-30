using PdfEditor.Core.Annotations;
using PdfEditor.Core.Localization;

namespace PdfEditor.App.ViewModels;

public sealed record ColorSwatch(string Name, AnnotationColor Color);

/// <summary>
/// The properties panel for the selected annotation.
/// </summary>
/// <remarks>
/// Every change is applied to the live annotation and recorded on the undo stack as one step, using
/// a snapshot taken when the selection was made.
/// </remarks>
public sealed class PropertiesViewModel : ViewModelBase
{
    private readonly DocumentViewModel _document;
    private Annotation? _target;
    private Annotation? _snapshot;

    public PropertiesViewModel(DocumentViewModel document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _document.SelectionChanged += (_, annotation) => Attach(annotation);
        Attach(document.SelectedAnnotation);
    }

    public static IReadOnlyList<ColorSwatch> Swatches { get; } =
    [
        new("אדום", AnnotationColor.Red),
        new("כחול", AnnotationColor.Blue),
        new("ירוק", AnnotationColor.Green),
        new("צהוב", AnnotationColor.Yellow),
        new("שחור", AnnotationColor.Black),
        new("אפור", new AnnotationColor(117, 117, 117)),
        new("כתום", new AnnotationColor(230, 126, 34)),
        new("סגול", new AnnotationColor(123, 76, 176))
    ];

    public bool HasTarget => _target is not null;

    public bool IsEditable => _target is { IsForeign: false };

    public bool IsForeign => _target is { IsForeign: true };

    public string TitleText => _target is null
        ? Strings.Properties
        : KindName(_target.Kind);

    public bool IsTextBox => _target is TextBoxAnnotation;

    public bool SupportsFill => _target is ShapeAnnotation
    { Kind: AnnotationKind.Rectangle or AnnotationKind.Ellipse };

    public bool SupportsLineWidth => _target is ShapeAnnotation or InkAnnotation or MarkAnnotation
        or TextBoxAnnotation;

    /// <summary>
    /// The colour swatch. A text box has no stroke of its own, so for one the swatch is the ink of
    /// the glyphs — otherwise picking a colour would appear to do nothing at all.
    /// </summary>
    public AnnotationColor Color
    {
        get => _target switch
        {
            TextBoxAnnotation t => t.TextColor,
            { } a => a.Color,
            null => AnnotationColor.Red
        };
        set => Mutate(a =>
        {
            a.Color = value;
            if (a is TextBoxAnnotation t) t.TextColor = value;
        }, Strings.ToolSelect);
    }

    public double LineWidth
    {
        get => _target?.LineWidth ?? 2;
        set => Mutate(a => a.LineWidth = Math.Clamp(value, 0.5, 24), Strings.ToolLine);
    }

    public double Opacity
    {
        get => _target?.Opacity ?? 1;
        set => Mutate(a => a.Opacity = Math.Clamp(value, 0.1, 1.0), Strings.Properties);
    }

    public string Text
    {
        get => (_target as TextBoxAnnotation)?.Text ?? string.Empty;
        set => Mutate(a => { if (a is TextBoxAnnotation t) t.Text = value; }, Strings.ToolTextBox);
    }

    public double FontSize
    {
        get => (_target as TextBoxAnnotation)?.FontSize ?? 14;
        set => Mutate(a => { if (a is TextBoxAnnotation t) t.FontSize = Math.Clamp(value, 6, 96); },
            Strings.ToolTextBox);
    }

    public bool Bold
    {
        get => (_target as TextBoxAnnotation)?.Bold ?? false;
        set => Mutate(a => { if (a is TextBoxAnnotation t) t.Bold = value; }, Strings.ToolTextBox);
    }

    public TextAlignment Alignment
    {
        get => (_target as TextBoxAnnotation)?.Alignment ?? TextAlignment.Start;
        set => Mutate(a => { if (a is TextBoxAnnotation t) t.Alignment = value; }, Strings.ToolTextBox);
    }

    /// <summary>
    /// Size as width × height, in PDF points.
    /// </summary>
    /// <remarks>
    /// The value is a left to right run in the middle of a right to left panel: the "×" is a neutral
    /// character, so inside a Hebrew paragraph the two numbers swap places on screen. Isolate
    /// characters are not enough here, so the view pins this readout to a left to right flow
    /// direction and the string itself stays plain.
    /// </remarks>
    public string GeometryText => _target is null
        ? string.Empty
        : $"{Math.Round(_target.Rect.Width)} × {Math.Round(_target.Rect.Height)}";

    public string PageText => _target is null
        ? string.Empty
        : ErrorMessages.Format(Strings.PageOf, _target.PageIndex + 1, _document.PageCount);

    public void ApplyColor(AnnotationColor color) => Color = color;

    private void Attach(Annotation? annotation)
    {
        _target = annotation;
        _snapshot = annotation?.Clone();
        RaiseAll(nameof(HasTarget), nameof(IsEditable), nameof(IsForeign), nameof(TitleText),
            nameof(IsTextBox), nameof(SupportsFill), nameof(SupportsLineWidth),
            nameof(Color), nameof(LineWidth), nameof(Opacity), nameof(Text), nameof(FontSize),
            nameof(Bold), nameof(Alignment), nameof(GeometryText), nameof(PageText));
    }

    private void Mutate(Action<Annotation> change, string description)
    {
        if (_target is null || _target.IsForeign || _snapshot is null) return;

        change(_target);
        _target.Touch();
        _document.RecordEdit(_target, _snapshot, description);
        _snapshot = _target.Clone();

        RaiseAll(nameof(Color), nameof(LineWidth), nameof(Opacity), nameof(Text), nameof(FontSize),
            nameof(Bold), nameof(Alignment), nameof(GeometryText));
    }

    /// <summary>Called after a drag or resize so the panel reflects the new geometry.</summary>
    public void RefreshGeometry()
    {
        _snapshot = _target?.Clone();
        RaisePropertyChanged(nameof(GeometryText));
    }

    private static string KindName(AnnotationKind kind) => kind switch
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
}
