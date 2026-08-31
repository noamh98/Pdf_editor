# Dependencies

Every third-party component, why it is here, what it is licensed under, and what happens if it has
to be replaced. Versions are pinned centrally in `Directory.Packages.props`.

A component is only acceptable if it is free, redistributable inside a portable build, and carries
no copyleft obligation that would reach the application's own source. Nothing here is commercial,
trial-limited or subscription-based.

## Shipped with the application

| Package | Version | Purpose | Licence | Source | In the package |
| --- | --- | --- | --- | --- | --- |
| PDFsharp | 6.2.4 | PDF object model: read, write, annotations, page import, content streams | MIT | github.com/empira/PDFsharp | Managed DLL — see note below |
| PDFtoImage | 4.1.1 | Managed wrapper that rasterises pages through PDFium | MIT | github.com/sungaila/PDFtoImage | Managed DLL |
| bblanchon.PDFium.Win32 | via PDFtoImage | PDFium native rasteriser for Windows | Apache-2.0 packaging; PDFium itself BSD-3-Clause | github.com/bblanchon/pdfium-binaries | `pdfium.dll` |
| bblanchon.PDFium.Linux | via PDFtoImage | Same, for the Linux development and CI machines | Apache-2.0 packaging; PDFium BSD-3-Clause | as above | not shipped to users |
| SkiaSharp | 2.88.9 | Bitmap handling shared by the renderer and the UI | MIT | github.com/mono/SkiaSharp | `libSkiaSharp.dll` |
| HarfBuzzSharp | via Avalonia | Text shaping for on-screen text | MIT | github.com/mono/SkiaSharp | native DLL |
| Avalonia | 11.3.7 | UI framework: XAML, layout, input, theming, right-to-left flow | MIT | github.com/AvaloniaUI/Avalonia | Managed DLLs |
| Avalonia.Desktop | 11.3.7 | Windows windowing and input backend | MIT | as above | Managed DLLs |
| Avalonia.Themes.Fluent | 11.3.7 | Base control templates the application's own styles build on | MIT | as above | Managed DLL |
| Tesseract | 5.2.0 | .NET binding for the Tesseract OCR engine | Apache-2.0 | github.com/charlesw/tesseract | `x64/tesseract50.dll`, `x64/leptonica-1.82.0.dll` |
| System.Drawing.Common | 8.0.10 | `System.Drawing.Printing`, used only for the Windows print path | MIT | dotnet/runtime | Managed DLL |
| System.Security.Cryptography.ProtectedData | 8.0.0 | DPAPI protection for stored signatures on Windows | MIT | dotnet/runtime | Managed DLL |
| .NET runtime | 8.0 | Self-contained runtime, so no framework install is required | MIT | dotnet/runtime | Runtime DLLs |

### A note on what PDFsharp actually ships

The PDFsharp NuGet package's `lib/` folder contains PdfSharp.dll alongside seven companion
assemblies — BarCodes, Charting, Cryptography, Quality, Shared, Snippets, System and WPFonts — none
of which this application's own source code references. Most of them turned out not to be optional:
PdfSharp.dll calls into several internally at runtime (confirmed by removing PdfSharp.System.dll and
watching `PdfDocument`'s constructor fail to resolve its logger), so a source-level grep for unused
namespaces is not sufficient evidence that a file is safe to drop.

One of them is not optional in the licensing sense either way: **PdfSharp.WPFonts.dll embeds six of
Microsoft's Segoe WP font files under a Microsoft EULA** that permits their use only "as permitted by
the EULA for the product in which this font is included" — a licence this project does not hold and
cannot grant onward. It shipped in every build until this was found by an independent design review
(`docs/PLAN_REVIEW.md`) that read the file's embedded strings.

`build/package.sh` and `build/package.ps1` now delete `PdfSharp.WPFonts.dll` from the published
output — and refuse to produce a package if it is still present — while leaving every other companion
assembly alone. That the application keeps working with only this one file removed was verified by
deleting it from the test output and running the full suite (430 tests, all four projects) rather
than assumed from its name.

## Assets fetched at build time

Downloaded by `build/fetch-assets.sh` (or the `.ps1` equivalent), copied into the package, and never
fetched while the application runs. They are excluded from git because they are binaries.

| Asset | Size | Purpose | Licence | Source |
| --- | --- | --- | --- | --- |
| Assistant-Regular.ttf, Assistant-Bold.ttf | ~150 KB | The font embedded into PDFs and used in the interface. Covers Hebrew and Latin in one face, which a single PDFsharp font resolver needs | SIL Open Font License 1.1 | github.com/hafontia-zz/Assistant |
| heb.traineddata | 938 KB | Hebrew OCR language data | Apache-2.0 | github.com/tesseract-ocr/tessdata_fast |
| eng.traineddata | 4.0 MB | English OCR language data | Apache-2.0 | as above |

## Test-only

| Package | Version | Licence |
| --- | --- | --- |
| xunit, xunit.runner.visualstudio | 2.9.x | Apache-2.0 |
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT |
| coverlet.collector | 6.0.2 | MIT |
| Avalonia.Headless, Avalonia.Headless.XUnit | 11.3.7 | MIT |

## Attribution obligations

- **SIL Open Font License 1.1** (Assistant): the licence text must ship with the font. `OFL.txt` is
  downloaded alongside it into `assets/fonts` and lands in the package.
- **Apache-2.0** (Tesseract, tessdata, PDFium binaries packaging): the licence and a NOTICE of
  modifications must be reproduced. See `THIRD_PARTY_NOTICES.md`.
- **BSD-3-Clause** (PDFium): the copyright notice and disclaimer must be reproduced.
- **MIT** (everything else): the copyright notice and permission notice must be reproduced.

None of these require the application's own source to be published, and none restrict use in a
private repository.

## Constraints deliberately accepted

- **PDFtoImage is held at 4.1.x.** Version 5.x builds against SkiaSharp 3.x/4.x, while Avalonia 11.3
  uses SkiaSharp 2.88. Both in one process load an incompatible native `libSkiaSharp` and every page
  render throws. Moving to PDFtoImage 5.x means moving to an Avalonia release on the same SkiaSharp
  line, and both must change together.
- **Tesseract 5.2.0 ships Windows-only natives.** Recognition cannot run in the Linux development or
  CI environment. The engine is behind `IOcrEngine` and reports itself unavailable rather than
  failing, and the geometry and search layers are pure and fully tested.

## If a dependency has to be replaced

| Component | Fallback | Cost |
| --- | --- | --- |
| PDFsharp | PdfPig (Apache-2.0) for reading; annotation writing would have to be built by hand | High. It is the document object model |
| PDFtoImage / PDFium | Docnet.Core, another PDFium binding, or PdfiumViewer | Moderate. `IPdfDocument` isolates it |
| Tesseract | Windows.Media.Ocr, if the user installs a Hebrew language pack | Moderate, and it weakens the offline promise |
| Avalonia | WPF, on a Windows development machine | High for the UI project, none for the rest |
| System.Drawing.Common | Direct P/Invoke to `winspool` and GDI | Moderate, and only the print path |

## Audit

`dotnet list PdfEditor.sln package --vulnerable --include-transitive` and `--deprecated` run in CI on
every push. A high or critical advisory fails the build.
