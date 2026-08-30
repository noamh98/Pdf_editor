using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Pdf.Documents;
using Xunit;

namespace PdfEditor.Pdf.Tests;

public class DocumentLifecycleTests
{
    /// <summary>Tests must never hang the suite; every awaited operation carries this budget.</summary>
    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    private static readonly PdfDocumentLoader Loader = new();
    private static readonly PdfDocumentWriter Writer = new();

    // ---- opening ---------------------------------------------------------------------------
    [Fact]
    public async Task OpensAGeneratedDocumentAndReportsItsPages()
    {
        using var work = new TempWorkspace();
        var path = work.Write("base.pdf", PdfFixtures.TextDocument(3, landscapeSecondPage: true));

        await using var doc = await Loader.OpenAsync(path, Ct);

        Assert.Equal(3, doc.PageCount);
        Assert.Equal(path, doc.SourcePath);
        Assert.NotEmpty(doc.Fingerprint);
        Assert.False(doc.IsProtected);
        Assert.Equal(Core.Printing.PageOrientationKind.Portrait, doc.Pages[0].Orientation);
        Assert.Equal(Core.Printing.PageOrientationKind.Landscape, doc.Pages[1].Orientation);
    }

    [Theory]
    [InlineData("missing.pdf", PdfOpenError.FileNotFound)]
    public async Task ReportsFileNotFound(string name, PdfOpenError expected)
    {
        using var work = new TempWorkspace();
        var error = await Assert.ThrowsAsync<PdfOpenException>(
            () => Loader.OpenAsync(work.File(name), Ct));
        Assert.Equal(expected, error.Error);
    }

    [Fact]
    public async Task RejectsAFileThatIsNotAPdf()
    {
        using var work = new TempWorkspace();
        var path = work.Write("notes.pdf", PdfFixtures.NotAPdf());
        var error = await Assert.ThrowsAsync<PdfOpenException>(
            () => Loader.OpenAsync(path, Ct));
        Assert.Equal(PdfOpenError.NotAPdf, error.Error);
    }

    [Fact]
    public async Task RejectsACorruptedFileWithoutThrowingSomethingUnexpected()
    {
        using var work = new TempWorkspace();
        var path = work.Write("broken.pdf", PdfFixtures.Malformed());
        var error = await Assert.ThrowsAsync<PdfOpenException>(
            () => Loader.OpenAsync(path, Ct));
        Assert.True(error.Error is PdfOpenError.Corrupted or PdfOpenError.NotAPdf);
    }

    [Fact]
    public async Task RejectsAnEmptyFile()
    {
        using var work = new TempWorkspace();
        var path = work.Write("empty.pdf", []);
        var error = await Assert.ThrowsAsync<PdfOpenException>(
            () => Loader.OpenAsync(path, Ct));
        Assert.Equal(PdfOpenError.NotAPdf, error.Error);
    }

    // ---- rendering -------------------------------------------------------------------------
    [Fact]
    public async Task RendersAPageToBitmapPixels()
    {
        using var work = new TempWorkspace();
        var path = work.Write("base.pdf", PdfFixtures.TextDocument(1));
        await using var doc = await Loader.OpenAsync(path, Ct);

        var page = await doc.RenderAsync(new RenderRequest(0, 1.0), Ct);

        Assert.Equal(0, page.PageIndex);
        Assert.True(page.PixelWidth > 500, $"width was {page.PixelWidth}");
        Assert.Equal(page.PixelWidth * page.PixelHeight * 4, page.BgraPixels.Length);
        Assert.Contains(page.BgraPixels, b => b != 0xFF);   // something other than white was drawn
    }

    [Fact]
    public async Task RenderScaleChangesTheOutputSize()
    {
        using var work = new TempWorkspace();
        var path = work.Write("base.pdf", PdfFixtures.TextDocument(1));
        await using var doc = await Loader.OpenAsync(path, Ct);

        var small = await doc.RenderAsync(new RenderRequest(0, 0.5), Ct);
        var large = await doc.RenderAsync(new RenderRequest(0, 1.5), Ct);

        Assert.True(large.PixelWidth > small.PixelWidth * 2);
    }

