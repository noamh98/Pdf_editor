using PdfEditor.Core.Documents;
using Xunit;

namespace PdfEditor.Core.Tests.Documents;

public class PageRangeStructTests
{
    [Fact]
    public void CountIsInclusiveOfBothEndpoints()
    {
        Assert.Equal(3, new PageRange(2, 4).Count);
        Assert.Equal(1, new PageRange(5, 5).Count);
    }

    [Theory]
    [InlineData(2, 4, 1, false)]
    [InlineData(2, 4, 2, true)]
    [InlineData(2, 4, 3, true)]
    [InlineData(2, 4, 4, true)]
    [InlineData(2, 4, 5, false)]
    public void ContainsChecksInclusiveBounds(int start, int end, int page, bool expected)
    {
        Assert.Equal(expected, new PageRange(start, end).Contains(page));
    }

    [Fact]
    public void ToStringIsSingleNumberWhenStartEqualsEnd()
    {
        Assert.Equal("5", new PageRange(5, 5).ToString());
    }

    [Fact]
    public void ToStringIsDashedWhenStartDiffersFromEnd()
    {
        Assert.Equal("2-4", new PageRange(2, 4).ToString());
    }
}

public class PageRangeParserParseTests
{
    [Fact]
    public void ParsesMixedSingleAndRangeExpression()
    {
        var result = PageRangeParser.Parse("1-3,5,8-10", 10);

        Assert.True(result.Success);
        Assert.Equal(
            new[] { new PageRange(1, 3), new PageRange(5, 5), new PageRange(8, 10) },
            result.Ranges);
        Assert.Equal(new[] { 1, 2, 3, 5, 8, 9, 10 }, result.ToPageNumbers());
    }

    [Fact]
    public void ParsesSinglePage()
    {
        var result = PageRangeParser.Parse("5", 10);

        Assert.True(result.Success);
        Assert.Equal([new PageRange(5, 5)], result.Ranges);
    }

    [Fact]
    public void IgnoresWhitespaceAroundAndInsideTheExpression()
    {
        var result = PageRangeParser.Parse(" 1 - 3 , 5 ", 10);

        Assert.True(result.Success);
        Assert.Equal([new PageRange(1, 3), new PageRange(5, 5)], result.Ranges);
    }

    [Theory]
    [InlineData(",1-3,5")]
    [InlineData("1-3,5,")]
    [InlineData(",1-3,5,")]
    [InlineData("1-3,,5")]
    public void IgnoresEmptyEntriesFromStrayCommas(string input)
    {
        var result = PageRangeParser.Parse(input, 10);

        Assert.True(result.Success);
        Assert.Equal([new PageRange(1, 3), new PageRange(5, 5)], result.Ranges);
    }

    [Fact]
    public void MergesOverlappingRangesOnParse()
    {
        var result = PageRangeParser.Parse("1-5,3-8", 10);

        Assert.True(result.Success);
        Assert.Equal([new PageRange(1, 8)], result.Ranges);
    }

    [Fact]
    public void MergesAdjacentRangesOnParse()
    {
        var result = PageRangeParser.Parse("1-3,4-6", 10);

        Assert.True(result.Success);
        Assert.Equal([new PageRange(1, 6)], result.Ranges);
    }

    [Fact]
    public void MergesAdjacentSinglePageIntoRange()
    {
        var result = PageRangeParser.Parse("1-3,4", 10);

        Assert.True(result.Success);
        Assert.Equal([new PageRange(1, 4)], result.Ranges);
    }

    // ---- errors -----------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsNullOrBlankInputAsEmpty(string? input)
    {
        var result = PageRangeParser.Parse(input, 10);

        Assert.False(result.Success);
        Assert.Equal(PageRangeError.Empty, result.Error);
    }

    [Fact]
    public void RejectsCommaOnlyInputAsEmpty()
    {
        var result = PageRangeParser.Parse(",,,", 10);

        Assert.False(result.Success);
        Assert.Equal(PageRangeError.Empty, result.Error);
    }

    [Fact]
    public void RejectsLetterAsInvalidCharacter()
    {
        var result = PageRangeParser.Parse("1,a,3", 10);

        Assert.False(result.Success);
        Assert.Equal(PageRangeError.InvalidCharacter, result.Error);
        Assert.Equal("a", result.Offending);
    }

    [Theory]
    [InlineData("1-2-3")]
    [InlineData("-5")]
    [InlineData("5-")]
    public void RejectsMalformedRangeShapes(string input)
    {
        var result = PageRangeParser.Parse(input, 10);

        Assert.False(result.Success);
        Assert.Equal(PageRangeError.MalformedRange, result.Error);
        Assert.Equal(input, result.Offending);
    }

    [Fact]
    public void RejectsOverflowingSingleNumberAsNotANumber()
    {
        var result = PageRangeParser.Parse("99999999999999", 10);

        Assert.False(result.Success);
        Assert.Equal(PageRangeError.NotANumber, result.Error);
        Assert.Equal("99999999999999", result.Offending);
    }

