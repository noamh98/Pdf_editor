using PdfEditor.Core.Annotations;
using PdfSharp.Pdf;

namespace PdfEditor.Pdf.Annotations;

/// <summary>Reads annotations back out of a document.</summary>
public static class AnnotationReader
{
    /// <summary>
    /// Returns every annotation on the page. Annotations this application wrote come back as the
    /// exact model that produced them; anything else is reported with
    /// <see cref="Annotation.IsForeign"/> set so the UI can show it without offering to edit it.
    /// </summary>
    public static IReadOnlyList<Annotation> Read(PdfPage page, int pageIndex)
    {
        ArgumentNullException.ThrowIfNull(page);
        var annots = page.Elements.GetArray("/Annots");
        if (annots is null) return [];

        var result = new List<Annotation>(annots.Elements.Count);
        for (int i = 0; i < annots.Elements.Count; i++)
        {
            var dict = annots.Elements.GetDictionary(i);
            if (dict is null) continue;

            var payload = dict.Elements.GetString(AnnotationSerializer.PrivateKey);
            var restored = AnnotationSerializer.Deserialize(payload);
            if (restored is not null)
            {
                restored.PageIndex = pageIndex;
                result.Add(restored);
                continue;
            }

            var foreign = ToForeign(dict, pageIndex);
            if (foreign is not null) result.Add(foreign);
        }
        return result;
    }

    /// <summary>
    /// Represents an annotation produced by another application. Only its position and colour are
    /// interpreted; it is never redrawn or rewritten, so the original object survives a save intact.
    /// </summary>
    private static Annotation? ToForeign(PdfDictionary dict, int pageIndex)
    {
        var rect = dict.Elements.GetRectangle("/Rect");
        if (rect.Width <= 0 && rect.Height <= 0) return null;

        var subtype = dict.Elements.GetName("/Subtype");
        // Popup and Link objects are structural, not marks a reviewer placed.
        if (subtype is "/Popup" or "/Link") return null;

        var kind = subtype switch
        {
            "/Circle" => AnnotationKind.Ellipse,
            "/Line" => AnnotationKind.Line,
            "/Highlight" => AnnotationKind.Highlight,
            _ => AnnotationKind.Rectangle
        };

        return new ShapeAnnotation(kind)
        {
            IsForeign = true,
            PageIndex = pageIndex,
            Rect = new PdfRect(rect.X1, rect.Y1, rect.Width, rect.Height),
            Color = ReadColor(dict) ?? AnnotationColor.Blue,
            Id = dict.Elements.GetString("/NM") is { Length: > 0 } nm ? nm : Guid.NewGuid().ToString("N")
        };
    }

    private static AnnotationColor? ReadColor(PdfDictionary dict)
    {
        var c = dict.Elements.GetArray("/C");
        if (c is null || c.Elements.Count < 3) return null;
        static byte Channel(double v) => (byte)Math.Clamp(v * 255, 0, 255);
        return new AnnotationColor(
            Channel(c.Elements.GetReal(0)), Channel(c.Elements.GetReal(1)), Channel(c.Elements.GetReal(2)));
    }
}
