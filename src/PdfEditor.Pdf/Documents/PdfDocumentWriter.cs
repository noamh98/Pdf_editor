using PdfEditor.Core.Annotations;
using PdfEditor.Core.Documents;
using PdfEditor.Core.Files;
using PdfEditor.Pdf.Annotations;
using PdfEditor.Pdf.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfEditor.Pdf.Documents;

/// <summary>
/// Writes a document back to disk, either keeping annotations editable or flattening them.
/// </summary>
/// <remarks>
/// The source is never modified. Work happens on a fresh copy parsed from the bytes the document
/// was loaded from, and the result reaches disk through <see cref="AtomicFileWriter"/>, so an
/// interrupted or failed save leaves any existing file exactly as it was.
/// </remarks>
public sealed class PdfDocumentWriter : IPdfDocumentWriter
{
    public async Task SaveAsync(IPdfDocument document, SaveRequest request, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(request);
        if (document is not PdfSharpDocument source)
            throw new ArgumentException("Unsupported document implementation.", nameof(document));

        PdfFonts.EnsureRegistered();
        var bytes = await Task.Run(() => Build(source, request, progress, cancellationToken), cancellationToken)
            .ConfigureAwait(false);

        await AtomicFileWriter.WriteAsync(request.TargetPath, bytes, cancellationToken).ConfigureAwait(false);
        progress?.Report(1.0);
    }

    private static byte[] Build(PdfSharpDocument source, SaveRequest request, IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var input = new MemoryStream(source.SourceBytes, writable: false);
        using var working = PdfReader.Open(input, PdfDocumentOpenMode.Modify);

        var plan = PagePlan.Create(working.PageCount, request.PageEdits);
        cancellationToken.ThrowIfCancellationRequested();

        // Reordering imports pages into a fresh document, and PDFsharp only allows importing from a
        // document opened read-only, so a second parse of the same bytes is needed for that path.
        PdfDocument? importSource = null;
        PdfDocument output;
        if (plan.RequiresReorder)
        {
            var importStream = new MemoryStream(source.SourceBytes, writable: false);
            importSource = PdfReader.Open(importStream, PdfDocumentOpenMode.Import);
            output = ApplyPlanByRebuilding(working, importSource, plan);
        }
        else
        {
            output = ApplyPlanInPlace(working, plan);
        }

        try
        {
            // Anything this application wrote earlier is replaced; foreign annotations are left alone.
            for (int i = 0; i < output.PageCount; i++) AnnotationWriter.RemoveOwnAnnotations(output.Pages[i]);

            var byPage = request.Annotations
                .Where(a => !a.IsForeign)
                .GroupBy(a => plan.MapToOutput(a.PageIndex))
                .Where(g => g.Key >= 0)
                .ToDictionary(g => g.Key, g => g.ToList());

            for (int i = 0; i < output.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = output.Pages[i];
                var annotations = byPage.GetValueOrDefault(i) ?? [];

                if (request.Mode == SaveMode.Flattened)
                    AnnotationFlattener.Flatten(page, annotations);
                else
                    foreach (var annotation in annotations)
                        AnnotationWriter.Write(output, page, annotation);

                progress?.Report(output.PageCount == 0 ? 1 : (double)(i + 1) / output.PageCount * 0.95);
            }

            cancellationToken.ThrowIfCancellationRequested();
            using var buffer = new MemoryStream();
            output.Save(buffer, closeStream: false);
            return buffer.ToArray();
        }
        finally
        {
            if (!ReferenceEquals(output, working)) output.Dispose();
            importSource?.Dispose();
        }
    }

    private static PdfDocument ApplyPlanInPlace(PdfDocument working, PagePlan plan)
    {
        foreach (int index in plan.DeletedSourceIndices.OrderByDescending(i => i))
            working.Pages.RemoveAt(index);

        for (int i = 0; i < working.PageCount; i++)
        {
            int sourceIndex = plan.OutputToSource[i];
            if (plan.RotationBySource.TryGetValue(sourceIndex, out int delta) && delta != 0)
                working.Pages[i].Rotate = PdfSharpDocument.NormalizeRotation(working.Pages[i].Rotate + delta);
        }
        return working;
    }

    /// <summary>
    /// Reordering is done by importing pages into a fresh document in the requested order, which is
    /// the only page move PDFsharp supports reliably. Imported pages keep their annotations.
    /// </summary>
    private static PdfDocument ApplyPlanByRebuilding(PdfDocument metadataSource, PdfDocument importSource, PagePlan plan)
    {
        var rebuilt = new PdfDocument();
        rebuilt.Info.Creator = metadataSource.Info.Creator;
        rebuilt.Info.Title = metadataSource.Info.Title;
        rebuilt.Info.Subject = metadataSource.Info.Subject;

        foreach (int sourceIndex in plan.OutputToSource)
        {
            var added = rebuilt.AddPage(importSource.Pages[sourceIndex]);
            if (plan.RotationBySource.TryGetValue(sourceIndex, out int delta) && delta != 0)
                added.Rotate = PdfSharpDocument.NormalizeRotation(added.Rotate + delta);
        }
        return rebuilt;
    }

    /// <summary>Resolves the requested page edits into a single ordered plan.</summary>
    private sealed class PagePlan
    {
        public required IReadOnlyList<int> OutputToSource { get; init; }
        public required IReadOnlySet<int> DeletedSourceIndices { get; init; }
        public required IReadOnlyDictionary<int, int> RotationBySource { get; init; }
        public required bool RequiresReorder { get; init; }

        private Dictionary<int, int>? _sourceToOutput;

        public int MapToOutput(int sourceIndex)
        {
            _sourceToOutput ??= OutputToSource
                .Select((source, output) => (source, output))
                .ToDictionary(x => x.source, x => x.output);
            return _sourceToOutput.GetValueOrDefault(sourceIndex, -1);
        }

        public static PagePlan Create(int pageCount, IReadOnlyList<PageEdit>? edits)
        {
            var deleted = new HashSet<int>();
            var rotation = new Dictionary<int, int>();
            IReadOnlyList<int>? explicitOrder = null;

            foreach (var edit in edits ?? [])
            {
                switch (edit)
                {
                    case PageEdit.Delete d when d.PageIndex >= 0 && d.PageIndex < pageCount:
                        deleted.Add(d.PageIndex);
                        break;
                    case PageEdit.Rotate r when r.PageIndex >= 0 && r.PageIndex < pageCount:
                        rotation[r.PageIndex] = rotation.GetValueOrDefault(r.PageIndex) + r.DegreesClockwise;
                        break;
                    case PageEdit.Reorder o:
                        explicitOrder = o.NewOrder.Where(i => i >= 0 && i < pageCount).Distinct().ToList();
                        break;
                }
            }

            var order = explicitOrder ?? Enumerable.Range(0, pageCount).ToList();
            var final = order.Where(i => !deleted.Contains(i)).ToList();
            if (final.Count == 0 && pageCount > 0)
            {
                // Refusing to produce a zero-page document is safer than writing an unopenable file.
                final = [order.FirstOrDefault()];
                deleted.Remove(final[0]);
            }

            bool reorder = explicitOrder is not null &&
                           !final.SequenceEqual(Enumerable.Range(0, pageCount).Where(i => !deleted.Contains(i)));

            return new PagePlan
            {
                OutputToSource = final,
                DeletedSourceIndices = deleted,
                RotationBySource = rotation,
                RequiresReorder = reorder
            };
        }
    }
}
