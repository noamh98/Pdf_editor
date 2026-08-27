# ADR-0009: Portable folder as the primary artifact, single file as a convenience

Status: accepted (milestone 2)

## Context

The requirement is a portable Windows application with no installer, no administrator rights and no
first-run download. "One EXE" is desirable, but correctness and licence compliance rank higher.

## Decision

Ship **both**, with the portable folder as the supported artifact:

1. `PdfEditor-win-x64-portable/` — a self-contained folder containing the executable, the .NET
   runtime, the PDFium and Tesseract natives, the bundled font and the `tessdata` language files.
2. `PdfEditor-win-x64-single.exe` — the same build published with `PublishSingleFile=true` and
   `IncludeNativeLibrariesForSelfExtract=true`, offered as a convenience.

## Consequences

- Verified: a self-contained `win-x64` single-file publish cross-built from Linux produced a 44 MB
  executable before OCR assets were added.
- The single-file variant extracts its native libraries to a temporary directory on first run. That
  is local extraction, not a download, but it means slower first start and a temporary footprint, so
  it is not the default recommendation.
- No code signing certificate is available. Unsigned executables trigger a Windows SmartScreen
  warning; `docs/RELEASE.md` explains this rather than pretending it does not happen.
- Release artifacts carry a published SHA-256 checksum.
