using PdfEditor.Core.Signatures;
using SkiaSharp;

namespace PdfEditor.Platform.Signatures;

/// <summary>
/// Cleans up a drawn or imported signature: optional white-background removal, transparent-margin
/// cropping, and re-encoding as PNG with an alpha channel.
/// </summary>
/// <remarks>
/// Cropping matters for placement. A signature photographed or scanned with wide margins would
/// otherwise be positioned by its whitespace rather than by the ink, and would appear to float away
/// from where the user dropped it.
/// </remarks>
public sealed class SignatureImageProcessor : ISignatureImageProcessor
{
    /// <summary>How close to white a pixel must be to count as background.</summary>
    public int WhiteThreshold { get; init; } = 235;

    /// <summary>Alpha at or below which a pixel counts as empty when cropping.</summary>
    public byte TransparencyThreshold { get; init; } = 8;

    /// <summary>Transparent pixels kept around the ink, so strokes are not clipped at the edge.</summary>
    public int Margin { get; init; } = 2;

    public byte[] Normalize(byte[] imageBytes, bool removeWhiteBackground, out int width, out int height)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0) throw new ArgumentException("The image is empty.", nameof(imageBytes));

        // An imported signature is untrusted input: the decoder throws several different exception
        // types for malformed data, and all of them mean the same thing to the caller.
        using var decoded = Decode(imageBytes);

        using var rgba = ToRgba(decoded);
        if (removeWhiteBackground) MakeWhiteTransparent(rgba, WhiteThreshold);

        var bounds = FindInkBounds(rgba, TransparencyThreshold);
        if (bounds.IsEmpty)
        {
            width = 0;
            height = 0;
            throw new ArgumentException("The image contains no visible ink.", nameof(imageBytes));
        }

        bounds.Inflate(Margin, Margin);
        bounds = SKRectI.Intersect(bounds, new SKRectI(0, 0, rgba.Width, rgba.Height));

        using var cropped = new SKBitmap(bounds.Width, bounds.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        if (!rgba.ExtractSubset(cropped, bounds))
            throw new InvalidOperationException("The signature could not be cropped.");

        width = cropped.Width;
        height = cropped.Height;

        using var image = SKImage.FromBitmap(cropped);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKBitmap Decode(byte[] imageBytes)
    {
        SKBitmap? bitmap;
        try
        {
            bitmap = SKBitmap.Decode(imageBytes);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IndexOutOfRangeException)
        {
            throw new ArgumentException("The image could not be decoded.", nameof(imageBytes), e);
        }
        return bitmap ?? throw new ArgumentException("The image could not be decoded.", nameof(imageBytes));
    }

    private static SKBitmap ToRgba(SKBitmap source)
    {
        var target = new SKBitmap(source.Width, source.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(target);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, 0, 0);
        return target;
    }

    private static void MakeWhiteTransparent(SKBitmap bitmap, int threshold)
    {
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Alpha == 0) continue;
                if (pixel.Red >= threshold && pixel.Green >= threshold && pixel.Blue >= threshold)
                    bitmap.SetPixel(x, y, SKColors.Transparent);
            }
        }
    }

    private static SKRectI FindInkBounds(SKBitmap bitmap, byte alphaThreshold)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha <= alphaThreshold) continue;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }
        return maxX < 0 ? SKRectI.Empty : new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }
}
