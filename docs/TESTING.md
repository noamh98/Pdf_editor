# Testing

## Running the suite

```bash
./build/test.sh
# or
dotnet test PdfEditor.sln -c Release
```

## Current state

Measured on the development machine described below, on the commit that introduced the UI shell.

| Project | Tests | Result |
| --- | --- | --- |
| `PdfEditor.Core.Tests` | 155 | pass |
| `PdfEditor.Pdf.Tests` | 61 | pass |
| `PdfEditor.Ocr.Tests` (includes the platform layer) | 82 | pass |
| `PdfEditor.App.Tests` (Avalonia headless) | 33 | pass |
| **Total** | **331** | **pass** |

Development and test machine: Ubuntu 24.04 x86_64, .NET SDK 8.0.130, 8 vCPU. Any number quoted
elsewhere in the documentation was measured here unless it says otherwise.

## What each layer covers

### Core — pure logic, no I/O beyond temporary directories

- **Bidirectional text**: paragraph direction, pure and mixed runs, numbers, dates, decimal and
  thousands separators, bracket pairing and mirroring, nikud, explicit marks and isolates, and the
  invariant that reordering is always a permutation. Includes the three cases a naive implementation
  got wrong: `26/08/2026`, `file.pdf` and a Latin-first sentence.
- **Page ranges**: `1-3,5,8-10`, every error code, Arabic-Indic and full-width digits, Hebrew maqaf
  and Unicode dashes, embedded bidi controls, and round-tripping through `Format`.
- **Print sequencing**: odd and even page counts, existing blanks in every position, leading and
  trailing blanks, mixed page sizes, rotation inheritance, subset printing and sheet estimation.
- **Atomic writes**: overwrite, failure mid-write leaving the original intact, cancellation, orphan
  cleanup.
- **Safe file names**: traversal rejection, reserved device names, length limits, Hebrew names,
  bidi override stripping.
- **Undo/redo**: ordering, redo-branch discard, capacity eviction, dirty tracking across save,
  transactions, and the guard against undoing inside one.
- **Settings, application paths, cache keys, error messages.**

### Pdf — integration tests against real files generated in code

- Open, render, and the typed error for a missing, non-PDF, corrupted or empty file.
- **Every annotation kind survives save and reopen** with its geometry, colour, text and stroke data.
- **Every saved annotation carries an `/AP` Form XObject**, and **PDFium actually paints it** —
  asserted by comparing a render with annotations against one without. PDFium is the engine behind
  Chrome and Edge, so this doubles as a compatibility check.
- **Flattened output matches editable output** to better than 98% of sampled pixels, because both
  come from the same drawing routine.
- **The source file's SHA-256 is unchanged** after Save As, Export, merge and split. This is a hard
  requirement and is asserted explicitly.
- Merge, split by range and one-per-page, delete, rotate, reorder, annotations following a reorder,
  annotations on deleted pages being dropped, and refusing to produce a zero-page document.
- Blank-page detection, print job materialisation, blank geometry matching the preceding page.
- Text layout: wrapping, truncation inside the box, right alignment for Hebrew, and a rendered
  Hebrew text box whose changed pixels all fall inside its rectangle — the regression test for the
  clipping the proof of concept exhibited.
- Cancellation of a long merge and of a render.

### Ocr and Platform

- Geometry conversion between image pixels and PDF user space, including the vertical flip, at
  several resolutions, with a round-trip.
- Hebrew-aware search: nikud-insensitive, final-letter equivalence, mixed Hebrew and Latin, combined
  bounds for a multi-word match, rectangle extraction in reading order.
- Cache round-trip, miss, prune by age, clear, and that a file name is a bare hash.
- The Tesseract engine reporting itself unavailable in Hebrew rather than throwing.
- Signature image cropping and background removal to exact pixel bounds.
- Signature library add, list, read, rename, delete, delete-all, payload removal, honest reporting
  of whether protection is available, and refusing an identifier that tries to escape its directory.
- The temporary file janitor refusing paths outside its root.
- The print service reporting itself unsupported off Windows instead of throwing.

### App — Avalonia headless, no display required

