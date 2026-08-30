using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Pdf.Documents;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfEditor.Pdf.Tests;

public class AnnotationRoundTripTests
{
    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;
    private static readonly PdfDocumentLoader Loader = new();
    private static readonly PdfDocumentWriter Writer = new();

    private static IEnumerable<Annotation> EveryKind() =>
    [
        new TextBoxAnnotation
        {
            PageIndex = 0, Rect = new PdfRect(50, 700, 300, 70),
            Text = "הערה בעברית ABC 42\nשורה שנייה", FontSize = 13,
            BackgroundColor = new AnnotationColor(255, 249, 196),
            BorderColor = new AnnotationColor(245, 176, 65)
        },
        new ShapeAnnotation(AnnotationKind.Rectangle)
        {
            PageIndex = 0, Rect = new PdfRect(50, 600, 200, 70),
            Color = AnnotationColor.Red, LineWidth = 3
        },
        new ShapeAnnotation(AnnotationKind.Ellipse)
        {
            PageIndex = 0, Rect = new PdfRect(300, 600, 150, 70),
            Color = AnnotationColor.Blue, FillColor = new AnnotationColor(200, 220, 255), LineWidth = 2
        },
        new ShapeAnnotation(AnnotationKind.Arrow)
        {
            PageIndex = 0, Rect = new PdfRect(50, 500, 200, 60),
            Start = new PdfPoint(50, 500), End = new PdfPoint(250, 560),
            Color = AnnotationColor.Green, LineWidth = 2.5
        },
        new ShapeAnnotation(AnnotationKind.Highlight)
        {
            PageIndex = 0, Rect = new PdfRect(50, 460, 220, 24),
            Color = AnnotationColor.Yellow, Opacity = 0.4
        },
        new MarkAnnotation(AnnotationKind.CheckMark) { PageIndex = 0, Rect = new PdfRect(320, 500, 40, 40) },
        new MarkAnnotation(AnnotationKind.CrossMark) { PageIndex = 0, Rect = new PdfRect(380, 500, 40, 40) },
        BuildInk()
    ];

    private static InkAnnotation BuildInk()
    {
        var ink = new InkAnnotation { PageIndex = 0, Color = AnnotationColor.Black, LineWidth = 2 };
        ink.Strokes.Add([new PdfPoint(60, 400), new PdfPoint(100, 430), new PdfPoint(140, 395), new PdfPoint(180, 425)]);
        ink.RecalculateBounds();
        return ink;
    }

