# Contributing

## Setting up

```bash
git clone https://github.com/noamh98/Pdf_editor.git
cd Pdf_editor
./build/fetch-assets.sh     # Windows: pwsh build/fetch-assets.ps1
./build/build.sh
./build/test.sh
```

Requires the .NET 8 SDK and nothing else. `docs/BUILD.md` covers the details.

## Ground rules

These come from what the product promises, not from taste. A change that breaks one of them will not
be accepted however useful it is.

1. **Never damage a source document.** Every write goes through `AtomicFileWriter`, and work happens
   on a fresh copy of the loaded bytes. If a change touches saving, add a test asserting the source
   file's SHA-256 is unchanged.
2. **No network, ever.** No HTTP client, socket or networking package in `src/`, no remote font,
   style or asset. CI fails the build if one appears.
3. **Nothing personal in a log.** Operation names, durations and error codes only — never a file
   name, page text, recognised text or signature data.
4. **Never block the UI thread.** Opening, parsing, rendering, recognising, merging and writing all
   run on the thread pool with a cancellation token.
5. **Hebrew text goes through the bidi engine.** Anything drawing text into a PDF must run it
   through `BidiAlgorithm` and measure before drawing. A naive reversal corrupts dates and file
   names, and there are tests that prove it.
6. **No user-visible English.** Every string the user sees comes from
   `PdfEditor.Core.Localization.Strings`.
7. **No document or personal file in the repository.** Test fixtures are generated in code.

## Layering

`PdfEditor.Core` depends on nothing but the base class library. Everything else depends on Core and
never on a sibling. The UI talks to interfaces, never to a concrete engine. If a change needs the UI
to reference `PdfEditor.Pdf` directly, the contract in Core is missing something.

## Code style

`.editorconfig` is authoritative; `dotnet format` applies it.

- File-scoped namespaces, nullable enabled, `var` when the type is obvious.
- Async all the way down. No `async void` except an event handler. No `.Result`, no `.Wait()`.
- Every long-running method takes a `CancellationToken`.
- XML doc comments on public types and on anything whose behaviour is not obvious from its name.
  Do not restate what the code says — explain why it is that way, what it does not do, and what
  breaks if it changes.
- No comment that a reader could have written by looking at the line below it.

## Tests

Every change needs one. Match the layer:

| Layer | Style |
| --- | --- |
| Core | Pure unit tests, no I/O beyond a temporary directory |
| Pdf | Integration tests against fixtures generated in code by `PdfFixtures` |
| Ocr / Platform | Unit tests for the pure parts; anything Windows-only must still pass on Linux with the feature reported unavailable |
| App | Avalonia headless tests with `[AvaloniaFact]` |

Name a test as a sentence about behaviour — `RejectsRangeThatExceedsPageCount`, not `TestParse3`.
Assert behaviour, not implementation. Clean up temporary files in a `finally` or a disposable
fixture.

If you find a bug that existing tests missed, write the failing test first.

## Commits and pull requests

- Branches: `feature/…`, `fix/…`, `docs/…`, `chore/…`.
- Commits in English, imperative mood, small and atomic. Every commit leaves the tree building and
  the tests passing.
- Explain **why** in the body, not what — the diff already says what.
- Before pushing: `dotnet format`, build, and the full test suite.

A pull request should say what changed, how it was tested, what the risks are, what is still not
covered, and include a screenshot when the interface changed.

Do not merge with a failing test. If a test is wrong, fix the test in its own commit and say why.

## Adding a dependency

The bar is high — the product's size and its privacy promise both depend on the set staying small.
A new package needs:

1. A reason no existing dependency covers.
2. A licence that permits redistribution in a portable build and imposes nothing on this source.
   No AGPL, no commercial or trial-limited component.
3. An entry in `docs/DEPENDENCIES.md` with purpose, licence, source, whether it ships in the package
   and what the fallback is.
4. Attribution in `THIRD_PARTY_NOTICES.md` if its licence requires it.
5. A pinned version in `Directory.Packages.props`.
6. A check that it pulls in no networking stack.

Note that Avalonia and PDFtoImage must stay on the same SkiaSharp major version — see
`docs/DEPENDENCIES.md`.

## Where to start

`docs/ARCHITECTURE.md` explains the shape and points at the three hard parts. `docs/PLAN.md` has the
scope and the risk register. `docs/adr/` records why each significant decision was made — read the
relevant one before changing something it covers, and add a new ADR if you change the decision.