    [Fact]
    public async Task RenderHonoursAMaximumPixelWidth()
    {
        using var work = new TempWorkspace();
        var path = work.Write("base.pdf", PdfFixtures.TextDocument(1));
        await using var doc = await Loader.OpenAsync(path, Ct);

        var page = await doc.RenderAsync(new RenderRequest(0, 8.0, MaxPixelWidth: 400),
            Ct);

        Assert.True(page.PixelWidth <= 420, $"width was {page.PixelWidth}");
    }

    [Fact]
    public async Task RenderRejectsAPageIndexOutsideTheDocument()
    {
        using var work = new TempWorkspace();
        var path = work.Write("base.pdf", PdfFixtures.TextDocument(2));
        await using var doc = await Loader.OpenAsync(path, Ct);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => doc.RenderAsync(new RenderRequest(5, 1.0), Ct));
    }

    [Fact]
    public async Task RenderCanBeCancelled()
    {
        using var work = new TempWorkspace();
        var path = work.Write("large.pdf", PdfFixtures.Large(20));
        await using var doc = await Loader.OpenAsync(path, Ct);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => doc.RenderAsync(new RenderRequest(0, 2.0), cts.Token));
    }

    // ---- blank detection -------------------------------------------------------------------
    [Fact]
    public async Task DetectsBlankAndNonBlankPages()
    {
        using var work = new TempWorkspace();
        var path = work.Write("blanks.pdf", PdfFixtures.WithBlankPages());
        await using var doc = await Loader.OpenAsync(path, Ct);

        Assert.False(await doc.IsPageBlankAsync(0, Ct));
        Assert.True(await doc.IsPageBlankAsync(1, Ct));
        Assert.False(await doc.IsPageBlankAsync(2, Ct));
        Assert.True(await doc.IsPageBlankAsync(3, Ct));
    }

    // ---- source safety ---------------------------------------------------------------------
    [Fact]
    public async Task SavingToANewPathLeavesTheSourceByteIdentical()
    {
        using var work = new TempWorkspace();
        var sourcePath = work.Write("source.pdf", PdfFixtures.TextDocument(2));
        var before = TempWorkspace.Sha256(sourcePath);

        await using var doc = await Loader.OpenAsync(sourcePath, Ct);
        var target = work.File("copy.pdf");
        await Writer.SaveAsync(doc,
            new SaveRequest(target, SaveMode.Editable, [SampleTextBox(0)]),
            null, Ct);

        Assert.Equal(before, TempWorkspace.Sha256(sourcePath));
        Assert.True(new FileInfo(target).Length > 0);
    }

    [Fact]
    public async Task ExportingAFlattenedCopyLeavesTheSourceByteIdentical()
    {
        using var work = new TempWorkspace();
        var sourcePath = work.Write("source.pdf", PdfFixtures.TextDocument(2));
        var before = TempWorkspace.Sha256(sourcePath);

        await using var doc = await Loader.OpenAsync(sourcePath, Ct);
        await Writer.SaveAsync(doc,
            new SaveRequest(work.File("flat.pdf"), SaveMode.Flattened, [SampleTextBox(0)]),
            null, Ct);

        Assert.Equal(before, TempWorkspace.Sha256(sourcePath));
    }

    [Fact]
    public async Task AFailedSaveLeavesAnExistingTargetUntouched()
    {
        using var work = new TempWorkspace();
        var sourcePath = work.Write("source.pdf", PdfFixtures.TextDocument(1));
        var target = work.Write("target.pdf", "original content"u8.ToArray());
        var targetBefore = TempWorkspace.Sha256(target);

        await using var doc = await Loader.OpenAsync(sourcePath, Ct);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Writer.SaveAsync(doc,
            new SaveRequest(target, SaveMode.Editable, []), null, cts.Token));

        Assert.Equal(targetBefore, TempWorkspace.Sha256(target));
        Assert.Empty(Directory.GetFiles(work.Root, "*.pdfeditor-tmp"));
    }

    internal static TextBoxAnnotation SampleTextBox(int pageIndex) => new()
    {
        PageIndex = pageIndex,
        Rect = new PdfRect(60, 480, 260, 70),
        Text = "הערה בעברית ABC 42",
        FontSize = 14,
        BackgroundColor = new AnnotationColor(255, 249, 196),
        BorderColor = new AnnotationColor(245, 176, 65),
        TextColor = AnnotationColor.Black
    };
}
