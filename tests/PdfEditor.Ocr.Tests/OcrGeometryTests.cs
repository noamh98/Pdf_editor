using PdfEditor.Core.Annotations;
using PdfEditor.Ocr;
using Xunit;

namespace PdfEditor.Ocr.Tests;

public class OcrGeometryTests
{
    private const double A4Height = 842;

    [Fact]
    public void ConvertsTheTopLeftOfTheImageToTheTopLeftOfThePage()
    {
        var rect = OcrGeometry.ImageRectToPdfRect(0, 0, 300, 150, dpi: 300, A4Height);

        Assert.Equal(0, rect.Left, 3);
        Assert.Equal(72, rect.Width, 3);            // 300 px at 300 dpi is one inch
        Assert.Equal(36, rect.Height, 3);
        Assert.Equal(A4Height, rect.Top, 3);        // the top of the image is the top of the page
    }

    [Fact]
    public void FlipsTheVerticalAxis()
    {
        // A box 1 inch down from the top of the image, 1 inch tall.
        var rect = OcrGeometry.ImageRectToPdfRect(0, 150, 150, 150, dpi: 150, A4Height);

        Assert.Equal(A4Height - 72, rect.Top, 3);
        Assert.Equal(A4Height - 144, rect.Bottom, 3);
    }

    [Theory]
    [InlineData(72)]
    [InlineData(150)]
    [InlineData(300)]
    [InlineData(600)]
    public void RoundTripsThroughImageSpaceAtAnyResolution(int dpi)
    {
        var original = new PdfRect(100, 200, 180, 60);

        var (left, top, width, height) = OcrGeometry.PdfRectToImageRect(original, dpi, A4Height);
        var restored = OcrGeometry.ImageRectToPdfRect(left, top, width, height, dpi, A4Height);

        Assert.Equal(original.Left, restored.Left, 0);
        Assert.Equal(original.Bottom, restored.Bottom, 0);
        Assert.Equal(original.Width, restored.Width, 0);
        Assert.Equal(original.Height, restored.Height, 0);
    }

    [Fact]
    public void HigherResolutionMeansMorePixelsForTheSameArea()
    {
        var low = OcrGeometry.ImageRectToPdfRect(0, 0, 100, 100, dpi: 100, A4Height);
        var high = OcrGeometry.ImageRectToPdfRect(0, 0, 300, 300, dpi: 300, A4Height);

        Assert.Equal(low.Width, high.Width, 3);
        Assert.Equal(low.Height, high.Height, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-150)]
    public void RejectsANonPositiveResolution(int dpi)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OcrGeometry.ImageRectToPdfRect(0, 0, 10, 10, dpi, A4Height));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OcrGeometry.PdfRectToImageRect(new PdfRect(0, 0, 10, 10), dpi, A4Height));
        Assert.Throws<ArgumentOutOfRangeException>(() => OcrGeometry.PixelSize(595, 842, dpi));
    }

    [Fact]
    public void ComputesThePixelSizeOfAPage()
    {
        var (width, height) = OcrGeometry.PixelSize(595, 842, 300);
        Assert.Equal(2479, width);
        Assert.Equal(3508, height);
    }

    [Fact]
    public void UnionCoversEveryRectangle()
    {
        var union = OcrGeometry.Union([
            new PdfRect(10, 10, 20, 10),
            new PdfRect(50, 5, 30, 20)
        ]);

        Assert.Equal(10, union.Left, 3);
        Assert.Equal(5, union.Bottom, 3);
        Assert.Equal(80, union.Right, 3);
        Assert.Equal(25, union.Top, 3);
    }

    [Fact]
    public void UnionOfNothingIsEmpty()
    {
        Assert.True(OcrGeometry.Union([]).IsEmpty);
        Assert.True(OcrGeometry.Union([default]).IsEmpty);
    }
}

public class HebrewTextNormalizerTests
{
    [Fact]
    public void RemovesNikud()
    {
        Assert.Equal("שלום", HebrewTextNormalizer.StripNikud("שָׁלוֹם"));
    }

    [Theory]
    [InlineData("ך", "כ")]
    [InlineData("ם", "מ")]
    [InlineData("ן", "נ")]
    [InlineData("ף", "פ")]
    [InlineData("ץ", "צ")]
    public void FoldsEveryFinalForm(string input, string expected)
    {
        Assert.Equal(expected, HebrewTextNormalizer.FoldFinalForms(input));
    }

    [Fact]
    public void NormalizationMakesAWordWithAndWithoutNikudEqual()
    {
        Assert.Equal(HebrewTextNormalizer.Normalize("שָׁלוֹם"), HebrewTextNormalizer.Normalize("שלום"));
    }

    [Fact]
    public void NormalizationMakesFinalAndRegularFormsEqual()
    {
        Assert.Equal(HebrewTextNormalizer.Normalize("ספר"), HebrewTextNormalizer.Normalize("ספר"));
        Assert.Equal(HebrewTextNormalizer.Normalize("מים"), HebrewTextNormalizer.Normalize("מימ"));
    }

    [Fact]
    public void CollapsesWhitespaceAndDropsBidiControls()
    {
        Assert.Equal("abc def", HebrewTextNormalizer.Normalize("  abc ‎\t\n def  "));
    }

    [Fact]
    public void LowerCasesLatinText()
    {
        Assert.Equal("hello", HebrewTextNormalizer.Normalize("HeLLo"));
    }

    [Fact]
    public void HandlesEmptyInput()
    {
        Assert.Equal(string.Empty, HebrewTextNormalizer.Normalize(""));
        Assert.Equal(string.Empty, HebrewTextNormalizer.Normalize("   "));
    }
}
