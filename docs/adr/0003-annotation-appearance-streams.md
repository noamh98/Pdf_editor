# ADR-0003: Generate our own appearance streams for annotations

Status: accepted (milestone 2)

## Context

Annotations must survive save, reopen and further editing, and must be visible in other viewers.
Viewers differ in how much they will render without an appearance stream: PDFium in particular does
not synthesise a `/FreeText` appearance, so an annotation written with only `/Contents` and `/DA`
would be invisible in Chrome and Edge — exactly the viewers a recipient is most likely to use.

PDFsharp 6.2 exposes `XForm` for drawing but makes `XForm.PdfForm`, which yields the underlying Form
XObject, `internal`. There is no supported public path from "draw with `XGraphics`" to "use the
result as `/AP /N`".

## Options

1. Write only annotation dictionaries and rely on the viewer to synthesise appearances. Rejected —
   proven not to render in PDFium.
2. Reach the internal `XForm.PdfForm` by reflection. Rejected — silently breaks on a library upgrade.
3. Hand-write the appearance content stream and build the font resources manually. Correct but means
   implementing font embedding, subsetting and width tables ourselves.
4. Draw onto a temporary page inside the same document, then lift its content stream and resource
   dictionary into a Form XObject, and delete the temporary page.

## Decision

Option 4. The technique is:

```
scratch = doc.AddPage(); scratch.Width = w; scratch.Height = h;
using (var g = XGraphics.FromPdfPage(scratch)) { draw the annotation }
bytes = scratch.Contents.CreateSingleContent().Stream.UnfilteredValue;
resources = scratch.Elements.GetDictionary("/Resources");
form = new PdfDictionary(doc); form.CreateStream(bytes);
form: /Type /XObject  /Subtype /Form  /FormType 1  /BBox [0 0 w h]  /Matrix [1 0 0 1 0 0]  /Resources resources
doc.Internals.AddObject(form); doc.Pages.Remove(scratch);
annotation: /AP << /N form >>
```

Everything uses public API, and PDFsharp performs the font embedding for us.

## Consequences

Verified by proof of concept:

- Save, reopen, and the annotations are found again with intact `/AP` streams, `/BBox` and
  `/Resources`.
- PDFium paints them: rendering the same page with and without annotations differed by roughly
  9,400 sampled pixels, so the appearance stream is genuinely being drawn.
- Importing an annotated page into another document (merge and split) carries the annotations.
- The temporary page must be removed before saving, and the test suite asserts the page count is
  unchanged so a leaked scratch page can never ship.

Costs:

- One extra page object is created and destroyed per appearance stream.
- Text layout inside the `/BBox` is our responsibility. The proof of concept clipped Hebrew text by
  drawing at a fixed offset without measuring; the implementation must measure and wrap, and the
  test suite covers it.

## Rejected alternatives

Reflection into `internal` members was rejected as an upgrade hazard for a feature that determines
whether user work is preserved.
