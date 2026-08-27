# Handoff — where the project stands

Last updated at the end of the first working session. **The project is not finished.** This file is
the truthful state, so work can resume without re-discovering anything.

## Current status

| Layer | State |
| --- | --- |
| `PdfEditor.Core` | Implemented and tested — 155 unit tests passing |
| `PdfEditor.Pdf` | Project scaffold only, no implementation |
| `PdfEditor.Ocr` | Project scaffold only, no implementation |
| `PdfEditor.Platform` | Project scaffold only, no implementation |
| `PdfEditor.App` | Project scaffold + manifest only, no UI |
| Docs | `PLAN.md` and ADRs 0001–0010 written; the remaining documents are not written |
| CI | Not set up |
| Release | Nothing packaged |

Nothing in this repository can open a PDF yet. The application does not run.

## What is genuinely done and verified

**Toolchain**: .NET SDK 8.0.130 installed from the Ubuntu archive (the Microsoft download host is
blocked by the environment's egress policy — use `apt-get install dotnet-sdk-8.0`). NuGet is
reachable. `gh` is not installed; GitHub work goes through the MCP tools.

**Proofs of concept — all executed, all passed** (scratch code, not in the repository):

1. PDFsharp 6.2.4 + PDFtoImage 5.4.0 restore and work; PDFium natives exist for `win-x64` and
   `linux-x64`, so rendering is testable on Linux.
2. Annotations written as raw dictionaries with an `/AP /N` Form XObject built from a scratch page
   are **rendered by PDFium** (about 9,400 sampled pixels differed between a render with and without
   annotations) and **survive save → reopen → merge/split page import**. The exact technique is in
   `docs/adr/0003-annotation-appearance-streams.md`.
3. Merge, split, rotate, blank-page interleaving and flatten-by-redraw all work, and the source file
   was byte-identical afterwards.
4. `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`
   cross-built from Linux produced a **44 MB single executable**.
5. Tesseract 5.3.4 with `heb+eng` on a 300 dpi render recognised Hebrew essentially
   character-perfectly. `tessdata_fast` `heb` (938 KB) and `eng` (4.0 MB) are Apache-2.0.
6. A naive bidi implementation turned `26/08/2026` into `2026/08/26` and `file.pdf` into `pdf.file`.
   The UAX#9 implementation now in `PdfEditor.Core` fixed both, confirmed by an OCR round trip of the
   rendered page.

**Implemented in `PdfEditor.Core`**, with tests:

- `Text/` — full UAX#9 bidirectional algorithm (P2–P3, X1–X10, W1–W7, N0–N2, I1–I2, L1–L4), a
  character classifier and a mirroring table. 37 tests.
- `Printing/PrintSequenceBuilder` — the blank-page interleaving logic. 19 tests.
- `Documents/PageRange.cs` — the `1-3,5,8-10` parser with typed errors.
- `Files/` — `AtomicFileWriter` (crash-safe saves) and `SafeFileName` (path-traversal safe names).
- `History/UndoRedo.cs` — bounded undo/redo with transactions and saved-state tracking.
- `Annotations/`, `Ocr/`, `Signatures/`, `Documents/PdfContracts.cs`, `Printing/PrintingContracts.cs`
  — the model and the service interfaces every other layer implements.
- `Storage/`, `Settings/`, `Localization/` — application paths, settings, and the Hebrew string
  catalogue.

## How to build and test

```bash
sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
cd /path/to/Pdf_editor
dotnet build PdfEditor.sln -c Release
dotnet test tests/PdfEditor.Core.Tests -c Release      # 155 tests, all passing
```

Windows portable build (the artifact users get):

```bash
dotnet publish src/PdfEditor.App -c Release -r win-x64 --self-contained true -o out/portable
```

## Exact next tasks, in dependency order

1. **`PdfEditor.Pdf`** — implement `IPdfDocument`, `IPdfDocumentLoader`, the annotation
   writer/flattener, `IPdfDocumentWriter`, `IDocumentAssembler` and `IPrintJobBuilder` against the
   contracts in `src/PdfEditor.Core/Documents/PdfContracts.cs`. Follow ADR-0003 exactly for
   appearance streams and always route Hebrew through `BidiAlgorithm.ToVisual` before drawing.
   Text inside an appearance `/BBox` must be measured and wrapped — the proof of concept clipped it.
2. **Tests for `PdfEditor.Pdf`** — synthetic fixtures generated in code, never real PDFs. Assert the
   source file's SHA-256 is unchanged after Save As, Export, merge and split.
3. **`PdfEditor.Ocr`** — Tesseract engine behind `IOcrEngine`, plus a pure `OcrGeometry` helper, a
   search index and a file-system cache. The engine must report itself unavailable (in Hebrew)
   when the Windows natives or `tessdata` are missing, so tests pass on Linux.
4. **`PdfEditor.Platform`** — Windows printing via `System.Drawing.Printing`, the DPAPI signature
   library per ADR-0006, and the temporary file janitor. Guard everything with
   `OperatingSystem.IsWindows()`.
5. **`PdfEditor.App`** — the Avalonia RTL shell, design tokens for light and dark, view models,
   keyboard map, and headless UI tests with `Avalonia.Headless.XUnit`.
6. **Independent design review** — `docs/PLAN_REVIEW.md` was started and did not complete. Run it
   before writing much more code; it is meant to attack the plan, not polish it.
7. **Remaining documents** — `ARCHITECTURE.md`, `THREAT_MODEL.md`, `PRIVACY.md`, `DEPENDENCIES.md`,
   `BUILD.md`, `TESTING.md`, `PRINTING.md`, `OCR.md`, `SIGNATURE_STORAGE.md`,
   `KNOWN_LIMITATIONS.md`, `RELEASE.md`, plus `README.md`, `CONTRIBUTING.md` and
   `THIRD_PARTY_NOTICES.md`.
8. **Asset bootstrap** — `.gitignore` excludes `*.traineddata` and font binaries, so a clean clone
   cannot produce a working build yet. Add `build/fetch-assets` that downloads the Apache-2.0
   `tessdata_fast` files and an OFL Hebrew font into `assets/`, and document it in `BUILD.md`.
9. **CI** — a Windows GitHub Actions job for build, test and packaging, plus a secret scan and a
   check that no networking API appears in application source (ADR-0010).

## Known gaps and risks carried forward

- Tesseract's NuGet natives are Windows-only, so OCR recognition cannot be covered by automated
  tests in this Linux environment. Recognition quality was verified with the Tesseract CLI instead.
- The forced-duplex workaround produces the correct page sequence but cannot guarantee driver or
  print-server behaviour. It has never been tested against a physical duplex printer.
- No clean-Windows-machine smoke test has been performed.
- The PDFium and Tesseract native libraries may require the Visual C++ runtime on a bare Windows
  install. This has not been verified and must be checked before any release.
- There is no code signing certificate, so Windows SmartScreen will warn about the executable.
