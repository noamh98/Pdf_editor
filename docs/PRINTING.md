# Printing, and the forced-duplex workaround

## The problem

In some workplaces the printer or the print server forces double-sided printing, whatever the user
asks for. Two content pages then share one sheet, which is wrong when each page has to stand alone —
a form to sign, a page to file, a page to hand to someone.

The Windows printing API can request simplex, but a print-server policy overrides it. No application
can defeat that from the client side.

## What this application does instead

It changes the **sequence of pages** so that forced duplex produces the desired result. When "הדפס
כל עמוד תוכן על גיליון נפרד" is enabled, a blank page is placed after each content page. The driver
then puts the content on the front of a sheet and the blank on the back, so every content page gets
its own sheet.

```
Off:  C1 C2 C3 C4         2 sheets, two content pages per sheet
On:   C1 __ C2 __ C3 __ C4    4 sheets, one content page per sheet
```

## The rules

Implemented in `PdfEditor.Core.Printing.PrintSequenceBuilder`, a pure function with 19 unit tests:

1. A blank page is placed between two consecutive content pages.
2. No blank page is appended after the final page — it would waste a sheet.
3. A blank page already present in the document is reused instead of adding a second one, so an
   existing separator never becomes a double blank.
4. An inserted blank copies the size, orientation and rotation of the page before it, so a job with
   mixed page sizes stays consistent.

Worked examples, where `C` is content, `e` an existing blank and `b` an inserted one:

| Document | Result | Note |
| --- | --- | --- |
| C C C | C b C b C | Three sheets |
| C | C | Nothing to separate |
| C e C | C e C | Rule 3: the existing blank is reused |
| C e e C | C e e C | Already separated |
| C e C C e C | C e C b C e C | Only the middle gap needs one |
| e C C | e C b C | A leading blank is left alone |
| C C e | C b C e | A trailing blank is left alone |

## What the user sees

The print dialog shows the real resulting sequence as a strip of sheets. Content pages carry their
page number; blanks are labelled "ריק" or "ריק (נוסף)" — labelled, not merely shaded, so the
distinction does not depend on colour. Beneath it:

> ‎N‎ עמודי תוכן, ‎M‎ עמודים ריקים, כ-‎S‎ גיליונות

and, permanently:

> התוצאה תלויה באופן שבו מנהל ההתקן או שרת ההדפסה מפרשים את רצף העמודים. האפליקציה מייצרת את הרצף
> הנכון, אך אינה יכולה לעקוף מדיניות ארגונית.

## What is never done

- The source document is not modified. Not a byte.
- No new PDF is saved into the user's folders. The job is built in
  `%LOCALAPPDATA%\PdfEditor\temp`, printed, and deleted — on success, on failure and on cancellation
  — with any leftover removed at the next start.
- Page size, orientation and rotation are preserved; pages are never scaled to a different paper
  size unless the user asks for scale-to-fit.

## Blank-page detection

A page counts as blank when it rasterises to near-white at 36 dpi. This is a printing heuristic, not
a semantic test:

- A page whose only content is white-on-white counts as blank.
- A page with a faint watermark or a stamp does not.
- A page containing only an invisible OCR text layer counts as blank, which is correct for printing.

If detection is wrong for a document, switching the option off prints the pages exactly as they are.

## Known limits

- **This is not a guarantee.** Whether the sheets come out as intended depends on how the driver or
  the print server interprets the sequence. Some servers impose booklet or N-up layouts that reorder
  pages again; nothing an application sends can override that.
- Some drivers ignore per-page orientation in a mixed job and apply the first page's setting to all
  of them. A job with mixed orientation may therefore print rotated. Splitting it into two jobs is
  the workaround.
- The job is rasterised at 200 dpi before being sent. Vector content is therefore printed as a
  high-resolution image, which is robust across drivers but larger than sending vectors. It is
  configurable in code via `WindowsPrintService.RenderDpi`.
- **This workflow has never been run against a physical duplex printer.** The sequencing logic is
  covered by unit tests and the preview is generated from the same code that builds the job, but the
  end-to-end behaviour is unverified. The protocol below exists to close that gap.

## Manual verification protocol

Automated tests cannot cover a printer. Run this on Windows and record the result in this file.

### A — Microsoft Print to PDF, no hardware needed

For each case, print with the option on, then open the resulting PDF and check the page sequence.

| # | Document | Expected |
| --- | --- | --- |
| A1 | 3 content pages, A4 portrait | 5 pages: C, blank, C, blank, C |
| A2 | 4 content pages | 7 pages, ending on a content page |
| A3 | 1 content page | 1 page, no blank |
| A4 | C, blank, C | 3 pages, unchanged |
| A5 | A4 landscape throughout | 5 pages, all landscape |
| A6 | Mixed A4 and Letter | Each blank matches the page before it |
| A7 | 10 pages, range `2-4` printed | 5 pages: C2, blank, C3, blank, C4 |
| A8 | Option off, 4 pages | 4 pages, unchanged |

- [ ] A1 — [ ] A2 — [ ] A3 — [ ] A4 — [ ] A5 — [ ] A6 — [ ] A7 — [ ] A8

### B — A physical printer without forced duplex

| # | Check |
| --- | --- |
| B1 | Option off, 4 pages, simplex: 4 sheets, one page each |
| B2 | Option on, 3 pages, simplex: 5 sheets, alternating content and blank. **This is the expected cost of the workaround on a simplex printer** — the option is meant for forced-duplex environments |

- [ ] B1 — [ ] B2

### C — A printer or server that forces duplex (the case this exists for)

| # | Check |
| --- | --- |
| C1 | Option off, 4 pages: 2 sheets, two content pages per sheet — reproduce the problem first |
| C2 | Option on, 4 pages: 4 sheets, one content page on the front of each, backs blank |
| C3 | Option on, 3 pages: 3 sheets, the last sheet's back blank |
| C4 | Option on, a document that already contains a blank between two content pages: no double blank |
| C5 | Option on, landscape: orientation preserved on every sheet |
| C6 | The sheet count printed matches the estimate shown in the preview |

- [ ] C1 — [ ] C2 — [ ] C3 — [ ] C4 — [ ] C5 — [ ] C6

### Recording a result

Add a row here after each run:

| Date | Printer / server | Driver | Cases | Result |
| --- | --- | --- | --- | --- |
| _(none yet)_ | | | | |
