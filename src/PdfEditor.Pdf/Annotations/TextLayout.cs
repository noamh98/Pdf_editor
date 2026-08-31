using PdfEditor.Core.Text;
using PdfSharp.Drawing;

namespace PdfEditor.Pdf.Annotations;

/// <summary>One laid-out line: the visual-order string plus where to draw it.</summary>
public sealed record LaidOutLine(string VisualText, double X, double Baseline, double Width);

/// <summary>
/// Wraps and orders text for drawing into a PDF.
/// </summary>
/// <remarks>
/// Two things make this necessary. PDF text operators place glyphs in the order supplied, so every
/// line has to be converted from logical to visual order with the bidirectional algorithm. And an
/// annotation's appearance stream is clipped to its bounding box, so the text must be measured and
/// wrapped rather than drawn at a fixed offset.
/// </remarks>
public static class TextLayout
{
    /// <summary>
    /// Lays out <paramref name="text"/> inside a box of the given size, in the coordinate space of
    /// an <see cref="XGraphics"/> whose origin is the top-left corner of the box.
    /// </summary>
    public static IReadOnlyList<LaidOutLine> Layout(
        XGraphics gfx,
        string text,
        XFont font,
        double boxWidth,
        double boxHeight,
        double padding,
        Core.Annotations.TextAlignment alignment,
        BidiParagraphDirection direction)
    {
        ArgumentNullException.ThrowIfNull(gfx);
        ArgumentNullException.ThrowIfNull(font);

        double available = boxWidth - 2 * padding;
        if (available <= 0 || string.IsNullOrEmpty(text)) return [];

        var lineHeight = font.GetHeight();
        var result = new List<LaidOutLine>();
        double y = padding;

        foreach (var paragraph in SplitParagraphs(text))
        {
            var analysis = BidiAlgorithm.Analyze(paragraph, direction);
            bool rtl = analysis.IsRightToLeftParagraph;

            // The base direction belongs to the paragraph, not to the line. UAX#9 resolves it once
            // (P2/P3) and reorders every line of the paragraph at that level, so the resolved
            // direction is passed down explicitly; asking for Auto again per line lets a line that
            // happens to begin with a Latin word read left to right inside a Hebrew paragraph.
            var lineDirection = rtl
                ? BidiParagraphDirection.RightToLeft
                : BidiParagraphDirection.LeftToRight;

            foreach (var logicalLine in WrapToWidth(gfx, paragraph, font, available))
            {
                if (y + lineHeight > boxHeight - padding && result.Count > 0) return result;

                var visual = BidiAlgorithm.ToVisual(logicalLine, lineDirection);
                double width = gfx.MeasureString(visual, font).Width;
                double x = ResolveX(alignment, rtl, padding, available, width);
                result.Add(new LaidOutLine(visual, x, y + font.GetHeight() * 0.8, width));
                y += lineHeight;
            }
        }
        return result;
    }

    private static double ResolveX(
        Core.Annotations.TextAlignment alignment, bool rtl, double padding, double available, double width)
    {
        // "Start" and "End" are relative to the paragraph direction, which is what a Hebrew user
        // means by "aligned to the beginning of the line".
        var effective = alignment switch
        {
            Core.Annotations.TextAlignment.Center => Side.Center,
            Core.Annotations.TextAlignment.Start => rtl ? Side.Right : Side.Left,
            _ => rtl ? Side.Left : Side.Right
        };
        return effective switch
        {
            Side.Left => padding,
            Side.Center => padding + (available - width) / 2,
            _ => padding + available - width
        };
    }

    private enum Side { Left, Center, Right }

    /// <summary>Greedy word wrap. Falls back to breaking inside a word that cannot fit alone.</summary>
    public static IReadOnlyList<string> WrapToWidth(XGraphics gfx, string paragraph, XFont font, double maxWidth)
    {
        if (paragraph.Length == 0) return [string.Empty];
        var lines = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var word in SplitWords(paragraph))
        {
            var candidate = current.Length == 0 ? word : current + word;
            if (gfx.MeasureString(candidate.TrimEnd(), font).Width <= maxWidth)
            {
                current.Append(word);
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current.ToString().TrimEnd());
                current.Clear();
            }

            if (gfx.MeasureString(word.TrimEnd(), font).Width <= maxWidth)
            {
                current.Append(word);
                continue;
            }

            foreach (var piece in BreakLongWord(gfx, word, font, maxWidth)) lines.Add(piece);
        }

        if (current.Length > 0) lines.Add(current.ToString().TrimEnd());
        return lines.Count == 0 ? [string.Empty] : lines;
    }

    private static IEnumerable<string> BreakLongWord(XGraphics gfx, string word, XFont font, double maxWidth)
    {
        int start = 0;
        while (start < word.Length)
        {
            int length = 1;
            while (start + length < word.Length &&
                   gfx.MeasureString(word.Substring(start, length + 1), font).Width <= maxWidth)
                length++;
            yield return word.Substring(start, length);
            start += length;
        }
    }

    /// <summary>Splits into words, keeping the trailing space attached so widths stay accurate.</summary>
    private static IEnumerable<string> SplitWords(string paragraph)
    {
        int i = 0;
        while (i < paragraph.Length)
        {
            int start = i;
            while (i < paragraph.Length && !char.IsWhiteSpace(paragraph[i])) i++;
            while (i < paragraph.Length && char.IsWhiteSpace(paragraph[i])) i++;
            yield return paragraph[start..i];
        }
    }

    public static IEnumerable<string> SplitParagraphs(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// <summary>Total height the text needs, used to grow a text box to fit its content.</summary>
    public static double MeasureHeight(XGraphics gfx, string text, XFont font, double boxWidth, double padding)
    {
        double available = boxWidth - 2 * padding;
        if (available <= 0) return 2 * padding;
        int lines = SplitParagraphs(text).Sum(p => WrapToWidth(gfx, p, font, available).Count);
        return lines * font.GetHeight() + 2 * padding;
    }
}
