using PdfEditor.Core.Annotations;

namespace PdfEditor.Ocr;

/// <summary>
/// Converts between the pixel coordinates an OCR engine reports and PDF user space.
/// </summary>
/// <remarks>
/// Image coordinates have their origin at the top-left with y increasing downwards; PDF user space
/// has its origin at the bottom-left with y increasing upwards. Getting this wrong puts every
/// search highlight in the wrong place, so the conversion lives here on its own and is unit tested
/// without any OCR engine present.
/// </remarks>
public static class OcrGeometry
{
    public const double PointsPerInch = 72.0;

    /// <summary>Converts a rectangle in image pixels to PDF user space.</summary>
    public static PdfRect ImageRectToPdfRect(
        int pixelLeft, int pixelTop, int pixelWidth, int pixelHeight,
        int dpi, double pageHeightPoints)
    {
        if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));

        double scale = PointsPerInch / dpi;
        double width = pixelWidth * scale;
        double height = pixelHeight * scale;
        double left = pixelLeft * scale;
        double top = pixelTop * scale;
        double bottom = pageHeightPoints - top - height;
        return new PdfRect(left, bottom, width, height);
    }

    /// <summary>Converts a PDF user-space rectangle back to image pixels at the given resolution.</summary>
    public static (int Left, int Top, int Width, int Height) PdfRectToImageRect(
        PdfRect rect, int dpi, double pageHeightPoints)
    {
        if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));

        double scale = dpi / PointsPerInch;
        int width = (int)Math.Round(rect.Width * scale);
        int height = (int)Math.Round(rect.Height * scale);
        int left = (int)Math.Round(rect.Left * scale);
        int top = (int)Math.Round((pageHeightPoints - rect.Top) * scale);
        return (left, top, width, height);
    }

    /// <summary>Pixel size of a page rendered at <paramref name="dpi"/>.</summary>
    public static (int Width, int Height) PixelSize(double widthPoints, double heightPoints, int dpi)
    {
        if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
        double scale = dpi / PointsPerInch;
        return ((int)Math.Round(widthPoints * scale), (int)Math.Round(heightPoints * scale));
    }

    /// <summary>Smallest rectangle containing all of <paramref name="rects"/>.</summary>
    public static PdfRect Union(IEnumerable<PdfRect> rects)
    {
        PdfRect? result = null;
        foreach (var rect in rects)
        {
            if (rect.Width <= 0 && rect.Height <= 0) continue;
            result = result is { } current ? current.Union(rect) : rect;
        }
        return result ?? default;
    }
}
