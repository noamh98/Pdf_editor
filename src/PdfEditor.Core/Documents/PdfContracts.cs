using PdfEditor.Core.Annotations;
using PdfEditor.Core.Printing;

namespace PdfEditor.Core.Documents;

/// <summary>Immutable description of one page of an open document.</summary>
public sealed record PdfPageInfo(
    int Index,
    double WidthPoints,
    double HeightPoints,
    int Rotation)
{
    /// <summary>Size after the page's /Rotate entry has been applied.</summary>
    public (double Width, double Height) DisplaySize =>
        Rotation is 90 or 270 or -90 ? (HeightPoints, WidthPoints) : (WidthPoints, HeightPoints);

    public PageOrientationKind Orientation =>
        DisplaySize.Width > DisplaySize.Height ? PageOrientationKind.Landscape : PageOrientationKind.Portrait;
}

/// <summary>Why a document could not be opened. Mapped to a Hebrew message by the UI.</summary>
public enum PdfOpenError
{
    None = 0,
    FileNotFound,
    AccessDenied,
    NotAPdf,
    Corrupted,
    PasswordRequired,
    UnsupportedEncryption,
    Unknown
}

public sealed class PdfOpenException(PdfOpenError error, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public PdfOpenError Error { get; } = error;
}

/// <summary>A rendered page bitmap.</summary>
public sealed record RenderedPage(int PageIndex, int PixelWidth, int PixelHeight, byte[] BgraPixels, double Scale);

public sealed record RenderRequest(
    int PageIndex,
    double Scale,
    bool IncludeAnnotations = true,
    int? MaxPixelWidth = null,
    int? MaxPixelHeight = null);

/// <summary>An open document. Implementations are not thread-safe unless stated otherwise.</summary>
public interface IPdfDocument : IAsyncDisposable
{
    /// <summary>Path the document was loaded from, or null for a document built in memory.</summary>
    string? SourcePath { get; }

    int PageCount { get; }

    IReadOnlyList<PdfPageInfo> Pages { get; }

    /// <summary>Fingerprint of the bytes the document was loaded from. Used for the OCR cache.</summary>
    string Fingerprint { get; }

    /// <summary>True when the source file is encrypted or carries permission restrictions.</summary>
    bool IsProtected { get; }

    /// <summary>Annotations recognised on load, including ones produced by other applications.</summary>
    IReadOnlyList<Annotation> LoadAnnotations();

    /// <summary>Renders one page. Safe to call from a background thread.</summary>
    Task<RenderedPage> RenderAsync(RenderRequest request, CancellationToken cancellationToken);

    /// <summary>Renders a page to PNG bytes, used as OCR input.</summary>
    Task<byte[]> RenderToPngAsync(int pageIndex, int dpi, CancellationToken cancellationToken);

    /// <summary>Heuristic used by the print pipeline: does this page contain any visible content?</summary>
    Task<bool> IsPageBlankAsync(int pageIndex, CancellationToken cancellationToken);
}

/// <summary>Opens PDF documents.</summary>
public interface IPdfDocumentLoader
{
    Task<IPdfDocument> OpenAsync(string path, CancellationToken cancellationToken);
    Task<IPdfDocument> OpenAsync(Stream stream, string? sourcePath, CancellationToken cancellationToken);
}

/// <summary>How a document is written back out.</summary>
public enum SaveMode
{
    /// <summary>Annotations stay as editable PDF annotation objects.</summary>
    Editable,
    /// <summary>Annotations are drawn into the page content and removed.</summary>
    Flattened
}

public sealed record SaveRequest(
    string TargetPath,
    SaveMode Mode,
    IReadOnlyList<Annotation> Annotations,
    IReadOnlyList<PageEdit>? PageEdits = null);

/// <summary>A structural change applied when the document is written out.</summary>
public abstract record PageEdit
{
    public sealed record Delete(int PageIndex) : PageEdit;
    public sealed record Rotate(int PageIndex, int DegreesClockwise) : PageEdit;
    /// <summary>New order expressed as source page indices.</summary>
    public sealed record Reorder(IReadOnlyList<int> NewOrder) : PageEdit;
}

/// <summary>Writes documents back to disk.</summary>
public interface IPdfDocumentWriter
{
    /// <summary>
    /// Writes the document. The implementation must write to a temporary file and replace the
    /// target atomically so an interrupted save cannot damage an existing file.
    /// </summary>
    Task SaveAsync(IPdfDocument document, SaveRequest request, IProgress<double>? progress,
        CancellationToken cancellationToken);
}

public sealed record MergeSource(string Path, IReadOnlyList<int>? PageIndices = null);

public sealed record SplitRequest(string SourcePath, string OutputDirectory, SplitMode Mode,
    IReadOnlyList<PageRange>? Ranges = null);

public enum SplitMode
{
    /// <summary>One output file containing the selected ranges.</summary>
    ExtractRanges,
    /// <summary>One output file per selected page.</summary>
    OnePerPage
}

/// <summary>Document-level operations that always produce new files.</summary>
public interface IDocumentAssembler
{
    Task MergeAsync(IReadOnlyList<MergeSource> sources, string targetPath,
        IProgress<double>? progress, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> SplitAsync(SplitRequest request,
        IProgress<double>? progress, CancellationToken cancellationToken);
}

/// <summary>Materialises a <see cref="PrintSequence"/> into a temporary PDF ready for printing.</summary>
public interface IPrintJobBuilder
{
    /// <summary>
    /// Writes the sequence to a temporary file and returns its path. The caller owns the file and
    /// must delete it; implementations also register it for cleanup on the next start.
    /// </summary>
    Task<string> BuildAsync(IPdfDocument document, PrintSequence sequence,
        string temporaryDirectory, CancellationToken cancellationToken);
}
