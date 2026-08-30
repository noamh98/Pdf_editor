# Handoff — where the project stands

Last updated at the end of the third working session. This file is the truthful state of the
project, so work can resume without re-discovering anything.

## Current status

| Layer | State |
| --- | --- |
| `PdfEditor.Core` | Implemented and tested |
| `PdfEditor.Pdf` | Implemented and tested, including the recovery sidecar |
| `PdfEditor.Ocr` | Implemented and tested (recognition itself is not covered; see below) |
| `PdfEditor.Platform` | Implemented; printing and DPAPI are Windows-only and unexercised on Linux |
| `PdfEditor.App` | Implemented — responsive right-to-left Avalonia shell |
| `tools/PdfEditor.Shots` | Headless screenshot harness; regenerates `docs/images` |
| Docs | All planned documents written, `PLAN_REVIEW.md` included |
| CI | GitHub Actions: Windows and Linux build and test, packaging, offline check, secret scan, dependency scan |
| Release | `build/package.sh` produces a 120 MB portable folder, a 53 MB zip and `SHA256SUMS.txt` |

The application runs, opens PDFs, renders them, fills forms, annotates, saves, reopens, exports,
merges, splits, recovers unsaved work and builds print jobs. **It has never been run on Windows.**

## What the third session changed

Four defects, each of which would have been felt by a user on day one.

**The font's licence notice was wrong.** The bundled `OFL.txt` collapsed two copyright lines into
one that credited the Assistant authors with Reserved Font Name `'Source'` — Adobe's, from Source
Sans Pro, which Assistant draws its Latin glyphs from. `THIRD_PARTY_NOTICES.md` disagreed with it
and named a third party appearing in neither. The licence now comes from Google Fonts' copy, which
carries both notices; upstream's own copy has the same collapsed line. The binaries also moved off
the abandoned `hafontia-zz` mirror onto `hafontia`.

**Closing the window discarded unsaved work silently.** Open and Close Document both asked first;
the window's own close button reached no guard at all. `MainWindow.OnClosing` now cancels the first
close, asks, and re-issues it.

**Autosave was a promise the code never kept.** `IAutosaveService` had no implementation and no call
site, while `AppSettings.AutosaveEnabled` defaulted to true at 45 seconds and the Hebrew recovery
dialogs were already written. The contract had no way to record anything either — only to find and
discard — so it gained `BeginSession`, `SaveAsync` and `RestoreAsync`, and
`FileSystemAutosaveService` implements it beside the annotation serializer it round-trips through.

**Text behaved like a sticky note, not a form field.** Placing text produced a pale yellow fill and
a border. The product's main job is filling forms in — a name, an identity number, a date — so text
is now created plain and an empty field gets a dashed guide that exists only on screen. The colour
swatch also did nothing visible for text, because it set a stroke colour a text box never uses; for
a text box it is now the ink of the glyphs.

## Verified at this commit

- `dotnet build PdfEditor.sln -c Release` — succeeds, no errors.
- `dotnet test PdfEditor.sln` — passes. See the README for the current count per project.
- `dotnet format PdfEditor.sln --verify-no-changes` — clean.
- `tools/PdfEditor.Shots` renders the shell at 1360, 1040, 820 and 680 in both themes; the images in
  `docs/images/` are those captures, not mockups.

Defects found by looking at rendered images rather than by a test, and fixed: the document canvas
was mirrored by the window's right-to-left flow direction; the size readout showed `70 × 250` for a
250 × 70 annotation because the neutral `×` let the numbers swap; and the palette never followed a
theme change because the brushes were declared outside the theme dictionaries. All three are fixed
and all three are traps that can return — see below.

## A measurement worth keeping

The headless UI suite fails intermittently on this Linux container — about one run in sixteen, a
random test, always `System.PlatformNotSupportedException` at `Dispatcher.PushFrame`. Before
attributing it to this session's work, the commit *before* any of it was run sixteen times in a
worktree and failed the same way at the same rate. It is the headless test host, not the product.
Re-run rather than chasing it, and do not weaken a test to silence it.

## Architecture, in one paragraph

`Core` holds everything pure — the UAX#9 bidirectional algorithm, page-range parsing, undo/redo,
print sequencing, safe paths, atomic writes, the Hebrew string catalogue and every service
interface. `Pdf` implements those interfaces over PDFsharp and PDFium. `Ocr` wraps Tesseract behind
a cache. `Platform` holds the Windows-only pieces: printing and DPAPI-protected signature storage.
`App` is the Avalonia shell. Dependencies point inward only; `Core` references nothing.

## Outstanding

1. **`LICENSE`** — deliberately not created. The licence of this code is the owner's decision.
   `docs/RELEASE.md` records the recommendation and what adding one entails. Until then the code is
   under exclusive copyright, which the README states.
2. **A Windows smoke test** — nobody has double-clicked the executable. `docs/TESTING.md` has the
   manual script to run first.
3. **The Visual C++ redistributable question** — whether PDFium, Skia and Tesseract need it on a
   bare Windows install is unverified and must be settled before any release.
4. **A physical print test** — the forced-duplex sequence has never reached a printer.
5. **No interface for the signature library or for reordering pages** — both are implemented and
   tested underneath, and neither is reachable from the window.

## How to build and test

```bash
# Install from the Ubuntu archive; the Microsoft host is blocked here. Do NOT run `apt-get update`
# first — it fails on unrelated third-party PPAs and takes the install down with it.
sudo apt-get install -y dotnet-sdk-8.0
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

build/fetch-assets.sh      # once after cloning: the font and the OCR language data
build/build.sh
build/test.sh
build/package.sh           # artifacts/

dotnet run --project tools/PdfEditor.Shots -- artifacts/shots   # regenerate docs/images
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
- **PdfSharp does no bidirectional reordering.** `XGraphics.DrawString` draws characters in the
  order it is handed them, so a Hebrew string given to it directly lands on the page backwards. Run
  it through `BidiAlgorithm.ToVisual` first — which is what the annotation renderer does.
- **The font must cover Hebrew *and* Latin.** One `IFontResolver` serves one family, and Noto Sans
  Hebrew has no Latin glyphs. Assistant (SIL OFL) covers both.
- **Autosave must not run on the UI thread.** `AtomicFileWriter` ends in a synchronous fsync, and
  an uncontended `SemaphoreSlim` completes without switching threads — so awaiting the service
  directly puts that fsync on the UI thread. The shell wraps it in `Task.Run`. For the same reason
  `FileSystemAutosaveService.DisposeAsync` does not wait on its gate: the composition root disposes
  synchronously.
- **The autosave timer belongs to the window, not the view model.** A headless test builds view
  models by the dozen and never closes them; a timer rooted in one outlives every one of them on
  the shared dispatcher.
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