- The window constructs and its XAML, styles and theme dictionaries load for real.
- The whole window is right-to-left, thumbnails take the leading column and properties the trailing
  one.
- Theme tokens resolve in both light and dark; the theme cycles and persists.
- Command availability with and without a document.
- The documented keyboard shortcuts are bound, and shortcuts are suppressed while a text input has
  focus.
- Open, render, thumbnail, annotate, undo, redo, copy, paste, delete, edit a property, save, reopen,
  zoom modes, page navigation, print preview interleaving and its Hebrew range error, close.

## What is not covered automatically, and why

| Area | Why | How it is covered instead |
| --- | --- | --- |
| OCR recognition accuracy | The Tesseract NuGet package ships Windows-only natives, so no recognition runs in Linux CI | Verified manually with the Tesseract 5.3.4 CLI on rendered pages; see `docs/OCR.md` for the measured result. The Windows CI job runs the same tests where the natives exist |
| Actual printing | Needs a printer and a Windows session | `docs/PRINTING.md` carries a manual protocol, including the forced-duplex case |
| DPAPI protection | Windows-only API | The library is tested on Linux with protection reported as unavailable; the Windows CI job exercises the protected path |
| Visual appearance | Headless tests assert structure, not pixels | Manual checklist below |
| Clean-machine start | No clean Windows machine is available in this environment | Manual checklist below. **Not yet performed** |
| Performance budgets | Would need a fixed reference machine | Not yet measured. The budgets in `docs/PLAN.md` are targets, and no number is claimed as achieved |

## Manual smoke checklist

Run on Windows 10 or 11 against the portable package, not a development build. Nothing here is
automated; treat an unticked box as untested.

**Opening and viewing**
- [ ] The application starts by double-clicking `PdfEditor.exe`, with no install and no admin prompt
- [ ] A PDF opens from the Open button and by dragging a file onto the window
- [ ] Thumbnails appear on the right and follow the current page
- [ ] Zoom in, out, fit width, fit page and 100% behave, and the page stays sharp
- [ ] A 300+ page document scrolls without the window freezing
- [ ] A file whose path contains Hebrew characters opens
- [ ] A corrupted file produces a Hebrew message, not a crash

**Editing**
- [ ] Each tool creates its annotation by dragging, and by a single click
- [ ] A Hebrew text box shows the text correctly, right-aligned, and wraps inside its box
- [ ] A number, a date and a Latin file name inside Hebrew text keep their order
- [ ] Selecting shows handles; moving and resizing work; Delete removes
- [ ] Ctrl+Z and Ctrl+Y walk the full history
- [ ] Shortcuts do not fire while typing into the text field

**Saving**
- [ ] Save keeps annotations editable; reopening finds and edits them again
- [ ] The saved file opens in Microsoft Edge and the annotations are visible there
- [ ] Export Final Copy warns first, writes a new file, and the flattened result looks identical
- [ ] The original file's timestamp and size are unchanged after an export
- [ ] Saving over a file that is open in another application produces a Hebrew message

**Documents**
- [ ] Merge produces one file in the chosen order
- [ ] Split one-per-page produces the right number of files, named readably
- [ ] Rotate and reorder survive a save and reopen

**OCR**
- [ ] Recognition runs on a scanned Hebrew page and finds text
- [ ] Search highlights a Hebrew term, including one written with a final letter
- [ ] Clearing the cache empties `%LOCALAPPDATA%\PdfEditor\ocr-cache`

**Signatures**
- [ ] A drawn signature is cropped to its ink and placed on a page
- [ ] It is still there after restarting the application
- [ ] Deleting it removes both files from `%LOCALAPPDATA%\PdfEditor\signatures`
- [ ] The interface states it is a graphical signature, not a verified digital one

**Printing** — see `docs/PRINTING.md` for the full protocol

**Interface**
- [ ] Light and dark both readable; the system setting is followed
- [ ] Every control reachable by Tab, in right-to-left reading order, with a visible focus ring
- [ ] Usable at 100%, 125%, 150% and 200% Windows scaling
- [ ] No English string appears anywhere the user can see

**Privacy**
- [ ] With Resource Monitor open, the process makes no network connection during a full session
