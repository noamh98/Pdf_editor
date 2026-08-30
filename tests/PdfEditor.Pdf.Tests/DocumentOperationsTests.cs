using PdfEditor.Core.Documents;
using PdfEditor.Core.Printing;
using PdfEditor.Pdf.Documents;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfEditor.Pdf.Tests;

public class DocumentOperationsTests
{
    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromMinutes(3)).Token;
    private static readonly PdfDocumentLoader Loader = new();
    private static readonly PdfDocumentWriter Writer = new();
    private static readonly DocumentAssembler Assembler = new();

    // ---- merge -----------------------------------------------------------------------------
    [Fact]
    public async Task MergeConcatenatesEveryPageInOrder()
    {
        using var work = new TempWorkspace();
        var a = work.Write("a.pdf", PdfFixtures.TextDocument(3));
        var b = work.Write("b.pdf", PdfFixtures.TextDocument(2));
        var target = work.File("merged.pdf");

        await Assembler.MergeAsync([new MergeSource(a), new MergeSource(b)], target, null, Ct);

        await using var merged = await Loader.OpenAsync(target, Ct);
        Assert.Equal(5, merged.PageCount);
    }

    [Fact]
    public async Task MergeCanTakeASubsetOfPages()
    {
        using var work = new TempWorkspace();
        var a = work.Write("a.pdf", PdfFixtures.TextDocument(4));
        var target = work.File("merged.pdf");

        await Assembler.MergeAsync([new MergeSource(a, [0, 2])], target, null, Ct);

        await using var merged = await Loader.OpenAsync(target, Ct);
        Assert.Equal(2, merged.PageCount);
    }

    [Fact]
    public async Task MergeLeavesEverySourceFileByteIdentical()
    {
        using var work = new TempWorkspace();
        var a = work.Write("a.pdf", PdfFixtures.TextDocument(2));
        var b = work.Write("b.pdf", PdfFixtures.MixedPageSizes());
        var beforeA = TempWorkspace.Sha256(a);
        var beforeB = TempWorkspace.Sha256(b);

        await Assembler.MergeAsync([new MergeSource(a), new MergeSource(b)], work.File("m.pdf"), null, Ct);

        Assert.Equal(beforeA, TempWorkspace.Sha256(a));
        Assert.Equal(beforeB, TempWorkspace.Sha256(b));
    }

    [Fact]
    public async Task MergePreservesDifferentPageSizesAndRotation()
    {
        using var work = new TempWorkspace();
        var mixed = work.Write("mixed.pdf", PdfFixtures.MixedPageSizes());
        var target = work.File("merged.pdf");

        await Assembler.MergeAsync([new MergeSource(mixed)], target, null, Ct);

        await using var merged = await Loader.OpenAsync(target, Ct);
        Assert.Equal(4, merged.PageCount);
        Assert.Equal(612, merged.Pages[1].WidthPoints, 0);
        Assert.Equal(PageOrientationKind.Landscape, merged.Pages[2].Orientation);
        Assert.Equal(90, merged.Pages[3].Rotation);
    }

    [Fact]
    public async Task MergeReportsProgressAndReachesOne()
    {
        using var work = new TempWorkspace();
        var a = work.Write("a.pdf", PdfFixtures.TextDocument(2));
        var reported = new List<double>();

        await Assembler.MergeAsync([new MergeSource(a)], work.File("m.pdf"),
            new Progress<double>(reported.Add), Ct);

        await Task.Delay(50, Ct);   // Progress<T> marshals through the synchronization context
        Assert.Contains(reported, v => v >= 1.0);
    }

    [Fact]
    public async Task MergeRejectsACorruptedInput()
    {
        using var work = new TempWorkspace();
        var good = work.Write("good.pdf", PdfFixtures.TextDocument(1));
        var bad = work.Write("bad.pdf", PdfFixtures.Malformed());

        var error = await Assert.ThrowsAsync<PdfOpenException>(() =>
            Assembler.MergeAsync([new MergeSource(good), new MergeSource(bad)], work.File("m.pdf"), null, Ct));
        Assert.True(error.Error is PdfOpenError.Corrupted or PdfOpenError.NotAPdf);
    }

    [Fact]
    public async Task MergeCanBeCancelled()
    {
        using var work = new TempWorkspace();
        var big = work.Write("big.pdf", PdfFixtures.Large(120));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Assembler.MergeAsync([new MergeSource(big)], work.File("m.pdf"), null, cts.Token));
        Assert.False(File.Exists(work.File("m.pdf")));
    }

    // ---- split -----------------------------------------------------------------------------
    [Fact]
    public async Task SplitOnePerPageWritesOneFilePerPage()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(4));
        var outDir = work.File("out");

        var files = await Assembler.SplitAsync(
            new SplitRequest(source, outDir, SplitMode.OnePerPage), null, Ct);

        Assert.Equal(4, files.Count);
        foreach (var file in files)
        {
            await using var single = await Loader.OpenAsync(file, Ct);
            Assert.Equal(1, single.PageCount);
        }
    }

    [Fact]
    public async Task SplitExtractsTheRequestedRanges()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(8));
        var outDir = work.File("out");

        var files = await Assembler.SplitAsync(
            new SplitRequest(source, outDir, SplitMode.ExtractRanges,
                [new PageRange(1, 3), new PageRange(6, 6)]),
            null, Ct);

        var extracted = Assert.Single(files);
        await using var doc = await Loader.OpenAsync(extracted, Ct);
        Assert.Equal(4, doc.PageCount);
    }

    [Fact]
    public async Task SplitLeavesTheSourceByteIdentical()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(3));
        var before = TempWorkspace.Sha256(source);

        await Assembler.SplitAsync(new SplitRequest(source, work.File("out"), SplitMode.OnePerPage), null, Ct);

        Assert.Equal(before, TempWorkspace.Sha256(source));
    }

    [Fact]
    public async Task SplitWritesEveryFileInsideTheRequestedDirectory()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(2));
        var outDir = Path.GetFullPath(work.File("out"));

        var files = await Assembler.SplitAsync(
            new SplitRequest(source, outDir, SplitMode.OnePerPage), null, Ct);

        Assert.All(files, f => Assert.StartsWith(outDir, Path.GetFullPath(f), StringComparison.Ordinal));
    }

    [Fact]
    public async Task SplitRejectsARangeThatSelectsNothing()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(2));

        await Assert.ThrowsAsync<InvalidOperationException>(() => Assembler.SplitAsync(
            new SplitRequest(source, work.File("out"), SplitMode.ExtractRanges, [new PageRange(50, 60)]),
            null, Ct));
    }

    // ---- page edits ------------------------------------------------------------------------
    [Fact]
    public async Task DeletingPagesRemovesThemFromTheOutput()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(5));
        var target = work.File("edited.pdf");

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, [],
                [new PageEdit.Delete(1), new PageEdit.Delete(3)]), null, Ct);

        await using var edited = await Loader.OpenAsync(target, Ct);
        Assert.Equal(3, edited.PageCount);
    }

    [Fact]
    public async Task RotatingAPageIsWrittenToTheOutput()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(2));
        var target = work.File("rotated.pdf");

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, [],
                [new PageEdit.Rotate(0, 90)]), null, Ct);

        await using var rotated = await Loader.OpenAsync(target, Ct);
        Assert.Equal(90, rotated.Pages[0].Rotation);
        Assert.Equal(0, rotated.Pages[1].Rotation);
        Assert.Equal(PageOrientationKind.Landscape, rotated.Pages[0].Orientation);
    }

    [Fact]
    public async Task RotationWrapsAroundInsteadOfGrowing()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(1));
        var target = work.File("rotated.pdf");

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, [],
                [new PageEdit.Rotate(0, 270), new PageEdit.Rotate(0, 180)]), null, Ct);

        await using var rotated = await Loader.OpenAsync(target, Ct);
        Assert.Equal(90, rotated.Pages[0].Rotation);
    }

    [Fact]
    public async Task ReorderingRearrangesThePages()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.MixedPageSizes());
        var target = work.File("reordered.pdf");

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, [],
                [new PageEdit.Reorder([2, 0, 1, 3])]), null, Ct);

        await using var reordered = await Loader.OpenAsync(target, Ct);
        Assert.Equal(4, reordered.PageCount);
        // Page 2 of the source was the landscape one and is now first.
        Assert.Equal(PageOrientationKind.Landscape, reordered.Pages[0].Orientation);
        Assert.Equal(612, reordered.Pages[2].WidthPoints, 0);
    }

    [Fact]
    public async Task AnnotationsFollowTheirPageThroughAReorder()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(3));
        var target = work.File("reordered.pdf");
        var annotation = DocumentLifecycleTests.SampleTextBox(2);

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, [annotation],
                [new PageEdit.Reorder([2, 1, 0])]), null, Ct);

        await using var reordered = await Loader.OpenAsync(target, Ct);
        var restored = Assert.Single(reordered.LoadAnnotations());
        Assert.Equal(0, restored.PageIndex);
    }

    [Fact]
    public async Task AnnotationsOnDeletedPagesAreDropped()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(3));
        var target = work.File("edited.pdf");

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable,
                [DocumentLifecycleTests.SampleTextBox(1)], [new PageEdit.Delete(1)]), null, Ct);

        await using var edited = await Loader.OpenAsync(target, Ct);
        Assert.Equal(2, edited.PageCount);
        Assert.Empty(edited.LoadAnnotations());
    }

    [Fact]
    public async Task DeletingEveryPageStillProducesAnOpenableDocument()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(2));
        var target = work.File("edited.pdf");

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, [],
                [new PageEdit.Delete(0), new PageEdit.Delete(1)]), null, Ct);

        await using var edited = await Loader.OpenAsync(target, Ct);
        Assert.Equal(1, edited.PageCount);
    }

    // ---- print job -------------------------------------------------------------------------
    [Fact]
    public async Task PrintJobMaterialisesTheInterleavedSequence()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(3));
        await using var doc = await Loader.OpenAsync(source, Ct);

        var pages = new List<PrintPageInfo>();
        foreach (var page in doc.Pages)
            pages.Add(new PrintPageInfo(page.Index, page.WidthPoints, page.HeightPoints, page.Rotation,
                await doc.IsPageBlankAsync(page.Index, Ct)));

        var sequence = PrintSequenceBuilder.Build(pages,
            new PrintSequenceOptions { SeparateSheetsPerContentPage = true });
        Assert.Equal(5, sequence.TotalPageCount);

        var jobPath = await new PrintJobBuilder().BuildAsync(doc, sequence, work.File("temp"), Ct);

        using var job = PdfReader.Open(jobPath, PdfDocumentOpenMode.Import);
        Assert.Equal(5, job.PageCount);
        // Every sheet, content or blank, keeps the geometry of the source page.
        for (int i = 0; i < job.PageCount; i++)
        {
            Assert.Equal(PdfFixtures.A4Width, job.Pages[i].Width.Point, 1);
            Assert.Equal(PdfFixtures.A4Height, job.Pages[i].Height.Point, 1);
        }
    }

    [Fact]
    public async Task PrintJobBlankPagesMatchTheGeometryOfThePageBeforeThem()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.MixedPageSizes());
        await using var doc = await Loader.OpenAsync(source, Ct);

        var pages = doc.Pages
            .Select(p => new PrintPageInfo(p.Index, p.WidthPoints, p.HeightPoints, p.Rotation, IsBlank: false))
            .ToList();
        var sequence = PrintSequenceBuilder.Build(pages,
            new PrintSequenceOptions { SeparateSheetsPerContentPage = true });

        var jobPath = await new PrintJobBuilder().BuildAsync(doc, sequence, work.File("temp"), Ct);

        using var job = PdfReader.Open(jobPath, PdfDocumentOpenMode.Import);
        Assert.Equal(7, job.PageCount);
        // Slot 1 is the blank following the A4 portrait page.
        Assert.Equal(PdfFixtures.A4Width, job.Pages[1].Width.Point, 1);
        // Slot 3 is the blank following the letter-sized page.
        Assert.Equal(612, job.Pages[3].Width.Point, 1);
    }

    [Fact]
    public async Task PrintJobLeavesTheSourceUntouchedAndWritesOnlyToTheTemporaryDirectory()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(2));
        var before = TempWorkspace.Sha256(source);
        var tempDir = work.File("temp");

        await using var doc = await Loader.OpenAsync(source, Ct);
        var sequence = PrintSequenceBuilder.Build(
            doc.Pages.Select(p => new PrintPageInfo(p.Index, p.WidthPoints, p.HeightPoints, p.Rotation, false)).ToList(),
            new PrintSequenceOptions { SeparateSheetsPerContentPage = true });

        var jobPath = await new PrintJobBuilder().BuildAsync(doc, sequence, tempDir, Ct);

        Assert.Equal(before, TempWorkspace.Sha256(source));
        Assert.StartsWith(Path.GetFullPath(tempDir), Path.GetFullPath(jobPath), StringComparison.Ordinal);
    }

    // ---- large documents -------------------------------------------------------------------
    [Fact]
    public async Task HandlesATwoHundredPageDocument()
    {
        using var work = new TempWorkspace();
        var source = work.Write("large.pdf", PdfFixtures.Large(200));

        await using var doc = await Loader.OpenAsync(source, Ct);
        Assert.Equal(200, doc.PageCount);

        var last = await doc.RenderAsync(new RenderRequest(199, 0.5), Ct);
        Assert.True(last.PixelWidth > 100);
    }
}