    [Fact]
    public void RejectsOverflowingRangeEndpointAsNotANumber()
    {
        var result = PageRangeParser.Parse("99999999999999-5", 10);

        Assert.False(result.Success);
        Assert.Equal(PageRangeError.NotANumber, result.Error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0-5")]
    public void RejectsZeroAsZeroOrNegative(string input)
    {
        var result = PageRangeParser.Parse(input, 10);

        Assert.False(result.Success);
        Assert.Equal(PageRangeError.ZeroOrNegative, result.Error);
    }

    [Fact]
    public void RejectsReversedRange()
    {
        var result = PageRangeParser.Parse("5-3", 10);

        Assert.False(result.Success);
        Assert.Equal(PageRangeError.ReversedRange, result.Error);
        Assert.Equal("5-3", result.Offending);
    }

    [Fact]
    public void RejectsSinglePageBeyondPageCountAsOutOfBounds()
    {
        var result = PageRangeParser.Parse("15", 10);

        Assert.False(result.Success);
        Assert.Equal(PageRangeError.OutOfBounds, result.Error);
        Assert.Equal("15", result.Offending);
    }

    [Fact]
    public void RejectsRangeThatExceedsPageCount()
    {
        var result = PageRangeParser.Parse("5-15", 10);

        Assert.False(result.Success);
        Assert.Equal(PageRangeError.OutOfBounds, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void RejectsNonPositivePageCountAsOutOfBounds(int pageCount)
    {
        var result = PageRangeParser.Parse("1-3", pageCount);

        Assert.False(result.Success);
        Assert.Equal(PageRangeError.OutOfBounds, result.Error);
    }

    // ---- alternate digit and separator scripts -----------------------------------------------

    [Fact]
    public void AcceptsArabicIndicDigits()
    {
        var result = PageRangeParser.Parse("١-٣", 10); // ١-٣

        Assert.True(result.Success);
        Assert.Equal([new PageRange(1, 3)], result.Ranges);
    }

    [Fact]
    public void AcceptsFullWidthDigitsAndFullWidthDash()
    {
        var result = PageRangeParser.Parse("１－３", 10); // １－３

        Assert.True(result.Success);
        Assert.Equal([new PageRange(1, 3)], result.Ranges);
    }

    [Theory]
    [InlineData("1־3")]   // Hebrew maqaf
    [InlineData("1–3")]   // en dash
    [InlineData("1—3")]   // em dash
    public void AcceptsHebrewMaqafAndUnicodeDashesAsRangeSeparator(string input)
    {
        var result = PageRangeParser.Parse(input, 10);

        Assert.True(result.Success);
        Assert.Equal([new PageRange(1, 3)], result.Ranges);
    }

    [Theory]
    [InlineData("‎1-3‏")]              // LRM ... RLM
    [InlineData("‪1-3‬")]              // LRE ... PDF
    [InlineData("1⁦-⁩3⁩")]        // embedded isolates around a digit
    public void StripsEmbeddedBidiControlCharacters(string input)
    {
        var result = PageRangeParser.Parse(input, 10);

        Assert.True(result.Success);
        Assert.Equal([new PageRange(1, 3)], result.Ranges);
    }
}

public class PageRangeParserMergeTests
{
    [Fact]
    public void MergeCombinesOverlappingRanges()
    {
        var merged = PageRangeParser.Merge([new PageRange(1, 5), new PageRange(3, 8)]);
        Assert.Equal([new PageRange(1, 8)], merged);
    }

    [Fact]
    public void MergeCombinesAdjacentRanges()
    {
        var merged = PageRangeParser.Merge([new PageRange(1, 3), new PageRange(4, 6)]);
        Assert.Equal([new PageRange(1, 6)], merged);
    }

    [Fact]
    public void MergeKeepsDisjointRangesSeparate()
    {
        var merged = PageRangeParser.Merge([new PageRange(1, 3), new PageRange(5, 6)]);
        Assert.Equal([new PageRange(1, 3), new PageRange(5, 6)], merged);
    }

    [Fact]
    public void MergeSortsUnorderedInput()
    {
        var merged = PageRangeParser.Merge([new PageRange(8, 10), new PageRange(1, 2)]);
        Assert.Equal([new PageRange(1, 2), new PageRange(8, 10)], merged);
    }

    [Fact]
    public void MergeOfEmptySequenceIsEmpty()
    {
        Assert.Empty(PageRangeParser.Merge([]));
    }
}

public class PageRangeParserFormatTests
{
    [Fact]
    public void FormatCollapsesConsecutivePagesIntoARange()
    {
        Assert.Equal("1-3,5,8-10", PageRangeParser.Format([1, 2, 3, 5, 8, 9, 10]));
    }

    [Fact]
    public void FormatDeduplicatesAndOrdersInput()
    {
        Assert.Equal("1-3", PageRangeParser.Format([3, 1, 2, 1, 3]));
    }

    [Fact]
    public void FormatOfEmptyCollectionIsEmptyString()
    {
        Assert.Equal(string.Empty, PageRangeParser.Format([]));
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 5, 8, 9, 10 })]
    [InlineData(new[] { 1 })]
    [InlineData(new[] { 2, 4, 6 })]
    public void FormatRoundTripsThroughParse(int[] pages)
    {
        var formatted = PageRangeParser.Format(pages);
        var parsed = PageRangeParser.Parse(formatted, 100);

        Assert.True(parsed.Success);
        Assert.Equal(pages.Distinct().OrderBy(p => p), parsed.ToPageNumbers());
    }
}

public class PageRangeParseResultToPageNumbersTests
{
    [Fact]
    public void ToPageNumbersDeduplicatesAndOrdersAcrossOverlappingInputs()
    {
        var result = PageRangeParser.Parse("5,1,3,1-3", 10);

        Assert.True(result.Success);
        Assert.Equal(new[] { 1, 2, 3, 5 }, result.ToPageNumbers());
    }
}
