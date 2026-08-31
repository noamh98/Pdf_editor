using Avalonia;
using Avalonia.Media;
using PdfEditor.Core.Annotations;
using PdfEditor.Core.Text;
using AvaloniaColor = Avalonia.Media.Color;

namespace PdfEditor.App.Controls;

/// <summary>
/// Draws the annotation model on screen, mirroring what
/// <see cref="PdfEditor.Pdf.Annotations.AnnotationRenderer"/> writes into the PDF.
/// </summary>
/// <remarks>
/// The page bitmap is rendered without annotations and this overlay supplies them, so an edit shows
/// immediately without re-rasterising the page. Annotations produced by other applications cannot
/// be reproduced from their appearance stream here, so they are drawn as a labelled placeholder;
/// the file keeps them untouched and an exported copy renders them for real.
/// </remarks>
internal static class AnnotationOverlay
{
    public static void Draw(DrawingContext context, Annotation annotation, Rect target, double scale)
    {
        if (target.Width <= 0 || target.Height <= 0) return;

        if (annotation.IsForeign)
        {
            DrawForeignPlaceholder(context, annotation, target);
            return;
        }

        var state = context.PushOpacity(Math.Clamp(annotation.Opacity, 0.05, 1.0));
        try
        {
            switch (annotation)
            {
                case TextBoxAnnotation t: DrawTextBox(context, t, target, scale); break;
                case ShapeAnnotation s: DrawShape(context, s, target, scale); break;
                case InkAnnotation i: DrawInk(context, i, target, scale); break;
                case MarkAnnotation m: DrawMark(context, m, target, scale); break;
                case SignatureAnnotation g: DrawSignature(context, g, target); break;
            }
        }
        finally
        {
            state.Dispose();
        }
    }

    private static AvaloniaColor ToAvalonia(AnnotationColor c) => AvaloniaColor.FromArgb(c.A, c.R, c.G, c.B);

    private static IBrush Brush(AnnotationColor c) => new SolidColorBrush(ToAvalonia(c));

    private static Pen Stroke(AnnotationColor c, double width) =>
        new(Brush(c), Math.Max(0.5, width)) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

    /// <summary>
    /// Editor-only hint marking an empty text field. Never written to the PDF, and deliberately a
    /// fixed colour rather than a theme one: it is drawn on the page, which is white paper in both
    /// themes, so a chrome colour picked against a dark panel would be the wrong one here.
    /// </summary>
    private static Pen EmptyFieldGuide(double scale) =>
        new(new SolidColorBrush(AvaloniaColor.FromArgb(150, 122, 132, 148)), Math.Max(1, scale))
        {
            DashStyle = new DashStyle([4, 3], 0)
        };

    private static void DrawTextBox(DrawingContext context, TextBoxAnnotation a, Rect target, double scale)
    {
        if (a.BackgroundColor is { } background) context.FillRectangle(Brush(background), target);
        if (a.BorderColor is { } border && a.LineWidth > 0)
            context.DrawRectangle(null, Stroke(border, a.LineWidth * scale), target.Deflate(a.LineWidth * scale / 2));

        // A text box carries no background or border by default, so an empty one would draw
        // nothing at all and the user would lose a field they had just placed. The editor shows a
        // faint dashed outline instead. It exists only here: AnnotationRenderer writes the PDF and
        // has no such guide, so nothing of it reaches the file.
        if (string.IsNullOrEmpty(a.Text))
        {
            if (a.BackgroundColor is null && a.BorderColor is null)
                context.DrawRectangle(null, EmptyFieldGuide(scale), target);
            return;
        }

        // Text is laid out logically here; Avalonia applies its own bidi when it draws.
        var typeface = new Typeface(FontFamily.Default,
            a.Italic ? FontStyle.Italic : FontStyle.Normal,
            a.Bold ? FontWeight.Bold : FontWeight.Normal);

        var formatted = new FormattedText(a.Text, System.Globalization.CultureInfo.CurrentCulture,
            BidiAlgorithm.Analyze(a.Text, a.Direction).IsRightToLeftParagraph
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight,
            typeface, a.FontSize * scale, Brush(a.TextColor))
        {
            MaxTextWidth = Math.Max(1, target.Width - 2 * a.Padding * scale),
            MaxTextHeight = Math.Max(1, target.Height - 2 * a.Padding * scale),
            TextAlignment = a.Alignment switch
            {
                Core.Annotations.TextAlignment.Center => Avalonia.Media.TextAlignment.Center,
                Core.Annotations.TextAlignment.End => Avalonia.Media.TextAlignment.End,
                _ => Avalonia.Media.TextAlignment.Start
            }
        };

        using (context.PushClip(target))
            context.DrawText(formatted,
                new Point(target.X + a.Padding * scale, target.Y + a.Padding * scale));
    }

