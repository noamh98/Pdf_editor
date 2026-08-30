using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Core.Text;
using PdfEditor.Pdf.Annotations;
using PdfEditor.Pdf.Documents;
using PdfEditor.Pdf.Fonts;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Xunit;

namespace PdfEditor.Pdf.Tests;

public class TextLayoutTests : IDisposable
{
    private static CancellationToken Ct => new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token;

    private readonly PdfDocument _document;
    private readonly XGraphics _gfx;

    public TextLayoutTests()
    {
        PdfFonts.EnsureRegistered();
        _document = new PdfDocument();
        var page = _document.AddPage();
        _gfx = XGraphics.FromPdfPage(page);
    }

    public void Dispose()
    {
        _gfx.Dispose();
        _document.Dispose();
        GC.SuppressFinalize(this);
    }

    private XFont Font(double size = 14) => PdfFonts.Create(size);

    [Fact]
    public void FontDataIsAvailableSoTextCanBeEmbedded()
    {
        Assert.True(PdfFonts.IsAvailable,
            "no font could be resolved; PDF text output would be empty");
    }

    [Fact]
    public void ShortTextFitsOnOneLine()
    {
        var lines = TextLayout.Layout(_gfx, "שלום", Font(), 200, 100, 4,
            TextAlignment.Start, BidiParagraphDirection.Auto);
        Assert.Single(lines);
    }

    [Fact]
    public void LongTextWrapsInsteadOfOverflowing()
    {
        const string text = "זוהי הערה ארוכה מאוד שאמורה להתפרס על פני כמה שורות בתוך תיבת הטקסט";
        var lines = TextLayout.Layout(_gfx, text, Font(), 160, 300, 4,
            TextAlignment.Start, BidiParagraphDirection.Auto);

        Assert.True(lines.Count > 1, "the text should have wrapped");
        Assert.All(lines, l => Assert.True(l.Width <= 160, $"line '{l.VisualText}' is {l.Width} wide"));
    }

    [Fact]
    public void ExplicitLineBreaksStartNewLines()
    {
        var lines = TextLayout.Layout(_gfx, "שורה\nשנייה\nשלישית", Font(), 300, 200, 4,
            TextAlignment.Start, BidiParagraphDirection.Auto);
        Assert.Equal(3, lines.Count);
    }

    [Fact]
    public void TextTallerThanTheBoxIsTruncatedRatherThanOverflowing()
    {
        var text = string.Join("\n", Enumerable.Repeat("שורה", 40));
        var lines = TextLayout.Layout(_gfx, text, Font(), 200, 60, 4,
            TextAlignment.Start, BidiParagraphDirection.Auto);

        Assert.True(lines.Count < 10, $"{lines.Count} lines were laid out into a 60pt box");
        Assert.All(lines, l => Assert.True(l.Baseline <= 60));
    }

    [Fact]
    public void HebrewParagraphIsAlignedToTheRightByDefault()
    {
        var lines = TextLayout.Layout(_gfx, "שלום", Font(), 200, 100, 4,
            TextAlignment.Start, BidiParagraphDirection.Auto);
        var line = Assert.Single(lines);
        Assert.True(line.X > 100, $"a Hebrew line should start near the right edge, x was {line.X}");
    }

    [Fact]
    public void EnglishParagraphIsAlignedToTheLeftByDefault()
    {
        var lines = TextLayout.Layout(_gfx, "Hello", Font(), 200, 100, 4,
            TextAlignment.Start, BidiParagraphDirection.Auto);
        var line = Assert.Single(lines);
        Assert.Equal(4, line.X, 1);
    }

    [Fact]
    public void LinesAreEmittedInVisualOrderReadyForAPdfTextOperator()
    {
        var lines = TextLayout.Layout(_gfx, "שלום", Font(), 200, 100, 4,
            TextAlignment.Start, BidiParagraphDirection.Auto);
        Assert.Equal("םולש", Assert.Single(lines).VisualText);
    }

