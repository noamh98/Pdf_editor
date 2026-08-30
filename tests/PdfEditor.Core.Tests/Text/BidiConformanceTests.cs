using System.Globalization;
using PdfEditor.Core.Text;
using Xunit;

namespace PdfEditor.Core.Tests.Text;

/// <summary>
/// Conformance tests aimed at the parts of UAX#9 that a naive implementation gets wrong and that
/// a Hebrew document exercises constantly: chains of combining marks, rule L3, supplementary
/// characters and the interaction between the two.
/// </summary>
public class BidiConformanceTests
{
    private const char Alef = 'א';
    private const char Bet = 'ב';
    private const char Sheva = 'ְ';   // NSM
    private const char Dagesh = 'ּ';  // NSM
    private const char ShinDot = 'ׁ'; // NSM

    // ---- W1: an NSM takes the *resolved* type of the character before it -------------------
    // "AL NSM NSM -> AL AL AL" is the worked example in UAX#9. A run of marks on one letter is
    // ordinary pointed Hebrew, so getting this wrong corrupts every vocalised word.
    [Fact]
    public void ChainOfCombiningMarksInheritsTheBaseLetterLevel()
    {
        var r = BidiAlgorithm.Analyze("a" + Alef + Sheva + Dagesh);
        Assert.Equal(0, r.Levels[0] & 1);                    // the Latin letter stays left-to-right
        Assert.Equal(1, r.Levels[1] & 1);                    // the Hebrew letter is right-to-left
        Assert.Equal(r.Levels[1], r.Levels[2]);              // first mark follows its base
        Assert.Equal(r.Levels[1], r.Levels[3]);              // and so does the second
    }

    [Fact]
    public void CombiningMarkAfterAnIsolateInitiatorBecomesNeutral()
    {
        // W1 makes an NSM directly after an isolate initiator ON rather than adopting the
        // initiator's own type; a following mark then inherits that ON, not NSM.
        var r = BidiAlgorithm.Analyze("⁦" + Sheva + Dagesh + "a");
        Assert.Equal(r.Levels[1], r.Levels[2]);
    }

    // ---- L3: combining marks must follow their base character in the visual string ----------
    [Fact]
    public void PointedHebrewKeepsEveryMarkAfterItsBaseLetter()
    {
        const string logical = "שָׁלוֹם"; // שָׁלוֹם
        var visual = BidiAlgorithm.ToVisual(logical);

        Assert.Equal(logical.Length, visual.Length);
        AssertNoOrphanedCombiningMark(visual);
        // ם  וֹ  ל  שָׁ — the word read right to left, each cluster intact.
        Assert.Equal("םוֹלשָׁ", visual);
    }

    [Fact]
    public void CombiningMarksStayWithTheirBaseInsideLeftToRightText()
    {
        var visual = BidiAlgorithm.ToVisual("word " + Bet + Dagesh + " end");
        AssertNoOrphanedCombiningMark(visual);
    }

    [Theory]
    [InlineData("שָׁלוֹם עוֹלָם")]
    [InlineData("בְּרֵאשִׁית בָּרָא")]
    [InlineData("mixed שָׁלוֹם 42 text")]
    [InlineData("הַכֹּל (טוֹב) כָּאן")]
    public void NoCombiningMarkIsEverOrphanedByReordering(string logical)
    {
        AssertNoOrphanedCombiningMark(BidiAlgorithm.ToVisual(logical));
    }

    // ---- surrogate pairs: L2 reverses code units, a pair is one character -------------------
    [Theory]
    [InlineData("שלום \U0001F600 עולם")]
    [InlineData("\U0001F600 שלום")]
    [InlineData("שלום\U0001F600")]
    [InlineData("Hello \U0001F600 שלום")]
    [InlineData("\U0001D400\U0001D401 שלום")]  // supplementary Latin letters, class L
    public void SupplementaryCharactersSurviveReordering(string logical)
    {
        var visual = BidiAlgorithm.ToVisual(logical);

        Assert.Equal(logical.Length, visual.Length);
        for (int i = 0; i < visual.Length; i++)
        {
            if (char.IsHighSurrogate(visual[i]))
                Assert.True(i + 1 < visual.Length && char.IsLowSurrogate(visual[i + 1]),
                    $"High surrogate at {i} lost its pair in '{Escape(visual)}'.");
            if (char.IsLowSurrogate(visual[i]))
                Assert.True(i > 0 && char.IsHighSurrogate(visual[i - 1]),
                    $"Low surrogate at {i} lost its pair in '{Escape(visual)}'.");
        }
        // Every code point present in the input is still present in the output.
        Assert.Equal(
            Sorted(logical.EnumerateRunes().Select(r => r.Value)),
            Sorted(visual.EnumerateRunes().Select(r => r.Value)));
    }

    [Fact]
    public void EmojiInsideHebrewIsStillTheSameCharacter()
    {
        const string logical = "שלום \U0001F600 עולם";
        Assert.Contains("\U0001F600", BidiAlgorithm.ToVisual(logical), StringComparison.Ordinal);
    }

