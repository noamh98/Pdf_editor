using PdfEditor.Core.Documents;
using PdfEditor.Core.Files;
using PdfEditor.Pdf.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfEditor.Pdf.Documents;

/// <summary>
/// Combines and separates documents. Every operation writes a new file and leaves its inputs
/// untouched.
/// </summary>
public sealed class DocumentAssembler : IDocumentAssembler
{
    public async Task MergeAsync(IReadOnlyList<MergeSource> sources, string targetPath,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        if (sources.Count == 0) throw new ArgumentException("At least one source is required.", nameof(sources));

        PdfFonts.EnsureRegistered();
        var bytes = await Task.Run(() =>
        {
            using var merged = new PdfDocument();
            int total = 0;

            for (int s = 0; s < sources.Count; s++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = sources[s];
                using var input = OpenForImport(source.Path);

                var indices = source.PageIndices
                    ?? Enumerable.Range(0, input.PageCount).ToList();

                foreach (int index in indices)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (index < 0 || index >= input.PageCount) continue;
                    // Pages are imported one at a time rather than by loading whole documents into
                    // memory, so a large merge stays bounded.
                    merged.AddPage(input.Pages[index]);
                    total++;
                }
                progress?.Report((double)(s + 1) / sources.Count * 0.95);
            }

            if (total == 0) throw new InvalidOperationException("The merge selected no pages.");

            using var buffer = new MemoryStream();
            merged.Save(buffer, closeStream: false);
            return buffer.ToArray();
        }, cancellationToken).ConfigureAwait(false);

        await AtomicFileWriter.WriteAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);
        progress?.Report(1.0);
    }

    public async Task<IReadOnlyList<string>> SplitAsync(SplitRequest request, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        PdfFonts.EnsureRegistered();
        Directory.CreateDirectory(request.OutputDirectory);

        using var source = OpenForImport(request.SourcePath);
        var stem = Path.GetFileNameWithoutExtension(request.SourcePath);
        var written = new List<string>();

        if (request.Mode == SplitMode.OnePerPage)
        {
            for (int i = 0; i < source.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = SafeFileName.Sanitize($"{stem} - עמוד {i + 1}.pdf");
                var path = SafeFileName.MakeUnique(SafeFileName.CombineWithin(request.OutputDirectory, name));
                await WriteSubsetAsync(source, [i], path, cancellationToken).ConfigureAwait(false);
                written.Add(path);
                progress?.Report((double)(i + 1) / source.PageCount);
            }
            return written;
        }

        var pageNumbers = (request.Ranges ?? [])
            .SelectMany(r => Enumerable.Range(r.Start, Math.Max(0, r.Count)))
            .Where(p => p >= 1 && p <= source.PageCount)
            .Distinct()
            .OrderBy(p => p)
            .ToList();
        if (pageNumbers.Count == 0) throw new InvalidOperationException("The requested range selected no pages.");

        var label = PageRangeParser.Format(pageNumbers);
        var outputName = SafeFileName.Sanitize($"{stem} - עמודים {label}.pdf");
        var outputPath = SafeFileName.MakeUnique(SafeFileName.CombineWithin(request.OutputDirectory, outputName));
        await WriteSubsetAsync(source, pageNumbers.Select(p => p - 1).ToList(), outputPath, cancellationToken)
            .ConfigureAwait(false);
        written.Add(outputPath);
        progress?.Report(1.0);
        return written;
    }

    private static async Task WriteSubsetAsync(PdfDocument source, IReadOnlyList<int> pageIndices,
        string targetPath, CancellationToken cancellationToken)
    {
        var bytes = await Task.Run(() =>
        {
            using var subset = new PdfDocument();
            foreach (int index in pageIndices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                subset.AddPage(source.Pages[index]);
            }
            using var buffer = new MemoryStream();
            subset.Save(buffer, closeStream: false);
            return buffer.ToArray();
        }, cancellationToken).ConfigureAwait(false);

        await AtomicFileWriter.WriteAsync(targetPath, bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Opens a file for import, translating failures into a typed error.</summary>
    internal static PdfDocument OpenForImport(string path)
    {
        try
        {
            if (!File.Exists(path)) throw new PdfOpenException(PdfOpenError.FileNotFound, path);
            var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            if (document.PageCount == 0)
            {
                document.Dispose();
                throw new PdfOpenException(PdfOpenError.Corrupted, "The document contains no pages.");
            }
            return document;
        }
        catch (PdfOpenException) { throw; }
        catch (UnauthorizedAccessException e)
        {
            throw new PdfOpenException(PdfOpenError.AccessDenied, path, e);
        }
        catch (PdfReaderException e) when (e.Message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            throw new PdfOpenException(PdfOpenError.PasswordRequired, path, e);
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            // The parser reports some malformations with a bare Exception; a PDF is untrusted input.
            throw new PdfOpenException(PdfOpenError.Corrupted, path, e);
        }
    }
}
