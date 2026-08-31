using PdfEditor.Core.Files;
using Xunit;

namespace PdfEditor.Core.Tests.Files;

public class SafeFileNameSanitizeTests
{
    [Theory]
    [InlineData("a<b>c:d\"e/f\\g|h?i*j", "a_b_c_d_e_f_g_h_i_j")]
    [InlineData("a/b", "a_b")]
    [InlineData("a\\b", "a_b")]
    public void ReplacesPathSeparatorsAndInvalidCharsWithUnderscore(string input, string expected)
    {
        Assert.Equal(expected, SafeFileName.Sanitize(input));
    }

    [Fact]
    public void StripsControlCharactersInsteadOfReplacingThem()
    {
        Assert.Equal("ab", SafeFileName.Sanitize("a\0b"));
        Assert.Equal("ab", SafeFileName.Sanitize("ab"));
    }

    [Fact]
    public void StripsBidiOverrideCharactersThatCouldDisguiseAnExtension()
    {
        // A right-to-left override can make "evil.exe" display as if it ended in a different extension.
        Assert.Equal("eviltxt.exe", SafeFileName.Sanitize("evil‮txt.exe"));
        Assert.Equal("ab", SafeFileName.Sanitize("a‎b"));
        Assert.Equal("ab", SafeFileName.Sanitize("a‏b"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FallsBackToDefaultWhenInputIsEmptyOrWhitespace(string? input)
    {
        Assert.Equal("document", SafeFileName.Sanitize(input));
    }

    [Fact]
    public void FallsBackToProvidedFallbackWhenInputIsEmpty()
    {
        Assert.Equal("my-fallback", SafeFileName.Sanitize("", "my-fallback"));
    }

    [Fact]
    public void FallsBackWhenInputBecomesEmptyAfterTrimmingDots()
    {
        Assert.Equal("document", SafeFileName.Sanitize(".."));
        Assert.Equal("document", SafeFileName.Sanitize("...."));
    }

    [Theory]
    [InlineData("readme.", "readme")]
    [InlineData("readme..", "readme")]
    [InlineData("  readme  ", "readme")]
    [InlineData("..readme", "readme")]
    public void TrimsTrailingAndLeadingDotsAndSpaces(string input, string expected)
    {
        Assert.Equal(expected, SafeFileName.Sanitize(input));
    }

    [Theory]
    [InlineData("CON", "_CON")]
    [InlineData("con", "_con")]
    [InlineData("NUL", "_NUL")]
    [InlineData("COM1", "_COM1")]
    [InlineData("LPT9", "_LPT9")]
    [InlineData("CON.pdf", "_CON.pdf")]
    public void PrefixesWindowsReservedDeviceNames(string input, string expected)
    {
        Assert.Equal(expected, SafeFileName.Sanitize(input));
    }

    [Fact]
    public void DoesNotTreatANonReservedNameAsReserved()
    {
        Assert.Equal("CONSOLE", SafeFileName.Sanitize("CONSOLE"));
    }

    [Fact]
    public void TruncatesNamesLongerThanMaxComponentLength()
    {
        var candidate = new string('a', 200);
        var result = SafeFileName.Sanitize(candidate);

        Assert.Equal(SafeFileName.MaxComponentLength, result.Length);
        Assert.Equal(candidate[..SafeFileName.MaxComponentLength], result);
    }

    [Fact]
    public void PreservesShortHebrewNamesIntact()
    {
        Assert.Equal("קובץ בעברית.pdf", SafeFileName.Sanitize("קובץ בעברית.pdf"));
    }
}

public class SafeFileNameCombineWithinTests
{
    [Fact]
    public void CombinesADirectoryAndAPlainFileName()
    {
        var dir = Path.GetFullPath("/tmp/pdfeditor-tests-dir");
        var combined = SafeFileName.CombineWithin(dir, "report.pdf");

        Assert.Equal(Path.Combine(dir, "report.pdf"), combined);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../../etc/passwd")]
    [InlineData("a/../../b")]
    [InlineData("..\\..\\secrets.txt")]
    public void NeverEscapesTheTargetDirectoryEvenForTraversalLikeCandidates(string maliciousCandidate)
    {
        // SafeFileName.Sanitize replaces every path separator and reduces bare ".." sequences to the
        // fallback name before CombineWithin ever compares paths, so the result always stays inside
        // the target directory: the traversal attempt is neutralized rather than rejected with a throw.
        var dir = Path.GetFullPath("/tmp/pdfeditor-tests-dir");
        var combined = SafeFileName.CombineWithin(dir, maliciousCandidate);

        var dirWithSeparator = dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar;
        Assert.StartsWith(dirWithSeparator, combined, StringComparison.Ordinal);
    }

    [Fact]
    public void ThrowsForNullOrWhitespaceDirectory()
    {
        Assert.Throws<ArgumentException>(() => SafeFileName.CombineWithin("   ", "report.pdf"));
    }
}

public class SafeFileNameMakeUniqueTests
{
    [Fact]
    public void ReturnsDesiredPathWhenNothingExists()
    {
        var path = Path.Combine("dir", "report.pdf");
        Assert.Equal(path, SafeFileName.MakeUnique(path, _ => false));
    }

    // Paths here are built with Path.Combine rather than written as "/dir/report.pdf". On Windows
    // Path.GetDirectoryName turns a leading "/" into "\\" while Path.Combine leaves it alone, so a
    // literal made the expected and the actual value differ in their very first character — which
    // is what these three did until CI ran them on Windows for the first time.
    [Fact]
    public void AppendsTwoWhenDesiredPathExists()
    {
        var path = Path.Combine("dir", "report.pdf");
        var existing = new HashSet<string> { path };
        var result = SafeFileName.MakeUnique(path, existing.Contains);

        Assert.Equal(Path.Combine("dir", "report (2).pdf"), result);
    }

    [Fact]
    public void KeepsIncrementingUntilAFreeNameIsFound()
    {
        var path = Path.Combine("dir", "report.pdf");
        var existing = new HashSet<string>
        {
            path,
            Path.Combine("dir", "report (2).pdf"),
        };
        var result = SafeFileName.MakeUnique(path, existing.Contains);

        Assert.Equal(Path.Combine("dir", "report (3).pdf"), result);
    }

    [Fact]
    public void WorksForPathsWithoutAnExtension()
    {
        var path = Path.Combine("dir", "report");
        var existing = new HashSet<string> { path };
        var result = SafeFileName.MakeUnique(path, existing.Contains);

        Assert.Equal(Path.Combine("dir", "report (2)"), result);
    }

    [Fact]
    public void UsesFileExistsWhenNoPredicateIsInjected()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pdfeditor-makeunique-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "report.pdf");
        try
        {
            File.WriteAllText(target, "x");
            var result = SafeFileName.MakeUnique(target);
            Assert.Equal(Path.Combine(dir, "report (2).pdf"), result);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

public class SafeFileNameDeriveOutputNameTests
{
    [Fact]
    public void CombinesSourceStemSuffixAndDefaultExtension()
    {
        var result = SafeFileName.DeriveOutputName("/a/b/report.pdf", "final");
        Assert.Equal("report - final.pdf", result);
    }

    [Fact]
    public void SanitizesAnUnsafeSuffix()
    {
        var result = SafeFileName.DeriveOutputName("/a/b/report.pdf", "na/me");
        Assert.Equal("report - na_me.pdf", result);
    }

    [Fact]
    public void UsesAGivenExtensionInsteadOfTheDefault()
    {
        var result = SafeFileName.DeriveOutputName("/a/b/report.pdf", "flattened", ".png");
        Assert.Equal("report - flattened.png", result);
    }

    [Fact]
    public void FallsBackToCopyWhenSuffixIsEmpty()
    {
        var result = SafeFileName.DeriveOutputName("/a/b/report.pdf", "");
        Assert.Equal("report - copy.pdf", result);
    }
}
