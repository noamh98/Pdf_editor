using PdfEditor.App.ViewModels;
using Xunit;

namespace PdfEditor.App.Tests;

/// <summary>
/// The breakpoint rules on their own. These need no window, which is the point of keeping them in a
/// pure class: the layout can be reasoned about and regression tested without rendering anything.
/// </summary>
public class ShellLayoutTests
{
    [Theory]
    [InlineData(480, LayoutSize.Compact)]
    [InlineData(680, LayoutSize.Compact)]
    [InlineData(899.9, LayoutSize.Compact)]
    [InlineData(900, LayoutSize.Medium)]
    [InlineData(1179.9, LayoutSize.Medium)]
    [InlineData(1180, LayoutSize.Wide)]
    [InlineData(2560, LayoutSize.Wide)]
    public void WidthsFallIntoTheExpectedSizeClass(double width, LayoutSize expected) =>
        Assert.Equal(expected, ShellLayout.Classify(width));

    [Fact]
    public void TheThumbnailRailNarrowsBeforeItDisappears()
    {
        Assert.True(ShellLayout.ThumbnailRailWidth(LayoutSize.Compact)
                    < ShellLayout.ThumbnailRailWidth(LayoutSize.Medium));
        Assert.True(ShellLayout.ThumbnailRailWidth(LayoutSize.Medium)
                    < ShellLayout.ThumbnailRailWidth(LayoutSize.Wide));
    }

    [Fact]
    public void EveryTileStillFitsInsideItsRail()
    {
        foreach (var size in new[] { LayoutSize.Compact, LayoutSize.Medium, LayoutSize.Wide })
        {
            Assert.True(ShellLayout.ThumbnailTileWidth(size) > 0);
            Assert.True(ShellLayout.ThumbnailTileWidth(size) < ShellLayout.ThumbnailRailWidth(size));
        }
    }

    [Fact]
    public void ThePropertiesPanelNeverBecomesUnusablyNarrow()
    {
        foreach (var size in new[] { LayoutSize.Compact, LayoutSize.Medium, LayoutSize.Wide })
            Assert.True(ShellLayout.PropertiesWidth(size) >= 220);
    }

    [Fact]
    public void ANarrowWindowGivesTheDocumentTheRoom()
    {
        Assert.False(ShellLayout.ThumbnailsOpenByDefault(LayoutSize.Compact));
        Assert.True(ShellLayout.ThumbnailsOpenByDefault(LayoutSize.Medium));
        Assert.True(ShellLayout.ThumbnailsOpenByDefault(LayoutSize.Wide));
        Assert.True(ShellLayout.PanelsFloat(LayoutSize.Compact));
        Assert.False(ShellLayout.PanelsFloat(LayoutSize.Wide));
    }

    [Fact]
    public void LabelsAndInlineOperationsOnlyAppearWhereTheyFit()
    {
        Assert.True(ShellLayout.ShowsCommandLabels(LayoutSize.Wide));
        Assert.False(ShellLayout.ShowsCommandLabels(LayoutSize.Medium));
        Assert.False(ShellLayout.ShowsCommandLabels(LayoutSize.Compact));

        Assert.True(ShellLayout.SearchFitsInCommandBar(LayoutSize.Medium));
        Assert.False(ShellLayout.SearchFitsInCommandBar(LayoutSize.Compact));

        Assert.True(ShellLayout.ShowsDocumentOpsInline(LayoutSize.Medium));
        Assert.False(ShellLayout.ShowsDocumentOpsInline(LayoutSize.Compact));
    }

    [Fact]
    public void TheNarrowestSupportedWindowIsStillClassifiedAndPadded()
    {
        Assert.Equal(LayoutSize.Compact, ShellLayout.Classify(ShellLayout.MinimumWindowWidth));
        Assert.True(ShellLayout.CanvasPadding(LayoutSize.Compact) > 0);
        Assert.True(ShellLayout.CanvasPadding(LayoutSize.Compact)
                    < ShellLayout.CanvasPadding(LayoutSize.Wide));
    }
}
