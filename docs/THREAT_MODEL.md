# Threat model

Scope: a single-user, offline Windows desktop application that opens PDF files, stores signatures
and caches OCR results locally.

## Assets

| Asset | Why it matters |
| --- | --- |
| The user's PDF documents | May be confidential. Corrupting one is the worst outcome the product can produce |
| Stored graphical signatures | Personal data; misuse could impersonate the user on a document |
| OCR results | Derived document content, so as sensitive as the documents |
| Recovery files | Unsaved work, containing document-derived content |
| The application package | If replaced, everything above is compromised |

## Trust boundaries

1. **The PDF file → the application.** A PDF is attacker-controlled input. It may be malformed,
   deliberately hostile, or contain strings designed to escape a path.
2. **The application → the file system.** Anything written must land where intended and nowhere else.
3. **Another local user or process → the application's data directory.** Windows file permissions
   and DPAPI are the boundary.
4. There is no network boundary, because there is no network code.

## Threats and what is done about them

### T1 — A malformed PDF crashes the application or is exploited

PDFsharp signals some malformations with a bare `Exception`, and PDFium is a large C++ codebase
processing untrusted input.

Mitigations: every parse is wrapped and any failure other than cancellation or memory exhaustion
becomes a typed `PdfOpenError.Corrupted`, so a bad file produces a Hebrew message rather than a
crash. The test suite includes a deliberately malformed fixture, a non-PDF file and an empty file.
PDFium runs in-process, which is the residual risk: a memory-safety bug in it is not contained.
Keeping the PDFium binaries current is the mitigation, and it is listed in `docs/RELEASE.md`.

### T2 — A document's own strings escape into a file path

A document title or embedded name could contain `..\`, a device name such as `CON`, or a bidi
override that disguises an extension.

Mitigations: `SafeFileName.Sanitize` strips separators, control characters, bidi controls and
reserved device names; `SafeFileName.CombineWithin` resolves the full path and refuses anything that
leaves the target directory. Both are unit tested, including traversal attempts.

### T3 — An interrupted save destroys the user's document

Mitigations: every write goes through `AtomicFileWriter` — a sibling temporary file, flushed to
disk, then `File.Replace`. A failure or cancellation deletes the temporary file and leaves the
original untouched. Tests assert the source file's SHA-256 is unchanged after Save As, Export, merge
and split, and that a cancelled save leaves an existing target byte-identical. `CleanupOrphans`
removes temporary files an earlier crash left behind.

### T4 — Another local user reads the stored signatures

Mitigations: signatures live under the current user's local application data, and on Windows the
payload is DPAPI-protected in `CurrentUser` scope, so another Windows account on the same machine
cannot decrypt it. `docs/SIGNATURE_STORAGE.md` states what this does and does not defend against.

Not defended: an attacker already running code as this Windows user; an attacker with offline access
to an unencrypted disk plus the ability to run as the user; malware with debug privileges. DPAPI in
`CurrentUser` scope does not protect against these, and the documentation does not claim it does.

### T5 — Deleted signatures remain recoverable

Mitigations: the payload is overwritten with random bytes before it is unlinked. On an SSD with wear
levelling this reduces but does not eliminate recoverability, and the documentation says so rather
than promising secure erasure.

### T6 — The cache leaks which documents were opened

Mitigations: cache file names are a SHA-256 of the fingerprint, page, language and resolution. No
file name, path or title is stored. Tested.

### T7 — A temporary print job leaks document content

A print job is a full copy of the pages being printed.

Mitigations: jobs are written only under `%LOCALAPPDATA%\PdfEditor\temp`, deleted when printing
finishes or fails, and any leftover is removed at the next start. `TempFileJanitor` refuses to track
or delete a path outside that directory, and that refusal is tested.

### T8 — Content leaks into a log

Mitigations: logs record operation names, durations and error codes only. The OCR engine is
configured to send its own debug output to the null device so recognised text is never written to
disk by the library.

### T9 — A document or a signature is committed to the repository

Mitigations: `.gitignore` blocks `*.pdf`, `signatures/`, `*.traineddata`, cache directories and key
material. CI fails if any of them becomes tracked. Test fixtures are generated in code.

### T10 — A supply-chain compromise in a dependency

Mitigations: versions are pinned centrally; `dotnet list package --vulnerable --include-transitive`
and `--deprecated` run in CI and a high or critical advisory fails the build; the dependency set is
small and each entry is justified in `docs/DEPENDENCIES.md`.

Not defended: a compromised upstream release that has not yet been reported. The build fetches
assets over HTTPS from pinned URLs but does not verify a publisher signature; adding a checksum
manifest to `build/fetch-assets` is recorded as future work.

### T11 — An unsigned executable is trusted by the user

The application is not code-signed, so Windows SmartScreen will warn about it.

Mitigations: `docs/RELEASE.md` explains the warning honestly and publishes a SHA-256 for every
artifact so a download can be verified. It does not tell users to bypass the warning.

## Explicitly out of scope

- Multi-user or shared installations.
- Protecting a document from its own owner.
- Password-protected or permission-restricted PDFs, which version 1 does not open for editing.
- Cryptographic signing or verification of any kind.
- Anything requiring administrator rights.
