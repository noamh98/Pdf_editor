using PdfEditor.Core.Documents;
using PdfEditor.Core.Ocr;

namespace PdfEditor.Ocr;

/// <summary>
/// Runs recognition over a document: renders each page, consults the cache, calls the engine, and
/// feeds the results into a searchable index.
/// </summary>
/// <remarks>
/// Recognition never modifies the document. Results live in the index and the local cache only,
/// which is what keeps version 1's promise that OCR cannot change a PDF.
/// </remarks>
public sealed class OcrService(IOcrEngine engine, IOcrCache cache)
{
    private readonly IOcrEngine _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    private readonly IOcrCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));

    public OcrTextIndex Index { get; } = new();

    public bool IsAvailable => _engine.IsAvailable;

    public string? UnavailableReason => _engine.UnavailableReason;

    /// <summary>Recognises one page, returning the cached result when there is one.</summary>
    public async Task<OcrPageResult> RecognizePageAsync(
        IPdfDocument document, int pageIndex, OcrLanguage language, int dpi,
        bool useCache, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (pageIndex < 0 || pageIndex >= document.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageIndex));

        var key = new OcrCacheKey(document.Fingerprint, pageIndex, language, dpi);
        if (useCache)
        {
            var cached = await _cache.TryGetAsync(key, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                Index.Add(cached);
                return cached;
            }
        }

        if (!_engine.IsAvailable) throw new InvalidOperationException(_engine.UnavailableReason);

        var page = document.Pages[pageIndex];
        var png = await document.RenderToPngAsync(pageIndex, dpi, cancellationToken).ConfigureAwait(false);
        var result = await _engine.RecognizeAsync(png, pageIndex, page.WidthPoints, page.HeightPoints,
            dpi, language, cancellationToken).ConfigureAwait(false);

        Index.Add(result);
        if (useCache) await _cache.SetAsync(key, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Recognises a set of pages in order, reporting progress after each one and stopping promptly
    /// when cancelled.
    /// </summary>
    public async Task<IReadOnlyList<OcrPageResult>> RecognizePagesAsync(
        IPdfDocument document, IReadOnlyList<int> pageIndices, OcrLanguage language, int dpi,
        bool useCache, IProgress<OcrProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(pageIndices);

        var results = new List<OcrPageResult>(pageIndices.Count);
        for (int i = 0; i < pageIndices.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int pageIndex = pageIndices[i];
            progress?.Report(new OcrProgress(i, pageIndices.Count, pageIndex));

            var result = await RecognizePageAsync(document, pageIndex, language, dpi, useCache, cancellationToken)
                .ConfigureAwait(false);
            results.Add(result);
        }
        progress?.Report(new OcrProgress(pageIndices.Count, pageIndices.Count, -1));
        return results;
    }

    public IReadOnlyList<OcrSearchHit> Search(string? query) => Index.Search(query);

    public Task<int> ClearCacheAsync(CancellationToken cancellationToken = default) =>
        _cache.ClearAsync(cancellationToken);

    public Task<int> PruneCacheAsync(TimeSpan maxAge, CancellationToken cancellationToken = default) =>
        _cache.PruneAsync(maxAge, cancellationToken);
}
