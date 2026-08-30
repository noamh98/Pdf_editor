using PdfEditor.Core.Annotations;
using PdfEditor.Pdf.Fonts;
using PdfSharp.Drawing;

namespace PdfEditor.Pdf.Annotations;

/// <summary>
/// Draws an annotation with <see cref="XGraphics"/>.
/// </summary>
/// <remarks>
/// This is deliberately the only place annotation appearance is defined. The same routine produces
/// the appearance stream stored in an editable PDF and the ink burned into the page during export,
/// so a flattened document looks exactly like the editable one did.
/// </remarks>
public static class AnnotationRenderer
{
    /// <summary>
    /// Draws <paramref name="annotation"/> into <paramref name="target"/>, a rectangle expressed in
    /// the current <see cref="XGraphics"/> coordinate space (origin top-left, y increasing down).
    /// </summary>
    public static void Draw(XGraphics gfx, Annotation annotation, XRect target)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        ArgumentNullException.ThrowIfNull(annotation);
        if (target.Width <= 0 || target.Height <= 0) return;

        var state = gfx.Save();
        try
        {
            if (Math.Abs(annotation.Rotation) > 0.01)
            {
                gfx.TranslateTransform(target.X + target.Width / 2, target.Y + target.Height / 2);
                gfx.RotateTransform(annotation.Rotation);
                gfx.TranslateTransform(-(target.X + target.Width / 2), -(target.Y + target.Height / 2));
            }

            switch (annotation)
            {
                case TextBoxAnnotation t: DrawTextBox(gfx, t, target); break;
                case ShapeAnnotation s: DrawShape(gfx, s, target); break;
                case InkAnnotation i: DrawInk(gfx, i, target); break;
                case MarkAnnotation m: DrawMark(gfx, m, target); break;
                case SignatureAnnotation g: DrawSignature(gfx, g, target); break;
            }
        }
        finally
        {
            gfx.Restore(state);
        }
    }

    private static XColor ToX(AnnotationColor c, double opacity = 1.0) =>
        XColor.FromArgb((int)Math.Clamp(c.A * opacity, 0, 255), c.R, c.G, c.B);

    private static void DrawTextBox(XGraphics gfx, TextBoxAnnotation a, XRect target)
    {
        if (a.BackgroundColor is { } bg)
            gfx.DrawRectangle(new XSolidBrush(ToX(bg, a.Opacity)), target);

        if (a.BorderColor is { } border && a.LineWidth > 0)
        {
            double inset = a.LineWidth / 2;
            gfx.DrawRectangle(new XPen(ToX(border, a.Opacity), a.LineWidth),
                new XRect(target.X + inset, target.Y + inset,
                    Math.Max(0, target.Width - a.LineWidth), Math.Max(0, target.Height - a.LineWidth)));
        }

        if (string.IsNullOrEmpty(a.Text) || !PdfFonts.IsAvailable) return;

        var font = PdfFonts.Create(a.FontSize, a.Bold, a.Italic);
        var brush = new XSolidBrush(ToX(a.TextColor, a.Opacity));
        var lines = TextLayout.Layout(gfx, a.Text, font, target.Width, target.Height,
            a.Padding, a.Alignment, a.Direction);

        var clip = gfx.Save();
        try
        {
            gfx.IntersectClip(target);
            foreach (var line in lines)
                gfx.DrawString(line.VisualText, font, brush,
                    new XPoint(target.X + line.X, target.Y + line.Baseline));
        }
        finally
        {
            gfx.Restore(clip);
        }
    }

    private static void DrawShape(XGraphics gfx, ShapeAnnotation a, XRect target)
    {
        var pen = new XPen(ToX(a.Color, a.Opacity), Math.Max(0.1, a.LineWidth));
        XBrush? fill = a.FillColor is { } f ? new XSolidBrush(ToX(f, a.Opacity)) : null;
        double inset = a.LineWidth / 2;
        var inner = new XRect(target.X + inset, target.Y + inset,
            Math.Max(0, target.Width - a.LineWidth), Math.Max(0, target.Height - a.LineWidth));

        switch (a.Kind)
        {
            case AnnotationKind.Rectangle:
                if (fill is not null) gfx.DrawRectangle(fill, inner);
                if (a.LineWidth > 0) gfx.DrawRectangle(pen, inner);
                break;

            case AnnotationKind.Ellipse:
                if (fill is not null) gfx.DrawEllipse(fill, inner);
                if (a.LineWidth > 0) gfx.DrawEllipse(pen, inner);
                break;

            case AnnotationKind.Highlight:
                // Highlight is a translucent wash; it must never hide the text underneath.
                var wash = new XSolidBrush(ToX(a.Color, Math.Min(a.Opacity, 0.45)));
                gfx.DrawRectangle(wash, target);
                break;

            case AnnotationKind.Line:
            case AnnotationKind.Arrow:
                var (p1, p2) = LocalEndpoints(a, target);
                gfx.DrawLine(pen, p1, p2);
                if (a.Kind == AnnotationKind.Arrow) DrawArrowHead(gfx, pen, p1, p2, a.LineWidth);
                break;
        }
    }

    private static (XPoint Start, XPoint End) LocalEndpoints(ShapeAnnotation a, XRect target)
    {
        // Endpoints are stored in PDF user space; map them into the target box, flipping y.
        double sx = a.Rect.Width <= 0 ? 0 : (a.Start.X - a.Rect.Left) / a.Rect.Width;
        double sy = a.Rect.Height <= 0 ? 0 : (a.Rect.Top - a.Start.Y) / a.Rect.Height;
        double ex = a.Rect.Width <= 0 ? 1 : (a.End.X - a.Rect.Left) / a.Rect.Width;
        double ey = a.Rect.Height <= 0 ? 1 : (a.Rect.Top - a.End.Y) / a.Rect.Height;
        return (new XPoint(target.X + sx * target.Width, target.Y + sy * target.Height),
                new XPoint(target.X + ex * target.Width, target.Y + ey * target.Height));
    }

    private static void DrawArrowHead(XGraphics gfx, XPen pen, XPoint from, XPoint to, double lineWidth)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 0.001) return;

        double head = Math.Max(6, lineWidth * 3.5);
        double angle = Math.Atan2(dy, dx);
        const double spread = Math.PI / 7;

        var left = new XPoint(to.X - head * Math.Cos(angle - spread), to.Y - head * Math.Sin(angle - spread));
        var right = new XPoint(to.X - head * Math.Cos(angle + spread), to.Y - head * Math.Sin(angle + spread));
        gfx.DrawPolygon(new XSolidBrush(pen.Color), [to, left, right], XFillMode.Winding);
    }

    private static void DrawInk(XGraphics gfx, InkAnnotation a, XRect target)
    {
        if (a.Rect.Width <= 0 || a.Rect.Height <= 0) return;
        var pen = new XPen(ToX(a.Color, a.Opacity), Math.Max(0.1, a.LineWidth))
        {
            LineCap = XLineCap.Round,
            LineJoin = XLineJoin.Round
        };

        foreach (var stroke in a.Strokes)
        {
            if (stroke.Count == 0) continue;
            var points = stroke.Select(p => new XPoint(
                target.X + (p.X - a.Rect.Left) / a.Rect.Width * target.Width,
                target.Y + (a.Rect.Top - p.Y) / a.Rect.Height * target.Height)).ToArray();

            if (points.Length == 1)
            {
                double r = Math.Max(0.25, a.LineWidth / 2);
                gfx.DrawEllipse(new XSolidBrush(pen.Color),
                    points[0].X - r, points[0].Y - r, r * 2, r * 2);
                continue;
            }
            gfx.DrawLines(pen, points);
        }
    }

    private static void DrawMark(XGraphics gfx, MarkAnnotation a, XRect target)
    {
        double w = Math.Max(0.1, a.LineWidth);
        var pen = new XPen(ToX(a.Color, a.Opacity), w) { LineCap = XLineCap.Round, LineJoin = XLineJoin.Round };
        double pad = w;
        double x = target.X + pad, y = target.Y + pad;
        double cw = Math.Max(0, target.Width - 2 * pad), ch = Math.Max(0, target.Height - 2 * pad);

        if (a.Kind == AnnotationKind.CheckMark)
        {
            // A tick: down to the low point at roughly a third of the width, then up to the right.
            gfx.DrawLines(pen,
            [
                new XPoint(x, y + ch * 0.55),
                new XPoint(x + cw * 0.35, y + ch),
                new XPoint(x + cw, y)
            ]);
        }
        else
        {
            gfx.DrawLine(pen, x, y, x + cw, y + ch);
            gfx.DrawLine(pen, x + cw, y, x, y + ch);
        }
    }

    private static void DrawSignature(XGraphics gfx, SignatureAnnotation a, XRect target)
    {
        if (a.ImagePng.Length == 0) return;
        using var stream = new MemoryStream(a.ImagePng, writable: false);
        XImage image;
        try { image = XImage.FromStream(stream); }
        catch (Exception e) when (e is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return; // A signature we cannot decode must not break the whole save.
        }

        using (image)
        {
            // Preserve aspect ratio and centre inside the placed box.
            double scale = Math.Min(target.Width / image.PixelWidth, target.Height / image.PixelHeight);
            double w = image.PixelWidth * scale, h = image.PixelHeight * scale;
            gfx.DrawImage(image, target.X + (target.Width - w) / 2, target.Y + (target.Height - h) / 2, w, h);
        }
    }
}