    private static void DrawShape(DrawingContext context, ShapeAnnotation a, Rect target, double scale)
    {
        double width = a.LineWidth * scale;
        var pen = Stroke(a.Color, width);
        var fill = a.FillColor is { } f ? Brush(f) : null;
        var inner = target.Deflate(width / 2);

        switch (a.Kind)
        {
            case AnnotationKind.Rectangle:
                context.DrawRectangle(fill, pen, inner);
                break;
            case AnnotationKind.Ellipse:
                context.DrawEllipse(fill, pen, inner.Center, inner.Width / 2, inner.Height / 2);
                break;
            case AnnotationKind.Highlight:
                context.FillRectangle(new SolidColorBrush(ToAvalonia(a.Color), Math.Min(a.Opacity, 0.45)), target);
                break;
            case AnnotationKind.Line:
            case AnnotationKind.Arrow:
                {
                    var (p1, p2) = Endpoints(a, target);
                    context.DrawLine(pen, p1, p2);
                    if (a.Kind == AnnotationKind.Arrow) DrawArrowHead(context, a, p1, p2, width);
                    break;
                }
        }
    }

    private static (Point Start, Point End) Endpoints(ShapeAnnotation a, Rect target)
    {
        double sx = a.Rect.Width <= 0 ? 0 : (a.Start.X - a.Rect.Left) / a.Rect.Width;
        double sy = a.Rect.Height <= 0 ? 0 : (a.Rect.Top - a.Start.Y) / a.Rect.Height;
        double ex = a.Rect.Width <= 0 ? 1 : (a.End.X - a.Rect.Left) / a.Rect.Width;
        double ey = a.Rect.Height <= 0 ? 1 : (a.Rect.Top - a.End.Y) / a.Rect.Height;
        return (new Point(target.X + sx * target.Width, target.Y + sy * target.Height),
                new Point(target.X + ex * target.Width, target.Y + ey * target.Height));
    }

    private static void DrawArrowHead(DrawingContext context, ShapeAnnotation a, Point from, Point to, double width)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        if (Math.Sqrt(dx * dx + dy * dy) < 0.5) return;

        double head = Math.Max(6, width * 3.5);
        double angle = Math.Atan2(dy, dx);
        const double spread = Math.PI / 7;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(to, true);
            ctx.LineTo(new Point(to.X - head * Math.Cos(angle - spread), to.Y - head * Math.Sin(angle - spread)));
            ctx.LineTo(new Point(to.X - head * Math.Cos(angle + spread), to.Y - head * Math.Sin(angle + spread)));
            ctx.EndFigure(true);
        }
        context.DrawGeometry(Brush(a.Color), null, geometry);
    }

    private static void DrawInk(DrawingContext context, InkAnnotation a, Rect target, double scale)
    {
        if (a.Rect.Width <= 0 || a.Rect.Height <= 0) return;
        var pen = Stroke(a.Color, a.LineWidth * scale);

        foreach (var stroke in a.Strokes)
        {
            if (stroke.Count < 2) continue;
            for (int i = 1; i < stroke.Count; i++)
                context.DrawLine(pen, Map(stroke[i - 1], a, target), Map(stroke[i], a, target));
        }
    }

    private static Point Map(PdfPoint p, InkAnnotation a, Rect target) => new(
        target.X + (p.X - a.Rect.Left) / a.Rect.Width * target.Width,
        target.Y + (a.Rect.Top - p.Y) / a.Rect.Height * target.Height);

    private static void DrawMark(DrawingContext context, MarkAnnotation a, Rect target, double scale)
    {
        double width = Math.Max(1, a.LineWidth * scale);
        var pen = Stroke(a.Color, width);
        var inner = target.Deflate(width);

        if (a.Kind == AnnotationKind.CheckMark)
        {
            context.DrawLine(pen,
                new Point(inner.X, inner.Y + inner.Height * 0.55),
                new Point(inner.X + inner.Width * 0.35, inner.Bottom));
            context.DrawLine(pen,
                new Point(inner.X + inner.Width * 0.35, inner.Bottom),
                new Point(inner.Right, inner.Y));
        }
        else
        {
            context.DrawLine(pen, inner.TopLeft, inner.BottomRight);
            context.DrawLine(pen, inner.TopRight, inner.BottomLeft);
        }
    }

    private static void DrawSignature(DrawingContext context, SignatureAnnotation a, Rect target)
    {
        if (a.ImagePng.Length == 0)
        {
            context.DrawRectangle(null, new Pen(Brushes.Gray, 1) { DashStyle = new DashStyle([3, 3], 0) }, target);
            return;
        }
        try
        {
            using var stream = new MemoryStream(a.ImagePng, writable: false);
            using var bitmap = new Avalonia.Media.Imaging.Bitmap(stream);
            double scale = Math.Min(target.Width / bitmap.Size.Width, target.Height / bitmap.Size.Height);
            double w = bitmap.Size.Width * scale, h = bitmap.Size.Height * scale;
            context.DrawImage(bitmap, new Rect(bitmap.Size),
                new Rect(target.X + (target.Width - w) / 2, target.Y + (target.Height - h) / 2, w, h));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException)
        {
            context.DrawRectangle(null, new Pen(Brushes.Gray, 1), target);
        }
    }

    /// <summary>
    /// A third-party annotation. Its real appearance lives in the file and is reproduced on export;
    /// here it is marked so the user can see that something is present and must not be lost.
    /// </summary>
    private static void DrawForeignPlaceholder(DrawingContext context, Annotation annotation, Rect target)
    {
        var pen = new Pen(Brush(annotation.Color), 1.25) { DashStyle = new DashStyle([5, 4], 0) };
        context.DrawRectangle(new SolidColorBrush(ToAvalonia(annotation.Color), 0.06), pen, target);
    }
}
