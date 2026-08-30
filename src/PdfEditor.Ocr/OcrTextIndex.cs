using PdfEditor.Core.Annotations;
using PdfEditor.Core.Ocr;

namespace PdfEditor.Ocr;

/// <summary>One search hit: where it is and what was matched.</summary>
public sealed record OcrSearchHit(int PageIndex, PdfRect Bounds, string MatchedText, int LineIndex);

/// <summary>
/// Holds recognised pages and answers searches over them.
/// </summary>
/// <remarks>
/// Pure and fully testable: it never touches an OCR engine, the file system or the network.
/// Matching is Hebrew-aware — nikud is ignored and final letter forms are treated as equal to their
/// regular counterparts, so a reader's idea of "the same word" is what actually matches.
/// </remarks>
public sealed class OcrTextIndex
{
    private readonly Dictionary<int, OcrPageResult> _pages = [];

    public IReadOnlyCollection<int> RecognizedPages => _pages.Keys;

    public int Count => _pages.Count;

    public void Add(OcrPageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _pages[result.PageIndex] = result;
    }

    public void Remove(int pageIndex) => _pages.Remove(pageIndex);

    public void Clear() => _pages.Clear();

    public bool Contains(int pageIndex) => _pages.ContainsKey(pageIndex);

    public OcrPageResult? Get(int pageIndex) => _pages.GetValueOrDefault(pageIndex);

    /// <summary>Recognised text of one page, or an empty string when it has not been processed.</summary>
    public string TextOf(int pageIndex) => _pages.GetValueOrDefault(pageIndex)?.Text ?? string.Empty;

    /// <summary>
    /// Finds every occurrence of <paramref name="query"/>, ordered by page and then by line.
    /// </summary>
    public IReadOnlyList<OcrSearchHit> Search(string? query)
    {
        var needle = HebrewTextNormalizer.Normalize(query ?? string.Empty);
        if (needle.Length == 0) return [];

        var hits = new List<OcrSearchHit>();
        foreach (int pageIndex in _pages.Keys.OrderBy(k => k))
        {
            var page = _pages[pageIndex];
            for (int lineIndex = 0; lineIndex < page.Lines.Count; lineIndex++)
            {
                var line = page.Lines[lineIndex];
                foreach (var bounds in MatchesWithinLine(line, needle))
                    hits.Add(new OcrSearchHit(pageIndex, bounds, line.Text, lineIndex));
            }
        }
        return hits;
    }

    /// <summary>
    /// Locates the query inside one line and returns the bounds of the words that cover it.
    /// </summary>
    private static IEnumerable<PdfRect> MatchesWithinLine(OcrLine line, string normalizedNeedle)
    {
        var normalizedLine = HebrewTextNormalizer.Normalize(line.Text);
        if (normalizedLine.Length == 0) yield break;

        int searchFrom = 0;
        while (true)
        {
            int index = normalizedLine.IndexOf(normalizedNeedle, searchFrom, StringComparison.Ordinal);
            if (index < 0) yield break;
            searchFrom = index + 1;

            var covering = WordsCovering(line, normalizedLine, index, normalizedNeedle.Length);
            yield return covering.Count > 0 ? OcrGeometry.Union(covering.Select(w => w.Bounds)) : line.Bounds;
        }
    }

    /// <summary>
    /// Maps a character range in the normalised line back to the words that produced it, by walking
    /// the words in order and tracking how much normalised text each one contributes.
    /// </summary>
    private static List<OcrWord> WordsCovering(OcrLine line, string normalizedLine, int start, int length)
    {
        var result = new List<OcrWord>();
        int end = start + length;
        int cursor = 0;

        foreach (var word in line.Words)
        {
            var normalizedWord = HebrewTextNormalizer.Normalize(word.Text);
            if (normalizedWord.Length == 0) continue;

            int wordStart = normalizedLine.IndexOf(normalizedWord, cursor, StringComparison.Ordinal);
            if (wordStart < 0) continue;
            int wordEnd = wordStart + normalizedWord.Length;
            cursor = wordEnd;

            if (wordStart < end && wordEnd > start) result.Add(word);
        }
        return result;
    }

    /// <summary>
    /// Recognised text inside a rectangle on one page, in reading order, for copy to clipboard.
    /// </summary>
    public string TextWithin(int pageIndex, PdfRect region)
    {
        var page = _pages.GetValueOrDefault(pageIndex);
        if (page is null) return string.Empty;

        var lines = page.Lines
            .Select(line => new
            {
                line.Bounds,
                Words = line.Words.Where(w => w.Bounds.IntersectsWith(region)).ToList()
            })
            .Where(x => x.Words.Count > 0)
            .OrderByDescending(x => x.Bounds.Top)     // PDF space: higher y is earlier in the page
            .Select(x => string.Join(' ', x.Words.Select(w => w.Text)));

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Every recognised page's text, in page order, for a whole-document copy.</summary>
    public string AllText() => string.Join(Environment.NewLine + Environment.NewLine,
        _pages.Keys.OrderBy(k => k).Select(k => _pages[k].Text));
}
