using PdfEditor.Core.Documents;
using PdfEditor.Core.Files;
using PdfEditor.Core.Printing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfEditor.Pdf.Documents;

/// <summary>
/// Turns a <see cref="PrintSequence"/> into a temporary PDF that can be handed to the printer.
/// </summary>
/// <remarks>
/// The source document is never modified and nothing is written to the user's folders: the job
/// lives in the application's temporary directory and is deleted once printing finishes or fails.
/// </remarks>
public sealed class PrintJobBuilder : IPrintJobBuilder
{
    public async Task<string> BuildAsync(IPdfDocument document, PrintSequence sequence,
        string temporaryDirectory, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sequence);
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryDirectory);
        if (document is not PdfSharpDocument source)
            throw new ArgumentException("Unsupported document implementation.", nameof(document));
        if (sequence.Slots.Count == 0)
            throw new InvalidOperationException("The print sequence is empty.");

        Directory.CreateDirectory(temporaryDirectory);
        var targetPath = Path.Combine(temporaryDirectory,
            $"print-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}.pdf".Replace(":", "-"));

        var bytes = await Task.Run(() =>
        {
            using var input = new MemoryStream(source.SourceBytes, writable: false);
            using var origin = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            using var job = new PdfDocument();

            foreach (var slot in sequence.Slots)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (slot.Kind == PrintSlotKind.Content && slot.SourcePageIndex is { } index
                    && index >= 0 && index < origin.PageCount)
                {
                    job.AddPage(origin.Pages[index]);
                    continue;
                }

                // A blank separator matching the size and rotation of the page it follows, so the
                // sheet it occupies has the same geometry as the content around it.
                var blank = job.AddPage();
                blank.Width = PdfSharp.Drawing.XUnit.FromPoint(slot.WidthPoints);
                blank.Height = PdfSharp.Drawing.XUnit.FromPoint(slot.HeightPoints);
                blank.Rotate = PdfSharpDocument.NormalizeRotation(slot.Rotation);
            }

            using var buffer = new MemoryStream();
            job.Save(buffer, closeStream: false);
            return buffer.ToArray();
        }, cancellationToken).ConfigureAwait(false);

        await AtomicFileWriter.WriteAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);
        return targetPath;
    }
}
