using System.Globalization;
using PdfEditor.Core.Annotations;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace PdfEditor.Pdf.Annotations;

/// <summary>
/// Writes the application's annotations into a document as standard PDF annotation objects with
/// generated appearance streams.
/// </summary>
/// <remarks>
/// Every annotation gets an <c>/AP /N</c> Form XObject. Viewers differ in how much they will
/// synthesise on their own — PDFium, the engine behind Chrome and Edge, draws nothing for a
/// <c>/FreeText</c> without one — so the appearance is always supplied rather than assumed.
/// The XObject is produced by drawing onto a temporary page and lifting its content stream, because
/// PDFsharp 6.2 does not expose the Form XObject behind <see cref="XForm"/> publicly.
/// </remarks>
public static class AnnotationWriter
{
    /// <summary>Removes every annotation this application previously wrote, leaving foreign ones.</summary>
    public static int RemoveOwnAnnotations(PdfPage page)
    {
        var annots = page.Elements.GetArray("/Annots");
        if (annots is null) return 0;

        int removed = 0;
        for (int i = annots.Elements.Count - 1; i >= 0; i--)
        {
            var dict = annots.Elements.GetDictionary(i);
            if (dict is null) continue;
            if (!dict.Elements.ContainsKey(AnnotationSerializer.PrivateKey)) continue;
            annots.Elements.RemoveAt(i);
            removed++;
        }
        if (annots.Elements.Count == 0) page.Elements.Remove("/Annots");
        return removed;
    }

    /// <summary>Writes one annotation onto <paramref name="page"/>.</summary>
    public static PdfDictionary Write(PdfDocument document, PdfPage page, Annotation annotation)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(annotation);

        var rect = annotation.Rect;
        double width = Math.Max(1, rect.Width);
        double height = Math.Max(1, rect.Height);

        var appearance = BuildAppearance(document, annotation, width, height);

        var dict = new PdfDictionary(document);
        dict.Elements["/Type"] = new PdfName("/Annot");
        dict.Elements["/Subtype"] = new PdfName(SubtypeFor(annotation.Kind));
        dict.Elements["/Rect"] = Rectangle(document, rect.Left, rect.Bottom, rect.Left + width, rect.Bottom + height);
        dict.Elements["/F"] = new PdfInteger(4);                    // Print
        dict.Elements["/NM"] = new PdfString(annotation.Id);
        dict.Elements["/M"] = new PdfString(FormatDate(annotation.ModifiedUtc));
        dict.Elements["/C"] = ColorArray(document, annotation.Color);
        dict.Elements["/CA"] = new PdfReal(Math.Clamp(annotation.Opacity, 0, 1));
        dict.Elements["/BS"] = BorderStyle(document, annotation.LineWidth);
        dict.Elements[AnnotationSerializer.PrivateKey] =
            new PdfString(AnnotationSerializer.Serialize(annotation), PdfStringEncoding.Unicode);

        AddKindSpecificEntries(document, dict, annotation);

        var ap = new PdfDictionary(document);
        ap.Elements["/N"] = appearance.Reference!;
        dict.Elements["/AP"] = ap;

