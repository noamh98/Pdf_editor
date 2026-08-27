# ADR-0002: Split PDF responsibilities between PDFsharp and PDFium

Status: accepted (milestone 2)

## Context

The application needs two different things from a PDF library: high quality rasterisation for the
viewer and for OCR input, and precise read/write access to the document object model for
annotations, merging, splitting and page operations. No permissively licensed .NET library does both
well.

## Options

1. **iText 7** — does everything, but is AGPL or commercial. Excluded by the licensing requirement.
2. **PdfPig (Apache-2.0)** — good reading and text extraction, limited annotation writing, no
   rasteriser.
3. **PDFsharp 6.2.x (MIT)** — mature document object model, page import, content stream drawing,
   font embedding. No rasteriser.
4. **PDFium via PDFtoImage (PDFtoImage MIT, PDFium BSD-3-Clause)** — the rendering engine used by
   Chrome and Edge. Excellent rasterisation, native binaries for `win-x64` and `linux-x64` shipped as
   NuGet packages. Its .NET binding is not intended for structural editing.
5. **Docnet.Core** — another PDFium binding, less actively maintained.

## Decision

Use **PDFsharp 6.2.4** for the document object model and **PDFium through PDFtoImage 5.4.0** for
rasterisation. `PdfEditor.Pdf` is the only project that knows either library exists; everything else
talks to `IPdfDocument`, `IPdfDocumentWriter` and `IDocumentAssembler`.

## Consequences

- Rendering fidelity matches what a user will see in Edge, because it is literally the same engine.
  That also makes PDFium a useful compatibility oracle in the test suite.
- Two native dependency sets must be shipped. Both provide `win-x64` binaries and both are
  redistributable.
- The libraries do not share a document handle, so a document is opened twice: once by PDFsharp for
  structure and once by PDFium (from the same bytes) for rendering. Memory cost is acceptable
  because PDFium loads lazily; the alternative would be writing our own rasteriser.
- PDFium is serialised behind a semaphore per document because the binding is not re-entrant.

## Rejected alternatives

iText was rejected on licence grounds alone. PdfPig alone could not render. A single-library
solution was not available at an acceptable licence.
