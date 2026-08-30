using PdfSharp.Drawing;
using PdfSharp.Fonts;

namespace PdfEditor.Pdf.Fonts;

/// <summary>
/// Owns the single global PDFsharp font resolver and hands out <see cref="XFont"/> instances.
/// </summary>
/// <remarks>
/// PDFsharp exposes the font resolver as process-global state, so registration happens exactly once
/// behind a lock. Callers never touch <see cref="GlobalFontSettings"/> directly.
/// </remarks>
public static class PdfFonts
{
    /// <summary>The family name every <see cref="XFont"/> is created with; the resolver maps it.</summary>
    public const string FamilyName = "PdfEditor";

    private static readonly object Gate = new();
    private static BundledFontResolver? _resolver;

    /// <summary>Registers the resolver. Safe to call repeatedly and from several threads.</summary>
    public static void EnsureRegistered(string? fontDirectory = null)
    {
        if (_resolver is not null) return;
        lock (Gate)
        {
            if (_resolver is not null) return;
            var resolver = new BundledFontResolver(fontDirectory);
            GlobalFontSettings.FontResolver = resolver;
            _resolver = resolver;
        }
    }

    /// <summary>True when the resolver found font data and text can be embedded.</summary>
    public static bool IsAvailable
    {
        get
        {
            EnsureRegistered();
            return _resolver?.HasFontData == true;
        }
    }

    public static XFont Create(double size, bool bold = false, bool italic = false)
    {
        EnsureRegistered();
        var style = XFontStyleEx.Regular;
        if (bold) style |= XFontStyleEx.Bold;
        if (italic) style |= XFontStyleEx.Italic;
        return new XFont(FamilyName, size, style);
    }
}
