using PdfEditor.Core.Printing;
using Xunit;

namespace PdfEditor.Core.Tests.Printing;

public class PrintSequenceBuilderTests
{
    private const double A4W = 595.276, A4H = 841.89;

    private static PrintPageInfo Content(int i, double w = A4W, double h = A4H, int rot = 0)
        => new(i, w, h, rot, IsBlank: false);

    private static PrintPageInfo Blank(int i, double w = A4W, double h = A4H, int rot = 0)
        => new(i, w, h, rot, IsBlank: true);

    private static PrintSequenceOptions On => new() { SeparateSheetsPerContentPage = true };
    private static PrintSequenceOptions Off => new() { SeparateSheetsPerContentPage = false };

    private static string Shape(PrintSequence s) =>
        string.Concat(s.Slots.Select(x => x.Kind switch
        {
            PrintSlotKind.Content => "C",
            PrintSlotKind.ExistingBlank => "e",
            _ => "b"
        }));

    [Fact]
    public void DisabledOptionLeavesTheDocumentUntouched()
    {
        var seq = PrintSequenceBuilder.Build([Content(0), Content(1), Content(2)], Off);
        Assert.Equal("CCC", Shape(seq));
        Assert.Equal(0, seq.InsertedBlankCount);
    }

    [Fact]
    public void InsertsBlankBetweenContentPages()
    {
        var seq = PrintSequenceBuilder.Build([Content(0), Content(1), Content(2)], On);
        Assert.Equal("CbCbC", Shape(seq));
        Assert.Equal(3, seq.ContentPageCount);
        Assert.Equal(2, seq.InsertedBlankCount);
        Assert.Equal(5, seq.TotalPageCount);
    }

    [Fact]
    public void NeverAppendsBlankAfterTheLastPage()
    {
        var seq = PrintSequenceBuilder.Build([Content(0), Content(1)], On);
        Assert.Equal("CbC", Shape(seq));
        Assert.EndsWith("C", Shape(seq));
    }

    [Fact]
    public void SinglePageGetsNoBlank()
    {
        var seq = PrintSequenceBuilder.Build([Content(0)], On);
        Assert.Equal("C", Shape(seq));
        Assert.Equal(0, seq.InsertedBlankCount);
    }

    [Fact]
    public void EmptyDocumentProducesEmptySequence()
    {
        var seq = PrintSequenceBuilder.Build([], On);
        Assert.Empty(seq.Slots);
        Assert.Equal(0, seq.EstimatedSheets(duplexForced: true));
    }

    [Fact]
    public void ExistingBlankIsReusedInsteadOfInsertingASecondOne()
    {
        var seq = PrintSequenceBuilder.Build([Content(0), Blank(1), Content(2)], On);
        Assert.Equal("CeC", Shape(seq));
        Assert.Equal(0, seq.InsertedBlankCount);
        Assert.Equal(1, seq.ExistingBlankCount);
        Assert.Equal(2, seq.ContentPageCount);
    }

    [Fact]
    public void TwoAdjacentExistingBlanksAreNotPaddedFurther()
    {
        var seq = PrintSequenceBuilder.Build([Content(0), Blank(1), Blank(2), Content(3)], On);
        Assert.Equal("CeeC", Shape(seq));
        Assert.Equal(0, seq.InsertedBlankCount);
    }

    [Fact]
    public void MixedExistingAndMissingBlanksAreHandledPerGap()
    {
        var seq = PrintSequenceBuilder.Build(
            [Content(0), Blank(1), Content(2), Content(3), Blank(4), Content(5)], On);
        Assert.Equal("CeCbCeC", Shape(seq));
        Assert.Equal(1, seq.InsertedBlankCount);
        Assert.Equal(2, seq.ExistingBlankCount);
    }

    [Fact]
    public void LeadingBlankIsPreservedWithoutAddingAnother()
    {
        var seq = PrintSequenceBuilder.Build([Blank(0), Content(1), Content(2)], On);
        Assert.Equal("eCbC", Shape(seq));
    }

