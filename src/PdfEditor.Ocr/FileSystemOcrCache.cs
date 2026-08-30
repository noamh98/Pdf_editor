using System.Text.Json;
using System.Text.Json.Serialization;
using PdfEditor.Core.Files;
using PdfEditor.Core.Ocr;
using PdfEditor.Core.Storage;

namespace PdfEditor.Ocr;

/// <summary>
/// Stores recognition results on disk so a page is not recognised twice.
/// </summary>
/// <remarks>
/// Entries live in <c>%LOCALAPPDATA%\PdfEditor\ocr-cache</c>, a local (never roaming, never
/// synchronised) folder. A file is named only by a hash of the document fingerprint, page, language
/// and resolution, so the directory listing reveals no file name and no document title. Changing a
/// document changes its fingerprint and therefore invalidates every entry for it. The cache can be
/// emptied from the settings screen and old entries are pruned by age.
/// </remarks>
public sealed class FileSystemOcrCache : IOcrCache
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _directory;

    public FileSystemOcrCache(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _directory = paths.OcrCache;
    }

    public FileSystemOcrCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public string Directory => _directory;

    public async Task<OcrPageResult?> TryGetAsync(OcrCacheKey key, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(_directory, key.ToFileName());
        try
        {
            if (!File.Exists(path)) return null;
            await using var stream = File.OpenRead(path);
            var entry = await JsonSerializer.DeserializeAsync<CacheEntry>(stream, Json, cancellationToken)
                .ConfigureAwait(false);
            return entry?.ToResult();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // A damaged cache entry is a performance problem, never a correctness one.
            return null;
        }
    }

    public async Task SetAsync(OcrCacheKey key, OcrPageResult result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        System.IO.Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, key.ToFileName());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(CacheEntry.From(result), Json);
        try
        {
            await AtomicFileWriter.WriteAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Failing to cache must not fail the recognition the user asked for.
        }
    }

    public Task<int> ClearAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => DeleteWhere(_ => true, cancellationToken), cancellationToken);

    public Task<int> PruneAsync(TimeSpan maxAge, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - maxAge;
        return Task.Run(() => DeleteWhere(info => info.LastWriteTimeUtc < cutoff, cancellationToken),
            cancellationToken);
    }

    private int DeleteWhere(Func<FileInfo, bool> predicate, CancellationToken cancellationToken)
    {
        if (!System.IO.Directory.Exists(_directory)) return 0;
        int removed = 0;
        foreach (var path in System.IO.Directory.EnumerateFiles(_directory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var info = new FileInfo(path);
                if (!predicate(info)) continue;
                info.Delete();
                removed++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return removed;
    }

    private sealed class CacheEntry
    {
        public int Page { get; set; }
        public string Text { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public OcrLanguage Language { get; set; }
        public int Dpi { get; set; }
        public List<LineEntry> Lines { get; set; } = [];

        public static CacheEntry From(OcrPageResult r) => new()
        {
            Page = r.PageIndex,
            Text = r.Text,
            Confidence = r.MeanConfidence,
            Language = r.Language,
            Dpi = r.RenderDpi,
            Lines = r.Lines.Select(LineEntry.From).ToList()
        };

        public OcrPageResult ToResult() => new(Page, Text,
            Lines.Select(l => l.ToLine()).ToList(), Confidence, Language, Dpi);
    }

    private sealed class LineEntry
    {
        public string Text { get; set; } = string.Empty;
        public double[] Box { get; set; } = [];
        public float Confidence { get; set; }
        public List<WordEntry> Words { get; set; } = [];

        public static LineEntry From(OcrLine l) => new()
        {
            Text = l.Text,
            Box = [l.Bounds.X, l.Bounds.Y, l.Bounds.Width, l.Bounds.Height],
            Confidence = l.Confidence,
            Words = l.Words.Select(WordEntry.From).ToList()
        };

        public OcrLine ToLine() => new(Text, Rect(Box), Words.Select(w => w.ToWord()).ToList(), Confidence);

        internal static Core.Annotations.PdfRect Rect(double[] box) =>
            box.Length == 4 ? new Core.Annotations.PdfRect(box[0], box[1], box[2], box[3]) : default;
    }

    private sealed class WordEntry
    {
        public string Text { get; set; } = string.Empty;
        public double[] Box { get; set; } = [];
        public float Confidence { get; set; }

        public static WordEntry From(OcrWord w) => new()
        {
            Text = w.Text,
            Box = [w.Bounds.X, w.Bounds.Y, w.Bounds.Width, w.Bounds.Height],
            Confidence = w.Confidence
        };

        public OcrWord ToWord() => new(Text, LineEntry.Rect(Box), Confidence);
    }
}
