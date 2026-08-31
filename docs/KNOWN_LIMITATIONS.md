# Known limitations

Everything here is real and current. Nothing is listed as done that has not been verified.

## Not verified yet

These are the honest gaps in verification, not features that are known broken.

| # | What | Why it is not verified |
| --- | --- | --- |
| V1 | The application has never been run on Windows | It was developed and tested on Linux. The whole solution builds, all 364 tests pass, and the Windows package is produced by cross-publishing, but no one has double-clicked the executable |
| V2 | The printing workflow has never reached a physical printer | Needs Windows and hardware. The sequencing logic is unit tested and the preview comes from the same code that builds the job. `docs/PRINTING.md` carries the protocol |
| V3 | OCR recognition is not covered by automated tests | The Tesseract NuGet package ships Windows-only natives. Accuracy was measured manually with the Tesseract CLI — see `docs/OCR.md` |
| V4 | DPAPI signature protection is not exercised on Linux | Windows-only API. The library is tested with protection reported as unavailable |
| V5 | Performance budgets are not measured | The numbers in `docs/PLAN.md` are targets. No figure is claimed as achieved |
| V6 | Whether the native binaries need the Visual C++ redistributable on a bare Windows install | Not checked. If they do, the package needs the runtime DLLs alongside it. **Check before any release** |
| V7 | The headless UI suite fails intermittently in this Linux container | About one run in sixteen, a random test fails with `System.PlatformNotSupportedException` at `Avalonia.Threading.Dispatcher.PushFrame`. It was measured on the commit before any of this session's work and reproduces there too, so it is the headless test host and not the product. Re-running clears it. It has not been diagnosed further |
| V8 | Output compatibility beyond PDFium | Saved files are verified against PDFium, the engine behind Chrome and Edge. Acrobat, Foxit and others are untested |

## Working, with a real limitation

| # | Area | Limitation |
| --- | --- | --- |
| L1 | Third-party annotations | Annotations created by other applications are shown as a labelled dashed placeholder in the editor, not with their true appearance. They are preserved byte-for-byte in the file, render normally in other viewers, and are drawn for real when a final copy is exported. Reproducing an arbitrary appearance stream inside the editor is future work |
| L2 | Forced-duplex printing | The application produces the correct page sequence and an accurate preview, but cannot guarantee the outcome. A print server may impose booklet or N-up layouts that reorder pages again, and no client-side sequence can override an enforced policy |
| L3 | Mixed orientation in one print job | Some drivers apply the first page's orientation to the whole job. Splitting it into two jobs is the workaround |
| L4 | Printing rasterises at 200 dpi | Vector content prints as a high-resolution image. Robust across drivers, larger than sending vectors |
| L5 | Protected documents | Password-protected or permission-restricted PDFs are not opened for editing. The document reports itself protected and the interface says so |
| L6 | The bidi character classifier | It is a pragmatic table, exact for Hebrew, Latin, Arabic, digits and common punctuation, with everything else falling back by Unicode category. Exotic scripts may order incorrectly |
| L7 | No text shaping | Hebrew needs reordering but not contextual joining, so this is sufficient for Hebrew and Latin. Arabic would additionally need shaping and is out of scope |
| L8 | Blank-page detection is a heuristic | A page rasterising to near-white counts as blank. White-on-white text counts as blank; a faint watermark does not |
| L9 | The whole file is held in memory | A document's bytes are read once and kept, because the renderer needs them and it removes any dependency on the file staying unchanged. A very large file therefore costs its own size in memory |
| L10 | Screen-reader support | Avalonia's UI Automation is less complete than WPF's. Every control carries an automation name and the application is fully keyboard operable, but it has not been tested with a screen reader |
| L11 | Signatures do not travel | DPAPI ties them to the Windows account on that machine. Copying the portable folder elsewhere leaves them unreadable — by design, and reported rather than silently lost |
| L12 | Deleting a signature is not secure erasure | The payload is overwritten with random bytes first, but wear levelling on an SSD may leave the original block recoverable |
| L13 | The executable is not code-signed | Windows SmartScreen will warn. `docs/RELEASE.md` explains it and publishes a SHA-256 rather than telling users to click through |
| L14 | Package size | About 120 MB: the .NET runtime, Avalonia, PDFium, Skia, Tesseract and the language data. The cost of needing no install and no network |
| L15 | Single-file publish | Works, but extracts native libraries to a temporary folder on first run. The portable folder is the supported artifact |
| L16 | One document at a time | The architecture allows more, but tabs are not implemented |
| L17 | PDFtoImage is held at 4.1.x | Version 5.x needs SkiaSharp 3.x/4.x while Avalonia 11.3 uses 2.88; both must move together |
| L18 | Recovery covers annotations, not the document | Autosave stores the unsaved annotations and nothing else. It cannot recover a document that was never saved anywhere, and it re-applies to the source file as it stands — if that file was replaced since the autosave, the offer says so rather than pretending the positions still match |
| L19 | Recovery files are not encrypted | The sidecar holds the text you typed, in the clear, under your local application data. It is deleted on save, on close and on any deliberate exit, but while it exists it is readable by anything running as you. `docs/PRIVACY.md` states this and how to avoid it |
| L20 | Assets are not committed | `build/fetch-assets` must be run once after cloning. A build without it produces an application that cannot embed text or run OCR |
| L21 | The asset download is not checksum-verified | Files are fetched over HTTPS from pinned URLs, but no publisher signature or hash manifest is checked |
| L22 | A signature can be imported but not drawn | The library is reachable now: placing the signature tool opens it, and a signature can be imported from a PNG or JPEG, picked, and deleted. Drawing one freehand is not implemented, although `Strings.DrawSignature` anticipates it. The freehand ink tool is the workaround |
| L23 | No interface for reordering pages | `PdfDocumentWriter` applies a reorder and it is covered by tests; the page operations dialog offers rotate, delete and extract only |
| L24 | Splitting is one file per page | The engine supports range-based splitting; the interface does not expose it yet |
| L25 | Merging cannot reorder the sources | Files are merged in the order the picker returns them. Drag-to-reorder before merging is not implemented |
| L26 | PDFium runs in-process | A memory-safety bug in it is not contained. Keeping the binaries current is the mitigation |

## Not in version 1, by decision

- Editing text that already exists inside a PDF.
- Adding an invisible OCR text layer to a file.
- Cryptographic digital signatures. The signature feature is graphical, and the interface says so.
- Form filling beyond preserving existing form objects untouched.
- Cloud, accounts, telemetry, update checks, paid components.
- macOS and Linux builds. The libraries are cross-platform; only Windows is supported.
- Windows Hello unlock for signatures — see `docs/SIGNATURE_STORAGE.md` for why.

## Recorded as future work

| Area | Idea |
| --- | --- |
| Third-party annotations | Draw their real appearance stream in the editor, not just on export |
| Assets | A checksum manifest verified by `fetch-assets` |
| Printing | Send vectors instead of a raster where the driver supports it |
| Documents | Tabs for several open files |
| OCR | An optional invisible text layer, as an explicit user action on a copy |
| Accessibility | A screen-reader pass, and a high-contrast theme |
| Packaging | Code signing, once a certificate exists |