    [Fact]
    public void TrailingBlankIsPreserved()
    {
        var seq = PrintSequenceBuilder.Build([Content(0), Content(1), Blank(2)], On);
        Assert.Equal("CbCe", Shape(seq));
    }

    [Fact]
    public void InsertedBlankCopiesSizeAndRotationOfThePrecedingPage()
    {
        var seq = PrintSequenceBuilder.Build([Content(0, A4H, A4W, 90), Content(1)], On);
        var blank = seq.Slots[1];
        Assert.Equal(PrintSlotKind.InsertedBlank, blank.Kind);
        Assert.Equal(A4H, blank.WidthPoints);
        Assert.Equal(A4W, blank.HeightPoints);
        Assert.Equal(90, blank.Rotation);
        Assert.Null(blank.SourcePageIndex);
    }

    [Fact]
    public void MixedPageSizesKeepTheirOwnDimensions()
    {
        var seq = PrintSequenceBuilder.Build(
            [Content(0, A4W, A4H), Content(1, 612, 792)], On);
        Assert.Equal(A4W, seq.Slots[0].WidthPoints);
        Assert.Equal(A4W, seq.Slots[1].WidthPoints);   // blank follows page 0
        Assert.Equal(612, seq.Slots[2].WidthPoints);
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(2, 3, 2)]
    [InlineData(3, 5, 3)]
    [InlineData(4, 7, 4)]
    [InlineData(5, 9, 5)]
    public void OddAndEvenPageCountsAlwaysGiveOneSheetPerContentPage(int contentPages, int expectedTotal, int expectedSheets)
    {
        var pages = Enumerable.Range(0, contentPages).Select(i => Content(i)).ToList();
        var seq = PrintSequenceBuilder.Build(pages, On);
        Assert.Equal(contentPages, seq.ContentPageCount);
        Assert.Equal(expectedTotal, seq.TotalPageCount);
        Assert.Equal(expectedSheets, seq.EstimatedSheets(duplexForced: true));
    }

    [Fact]
    public void WithoutTheOptionDuplexPacksTwoContentPagesPerSheet()
    {
        var pages = Enumerable.Range(0, 5).Select(i => Content(i)).ToList();
        var seq = PrintSequenceBuilder.Build(pages, Off);
        Assert.Equal(3, seq.EstimatedSheets(duplexForced: true));
    }

    [Fact]
    public void SubsetPrintingOnlyIncludesSelectedPages()
    {
        var pages = Enumerable.Range(0, 6).Select(i => Content(i)).ToList();
        var seq = PrintSequenceBuilder.Build(pages,
            new PrintSequenceOptions { SeparateSheetsPerContentPage = true, SelectedPageIndices = [1, 3, 5] });
        Assert.Equal("CbCbC", Shape(seq));
        Assert.Equal([1, null, 3, null, 5], seq.Slots.Select(s => s.SourcePageIndex).ToArray());
    }

    [Fact]
    public void SubsetPrintingHonoursTheGivenOrder()
    {
        var pages = Enumerable.Range(0, 4).Select(i => Content(i)).ToList();
        var seq = PrintSequenceBuilder.Build(pages,
            new PrintSequenceOptions { SelectedPageIndices = [3, 0] });
        Assert.Equal([3, 0], seq.Slots.Select(s => s.SourcePageIndex).ToArray());
    }

    [Fact]
    public void UnknownSelectedIndicesAreIgnored()
    {
        var seq = PrintSequenceBuilder.Build([Content(0)],
            new PrintSequenceOptions { SelectedPageIndices = [0, 99] });
        Assert.Single(seq.Slots);
    }

    [Theory]
    [InlineData(595.276, 841.89, 0, PageOrientationKind.Portrait)]
    [InlineData(841.89, 595.276, 0, PageOrientationKind.Landscape)]
    [InlineData(595.276, 841.89, 90, PageOrientationKind.Landscape)]
    [InlineData(841.89, 595.276, 270, PageOrientationKind.Portrait)]
    public void OrientationAccountsForPageRotation(double w, double h, int rot, PageOrientationKind expected)
    {
        Assert.Equal(expected, new PrintPageInfo(0, w, h, rot, false).Orientation);
    }
}
