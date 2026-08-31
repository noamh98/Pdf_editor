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

        var foreignDrawn = ForeignFlattener.DrawExistingAppearances(page);

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

        int foreignLeft = RemoveWhatWasBurnedIn(page, foreignDrawn);
        return new FlattenResult(redrawn, foreignDrawn.Count, foreignLeft);
    }

    /// <summary>
    /// Drops the annotations whose ink is now part of the page content and returns how many were
    /// left behind.
    /// </summary>
    /// <remarks>
    /// Ours go because they have just been redrawn, and a foreign annotation goes once its
    /// appearance stream has been drawn into the content. Everything else stays exactly as it was.
    /// Clearing <c>/Annots</c> wholesale instead would silently destroy every mark this code
    /// cannot draw — a comment or a stamp with no appearance stream, a hidden annotation, and
    /// every hyperlink on the page — none of which flattening puts anything in the place of.
    /// </remarks>
    private static int RemoveWhatWasBurnedIn(PdfPage page, IReadOnlySet<int> drawn)
    {
        var annots = page.Elements.GetArray("/Annots");
        if (annots is null) return 0;

        for (int i = annots.Elements.Count - 1; i >= 0; i--)
        {
            var dict = annots.Elements.GetDictionary(i);
            if (dict is null || drawn.Contains(i) ||
                dict.Elements.ContainsKey(AnnotationSerializer.PrivateKey))
                annots.Elements.RemoveAt(i);
        }

        int remaining = annots.Elements.Count;
        if (remaining == 0) page.Elements.Remove("/Annots");
        return remaining;
    }
}
