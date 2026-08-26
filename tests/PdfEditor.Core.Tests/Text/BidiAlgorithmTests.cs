using PdfEditor.Core.Text;
using Xunit;

namespace PdfEditor.Core.Tests.Text;

public class BidiAlgorithmTests
{
    // ---- paragraph direction (P2/P3) -------------------------------------------------------
    [Theory]
    [InlineData("שלום", true)]
    [InlineData("Hello", false)]
    [InlineData("123 שלום", true)]        // digits are not strong -> first strong is Hebrew
    [InlineData("123 Hello", false)]
    [InlineData("!!! שלום", true)]
    [InlineData("", false)]
    [InlineData("12345", false)]           // no strong character at all -> LTR
    public void DetectsParagraphDirection(string text, bool expectRtl)
    {
        Assert.Equal(expectRtl, BidiAlgorithm.Analyze(text).IsRightToLeftParagraph);
    }

    // ---- pure runs -------------------------------------------------------------------------
    [Fact]
    public void PureHebrewIsReversed()
    {
        Assert.Equal("םולש", BidiAlgorithm.ToVisual("שלום"));
    }

    [Fact]
    public void PureLatinIsUnchanged()
    {
        Assert.Equal("Hello world", BidiAlgorithm.ToVisual("Hello world"));
    }

    // ---- the cases the naive "reverse the string" approach got wrong ------------------------
    [Fact]
    public void DateInsideHebrewKeepsItsInternalOrder()
    {
        // "תאריך 26/08/2026" must show the date exactly as typed, at the left of the line.
        var visual = BidiAlgorithm.ToVisual("תאריך 26/08/2026");
        Assert.Contains("26/08/2026", visual);
        Assert.EndsWith("ךיראת", visual);
    }

    [Fact]
    public void LatinFileNameInsideHebrewKeepsItsInternalOrder()
    {
        var visual = BidiAlgorithm.ToVisual("הקובץ file.pdf נשמר");
        Assert.Contains("file.pdf", visual);
    }

    [Fact]
    public void EnglishSentenceStartingWithLatinStaysLtr()
    {
        const string s = "This line is written in English for mixed language testing.";
        Assert.Equal(s, BidiAlgorithm.ToVisual(s));
    }

    [Fact]
    public void MixedSentenceKeepsBothRunsReadable()
    {
        var visual = BidiAlgorithm.ToVisual("שלום עולם - Hello 123 עברית");
        Assert.Contains("Hello 123", visual);
        Assert.Contains("םלוע םולש", visual);
    }

    // ---- numbers ---------------------------------------------------------------------------
    [Fact]
    public void NumbersInsideHebrewAreNotReversed()
    {
        var visual = BidiAlgorithm.ToVisual("מספר 12345 סופי");
        Assert.Contains("12345", visual);
        Assert.DoesNotContain("54321", visual);
    }

    [Fact]
    public void DecimalSeparatorStaysInsideTheNumber()
    {
        Assert.Contains("3.14", BidiAlgorithm.ToVisual("הערך 3.14 מעלות"));
    }

    [Fact]
    public void ThousandsSeparatorStaysInsideTheNumber()
    {
        Assert.Contains("1,250", BidiAlgorithm.ToVisual("סכום 1,250 שקלים"));
    }

    [Fact]
    public void SignedNumberKeepsSignAttached()
    {
        // ES between EN and EN only merges when it is a single separator (W4).
        Assert.Contains("2026-08", BidiAlgorithm.ToVisual("תאריך 2026-08 בלבד"));
    }

    // ---- neutrals / brackets ---------------------------------------------------------------
    [Fact]
    public void BracketsAreMirroredInsideHebrew()
    {
        var visual = BidiAlgorithm.ToVisual("הערה (חשוב) כאן");
        // The opening glyph in logical order must be drawn as the closing glyph on the right.
        Assert.Contains("(בושח)", visual);
    }

    [Fact]
    public void LatinInsideHebrewBracketsKeepsBracketsAroundIt()
    {
        var visual = BidiAlgorithm.ToVisual("קובץ (PDF) חדש");
        Assert.Contains("(PDF)", visual);
    }

