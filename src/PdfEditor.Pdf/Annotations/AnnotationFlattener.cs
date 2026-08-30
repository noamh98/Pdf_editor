using PdfEditor.Core.Annotations;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PdfEditor.Pdf.Annotations;

/// <summary>
/// Burns annotations into page content so the result can no longer be un-marked.
/// </summary>
/// <remarks>
/// Our own annotations are redrawn through <see cref="AnnotationRenderer"/>, the same routine that
/// produced their appearance streams, so a flattened page is visually identical to the editable one.
/// Annotations written by other applications are drawn from their existing appearance stream when
/// they have one; when they do not, they are left in place rather than silently dropped, and the
/// caller is told how many that was.
/// </remarks>
public static class AnnotationFlattener
{
    public sealed record FlattenResult(int Redrawn, int ForeignDrawn, int ForeignLeftAsAnnotations);

    public static FlattenResult Flatten(PdfPage page, IReadOnlyList<Annotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(annotations);

        int foreignDrawn = ForeignFlattener.DrawExistingAppearances(page);
        int foreignLeft = CountRemainingAnnotations(page);

        int redrawn = 0;
        var ours = annotations.Where(a => !a.IsForeign && !a.Rect.IsEmpty).ToList();
        if (ours.Count > 0)
        {
            double pageHeight = page.Height.Point;
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            gfx.SmoothingMode = XSmoothingMode.HighQuality;
            foreach (var annotation in ours)
            {
                var r = annotation.Rect;
                // PDF user space has its origin at the bottom-left; XGraphics at the top-left.
                AnnotationRenderer.Draw(gfx, annotation,
                    new XRect(r.Left, pageHeight - r.Top, r.Width, r.Height));
                redrawn++;
            }
        }

        page.Elements.Remove("/Annots");
        return new FlattenResult(redrawn, foreignDrawn, foreignLeft);
    }

    private static int CountRemainingAnnotations(PdfPage page)
    {
        var annots = page.Elements.GetArray("/Annots");
        if (annots is null) return 0;
        int count = 0;
        for (int i = 0; i < annots.Elements.Count; i++)
        {
            var dict = annots.Elements.GetDictionary(i);
            if (dict is null) continue;
            if (dict.Elements.ContainsKey(AnnotationSerializer.PrivateKey)) continue;
            if (dict.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N") is null) count++;
        }
        return count;
    }
}
