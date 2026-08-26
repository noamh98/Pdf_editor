using PdfEditor.Core.Text;

namespace PdfEditor.Core.Annotations;

/// <summary>The kinds of annotation this application can create and re-edit.</summary>
public enum AnnotationKind
{
    TextBox,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Ink,
    Highlight,
    CheckMark,
    CrossMark,
    Signature
}

/// <summary>A point in PDF user space (origin bottom-left, unit = 1/72 inch).</summary>
public readonly record struct PdfPoint(double X, double Y);

/// <summary>A rectangle in PDF user space, normalised so Width/Height are never negative.</summary>
public readonly record struct PdfRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Bottom => Y;
    public double Right => X + Width;
    public double Top => Y + Height;

    public static PdfRect FromCorners(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    public PdfRect Inflate(double amount) =>
        new(X - amount, Y - amount, Width + 2 * amount, Height + 2 * amount);

    public PdfRect Translate(double dx, double dy) => new(X + dx, Y + dy, Width, Height);

    public bool Contains(PdfPoint p) => p.X >= Left && p.X <= Right && p.Y >= Bottom && p.Y <= Top;

    public bool IntersectsWith(PdfRect other) =>
        Left < other.Right && Right > other.Left && Bottom < other.Top && Top > other.Bottom;

    public PdfRect Union(PdfRect other) => FromCorners(
        Math.Min(Left, other.Left), Math.Min(Bottom, other.Bottom),
        Math.Max(Right, other.Right), Math.Max(Top, other.Top));

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>sRGB colour with alpha, independent of any UI framework.</summary>
public readonly record struct AnnotationColor(byte R, byte G, byte B, byte A = 255)
{
    public static AnnotationColor Black => new(0, 0, 0);
    public static AnnotationColor Red => new(211, 47, 47);
    public static AnnotationColor Blue => new(25, 118, 210);
    public static AnnotationColor Green => new(56, 142, 60);
    public static AnnotationColor Yellow => new(255, 214, 0);

    public double Opacity => A / 255.0;

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";

    public static AnnotationColor FromHex(string hex)
    {
        var s = hex.AsSpan().TrimStart('#');
        if (s.Length == 6)
            return new AnnotationColor(
                byte.Parse(s[..2], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(s[2..4], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(s[4..6], System.Globalization.NumberStyles.HexNumber));
        if (s.Length == 8)
            return new AnnotationColor(
                byte.Parse(s[..2], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(s[2..4], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(s[4..6], System.Globalization.NumberStyles.HexNumber),
                byte.Parse(s[6..8], System.Globalization.NumberStyles.HexNumber));
        throw new FormatException($"Unsupported colour literal '{hex}'.");
    }
}

public enum TextAlignment { Start, Center, End }

/// <summary>Base class for everything the user can place on a page and edit again later.</summary>
public abstract class Annotation
{
    protected Annotation(AnnotationKind kind)
    {
        Kind = kind;
        Id = Guid.NewGuid().ToString("N");
        CreatedUtc = DateTimeOffset.UtcNow;
        ModifiedUtc = CreatedUtc;
    }

    public AnnotationKind Kind { get; }

    /// <summary>
    /// Stable identifier, written to the PDF as /NM so the annotation can be matched again when the
    /// document is reopened.
    /// </summary>
    public string Id { get; set; }

    public int PageIndex { get; set; }

    /// <summary>Bounding box in PDF user space.</summary>
    public PdfRect Rect { get; set; }

    public AnnotationColor Color { get; set; } = AnnotationColor.Red;

    public double LineWidth { get; set; } = 2.0;

    /// <summary>0..1. Written to the PDF as /CA.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>Clockwise rotation in degrees applied when the annotation is drawn.</summary>
    public double Rotation { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset ModifiedUtc { get; set; }

    /// <summary>True when this annotation was read from the file and is not one of ours.</summary>
    public bool IsForeign { get; init; }

    public abstract Annotation Clone();

    protected void CopyBaseTo(Annotation target)
    {
        target.Id = Id;
        target.PageIndex = PageIndex;
        target.Rect = Rect;
        target.Color = Color;
        target.LineWidth = LineWidth;
        target.Opacity = Opacity;
        target.Rotation = Rotation;
        target.CreatedUtc = CreatedUtc;
        target.ModifiedUtc = ModifiedUtc;
    }

    public void Touch() => ModifiedUtc = DateTimeOffset.UtcNow;
}

public sealed class TextBoxAnnotation : Annotation
{
    public TextBoxAnnotation() : base(AnnotationKind.TextBox) { }

    public string Text { get; set; } = string.Empty;
    public double FontSize { get; set; } = 14;
    public string FontFamily { get; set; } = "Default";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public AnnotationColor TextColor { get; set; } = AnnotationColor.Black;
    public AnnotationColor? BackgroundColor { get; set; }
    public AnnotationColor? BorderColor { get; set; }
    public TextAlignment Alignment { get; set; } = TextAlignment.Start;
    public double Padding { get; set; } = 4;

    /// <summary>
    /// Base direction used when the text is laid out. <see cref="BidiParagraphDirection.Auto"/>
    /// follows UAX#9 rules P2/P3, which is what a Hebrew user expects by default.
    /// </summary>
    public BidiParagraphDirection Direction { get; set; } = BidiParagraphDirection.Auto;

    public override Annotation Clone()
    {
        var c = new TextBoxAnnotation
        {
            Text = Text,
            FontSize = FontSize,
            FontFamily = FontFamily,
            Bold = Bold,
            Italic = Italic,
            TextColor = TextColor,
            BackgroundColor = BackgroundColor,
            BorderColor = BorderColor,
            Alignment = Alignment,
            Padding = Padding,
            Direction = Direction
        };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class ShapeAnnotation : Annotation
{
    public ShapeAnnotation(AnnotationKind kind) : base(kind)
    {
        if (kind is not (AnnotationKind.Rectangle or AnnotationKind.Ellipse
            or AnnotationKind.Line or AnnotationKind.Arrow or AnnotationKind.Highlight))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a shape annotation kind.");
    }

    public AnnotationColor? FillColor { get; set; }

    /// <summary>Start point for line and arrow annotations, in PDF user space.</summary>
    public PdfPoint Start { get; set; }

    /// <summary>End point for line and arrow annotations, in PDF user space.</summary>
    public PdfPoint End { get; set; }

    public override Annotation Clone()
    {
        var c = new ShapeAnnotation(Kind) { FillColor = FillColor, Start = Start, End = End };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class InkAnnotation : Annotation
{
    public InkAnnotation() : base(AnnotationKind.Ink) { }

    /// <summary>One entry per pen stroke; each stroke is a polyline in PDF user space.</summary>
    public List<List<PdfPoint>> Strokes { get; init; } = [];

    public override Annotation Clone()
    {
        var c = new InkAnnotation();
        foreach (var stroke in Strokes) c.Strokes.Add([.. stroke]);
        CopyBaseTo(c);
        return c;
    }

    /// <summary>Recomputes <see cref="Annotation.Rect"/> from the strokes plus the pen width.</summary>
    public void RecalculateBounds()
    {
        var points = Strokes.SelectMany(s => s).ToList();
        if (points.Count == 0) { Rect = default; return; }
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        Rect = PdfRect.FromCorners(minX, minY, maxX, maxY).Inflate(LineWidth);
    }
}

/// <summary>A V or X mark, drawn as a stamp with an appearance stream we generate.</summary>
public sealed class MarkAnnotation : Annotation
{
    public MarkAnnotation(AnnotationKind kind) : base(kind)
    {
        if (kind is not (AnnotationKind.CheckMark or AnnotationKind.CrossMark))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a mark annotation kind.");
        Color = kind == AnnotationKind.CheckMark ? AnnotationColor.Green : AnnotationColor.Red;
        LineWidth = 3.0;
    }

    public override Annotation Clone()
    {
        var c = new MarkAnnotation(Kind);
        CopyBaseTo(c);
        return c;
    }
}

/// <summary>A graphical signature placed on the page. Never a cryptographic signature.</summary>
public sealed class SignatureAnnotation : Annotation
{
    public SignatureAnnotation() : base(AnnotationKind.Signature) { }

    /// <summary>Identifier of the entry in the local signature library.</summary>
    public string SignatureId { get; set; } = string.Empty;

    /// <summary>PNG bytes with transparency. Held in memory only while the document is open.</summary>
    public byte[] ImagePng { get; set; } = [];

    public override Annotation Clone()
    {
        var c = new SignatureAnnotation
        {
            SignatureId = SignatureId,
            ImagePng = ImagePng
        };
        CopyBaseTo(c);
        return c;
    }
}
