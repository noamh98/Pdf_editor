using PdfEditor.Core.Annotations;
using PdfEditor.Core.Ocr;
using PdfEditor.Ocr;
using Xunit;

namespace PdfEditor.Ocr.Tests;

public class OcrTextIndexTests
{
    private static OcrWord Word(string text, double x, double y, double w = 40, double h = 14) =>
        new(text, new PdfRect(x, y, w, h), 92f);

    private static OcrLine Line(params OcrWord[] words)
    {
        var text = string.Join(' ', words.Select(w => w.Text));
        return new OcrLine(text, OcrGeometry.Union(words.Select(w => w.Bounds)), words, 92f);
    }

    private static OcrPageResult Page(int index, params OcrLine[] lines) =>
        new(index, string.Join('\n', lines.Select(l => l.Text)), lines, 92f, OcrLanguage.HebrewAndEnglish, 300);

    private static OcrTextIndex BuildIndex()
    {
        var index = new OcrTextIndex();
        index.Add(Page(0,
            Line(Word("שלום", 400, 700), Word("עולם", 340, 700)),
            Line(Word("הקובץ", 400, 660), Word("file.pdf", 320, 660), Word("נשמר", 250, 660))));
        index.Add(Page(2,
            Line(Word("שלום", 400, 700), Word("שוב", 350, 700)),
            Line(Word("Hello", 100, 660), Word("world", 160, 660))));
        return index;
    }

    [Fact]
    public void FindsAHebrewWordOnEveryPageThatContainsIt()
    {
        var hits = BuildIndex().Search("שלום");

        Assert.Equal(2, hits.Count);
        Assert.Equal([0, 2], hits.Select(h => h.PageIndex).ToArray());
    }

    [Fact]
    public void SearchIsInsensitiveToNikud()
    {
        Assert.Single(BuildIndex().Search("שָׁלוֹם").Where(h => h.PageIndex == 0));
    }

    [Fact]
    public void SearchTreatsFinalLettersAsEquivalent()
    {
        var index = new OcrTextIndex();
        index.Add(Page(0, Line(Word("מים", 400, 700))));

        Assert.NotEmpty(index.Search("מימ"));
    }

    [Fact]
    public void SearchIsCaseInsensitiveForLatinText()
    {
        Assert.NotEmpty(BuildIndex().Search("HELLO"));
    }

    [Fact]
    public void FindsALatinFileNameEmbeddedInHebrewText()
    {
        var hit = Assert.Single(BuildIndex().Search("file.pdf"));
        Assert.Equal(0, hit.PageIndex);
    }

    [Fact]
    public void AMatchSpanningTwoWordsReturnsTheirCombinedBounds()
    {
        var hit = Assert.Single(BuildIndex().Search("שלום עולם"));

        Assert.Equal(340, hit.Bounds.Left, 1);
        Assert.Equal(440, hit.Bounds.Right, 1);
    }

    [Fact]
    public void AMissingTermReturnsNothing()
    {
        Assert.Empty(BuildIndex().Search("מילהשאיננה"));
    }

    [Fact]
    public void AnEmptyQueryReturnsNothing()
    {
        Assert.Empty(BuildIndex().Search(""));
        Assert.Empty(BuildIndex().Search("   "));
        Assert.Empty(BuildIndex().Search(null));
    }

    [Fact]
    public void ResultsAreOrderedByPage()
    {
        var pages = BuildIndex().Search("שלום").Select(h => h.PageIndex).ToList();
        Assert.Equal(pages.OrderBy(p => p), pages);
    }

    [Fact]
    public void ReportsWhichPagesHaveBeenRecognised()
    {
        var index = BuildIndex();

        Assert.Equal(2, index.Count);
        Assert.True(index.Contains(0));
        Assert.False(index.Contains(1));
        Assert.Equal([0, 2], index.RecognizedPages.OrderBy(p => p).ToArray());
    }

    [Fact]
    public void RemovingAndClearingDropTheStoredPages()
    {
        var index = BuildIndex();
        index.Remove(0);
        Assert.False(index.Contains(0));

        index.Clear();
        Assert.Equal(0, index.Count);
        Assert.Empty(index.Search("שלום"));
    }