        document.Internals.AddObject(dict);
        Append(page, dict);
        return dict;
    }

    private static void AddKindSpecificEntries(PdfDocument document, PdfDictionary dict, Annotation annotation)
    {
        switch (annotation)
        {
            case TextBoxAnnotation t:
                dict.Elements["/Contents"] = new PdfString(t.Text, PdfStringEncoding.Unicode);
                dict.Elements["/DA"] = new PdfString(
                    string.Create(CultureInfo.InvariantCulture,
                        $"/Helv {t.FontSize:0.##} Tf {t.TextColor.R / 255.0:0.###} {t.TextColor.G / 255.0:0.###} {t.TextColor.B / 255.0:0.###} rg"));
                dict.Elements["/Q"] = new PdfInteger(t.Alignment switch
                {
                    TextAlignment.Center => 1,
                    TextAlignment.End => 2,
                    _ => 0
                });
                break;

            case ShapeAnnotation s when s.Kind is AnnotationKind.Line or AnnotationKind.Arrow:
                dict.Elements["/L"] = new PdfArray(document,
                    new PdfReal(s.Start.X), new PdfReal(s.Start.Y),
                    new PdfReal(s.End.X), new PdfReal(s.End.Y));
                if (s.Kind == AnnotationKind.Arrow)
                    dict.Elements["/LE"] = new PdfArray(document, new PdfName("/None"), new PdfName("/ClosedArrow"));
                break;

            case ShapeAnnotation s when s.Kind == AnnotationKind.Highlight:
                var r = s.Rect;
                // QuadPoints order per the specification: upper-left, upper-right, lower-left, lower-right.
                dict.Elements["/QuadPoints"] = new PdfArray(document,
                    new PdfReal(r.Left), new PdfReal(r.Top),
                    new PdfReal(r.Right), new PdfReal(r.Top),
                    new PdfReal(r.Left), new PdfReal(r.Bottom),
                    new PdfReal(r.Right), new PdfReal(r.Bottom));
                break;

            case ShapeAnnotation s when s.FillColor is { } fill:
                dict.Elements["/IC"] = ColorArray(document, fill);
                break;

            case InkAnnotation ink:
                var inkList = new PdfArray(document);
                foreach (var stroke in ink.Strokes)
                {
                    var points = new PdfArray(document);
                    foreach (var p in stroke)
                    {
                        points.Elements.Add(new PdfReal(p.X));
                        points.Elements.Add(new PdfReal(p.Y));
                    }
                    inkList.Elements.Add(points);
                }
                dict.Elements["/InkList"] = inkList;
                break;

            case MarkAnnotation m:
                dict.Elements["/Name"] = new PdfName(m.Kind == AnnotationKind.CheckMark ? "/Check" : "/Cross");
                break;

            case SignatureAnnotation:
                dict.Elements["/Name"] = new PdfName("/Signature");
                break;
        }
    }

    /// <summary>
    /// Draws the annotation onto a temporary page and converts that page's content stream into a
    /// Form XObject suitable for use as <c>/AP /N</c>.
    /// </summary>
    private static PdfDictionary BuildAppearance(PdfDocument document, Annotation annotation,
        double width, double height)
    {
        var scratch = document.AddPage();
        try
        {
            scratch.Width = XUnit.FromPoint(width);
            scratch.Height = XUnit.FromPoint(height);
            using (var gfx = XGraphics.FromPdfPage(scratch))
            {
                gfx.SmoothingMode = XSmoothingMode.HighQuality;
                AnnotationRenderer.Draw(gfx, annotation, new XRect(0, 0, width, height));
            }

            var content = scratch.Contents.CreateSingleContent();
            byte[] bytes = content.Stream.UnfilteredValue;
            var resources = scratch.Elements.GetDictionary("/Resources");

            var form = new PdfDictionary(document);
            form.CreateStream(bytes);
            form.Elements["/Type"] = new PdfName("/XObject");
            form.Elements["/Subtype"] = new PdfName("/Form");
            form.Elements["/FormType"] = new PdfInteger(1);
            form.Elements["/BBox"] = new PdfArray(document,
                new PdfReal(0), new PdfReal(0), new PdfReal(width), new PdfReal(height));
            form.Elements["/Matrix"] = new PdfArray(document,
                new PdfReal(1), new PdfReal(0), new PdfReal(0), new PdfReal(1), new PdfReal(0), new PdfReal(0));
            if (resources is not null)
                form.Elements["/Resources"] = (PdfItem?)resources.Reference ?? resources;

            document.Internals.AddObject(form);
            return form;
        }
        finally
        {
            // The temporary page must never survive into the saved document.
            document.Pages.Remove(scratch);
        }
    }

    private static void Append(PdfPage page, PdfDictionary annotation)
    {
        var annots = page.Elements.GetArray("/Annots");
        if (annots is null)
        {
            annots = new PdfArray(page.Owner);
            page.Elements["/Annots"] = annots;
        }
        annots.Elements.Add(annotation.Reference!);
    }

    private static PdfArray Rectangle(PdfDocument doc, double x1, double y1, double x2, double y2) =>
        new(doc, new PdfReal(x1), new PdfReal(y1), new PdfReal(x2), new PdfReal(y2));

    private static PdfArray ColorArray(PdfDocument doc, AnnotationColor c) =>
        new(doc, new PdfReal(c.R / 255.0), new PdfReal(c.G / 255.0), new PdfReal(c.B / 255.0));

    private static PdfDictionary BorderStyle(PdfDocument doc, double width)
    {
        var bs = new PdfDictionary(doc);
        bs.Elements["/Type"] = new PdfName("/Border");
        bs.Elements["/W"] = new PdfReal(Math.Max(0, width));
        bs.Elements["/S"] = new PdfName("/S");
        return bs;
    }

    private static string FormatDate(DateTimeOffset value) =>
        value.UtcDateTime.ToString(@"\D\:yyyyMMddHHmmss", CultureInfo.InvariantCulture) + "Z";

    internal static string SubtypeFor(AnnotationKind kind) => kind switch
    {
        AnnotationKind.TextBox => "/FreeText",
        AnnotationKind.Rectangle => "/Square",
        AnnotationKind.Ellipse => "/Circle",
        AnnotationKind.Line or AnnotationKind.Arrow => "/Line",
        AnnotationKind.Ink => "/Ink",
        AnnotationKind.Highlight => "/Highlight",
        _ => "/Stamp"
    };
}