    [Fact]
    public void PunctuationBetweenTwoHebrewWordsFollowsHebrew()
    {
        var visual = BidiAlgorithm.ToVisual("אלף, בית");
        Assert.Equal("תיב ,ףלא", visual);
    }

    // ---- levels ----------------------------------------------------------------------------
    [Fact]
    public void HebrewCharactersGetOddLevels()
    {
        var r = BidiAlgorithm.Analyze("שלום");
        Assert.All(r.Levels, l => Assert.Equal(1, l & 1));
    }

    [Fact]
    public void LatinRunInsideHebrewGetsHigherEvenLevel()
    {
        var r = BidiAlgorithm.Analyze("שלום Hello שלום");
        int idx = r.Text.IndexOf('H');
        Assert.Equal(2, r.Levels[idx]);
        Assert.Equal(1, r.Levels[0]);
    }

    [Fact]
    public void NumberInsideHebrewGetsLevelTwo()
    {
        var r = BidiAlgorithm.Analyze("מספר 42");
        int idx = r.Text.IndexOf('4');
        Assert.Equal(2, r.Levels[idx]);
    }

    // ---- nikud (NSM) -----------------------------------------------------------------------
    [Fact]
    public void NikudInheritsTheBaseLetterLevel()
    {
        const string withNikud = "שָׁלוֹם";
        var r = BidiAlgorithm.Analyze(withNikud);
        Assert.All(r.Levels, l => Assert.Equal(1, l & 1));
        // Every code unit must still be present after reordering.
        var visual = BidiAlgorithm.ToVisual(withNikud);
        Assert.Equal(withNikud.Length, visual.Length);
    }

    // ---- explicit marks --------------------------------------------------------------------
    [Fact]
    public void ForcedDirectionOverridesAutoDetection()
    {
        Assert.True(BidiAlgorithm.Analyze("Hello", BidiParagraphDirection.RightToLeft).IsRightToLeftParagraph);
        Assert.False(BidiAlgorithm.Analyze("שלום", BidiParagraphDirection.LeftToRight).IsRightToLeftParagraph);
    }

    [Fact]
    public void RightToLeftMarkMakesParagraphRtl()
    {
        Assert.True(BidiAlgorithm.Analyze("‏Hello").IsRightToLeftParagraph);
    }

    [Fact]
    public void IsolateKeepsEmbeddedRunSelfContained()
    {
        // FSI ... PDI around a Latin fragment inside Hebrew.
        var visual = BidiAlgorithm.ToVisual("קובץ ⁨abc 12⁩ סוף");
        Assert.Contains("abc 12", visual);
    }

    // ---- invariants ------------------------------------------------------------------------
    [Theory]
    [InlineData("שלום עולם - Hello 123 עברית")]
    [InlineData("מספרים: 12345 ותאריך 26/08/2026")]
    [InlineData("רשימה: אלף, בית, גימל, דלת")]
    [InlineData("mixed שלום 42 (test) סוף")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("‫ RTL embed ‬ after")]
    public void ReorderingIsAPermutation(string text)
    {
        var r = BidiAlgorithm.Analyze(text);
        Assert.Equal(text.Length, r.VisualToLogical.Length);
        Assert.Equal(Enumerable.Range(0, text.Length).ToHashSet(), r.VisualToLogical.ToHashSet());
        Assert.Equal(text.Length, BidiAlgorithm.ToVisual(text).Length);
    }

    [Fact]
    public void ApplyingTheAlgorithmTwiceIsStableForPureLatin()
    {
        const string s = "Stable ASCII text 123.";
        Assert.Equal(s, BidiAlgorithm.ToVisual(BidiAlgorithm.ToVisual(s)));
    }

    [Fact]
    public void HandlesLongTextWithoutStackOverflow()
    {
        var text = string.Concat(Enumerable.Repeat("שלום Hello 123 ", 5000));
        var r = BidiAlgorithm.Analyze(text);
        Assert.Equal(text.Length, r.Levels.Length);
    }

    [Fact]
    public void NullTextThrows()
    {
        Assert.Throws<ArgumentNullException>(() => BidiAlgorithm.Analyze(null!));
    }
}