    [Fact]
    public async Task EveryAnnotationKindSurvivesSaveAndReopen()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(2));
        var target = work.File("annotated.pdf");
        var originals = EveryKind().ToList();

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, originals), null, Ct);

        await using var reopened = await Loader.OpenAsync(target, Ct);
        var restored = reopened.LoadAnnotations();

        Assert.Equal(originals.Count, restored.Count);
        foreach (var original in originals)
        {
            var match = Assert.Single(restored, a => a.Id == original.Id);
            Assert.Equal(original.Kind, match.Kind);
            Assert.Equal(original.PageIndex, match.PageIndex);
            Assert.Equal(original.Rect.X, match.Rect.X, 2);
            Assert.Equal(original.Rect.Y, match.Rect.Y, 2);
            Assert.Equal(original.Rect.Width, match.Rect.Width, 2);
            Assert.Equal(original.Rect.Height, match.Rect.Height, 2);
            Assert.Equal(original.Color, match.Color);
            Assert.Equal(original.LineWidth, match.LineWidth, 3);
            Assert.False(match.IsForeign);
        }
    }

    [Fact]
    public async Task TextBoxKeepsItsContentAndStyleAcrossAReopen()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(1));
        var target = work.File("annotated.pdf");
        var original = (TextBoxAnnotation)EveryKind().First();

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, [original]), null, Ct);

        await using var reopened = await Loader.OpenAsync(target, Ct);
        var restored = Assert.IsType<TextBoxAnnotation>(Assert.Single(reopened.LoadAnnotations()));

        Assert.Equal(original.Text, restored.Text);
        Assert.Equal(original.FontSize, restored.FontSize, 3);
        Assert.Equal(original.BackgroundColor, restored.BackgroundColor);
        Assert.Equal(original.BorderColor, restored.BorderColor);
    }

    [Fact]
    public async Task InkStrokeGeometrySurvivesAReopen()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(1));
        var target = work.File("ink.pdf");
        var original = BuildInk();

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, [original]), null, Ct);

        await using var reopened = await Loader.OpenAsync(target, Ct);
        var restored = Assert.IsType<InkAnnotation>(Assert.Single(reopened.LoadAnnotations()));

        Assert.Equal(original.Strokes.Count, restored.Strokes.Count);
        Assert.Equal(original.Strokes[0].Count, restored.Strokes[0].Count);
        Assert.Equal(original.Strokes[0][2].X, restored.Strokes[0][2].X, 3);
        Assert.Equal(original.Strokes[0][2].Y, restored.Strokes[0][2].Y, 3);
    }

    [Fact]
    public async Task EverySavedAnnotationCarriesAnAppearanceStream()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(1));
        var target = work.File("annotated.pdf");

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, EveryKind().ToList()), null, Ct);

        using var check = PdfReader.Open(target, PdfDocumentOpenMode.Import);
        var annots = check.Pages[0].Elements.GetArray("/Annots");
        Assert.NotNull(annots);
        for (int i = 0; i < annots!.Elements.Count; i++)
        {
            var dict = annots.Elements.GetDictionary(i)!;
            var normal = dict.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
            Assert.NotNull(normal);
            Assert.NotNull(normal!.Stream);
            Assert.True(normal.Elements.ContainsKey("/BBox"), "the appearance needs a bounding box");
            Assert.Equal("/Form", normal.Elements.GetName("/Subtype"));
        }
    }

    [Fact]
    public async Task SavingDoesNotLeakTheTemporaryPagesUsedToBuildAppearances()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(3));
        var target = work.File("annotated.pdf");

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, EveryKind().ToList()), null, Ct);

        await using var reopened = await Loader.OpenAsync(target, Ct);
        Assert.Equal(3, reopened.PageCount);
    }

    [Fact]
    public async Task SavingTwiceDoesNotDuplicateAnnotations()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(1));
        var first = work.File("first.pdf");
        var second = work.File("second.pdf");
        var annotations = EveryKind().ToList();

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(first, SaveMode.Editable, annotations), null, Ct);

        await using (var reopened = await Loader.OpenAsync(first, Ct))
        {
            var restored = reopened.LoadAnnotations();
            Assert.Equal(annotations.Count, restored.Count);
            await Writer.SaveAsync(reopened, new SaveRequest(second, SaveMode.Editable, restored), null, Ct);
        }

        await using var third = await Loader.OpenAsync(second, Ct);
        Assert.Equal(annotations.Count, third.LoadAnnotations().Count);
    }

    [Fact]
    public async Task PdfiumActuallyPaintsTheAppearanceStreams()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(1, hebrew: false));
        var target = work.File("annotated.pdf");

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, EveryKind().ToList()), null, Ct);

        await using var reopened = await Loader.OpenAsync(target, Ct);
        var withAnnotations = await reopened.RenderAsync(new RenderRequest(0, 1.5, IncludeAnnotations: true), Ct);
        var without = await reopened.RenderAsync(new RenderRequest(0, 1.5, IncludeAnnotations: false), Ct);

        Assert.Equal(withAnnotations.BgraPixels.Length, without.BgraPixels.Length);
        int differing = 0;
        for (int i = 0; i < withAnnotations.BgraPixels.Length; i += 4)
            if (withAnnotations.BgraPixels[i] != without.BgraPixels[i]) differing++;

        // PDFium is the engine behind Chrome and Edge, so this is also a compatibility check.
        Assert.True(differing > 2000, $"only {differing} pixels changed; the appearances were not drawn");
    }

    [Fact]
    public async Task FlatteningDrawsTheInkAndRemovesTheAnnotations()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(1, hebrew: false));
        var flat = work.File("flat.pdf");

        await using (var doc = await Loader.OpenAsync(source, Ct))
            await Writer.SaveAsync(doc, new SaveRequest(flat, SaveMode.Flattened, EveryKind().ToList()), null, Ct);

        using (var check = PdfReader.Open(flat, PdfDocumentOpenMode.Import))
            Assert.False(check.Pages[0].Elements.ContainsKey("/Annots"));

        await using var reopened = await Loader.OpenAsync(flat, Ct);
        Assert.Empty(reopened.LoadAnnotations());

        var rendered = await reopened.RenderAsync(new RenderRequest(0, 1.5, IncludeAnnotations: false), Ct);
        int inked = 0;
        for (int i = 0; i < rendered.BgraPixels.Length; i += 4)
            if (rendered.BgraPixels[i] < 240) inked++;
        Assert.True(inked > 2000, $"the flattened page only has {inked} inked pixels");
    }

    [Fact]
    public async Task FlattenedAndEditableOutputLookTheSame()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(1, hebrew: false));
        var editable = work.File("editable.pdf");
        var flat = work.File("flat.pdf");
        var annotations = EveryKind().ToList();

        await using (var doc = await Loader.OpenAsync(source, Ct))
        {
            await Writer.SaveAsync(doc, new SaveRequest(editable, SaveMode.Editable, annotations), null, Ct);
            await Writer.SaveAsync(doc, new SaveRequest(flat, SaveMode.Flattened, annotations), null, Ct);
        }

        await using var a = await Loader.OpenAsync(editable, Ct);
        await using var b = await Loader.OpenAsync(flat, Ct);
        var editableRender = await a.RenderAsync(new RenderRequest(0, 1.0, IncludeAnnotations: true), Ct);
        var flatRender = await b.RenderAsync(new RenderRequest(0, 1.0, IncludeAnnotations: false), Ct);

        Assert.Equal(editableRender.PixelWidth, flatRender.PixelWidth);
        int same = 0, total = 0;
        for (int i = 0; i < editableRender.BgraPixels.Length; i += 4)
        {
            total++;
            if (Math.Abs(editableRender.BgraPixels[i] - flatRender.BgraPixels[i]) <= 24) same++;
        }
        // The same drawing routine produces both, so they should agree almost everywhere.
        Assert.True(same / (double)total > 0.98, $"only {same * 100.0 / total:0.0}% of pixels matched");
    }

    [Fact]
    public async Task ForeignAnnotationsArePreservedAndNotClaimedAsOurs()
    {
        using var work = new TempWorkspace();
        var source = work.Write("foreign.pdf", DocumentWithForeignAnnotation());

        await using var doc = await Loader.OpenAsync(source, Ct);
        var annotations = doc.LoadAnnotations();

        var foreign = Assert.Single(annotations);
        Assert.True(foreign.IsForeign);

        var target = work.File("resaved.pdf");
        await Writer.SaveAsync(doc, new SaveRequest(target, SaveMode.Editable, [DocumentLifecycleTests.SampleTextBox(0)]),
            null, Ct);

        await using var reopened = await Loader.OpenAsync(target, Ct);
        var after = reopened.LoadAnnotations();
        Assert.Equal(2, after.Count);
        Assert.Single(after, a => a.IsForeign);
        Assert.Single(after, a => !a.IsForeign);
    }

    /// <summary>A /Square annotation written without this application's private payload.</summary>
    private static byte[] DocumentWithForeignAnnotation()
    {
        using var input = new MemoryStream(PdfFixtures.TextDocument(1), writable: false);
        using var doc = PdfReader.Open(input, PdfDocumentOpenMode.Modify);
        var page = doc.Pages[0];

        var annot = new PdfDictionary(doc);
        annot.Elements["/Type"] = new PdfName("/Annot");
        annot.Elements["/Subtype"] = new PdfName("/Square");
        annot.Elements["/Rect"] = new PdfArray(doc,
            new PdfReal(100), new PdfReal(200), new PdfReal(300), new PdfReal(280));
        annot.Elements["/C"] = new PdfArray(doc, new PdfReal(0), new PdfReal(0), new PdfReal(1));
        annot.Elements["/F"] = new PdfInteger(4);
        annot.Elements["/NM"] = new PdfString("other-app-1");
        doc.Internals.AddObject(annot);

        var annots = new PdfArray(doc);
        annots.Elements.Add(annot.Reference!);
        page.Elements["/Annots"] = annots;

        using var buffer = new MemoryStream();
        doc.Save(buffer, closeStream: false);
        return buffer.ToArray();
    }
}
