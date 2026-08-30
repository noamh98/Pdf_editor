using PdfEditor.Core.Localization;
using PdfEditor.Core.Ocr;
using Tesseract;

namespace PdfEditor.Ocr;

/// <summary>
/// Local, offline text recognition backed by Tesseract 5.
/// </summary>
/// <remarks>
/// Nothing here contacts the network: the engine loads language data from a <c>tessdata</c> folder
/// shipped with the application. The <c>Tesseract</c> NuGet package supplies native binaries for
/// Windows only, so on any other platform — including the Linux machines this project is developed
/// and tested on — the engine reports itself unavailable instead of throwing, and the pure parts of
/// the OCR pipeline stay testable.
/// </remarks>
public sealed class TesseractOcrEngine : IOcrEngine
{
    private readonly string _tessdataDirectory;
    private readonly object _gate = new();
    private readonly Dictionary<OcrLanguage, TesseractEngine> _engines = [];
    private bool _disposed;

    public TesseractOcrEngine(string? tessdataDirectory = null)
    {
        _tessdataDirectory = tessdataDirectory ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
        UnavailableReason = DetermineUnavailableReason(_tessdataDirectory);
    }

    public string TessdataDirectory => _tessdataDirectory;

    public bool IsAvailable => UnavailableReason is null;

    public string? UnavailableReason { get; }

    public IReadOnlyList<OcrLanguage> SupportedLanguages =>
        [OcrLanguage.Hebrew, OcrLanguage.English, OcrLanguage.HebrewAndEnglish];

    /// <summary>How much of the page the engine is told to treat as a single block of text.</summary>
    public PageSegMode SegmentationMode { get; init; } = PageSegMode.SingleBlock;

    private static string? DetermineUnavailableReason(string directory)
    {
        if (!OperatingSystem.IsWindows())
            return Strings.OcrNotAvailable + " (Tesseract ships native binaries for Windows only)";
        if (!Directory.Exists(directory))
            return Strings.OcrNotAvailable;
        foreach (var file in new[] { "heb.traineddata", "eng.traineddata" })
            if (!File.Exists(Path.Combine(directory, file)))
                return Strings.OcrNotAvailable;
        return null;
    }

    /// <summary>The Tesseract language code used for a given language selection.</summary>
    public static string LanguageCode(OcrLanguage language) => language switch
    {
        OcrLanguage.Hebrew => "heb",
        OcrLanguage.English => "eng",
        _ => "heb+eng"
    };

    public async Task<OcrPageResult> RecognizeAsync(
        byte[] pageImagePng, int pageIndex, double pageWidthPoints, double pageHeightPoints,
        int dpi, OcrLanguage language, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pageImagePng);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable) throw new InvalidOperationException(UnavailableReason);
        if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));
        if (pageImagePng.Length == 0) return OcrPageResult.Empty(pageIndex, language, dpi);

        return await Task.Run(() =>
            Recognize(pageImagePng, pageIndex, pageHeightPoints, dpi, language, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private OcrPageResult Recognize(byte[] png, int pageIndex, double pageHeightPoints, int dpi,
        OcrLanguage language, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var engine = GetEngine(language);
        using var image = Pix.LoadFromMemory(png);

        // The engine object is shared per language, so one page is processed at a time.
        lock (engine)
        {
            using var page = engine.Process(image, SegmentationMode);
            using var iterator = page.GetIterator();
            iterator.Begin();

            var lines = new List<OcrLine>();
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = ReadLine(iterator, dpi, pageHeightPoints);
                if (line is not null) lines.Add(line);
            }
            while (iterator.Next(PageIteratorLevel.TextLine));

            var text = string.Join(Environment.NewLine, lines.Select(l => l.Text));
            float confidence = lines.Count == 0 ? 0 : lines.Average(l => l.Confidence);
            return new OcrPageResult(pageIndex, text, lines, confidence, language, dpi);
        }
    }

    private static OcrLine? ReadLine(ResultIterator iterator, int dpi, double pageHeightPoints)
    {
        var lineText = iterator.GetText(PageIteratorLevel.TextLine);
        if (string.IsNullOrWhiteSpace(lineText)) return null;

        var lineBounds = ToPdfRect(iterator, PageIteratorLevel.TextLine, dpi, pageHeightPoints);
        var words = new List<OcrWord>();

        // Walk the words that belong to this line before the outer loop advances past it.
        do
        {
            var wordText = iterator.GetText(PageIteratorLevel.Word);
            if (!string.IsNullOrWhiteSpace(wordText))
                words.Add(new OcrWord(
                    wordText.Trim(),
                    ToPdfRect(iterator, PageIteratorLevel.Word, dpi, pageHeightPoints),
                    iterator.GetConfidence(PageIteratorLevel.Word)));
        }
        while (iterator.Next(PageIteratorLevel.TextLine, PageIteratorLevel.Word));

        return new OcrLine(lineText.TrimEnd('\n', '\r'), lineBounds, words,
            iterator.GetConfidence(PageIteratorLevel.TextLine));
    }

    private static Core.Annotations.PdfRect ToPdfRect(
        ResultIterator iterator, PageIteratorLevel level, int dpi, double pageHeightPoints)
    {
        if (!iterator.TryGetBoundingBox(level, out var box)) return default;
        return OcrGeometry.ImageRectToPdfRect(box.X1, box.Y1, box.Width, box.Height, dpi, pageHeightPoints);
    }

    private TesseractEngine GetEngine(OcrLanguage language)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_engines.TryGetValue(language, out var existing)) return existing;

            var engine = new TesseractEngine(_tessdataDirectory, LanguageCode(language), EngineMode.LstmOnly);
            // Recognised text must never be written to a log file.
            engine.SetVariable("debug_file", OperatingSystem.IsWindows() ? "NUL" : "/dev/null");
            _engines[language] = engine;
            return engine;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var engine in _engines.Values) engine.Dispose();
            _engines.Clear();
        }
    }
}

/// <summary>
/// Stands in when no recognition engine can run, so the rest of the application has something to
/// bind to and can explain the situation in Hebrew rather than failing unexpectedly.
/// </summary>
public sealed class NullOcrEngine : IOcrEngine
{
    public bool IsAvailable => false;

    public string UnavailableReason { get; init; } = Strings.OcrNotAvailable;

    public IReadOnlyList<OcrLanguage> SupportedLanguages => [];

    string? IOcrEngine.UnavailableReason => UnavailableReason;

    public Task<OcrPageResult> RecognizeAsync(byte[] pageImagePng, int pageIndex,
        double pageWidthPoints, double pageHeightPoints, int dpi, OcrLanguage language,
        CancellationToken cancellationToken) =>
        Task.FromException<OcrPageResult>(new InvalidOperationException(UnavailableReason));

    public void Dispose() { }
}
