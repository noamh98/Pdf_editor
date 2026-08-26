using PdfEditor.Core.Annotations;

namespace PdfEditor.Core.Ocr;

public enum OcrLanguage
{
    Hebrew,
    English,
    HebrewAndEnglish
}

/// <summary>One recognised word with its position on the page, in PDF user space.</summary>
public sealed record OcrWord(string Text, PdfRect Bounds, float Confidence);

/// <summary>One recognised line: the words plus the reading-order text.</summary>
public sealed record OcrLine(string Text, PdfRect Bounds, IReadOnlyList<OcrWord> Words, float Confidence);

/// <summary>Recognition result for a single page.</summary>
public sealed record OcrPageResult(
    int PageIndex,
    string Text,
    IReadOnlyList<OcrLine> Lines,
    float MeanConfidence,
    OcrLanguage Language,
    int RenderDpi)
{
    public IEnumerable<OcrWord> Words => Lines.SelectMany(l => l.Words);
    public static OcrPageResult Empty(int pageIndex, OcrLanguage language, int dpi) =>
        new(pageIndex, string.Empty, [], 0f, language, dpi);
}

/// <summary>Progress report for a multi-page recognition run.</summary>
public sealed record OcrProgress(int CompletedPages, int TotalPages, int CurrentPageIndex)
{
    public double Fraction => TotalPages <= 0 ? 0 : (double)CompletedPages / TotalPages;
}

/// <summary>
/// A local, offline OCR engine. Implementations must never contact the network.
/// </summary>
public interface IOcrEngine : IDisposable
{
    /// <summary>False when the language data required by the engine is not present.</summary>
    bool IsAvailable { get; }

    /// <summary>Human-readable reason the engine cannot run, in Hebrew, or null when it can.</summary>
    string? UnavailableReason { get; }

    IReadOnlyList<OcrLanguage> SupportedLanguages { get; }

    /// <summary>
    /// Recognises one already-rasterised page.
    /// </summary>
    /// <param name="pageImagePng">The page rendered to PNG at <paramref name="dpi"/>.</param>
    /// <param name="pageIndex">Zero-based page index, echoed back in the result.</param>
    /// <param name="pageWidthPoints">Page width in PDF points, used to map boxes back to user space.</param>
    /// <param name="pageHeightPoints">Page height in PDF points.</param>
    Task<OcrPageResult> RecognizeAsync(
        byte[] pageImagePng,
        int pageIndex,
        double pageWidthPoints,
        double pageHeightPoints,
        int dpi,
        OcrLanguage language,
        CancellationToken cancellationToken);
}

/// <summary>Persisted OCR results, keyed so that a changed document invalidates the entry.</summary>
public interface IOcrCache
{
    Task<OcrPageResult?> TryGetAsync(OcrCacheKey key, CancellationToken cancellationToken = default);
    Task SetAsync(OcrCacheKey key, OcrPageResult result, CancellationToken cancellationToken = default);
    Task<int> ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes entries older than <paramref name="maxAge"/>. Returns how many were removed.</summary>
    Task<int> PruneAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
}