    [Fact]
    public void RecognisingAPageAgainReplacesTheEarlierResult()
    {
        var index = BuildIndex();
        index.Add(Page(0, Line(Word("חדש", 400, 700))));

        Assert.Empty(index.Search("עולם"));
        Assert.NotEmpty(index.Search("חדש"));
    }

    [Fact]
    public void ExtractsTheTextInsideARectangleForCopying()
    {
        var text = BuildIndex().TextWithin(0, new PdfRect(300, 650, 200, 80));

        Assert.Contains("שלום", text);
        Assert.Contains("file.pdf", text);
        Assert.DoesNotContain("נשמר", text);   // that word sits outside the rectangle
    }

    [Fact]
    public void ExtractedTextIsOrderedFromTheTopOfThePageDown()
    {
        var text = BuildIndex().TextWithin(0, new PdfRect(0, 600, 600, 200));
        Assert.True(text.IndexOf("שלום", StringComparison.Ordinal)
                  < text.IndexOf("הקובץ", StringComparison.Ordinal));
    }

    [Fact]
    public void ExtractingFromAnUnrecognisedPageReturnsNothing()
    {
        Assert.Equal(string.Empty, BuildIndex().TextWithin(5, new PdfRect(0, 0, 999, 999)));
    }

    [Fact]
    public void AllTextConcatenatesThePagesInOrder()
    {
        var all = BuildIndex().AllText();
        Assert.True(all.IndexOf("עולם", StringComparison.Ordinal)
                  < all.IndexOf("Hello", StringComparison.Ordinal));
    }
}

public class FileSystemOcrCacheTests
{
    private static OcrPageResult SampleResult(int page = 0) => new(
        page, "שלום עולם",
        [new OcrLine("שלום עולם", new PdfRect(300, 700, 140, 16),
            [new OcrWord("שלום", new PdfRect(380, 700, 60, 16), 95f)], 95f)],
        95f, OcrLanguage.HebrewAndEnglish, 300);

