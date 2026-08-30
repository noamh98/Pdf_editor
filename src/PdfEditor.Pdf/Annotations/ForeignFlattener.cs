using System.Globalization;
using System.Text;
using PdfSharp.Pdf;

namespace PdfEditor.Pdf.Annotations;

/// <summary>
/// Draws the existing appearance streams of annotations written by other applications into the
/// page content, so exporting a final copy flattens them too.
/// </summary>
/// <remarks>
/// The appearance is a Form XObject. Flattening it means registering it in the page's resource
/// dictionary and appending a content stream that positions and invokes it, mapping the form's
/// bounding box onto the annotation rectangle exactly as a viewer would (PDF 32000-1, 12.5.5).
/// </remarks>
internal static class ForeignFlattener
{
    /// <summary>
    /// Draws what it can and returns the positions in <c>/Annots</c> it drew, so the caller can
    /// remove exactly those and leave the rest of the array alone.
    /// </summary>
    public static IReadOnlySet<int> DrawExistingAppearances(PdfPage page)
    {
        var annots = page.Elements.GetArray("/Annots");
        if (annots is null) return new HashSet<int>();

        var resources = GetOrCreate(page.Elements, "/Resources", page.Owner);
        var xobjects = GetOrCreate(resources.Elements, "/XObject", page.Owner);

        var content = new StringBuilder();
        var drawnIndices = new HashSet<int>();
        int drawn = 0;

        for (int i = 0; i < annots.Elements.Count; i++)
        {
            var annot = annots.Elements.GetDictionary(i);
            if (annot is null) continue;
            if (annot.Elements.ContainsKey(AnnotationSerializer.PrivateKey)) continue; // ours, redrawn separately
            if (IsHidden(annot)) continue;

            var form = ResolveNormalAppearance(annot);
            if (form is null || form.Stream is null) continue;

            var rect = annot.Elements.GetRectangle("/Rect");
            if (rect.Width <= 0 || rect.Height <= 0) continue;

            var bbox = form.Elements.GetRectangle("/BBox");
            if (bbox.Width <= 0 || bbox.Height <= 0) continue;

            string name = "/PdfEditorFlat" + drawn.ToString(CultureInfo.InvariantCulture);
            xobjects.Elements[name] = (PdfItem?)form.Reference ?? form;

            // Map the form's bounding box onto the annotation rectangle.
            double sx = rect.Width / bbox.Width;
            double sy = rect.Height / bbox.Height;
            double tx = rect.X1 - bbox.X1 * sx;
            double ty = rect.Y1 - bbox.Y1 * sy;

            content.Append(CultureInfo.InvariantCulture,
                $"q {sx:0.######} 0 0 {sy:0.######} {tx:0.######} {ty:0.######} cm {name} Do Q\n");
            drawnIndices.Add(i);
            drawn++;
        }

        if (drawn == 0) return drawnIndices;

        var appended = page.Contents.AppendContent();
        appended.CreateStream(Encoding.ASCII.GetBytes(content.ToString()));
        return drawnIndices;
    }

    private static bool IsHidden(PdfDictionary annot)
    {
        const int hidden = 2, noView = 32;
        int flags = annot.Elements.GetInteger("/F");
        return (flags & hidden) != 0 || (flags & noView) != 0;
    }

    /// <summary>
    /// Resolves <c>/AP /N</c>, following the appearance sub-dictionary selected by <c>/AS</c> when
    /// the normal appearance is a state dictionary rather than a stream.
    /// </summary>
    private static PdfDictionary? ResolveNormalAppearance(PdfDictionary annot)
    {
        var normal = annot.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
        if (normal is null) return null;
        if (normal.Stream is not null) return normal;

        var state = annot.Elements.GetName("/AS");
        if (!string.IsNullOrEmpty(state) && normal.Elements.GetDictionary(state) is { Stream: not null } selected)
            return selected;

        foreach (var key in normal.Elements.Keys)
            if (normal.Elements.GetDictionary(key) is { Stream: not null } first)
                return first;
        return null;
    }

    private static PdfDictionary GetOrCreate(PdfDictionary.DictionaryElements elements, string key, PdfDocument owner)
    {
        if (elements.GetDictionary(key) is { } existing) return existing;
        var created = new PdfDictionary(owner);
        elements[key] = created;
        return created;
    }
}