    [Fact]
    public void DatesAndFileNamesKeepTheirOrderInsideHebrewText()
    {
        var lines = TextLayout.Layout(_gfx, "הקובץ file.pdf נשמר בתאריך 26/08/2026", Font(11), 400, 100, 4,
            TextAlignment.Start, BidiParagraphDirection.Auto);
        var joined = string.Concat(lines.Select(l => l.VisualText));

        Assert.Contains("file.pdf", joined);
        Assert.Contains("26/08/2026", joined);
    }

    [Fact]
    public void ForcedDirectionOverridesTheDetectedOne()
    {
        var lines = TextLayout.Layout(_gfx, "Hello", Font(), 200, 100, 4,
            TextAlignment.Start, BidiParagraphDirection.RightToLeft);
        Assert.True(Assert.Single(lines).X > 100);
    }

    [Fact]
    public void AWordWiderThanTheBoxIsBrokenRatherThanClipped()
    {
        var lines = TextLayout.WrapToWidth(_gfx, new string('מ', 200), Font(14), 80);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(_gfx.MeasureString(l, Font(14)).Width <= 82));
    }

    [Fact]
    public void EmptyAndWhitespaceTextProduceNoCrash()
    {
        Assert.Empty(TextLayout.Layout(_gfx, "", Font(), 100, 100, 4,
            TextAlignment.Start, BidiParagraphDirection.Auto));
        Assert.NotNull(TextLayout.Layout(_gfx, "   ", Font(), 100, 100, 4,
            TextAlignment.Start, BidiParagraphDirection.Auto));
    }

    [Fact]
    public void ZeroWidthBoxProducesNoLinesInsteadOfLoopingForever()
    {
        Assert.Empty(TextLayout.Layout(_gfx, "שלום", Font(), 4, 100, 4,
            TextAlignment.Start, BidiParagraphDirection.Auto));
    }

    [Fact]
    public async Task HebrewTextBoxRendersVisibleGlyphsInsideItsBounds()
    {
        using var work = new TempWorkspace();
        var source = work.Write("source.pdf", PdfFixtures.TextDocument(1, hebrew: false));
        var target = work.File("annotated.pdf");

        var annotation = new TextBoxAnnotation
        {
            PageIndex = 0,
            Rect = new PdfRect(60, 600, 300, 90),
            Text = "הערה בעברית עם מספר 42 ותאריך 26/08/2026",
            FontSize = 13,
            TextColor = AnnotationColor.Black
        };

        var loader = new PdfDocumentLoader();
        await using (var doc = await loader.OpenAsync(source, Ct))
            await new PdfDocumentWriter().SaveAsync(doc,
                new SaveRequest(target, SaveMode.Editable, [annotation]), null, Ct);

        await using var reopened = await loader.OpenAsync(target, Ct);
        var with = await reopened.RenderAsync(new RenderRequest(0, 2.0, IncludeAnnotations: true), Ct);
        var without = await reopened.RenderAsync(new RenderRequest(0, 2.0, IncludeAnnotations: false), Ct);

        // Count changed pixels and confirm they all fall inside the annotation rectangle.
        double scale = with.PixelWidth / reopened.Pages[0].WidthPoints;
        double pageHeight = reopened.Pages[0].HeightPoints;
        int changed = 0, outside = 0;
        for (int y = 0; y < with.PixelHeight; y++)
        {
            for (int x = 0; x < with.PixelWidth; x++)
            {
                int i = (y * with.PixelWidth + x) * 4;
                if (with.BgraPixels[i] == without.BgraPixels[i]) continue;
                changed++;

                double px = x / scale;
                double py = pageHeight - y / scale;
                if (px < annotation.Rect.Left - 2 || px > annotation.Rect.Right + 2 ||
                    py < annotation.Rect.Bottom - 2 || py > annotation.Rect.Top + 2) outside++;
            }
        }

        Assert.True(changed > 400, $"only {changed} pixels changed; the Hebrew text did not render");
        Assert.Equal(0, outside);
    }
}