    private sealed class TempDir : IDisposable
    {
        public TempDir() => Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "pdfeditor-ocr-tests", Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
            catch (IOException) { }
        }
    }

    [Fact]
    public async Task StoresAndReadsBackAResult()
    {
        using var dir = new TempDir();
        var cache = new FileSystemOcrCache(dir.Path);
        var key = new OcrCacheKey("fingerprint", 0, OcrLanguage.HebrewAndEnglish, 300);

        await cache.SetAsync(key, SampleResult(), TestCancellation.Token);
        var restored = await cache.TryGetAsync(key, TestCancellation.Token);

        Assert.NotNull(restored);
        Assert.Equal("שלום עולם", restored!.Text);
        Assert.Equal(300, restored.RenderDpi);
        var line = Assert.Single(restored.Lines);
        Assert.Equal(380, Assert.Single(line.Words).Bounds.Left, 3);
    }

    [Fact]
    public async Task AMissingEntryReturnsNull()
    {
        using var dir = new TempDir();
        var cache = new FileSystemOcrCache(dir.Path);

        Assert.Null(await cache.TryGetAsync(
            new OcrCacheKey("nothing", 0, OcrLanguage.Hebrew, 300), TestCancellation.Token));
    }

    [Fact]
    public async Task ADifferentFingerprintDoesNotHitTheSameEntry()
    {
        using var dir = new TempDir();
        var cache = new FileSystemOcrCache(dir.Path);

        await cache.SetAsync(new OcrCacheKey("a", 0, OcrLanguage.Hebrew, 300), SampleResult(), TestCancellation.Token);

        Assert.Null(await cache.TryGetAsync(
            new OcrCacheKey("b", 0, OcrLanguage.Hebrew, 300), TestCancellation.Token));
    }

    [Fact]
    public async Task TheStoredFileNameRevealsNothingAboutTheDocument()
    {
        using var dir = new TempDir();
        var cache = new FileSystemOcrCache(dir.Path);
        await cache.SetAsync(new OcrCacheKey("secret-document-fingerprint", 3, OcrLanguage.Hebrew, 300),
            SampleResult(3), TestCancellation.Token);

        var file = Path.GetFileName(Assert.Single(Directory.GetFiles(dir.Path)));

        Assert.EndsWith(".json", file, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", file, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, file);
        Assert.Matches("^[0-9a-f]{64}\\.json$", file);
    }

    [Fact]
    public async Task ClearRemovesEveryEntry()
    {
        using var dir = new TempDir();
        var cache = new FileSystemOcrCache(dir.Path);
        for (int i = 0; i < 3; i++)
            await cache.SetAsync(new OcrCacheKey("f", i, OcrLanguage.Hebrew, 300), SampleResult(i),
                TestCancellation.Token);

        Assert.Equal(3, await cache.ClearAsync(TestCancellation.Token));
        Assert.Empty(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task PruneRemovesOnlyEntriesOlderThanTheLimit()
    {
        using var dir = new TempDir();
        var cache = new FileSystemOcrCache(dir.Path);
        await cache.SetAsync(new OcrCacheKey("old", 0, OcrLanguage.Hebrew, 300), SampleResult(),
            TestCancellation.Token);
        var old = Directory.GetFiles(dir.Path).Single();
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));

        await cache.SetAsync(new OcrCacheKey("new", 1, OcrLanguage.Hebrew, 300), SampleResult(1),
            TestCancellation.Token);

        Assert.Equal(1, await cache.PruneAsync(TimeSpan.FromDays(30), TestCancellation.Token));
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task ADamagedEntryIsTreatedAsAMissOnly()
    {
        using var dir = new TempDir();
        var cache = new FileSystemOcrCache(dir.Path);
        var key = new OcrCacheKey("f", 0, OcrLanguage.Hebrew, 300);
        await cache.SetAsync(key, SampleResult(), TestCancellation.Token);

        await File.WriteAllTextAsync(Directory.GetFiles(dir.Path).Single(), "{ this is not json",
            TestCancellation.Token);

        Assert.Null(await cache.TryGetAsync(key, TestCancellation.Token));
    }

    [Fact]
    public async Task ClearingAnEmptyOrMissingDirectoryIsHarmless()
    {
        using var dir = new TempDir();
        var cache = new FileSystemOcrCache(dir.Path);
        Assert.Equal(0, await cache.ClearAsync(TestCancellation.Token));
    }
}

public class OcrEngineAvailabilityTests
{
    [Fact]
    public void TheTesseractEngineReportsWhyItCannotRunInsteadOfThrowing()
    {
        using var engine = new TesseractOcrEngine(Path.Combine(Path.GetTempPath(), "no-tessdata-here"));

        Assert.False(engine.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(engine.UnavailableReason));
        Assert.Contains("זיהוי הטקסט", engine.UnavailableReason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnavailableEngineRefusesToRecogniseRatherThanFailingOpaquely()
    {
        using var engine = new TesseractOcrEngine(Path.Combine(Path.GetTempPath(), "no-tessdata-here"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RecognizeAsync(
            [1, 2, 3], 0, 595, 842, 300, OcrLanguage.Hebrew, TestCancellation.Token));
    }

    [Theory]
    [InlineData(OcrLanguage.Hebrew, "heb")]
    [InlineData(OcrLanguage.English, "eng")]
    [InlineData(OcrLanguage.HebrewAndEnglish, "heb+eng")]
    public void MapsEachLanguageToItsTesseractCode(OcrLanguage language, string expected)
    {
        Assert.Equal(expected, TesseractOcrEngine.LanguageCode(language));
    }

    [Fact]
    public void TheNullEngineIsNeverAvailableAndSaysSoInHebrew()
    {
        using var engine = new NullOcrEngine();

        Assert.False(engine.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(engine.UnavailableReason));
        Assert.Empty(engine.SupportedLanguages);
    }

    [Fact]
    public async Task TheNullEngineFailsTheCallRatherThanReturningEmptyResults()
    {
        using var engine = new NullOcrEngine();

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.RecognizeAsync(
            [], 0, 595, 842, 300, OcrLanguage.Hebrew, TestCancellation.Token));
    }
}

internal static class TestCancellation
{
    public static CancellationToken Token => new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;
}
