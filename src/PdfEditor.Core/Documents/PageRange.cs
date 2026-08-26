using System.Globalization;
using System.Text;

namespace PdfEditor.Core.Documents;

/// <summary>A closed, 1-based page interval.</summary>
public readonly record struct PageRange(int Start, int End)
{
    public int Count => End - Start + 1;

    public bool Contains(int pageNumber) => pageNumber >= Start && pageNumber <= End;

    public override string ToString() => Start == End
        ? Start.ToString(CultureInfo.InvariantCulture)
        : $"{Start}-{End}";
}

/// <summary>Why a page-range expression could not be parsed. Used to pick the Hebrew message.</summary>
public enum PageRangeError
{
    None = 0,
    Empty,
    InvalidCharacter,
    MalformedRange,
    NotANumber,
    ZeroOrNegative,
    ReversedRange,
    OutOfBounds
}

public sealed record PageRangeParseResult(
    bool Success,
    IReadOnlyList<PageRange> Ranges,
    PageRangeError Error,
    string? Offending)
{
    public static PageRangeParseResult Ok(IReadOnlyList<PageRange> ranges) =>
        new(true, ranges, PageRangeError.None, null);

    public static PageRangeParseResult Fail(PageRangeError error, string? offending = null) =>
        new(false, [], error, offending);

    /// <summary>Every page number covered by the parsed ranges, ascending and de-duplicated.</summary>
    public IReadOnlyList<int> ToPageNumbers()
    {
        var set = new SortedSet<int>();
        foreach (var r in Ranges)
            for (int p = r.Start; p <= r.End; p++)
                set.Add(p);
        return set.ToArray();
    }
}

/// <summary>
/// Parses page-range expressions such as <c>1-3,5,8-10</c>.
/// </summary>
/// <remarks>
/// Accepts ASCII and Arabic-Indic digits, ASCII hyphen plus the common Unicode dashes, and both
/// comma and Hebrew-keyboard comma-like separators. Whitespace is ignored. The parser never throws;
/// callers turn <see cref="PageRangeError"/> into a localized message.
/// </remarks>
public static class PageRangeParser
{
    private const char Hyphen = '-';

    public static PageRangeParseResult Parse(string? input, int pageCount)
    {
        if (pageCount <= 0) return PageRangeParseResult.Fail(PageRangeError.OutOfBounds);
        if (string.IsNullOrWhiteSpace(input)) return PageRangeParseResult.Fail(PageRangeError.Empty);

        var normalized = Normalize(input);
        var parts = normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return PageRangeParseResult.Fail(PageRangeError.Empty);

        var ranges = new List<PageRange>();
        foreach (var part in parts)
        {
            foreach (char c in part)
            {
                if (!char.IsAsciiDigit(c) && c != Hyphen)
                    return PageRangeParseResult.Fail(PageRangeError.InvalidCharacter, part);
            }

            int dash = part.IndexOf(Hyphen);
            if (dash < 0)
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int single))
                    return PageRangeParseResult.Fail(PageRangeError.NotANumber, part);
                if (single <= 0) return PageRangeParseResult.Fail(PageRangeError.ZeroOrNegative, part);
                if (single > pageCount) return PageRangeParseResult.Fail(PageRangeError.OutOfBounds, part);
                ranges.Add(new PageRange(single, single));
                continue;
            }

            if (part.IndexOf(Hyphen, dash + 1) >= 0 || dash == 0 || dash == part.Length - 1)
                return PageRangeParseResult.Fail(PageRangeError.MalformedRange, part);

            var left = part[..dash];
            var right = part[(dash + 1)..];
            if (!int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out int start) ||
                !int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out int end))
                return PageRangeParseResult.Fail(PageRangeError.NotANumber, part);
            if (start <= 0 || end <= 0) return PageRangeParseResult.Fail(PageRangeError.ZeroOrNegative, part);
            if (start > end) return PageRangeParseResult.Fail(PageRangeError.ReversedRange, part);
            if (end > pageCount) return PageRangeParseResult.Fail(PageRangeError.OutOfBounds, part);
            ranges.Add(new PageRange(start, end));
        }

        return PageRangeParseResult.Ok(Merge(ranges));
    }

    /// <summary>Collapses overlapping and adjacent ranges so the result is minimal and ordered.</summary>
    public static IReadOnlyList<PageRange> Merge(IEnumerable<PageRange> ranges)
    {
        var sorted = ranges.OrderBy(r => r.Start).ThenBy(r => r.End).ToList();
        var result = new List<PageRange>();
        foreach (var r in sorted)
        {
            if (result.Count > 0 && r.Start <= result[^1].End + 1)
            {
                var last = result[^1];
                result[^1] = new PageRange(last.Start, Math.Max(last.End, r.End));
            }
            else result.Add(r);
        }
        return result;
    }

    /// <summary>Renders a set of page numbers back into the compact <c>1-3,5</c> form.</summary>
    public static string Format(IEnumerable<int> pageNumbers)
    {
        var ordered = new SortedSet<int>(pageNumbers).ToArray();
        if (ordered.Length == 0) return string.Empty;
        var sb = new StringBuilder();
        int i = 0;
        while (i < ordered.Length)
        {
            int start = ordered[i];
            int end = start;
            while (i + 1 < ordered.Length && ordered[i + 1] == end + 1) { i++; end = ordered[i]; }
            if (sb.Length > 0) sb.Append(',');
            sb.Append(start.ToString(CultureInfo.InvariantCulture));
            if (end != start) sb.Append(Hyphen).Append(end.ToString(CultureInfo.InvariantCulture));
            i++;
        }
        return sb.ToString();
    }

    private static string Normalize(string input)
    {
        var sb = new StringBuilder(input.Length);
        foreach (char raw in input)
        {
            char c = raw switch
            {
                '‐' or '‑' or '‒' or '–' or '—' or '―'
                    or '−' or '－' or '־' => Hyphen,   // dashes incl. Hebrew maqaf
                '،' or '，' or '؛' or ';' => ',',       // comma-like separators
                '‎' or '‏' or '‪' or '‫' or '‬'
                    or '⁦' or '⁧' or '⁨' or '⁩' => '\0', // strip bidi marks
                _ => c2(raw)
            };
            if (c == '\0') continue;
            if (char.IsWhiteSpace(c)) continue;
            sb.Append(c);
        }
        return sb.ToString();

        static char c2(char raw)
        {
            // Arabic-Indic and Extended Arabic-Indic digits -> ASCII
            if (raw >= '٠' && raw <= '٩') return (char)('0' + (raw - '٠'));
            if (raw >= '۰' && raw <= '۹') return (char)('0' + (raw - '۰'));
            if (raw >= '０' && raw <= '９') return (char)('0' + (raw - '０'));
            return raw;
        }
    }
}
