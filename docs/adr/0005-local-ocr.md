# ADR-0005: Local OCR with Tesseract and bundled tessdata_fast

Status: accepted (milestone 2)

## Context

OCR must be fully offline, support Hebrew and English, and ship inside the portable package with no
first-run download and no cloud API.

## Options

1. **Windows.Media.Ocr** — built into Windows, no bundling, but no Hebrew language pack on most
   installations and it would require the user to install one.
2. **Tesseract 5 via the `Tesseract` NuGet package (Apache-2.0)** with `heb` and `eng` language data.
3. **PaddleOCR / ONNX models** — better on hard scans, but far larger models and a heavier runtime.

## Decision

Tesseract 5 through the `Tesseract` 5.2.0 NuGet package, with `heb.traineddata` (938 KB) and
`eng.traineddata` (4.0 MB) from `tessdata_fast`, all Apache-2.0 and redistributable, shipped in a
`tessdata` folder beside the executable.

## Consequences

Measured, not assumed: a Hebrew/English test page rendered at 300 dpi and recognised with Tesseract
5.3.4 using `heb+eng` reproduced the Hebrew lines essentially character-perfect, the English line
exactly, and the numbers and date exactly.

- The `Tesseract` package ships **Windows-only** native binaries (`x64/tesseract50.dll`,
  `x64/leptonica-1.82.0.dll`). Recognition therefore cannot run in this Linux development and CI
  environment. The architecture answers this rather than hiding it: `IOcrEngine` is an interface,
  the geometry conversion and the search index are pure classes with full unit tests, and the engine
  reports `IsAvailable = false` with a Hebrew explanation when the natives or language data are
  missing. Recognition itself is verified manually on Windows and by the Tesseract CLI here.
- Adds about 5 MB of language data to the package.
- Version 1 does not write an invisible text layer back into the PDF. Recognition results live in a
  local cache and power search, highlight and copy only, so OCR can never alter a document.

## Rejected alternatives

`Windows.Media.Ocr` was rejected because Hebrew is not reliably present. Neural OCR models were
rejected on package size for a marginal accuracy gain on the documents this product targets.
