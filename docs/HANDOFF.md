# Handoff — where the project stands

Last updated at the end of the second working session. This file is the truthful state of the
project, so work can resume without re-discovering anything.

## Current status

| Layer | State |
| --- | --- |
| `PdfEditor.Core` | Implemented and tested — 155 tests |
| `PdfEditor.Pdf` | Implemented and tested — 61 tests |
| `PdfEditor.Ocr` | Implemented and tested — 82 tests (recognition itself is not covered; see below) |
| `PdfEditor.Platform` | Implemented; printing and DPAPI are Windows-only and unexercised on Linux |
| `PdfEditor.App` | Implemented — responsive right-to-left Avalonia shell, 50 headless UI tests |
| Docs | All planned documents written except `PLAN_REVIEW.md` — see *Outstanding* |
| CI | GitHub Actions: Windows and Linux build and test, packaging, offline check, secret scan, dependency scan |
| Release | `build/package.sh` produces a 120 MB portable folder, a 53 MB zip and `SHA256SUMS.txt` |

The application runs, opens PDFs, renders them, annotates, saves, reopens, exports, merges, splits
and builds print jobs. **It has never been run on Windows.**

## Verified at this commit

- `dotnet build PdfEditor.sln -c Release` — succeeds, no errors.
- `dotnet test PdfEditor.sln` — **348 tests, 0 failures.**
- `build/package.sh` — produces `artifacts/PdfEditor-0.1.0-win-x64-portable/` (120 MB), the zip
  (53 MB) and a SHA-256 manifest, cross-published from Linux.
- The interface was rendered headlessly to PNG in both themes and at a compact width; the images in
  `docs/images/` are those captures, not mockups.

Two real defects were found by looking at those captures rather than by a test, and both are fixed:
the document canvas was being mirrored by the window's right-to-left flow direction, and the size
readout in the properties panel showed `70 × 250` for a 250 × 70 annotation because the neutral `×`
let the two numbers swap. Both surfaces are now pinned to a left-to-right flow direction.

A third was found the same way: the palette never followed a theme change, because the brushes were
declared outside the theme dictionaries and derived from colour keys with `DynamicResource`. A
`SolidColorBrush` at dictionary level is not in the logical tree, so it does not see the variant
change. The brushes now live inside the `Light` and `Dark` dictionaries.

## Architecture, in one paragraph

`Core` holds everything pure — the UAX#9 bidirectional algorithm, page-range parsing, undo/redo,
print sequencing, safe paths, atomic writes, the Hebrew string catalogue and every service
interface. `Pdf` implements those interfaces over PDFsharp and PDFium. `Ocr` wraps Tesseract behind
a cache. `Platform` holds the Windows-only pieces: printing and DPAPI-protected signature storage.
`App` is the Avalonia shell. Dependencies point inward only; `Core` references nothing.

## Outstanding

1. **`docs/PLAN_REVIEW.md`** — the independent critical design review. Three attempts to run it as a
   separate reviewer agent were cut short by session limits. It is the one planned document that
   does not exist. It should attack the plan, not polish it.
2. **`LICENSE`** — deliberately not created. The licence of this code is the owner's decision.
   `docs/RELEASE.md` records the recommendation and what adding one entails. Until then the code is
   under exclusive copyright, which the README states.
3. **A Windows smoke test** — nobody has double-clicked the executable. `docs/TESTING.md` has the
   manual script to run first.
4. **The Visual C++ redistributable question** — whether PDFium, Skia and Tesseract need it on a
   bare Windows install is unverified and must be settled before any release.
5. **A physical print test** — the forced-duplex sequence has never reached a printer.

## How to build and test

```bash
sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0   # the Microsoft host is blocked here
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

build/fetch-assets.sh      # once after cloning: the font and the OCR language data
build/build.sh
build/test.sh              # 348 tests
build/package.sh           # artifacts/
```

## Traps worth knowing before touching the code

- **SkiaSharp version lock.** Avalonia 11.3 uses SkiaSharp 2.88; PDFtoImage 5.x wants 3.x/4.x.
  Loading both makes the native `libSkiaSharp` refuse to load and every page render throws at
  runtime — not at build time. PDFtoImage is pinned to 4.1.1 with a comment in
  `Directory.Packages.props`. Move them together or not at all.
- **`XForm.PdfForm` is internal in PDFsharp 6.2.** Appearance streams are built by drawing onto a
  scratch page and lifting its content stream and resources into a `/Subtype /Form` XObject, then
  removing the scratch page. See ADR-0003; do not "simplify" it.
- **Reordering pages needs a second parse.** PDFsharp refuses to import pages from a document opened
  for modification, so `PdfDocumentWriter` opens the same bytes again in `Import` mode.
- **A corrupted PDF surfaces as a bare `System.Exception`.** The loader catches broadly on purpose;
  a PDF is untrusted input.
- **The font must cover Hebrew *and* Latin.** One `IFontResolver` serves one family, and Noto Sans
  Hebrew has no Latin glyphs. Assistant (SIL OFL) covers both.
- **A right-to-left flow direction mirrors custom-drawn content.** Anything that draws a document —
  the page surface, the thumbnails — must be pinned to `LeftToRight`.
- **Neutral characters swap numbers.** `250 × 70` inside a Hebrew paragraph renders as `70 × 250`.
  Isolate characters were not enough; the view pins such readouts to `LeftToRight`.
- **`System.Threading.Lock` is .NET 9.** This targets .NET 8; use `object`.
- **xUnit here is v2.** `TestContext.Current.CancellationToken` is a v3 API.

## Known gaps and risks carried forward

- Tesseract's NuGet natives are Windows-only, so OCR recognition cannot be covered by automated
  tests in this Linux environment. Recognition quality was measured with the Tesseract CLI instead
  and written up in `docs/OCR.md`.
- The forced-duplex workaround produces the correct page sequence but cannot guarantee driver or
  print-server behaviour.
- Performance budgets in `docs/PLAN.md` are targets. Nothing has been measured, and no figure is
  presented as achieved.
- There is no code signing certificate, so Windows SmartScreen will warn about the executable.
- `docs/KNOWN_LIMITATIONS.md` is the full list and is kept honest.
