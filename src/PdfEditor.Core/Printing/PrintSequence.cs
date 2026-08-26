namespace PdfEditor.Core.Printing;

public enum PageOrientationKind
{
    Portrait,
    Landscape
}

/// <summary>What a single sheet-side in the generated print job holds.</summary>
public enum PrintSlotKind
{
    /// <summary>A page taken from the source document.</summary>
    Content,
    /// <summary>A blank page that already existed in the source document.</summary>
    ExistingBlank,
    /// <summary>A blank page inserted by the application to force a page break.</summary>
    InsertedBlank
}

/// <summary>Description of one source page, supplied by the PDF layer.</summary>
public sealed record PrintPageInfo(
    int SourcePageIndex,
    double WidthPoints,
    double HeightPoints,
    int Rotation,
    bool IsBlank)
{
    public PageOrientationKind Orientation
    {
        get
        {
            bool swapped = Rotation is 90 or 270 or -90;
            double w = swapped ? HeightPoints : WidthPoints;
            double h = swapped ? WidthPoints : HeightPoints;
            return w > h ? PageOrientationKind.Landscape : PageOrientationKind.Portrait;
        }
    }
}

/// <summary>One entry of the generated print job.</summary>
public sealed record PrintSlot(
    PrintSlotKind Kind,
    int? SourcePageIndex,
    double WidthPoints,
    double HeightPoints,
    int Rotation)
{
    public bool IsBlank => Kind != PrintSlotKind.Content;
}

public sealed record PrintSequence(
    IReadOnlyList<PrintSlot> Slots,
    int ContentPageCount,
    int InsertedBlankCount,
    int ExistingBlankCount)
{
    public int TotalPageCount => Slots.Count;

    /// <summary>
    /// Number of physical sheets the job is expected to consume. With duplex forced by the driver
    /// two consecutive pages share one sheet, so the count is rounded up.
    /// </summary>
    public int EstimatedSheets(bool duplexForced) =>
        duplexForced ? (TotalPageCount + 1) / 2 : TotalPageCount;
}

public sealed record PrintSequenceOptions
{
    /// <summary>
    /// When true, a blank page is inserted between content pages so that a driver or print server
    /// that forces duplex still prints one content page per sheet.
    /// </summary>
    public bool SeparateSheetsPerContentPage { get; init; }

    /// <summary>Zero-based source page indices to print, in the order they should print.</summary>
    public IReadOnlyList<int>? SelectedPageIndices { get; init; }
}
