# Building from scratch

## Requirements

- .NET SDK 8.0. Nothing else — no Visual Studio, no Windows SDK, no native toolchain.
- An internet connection **once**, to restore NuGet packages and download the bundled font and OCR
  language data. After that the build, and the application, work offline.

Windows is the target platform, but the whole solution builds and tests on Linux and macOS as well,
and the Windows package can be cross-published from any of them.

### Installing the SDK

```bash
# Ubuntu / Debian
sudo apt-get update && sudo apt-get install -y dotnet-sdk-8.0

# Windows
winget install Microsoft.DotNet.SDK.8
```

Verify with `dotnet --version`; it should print 8.0.x.

## First build

```bash
git clone https://github.com/noamh98/Pdf_editor.git
cd Pdf_editor

./build/fetch-assets.sh          # Windows: pwsh build/fetch-assets.ps1
./build/build.sh
./build/test.sh
```

`fetch-assets` downloads about 5 MB into `assets/`:

| File | Licence |
| --- | --- |
| `assets/fonts/Assistant-Regular.ttf`, `Assistant-Bold.ttf`, `OFL.txt` | SIL OFL 1.1 |
| `assets/tessdata/heb.traineddata`, `eng.traineddata` | Apache-2.0 |

These are binaries, so they are not committed. **The build succeeds without them, but the resulting
application cannot embed text into a PDF and cannot run OCR.** Run the script before packaging.

## Producing the Windows package

```bash
./build/package.sh               # Windows: pwsh build/package.ps1
```

This writes:

```
artifacts/PdfEditor-<version>-win-x64-portable/      the supported artifact
artifacts/PdfEditor-<version>-win-x64-portable.zip
artifacts/SHA256SUMS.txt
```

The script fails if a required file is missing from the package, so an incomplete build cannot be
released by accident. It checks for the executable, `pdfium.dll`, `libSkiaSharp.dll`, the font, both
language files and the Tesseract natives.

The result is roughly 120 MB: the .NET runtime, Avalonia, PDFium, Skia, Tesseract, the font and the
language data. It needs no installer, no runtime install and no administrator rights.

## Repository layout

```
src/
  PdfEditor.Core/       domain model, bidi, page ranges, undo/redo, printing rules, contracts
  PdfEditor.Pdf/        PDFsharp + PDFium: open, render, annotate, save, flatten, merge, split
  PdfEditor.Ocr/        Tesseract engine, geometry, search index, result cache
  PdfEditor.Platform/   Windows printing, DPAPI signature storage, temporary file cleanup
  PdfEditor.App/        Avalonia UI, view models, themes, dialogs
tests/                  one test project per source project
build/                  fetch-assets, build, test, clean, package
docs/                   plan, architecture decisions, this file and the rest
assets/                 downloaded font and OCR data (not committed)
artifacts/              packaging output (not committed)
```

`PdfEditor.Core` depends on nothing but the base class library. Everything else depends on Core and
never on a sibling, except `PdfEditor.Platform`, which references `PdfEditor.Pdf` for the print-job
document type. The UI talks to interfaces, never to a concrete engine.

## Everyday commands

```bash
dotnet build PdfEditor.sln -c Release
dotnet test  PdfEditor.sln -c Release
dotnet test  tests/PdfEditor.Pdf.Tests -c Release          # one project
dotnet test  tests/PdfEditor.Core.Tests -c Release --filter "FullyQualifiedName~Bidi"
dotnet run   --project src/PdfEditor.App                   # needs a desktop session
./build/clean.sh                                           # removes bin, obj and artifacts
```

## Troubleshooting

**`dotnet` is not found after installing the SDK on Linux.** The Ubuntu package installs to
`/usr/bin/dotnet`. If you installed with the official script instead, add `$HOME/.dotnet` to `PATH`.

**Restore cannot reach nuget.org.** The first restore needs network access. Behind a proxy, set
`HTTPS_PROXY` before running, or restore once on a connected machine and copy `~/.nuget/packages`.

**`fetch-assets` fails.** It downloads from `raw.githubusercontent.com`. If that host is blocked,
fetch the five files by hand from the URLs in the script and drop them into `assets/fonts` and
`assets/tessdata`; the build only looks at the file names.

**The application starts but text in a saved PDF is empty.** The font is missing from
`assets/fonts`. Run `fetch-assets` and rebuild.

**OCR reports "רכיבי זיהוי הטקסט אינם זמינים בהתקנה זו".** Either `tessdata` is missing from the
package, or you are not on Windows. The Tesseract NuGet package ships Windows-only native binaries,
so recognition does not run on Linux or macOS. Everything else does.

**Page rendering throws about an incompatible `libSkiaSharp` version.** Something has pulled a
different SkiaSharp major version into the graph. Avalonia 11.3 and PDFtoImage 4.1.1 must both stay
on SkiaSharp 2.88 — see `docs/DEPENDENCIES.md`.

**A test leaves files in the temp directory.** Every test uses a `TempWorkspace` or `TempRoot` that
deletes itself. If a run is killed, remove `%TEMP%\pdfeditor-*` by hand.