    // ---- property: reordering never invents, loses or splits a character -------------------
    [Theory]
    [InlineData(7)]
    [InlineData(101)]
    [InlineData(20260826)]
    public void ReorderingIsAlwaysALosslessPermutationOfCodePoints(int seed)
    {
        var alphabet = new[]
        {
            "a", "Z", "1", "9", ".", ",", "-", " ", "(", ")", "[", "]", "\"",
            "א", "ת", "ּ", "ְ", "ׁ",      // Hebrew letters + marks
            "ا", "ـ", "٠",                          // Arabic letter, tatweel, digit
            "‎", "‏", "‪", "‫", "‬",      // LRM, RLM, LRE, RLE, PDF
            "⁦", "⁧", "⁨", "⁩",                // LRI, RLI, FSI, PDI
            "\U0001F600", "\U0001D400"                             // supplementary
        };

        var random = new Random(seed);
        for (int iteration = 0; iteration < 400; iteration++)
        {
            var text = string.Concat(Enumerable
                .Range(0, random.Next(0, 24))
                .Select(_ => alphabet[random.Next(alphabet.Length)]));

            foreach (var direction in new[]
                     {
                         BidiParagraphDirection.Auto,
                         BidiParagraphDirection.LeftToRight,
                         BidiParagraphDirection.RightToLeft
                     })
            {
                var result = BidiAlgorithm.Analyze(text, direction);
                Assert.Equal(text.Length, result.VisualToLogical.Length);
                Assert.Equal(Enumerable.Range(0, text.Length).ToHashSet(), result.VisualToLogical.ToHashSet());
                AssertMarksKeepTheirBase(text, result);

                var visual = BidiAlgorithm.ToVisual(text, direction);
                Assert.Equal(text.Length, visual.Length);
                Assert.DoesNotContain('�', visual);
                Assert.Equal(
                    Sorted(text.EnumerateRunes().Select(r => MirrorInsensitive(r.Value))),
                    Sorted(visual.EnumerateRunes().Select(r => MirrorInsensitive(r.Value))));
                AssertNoSplitSurrogatePair(visual);
            }
        }
    }

    /// <summary>
    /// Rule L3: a combining mark that follows a letter in logical order must still sit immediately
    /// after that same letter once the line has been reordered, or the mark lands on a different
    /// glyph.
    /// </summary>
    private static void AssertMarksKeepTheirBase(string logical, BidiResult result)
    {
        var visualPositionOf = new int[logical.Length];
        for (int v = 0; v < result.VisualToLogical.Length; v++) visualPositionOf[result.VisualToLogical[v]] = v;

        for (int i = 1; i < logical.Length; i++)
        {
            if (BidiClassifier.Classify(logical[i]) != BidiClass.NSM) continue;
            if (BidiClassifier.Classify(logical[i - 1]) is not (BidiClass.L or BidiClass.R or BidiClass.AL))
                continue;   // an orphaned mark has no base to stay with
            Assert.True(visualPositionOf[i] == visualPositionOf[i - 1] + 1,
                $"The mark at logical {i} left its base letter in '{Escape(logical)}'.");
        }
    }

    private static void AssertNoSplitSurrogatePair(string visual)
    {
        for (int i = 0; i < visual.Length; i++)
        {
            if (char.IsHighSurrogate(visual[i]))
                Assert.True(i + 1 < visual.Length && char.IsLowSurrogate(visual[i + 1]),
                    $"High surrogate at {i} lost its pair in '{Escape(visual)}'.");
            if (char.IsLowSurrogate(visual[i]))
                Assert.True(i > 0 && char.IsHighSurrogate(visual[i - 1]),
                    $"Low surrogate at {i} lost its pair in '{Escape(visual)}'.");
        }
    }

    // ---- an unpaired surrogate is text, not a crash ----------------------------------------
    [Theory]
    [InlineData("lone high", "\uD83D")]
    [InlineData("lone low", "\uDE00")]
    [InlineData("truncated pair after Hebrew", "שלום\uD83D")]
    [InlineData("stray low surrogate before Hebrew", "\uDE00 שלום")]
    public void UnpairedSurrogateIsClassifiedRatherThanThrowing(string because, string text)
    {
        var visual = BidiAlgorithm.ToVisual(text);
        Assert.True(text.Length == visual.Length, because);
    }

    [Fact]
    public void ClassifierAcceptsALoneSurrogate()
    {
        Assert.Equal(BidiClass.L, BidiClassifier.Classify('\uD83D'));
        Assert.Equal(BidiClass.L, BidiClassifier.Classify('\uDE00'));
    }

    /// <summary>Rule L4 swaps a bracket for its mirror, so compare on the canonical member.</summary>
    private static int MirrorInsensitive(int codePoint)
    {
        if (codePoint > char.MaxValue) return codePoint;
        char c = (char)codePoint;
        char mirrored = BidiMirroring.Mirror(c);
        return Math.Min(c, mirrored);
    }

    private static string Sorted(IEnumerable<int> values) =>
        string.Join(",", values.OrderBy(v => v).Select(v => v.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// A renderer that places glyphs in the order given attaches a mark to whatever precedes it,
    /// so a mark that ends up first, or separated from its base, is a corrupted cluster.
    /// </summary>
    private static void AssertNoOrphanedCombiningMark(string visual)
    {
        for (int i = 0; i < visual.Length; i++)
        {
            if (BidiClassifier.Classify(visual[i]) != BidiClass.NSM) continue;
            Assert.True(i > 0, $"A combining mark opens the visual string '{Escape(visual)}'.");
            Assert.NotEqual(BidiClass.WS, BidiClassifier.Classify(visual[i - 1]));
        }
    }

    private static string Escape(string s) =>
        string.Concat(s.Select(c => c < 0x20 || c > 0x7E ? $"\\u{(int)c:X4}" : c.ToString()));
}
