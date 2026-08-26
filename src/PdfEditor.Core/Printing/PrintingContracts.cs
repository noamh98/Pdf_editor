namespace PdfEditor.Core.Printing;

public sealed record PrinterInfo(string Name, bool IsDefault, bool SupportsDuplex, bool SupportsColor);

public sealed record PrintJobRequest(
    string PrinterName,
    string DocumentPath,
    PrintSequence Sequence,
    int Copies = 1,
    bool ScaleToFit = true);

public sealed record PrintJobResult(bool Succeeded, int PagesSent, string? ErrorMessage);

/// <summary>
/// Sends a prepared print job to a Windows printer.
/// </summary>
/// <remarks>
/// Implementations run on Windows only. The caller checks <see cref="IsSupported"/> first; on any
/// other platform the service reports itself unavailable rather than throwing.
/// </remarks>
public interface IPrintService
{
    bool IsSupported { get; }

    Task<IReadOnlyList<PrinterInfo>> GetPrintersAsync(CancellationToken cancellationToken = default);

    Task<PrintJobResult> PrintAsync(PrintJobRequest request, IProgress<double>? progress,
        CancellationToken cancellationToken = default);
}
