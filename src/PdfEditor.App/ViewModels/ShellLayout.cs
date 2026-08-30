namespace PdfEditor.App.ViewModels;

/// <summary>How much horizontal room the shell has to work with.</summary>
public enum LayoutSize
{
    /// <summary>Narrow window: one side panel at a time, and it floats over the document.</summary>
    Compact,

    /// <summary>Both side panels fit, but the chrome drops its text labels.</summary>
    Medium,

    /// <summary>Everything fits, including the labels on the primary commands.</summary>
    Wide
}

/// <summary>
/// The breakpoints of the shell, kept as pure functions so the layout rules can be tested without
/// a window. Widths are in device independent pixels, which is what Avalonia reports.
/// </summary>
public static class ShellLayout
{
    /// <summary>Below this width the side panels stop sharing the row with the document.</summary>
    public const double CompactBelow = 900;

    /// <summary>At or above this width the command bar can afford text next to its icons.</summary>
    public const double WideFrom = 1180;

    /// <summary>The narrowest window the shell is designed for.</summary>
    public const double MinimumWindowWidth = 680;

    public static LayoutSize Classify(double width) => width switch
    {
        < CompactBelow => LayoutSize.Compact,
        < WideFrom => LayoutSize.Medium,
        _ => LayoutSize.Wide
    };

    /// <summary>The thumbnail rail narrows before it disappears.</summary>
    public static double ThumbnailRailWidth(LayoutSize size) => size switch
    {
        LayoutSize.Compact => 148,
        LayoutSize.Medium => 168,
        _ => 200
    };

    public static double ThumbnailTileWidth(LayoutSize size) => ThumbnailRailWidth(size) - 46;

    /// <summary>The properties panel keeps a usable width even when it floats.</summary>
    public static double PropertiesWidth(LayoutSize size) => size switch
    {
        LayoutSize.Compact => 236,
        LayoutSize.Medium => 248,
        _ => 268
    };

    /// <summary>Breathing room around the pages; a narrow window cannot spare much.</summary>
    public static double CanvasPadding(LayoutSize size) => size switch
    {
        LayoutSize.Compact => 10,
        LayoutSize.Medium => 18,
        _ => 28
    };

    /// <summary>
    /// Whether the thumbnail rail should be open by default at this width. Compact windows start
    /// closed so the document keeps the room; the user can still open the rail by hand.
    /// </summary>
    public static bool ThumbnailsOpenByDefault(LayoutSize size) => size != LayoutSize.Compact;

    /// <summary>Text labels next to the primary command icons only fit in a wide window.</summary>
    public static bool ShowsCommandLabels(LayoutSize size) => size == LayoutSize.Wide;

    /// <summary>The search field sits in the command bar unless the window is compact.</summary>
    public static bool SearchFitsInCommandBar(LayoutSize size) => size != LayoutSize.Compact;

    /// <summary>
    /// Below medium the document operations move into an overflow menu rather than being cut off.
    /// </summary>
    public static bool ShowsDocumentOpsInline(LayoutSize size) => size != LayoutSize.Compact;

    /// <summary>A floating side panel is the compact answer to two panels and no room.</summary>
    public static bool PanelsFloat(LayoutSize size) => size == LayoutSize.Compact;
}
