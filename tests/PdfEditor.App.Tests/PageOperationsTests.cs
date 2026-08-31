using PdfEditor.App.ViewModels;
using PdfEditor.Core.Documents;
using Xunit;

namespace PdfEditor.App.Tests;

/// <summary>
/// The page operations dialog. It never touches a document, so it needs no window and no fixture:
/// it turns a range and an operation into the edits the writer is asked to apply.
/// </summary>
public class PageOperationsTests
{
    [Fact]
    public void ItOpensOnTheCurrentPage()
    {
        var operations = new PageOperationsViewModel(pageCount: 10, currentPageIndex: 4);

        Assert.Equal("5", operations.RangeText);
        Assert.Equal([4], operations.SelectedPageIndices);
        Assert.True(operations.CanApply);
        Assert.False(operations.HasRangeError);
    }

    [Fact]
    public void AnEmptyRangeMeansEveryPage()
    {
        var operations = new PageOperationsViewModel(6, 0) { RangeText = "  " };

        Assert.Equal(Enumerable.Range(0, 6), operations.SelectedPageIndices);
        Assert.False(operations.HasRangeError);
    }

    [Fact]
    public void ABadRangeIsReportedInHebrewAndBlocksTheOperation()
    {
        var operations = new PageOperationsViewModel(6, 0) { RangeText = "3-1" };

        Assert.True(operations.HasRangeError);
        Assert.False(operations.CanApply);
        Assert.False(string.IsNullOrWhiteSpace(operations.RangeError));
        Assert.Empty(operations.SelectedPageIndices);
        Assert.Equal(string.Empty, operations.SummaryText);
    }

    [Fact]
    public void ARangeBeyondTheDocumentIsRejected()
    {
        var operations = new PageOperationsViewModel(3, 0) { RangeText = "1-9" };

        Assert.True(operations.HasRangeError);
        Assert.False(operations.CanApply);
    }

    [Theory]
    [InlineData(PageOperation.RotateRight, 90)]
    [InlineData(PageOperation.RotateLeft, -90)]
    public void RotatingProducesOneRotationPerSelectedPage(PageOperation operation, int degrees)
    {
        var operations = new PageOperationsViewModel(8, 0)
        {
            RangeText = "2-4",
            Operation = operation
        };

        var edits = operations.BuildEdits();

        Assert.Equal(3, edits.Count);
        Assert.All(edits, e => Assert.Equal(degrees, Assert.IsType<PageEdit.Rotate>(e).DegreesClockwise));
        Assert.Equal([1, 2, 3], edits.Cast<PageEdit.Rotate>().Select(r => r.PageIndex));

        // Rotation keeps every page.
        Assert.Equal(8, operations.ResultingPageCount);
    }

    [Fact]
    public void DeletingRemovesTheSelectedPagesFromTheCount()
    {
        var operations = new PageOperationsViewModel(10, 0)
        {
            RangeText = "1-3",
            Operation = PageOperation.Delete
        };

        var edits = operations.BuildEdits();

        Assert.Equal(3, edits.Count);
        Assert.All(edits, e => Assert.IsType<PageEdit.Delete>(e));
        Assert.Equal(7, operations.ResultingPageCount);
    }

    [Fact]
    public void ExtractingKeepsOnlyTheSelectedPagesInTheOrderGiven()
    {
        var operations = new PageOperationsViewModel(10, 0)
        {
            RangeText = "5,1-2",
            Operation = PageOperation.Extract
        };

        var reorder = Assert.IsType<PageEdit.Reorder>(Assert.Single(operations.BuildEdits()));

        Assert.Equal([0, 1, 4], reorder.NewOrder);
        Assert.Equal(3, operations.ResultingPageCount);
    }

    [Fact]
    public void TheResultNeverClaimsToBeAZeroPageDocument()
    {
        var operations = new PageOperationsViewModel(4, 0)
        {
            RangeText = "1-4",
            Operation = PageOperation.Delete
        };

        // The writer refuses to produce an empty document, and the summary says the same.
        Assert.Equal(1, operations.ResultingPageCount);
    }

    [Fact]
    public void TheSuggestedNameKeepsTheStemAndStaysAPdf()
    {
        var operations = new PageOperationsViewModel(4, 0) { Operation = PageOperation.Extract };

        var name = operations.SuggestOutputName("חוזה שכירות.pdf");

        Assert.StartsWith("חוזה שכירות - ", name);
        Assert.EndsWith(".pdf", name);
    }

    [Fact]
    public void ChangingTheOperationRefreshesTheSummary()
    {
        var operations = new PageOperationsViewModel(10, 0) { RangeText = "1-4" };
        var summaryBefore = operations.SummaryText;

        operations.Operation = PageOperation.Delete;

        Assert.NotEqual(summaryBefore, operations.SummaryText);
        Assert.Contains("6", operations.SummaryText);
    }
}
