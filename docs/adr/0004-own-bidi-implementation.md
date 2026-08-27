# ADR-0004: Implement UAX#9 in Core rather than relying on the UI toolkit

Status: accepted (milestone 2)

## Context

PDF text-showing operators place glyphs strictly in the order supplied. A Hebrew string handed to
`DrawString` in logical order comes out reversed and, worse, mixed content comes out subtly wrong:
dates, decimal numbers and Latin file names get scrambled while still looking "Hebrew enough" to
pass a casual glance. The UI toolkit's own bidi handling applies to on-screen text only and is not
reachable from the PDF writer.

## Options

1. Reverse the string when it contains Hebrew. Simple, and wrong.
2. Reverse, then re-reverse the Latin runs. Better, still wrong.
3. Take a dependency on an existing .NET bidi library. The available ones are old, unmaintained, or
   carry unclear licensing.
4. Implement the Unicode Bidirectional Algorithm.

## Decision

Implement UAX#9 in `PdfEditor.Core.Text`: rules P2–P3, X1–X10, W1–W7, N0–N2, I1–I2 and L1–L4,
including paired-bracket resolution and mirroring, over a character classifier covering Hebrew,
Arabic, Latin, digits and the common separators.

Options 1 and 2 were not rejected on theory — option 2 was built, used to render a Hebrew test page,
and measured with OCR. It turned `26/08/2026` into `2026/08/26`, `file.pdf` into `pdf.file`, and
mangled the leading word of an English sentence. The same page rendered through the UAX#9
implementation and read back by OCR reproduced every line correctly.

## Consequences

- Hebrew, mixed and numeric content is written correctly, and 37 unit tests pin the behaviour,
  including the three cases the naive approach got wrong.
- The classifier is a pragmatic table, not a copy of `DerivedBidiClass.txt`. It is exact for the
  scripts this product targets and falls back to `L`/`ON` for exotic scripts. Recorded in
  `docs/KNOWN_LIMITATIONS.md`.
- No shaping engine is included. Hebrew needs reordering but not contextual joining, so this is
  sufficient for Hebrew and Latin; Arabic would additionally need shaping and is out of scope.
- The algorithm is pure and framework-free, so it is testable and reusable by the UI for text
  measurement.
