# ADR-0007: Blank-page interleaving for printers that force duplex

Status: accepted (milestone 2)

## Context

In the target environment a printer or print server forces double-sided printing regardless of what
the user asks for. The user needs each content page on its own sheet.

## Options

1. Change the driver setting through the Windows printing API. A print server policy overrides it,
   so this cannot be relied on.
2. Modify the document by inserting blank pages. Rejected — it changes the user's file.
3. Save a new "print-ready" PDF beside the original. Rejected as the default — it clutters the user's
   folders with files they did not ask for.
4. Build a temporary print job in which a blank page follows each content page, print it, delete it.

## Decision

Option 4, with the sequencing implemented as a pure function in `PdfEditor.Core.Printing` so it can
be exhaustively tested without a printer:

1. A blank page is placed between two consecutive content pages.
2. No blank page is appended after the final page.
3. A blank page already present in the document is reused instead of adding a second one.
4. An inserted blank copies the size, orientation and rotation of the page before it.

The preview shows the real resulting sequence, and the UI reports content pages, blank pages and the
estimated sheet count.

## Consequences

- 19 unit tests cover odd and even page counts, existing blanks in every position, leading and
  trailing blanks, mixed page sizes, rotation inheritance, subset printing and sheet estimation.
- The source document is untouched and no file is written to the user's folders; the temporary job
  lives under `%LOCALAPPDATA%\PdfEditor\temp` and is deleted after printing and at next startup.
- **This is not a guarantee.** Whether the sheets come out as intended depends on how the driver or
  print server interprets the page sequence, and no application-side sequence can override an
  enforced organisational policy. The UI says this in Hebrew next to the option, and
  `docs/PRINTING.md` records it together with the manual verification protocol for a physical
  duplex printer.
- Blank detection is a rendering heuristic (rasterise small, test for near-white). A page whose only
  content is invisible or white-on-white counts as blank; a page with a faint watermark does not.
  Documented in `docs/PRINTING.md`.
