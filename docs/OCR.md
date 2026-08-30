# Local text recognition

## What it is for

Recognising text on scanned pages so it can be searched, highlighted and copied. It runs entirely on
the machine, from language data shipped inside the package.

## Scope

**In version 1**

- Recognise Hebrew, English, or both together, on a page or a set of pages.
- Search the recognised text, with results highlighted where a bounding box is available.
- Select and copy recognised text.
- Cache results so a page is not recognised twice.

**Deliberately not in version 1**

- Editing text that already exists inside the PDF.
- Adding an invisible OCR text layer to the file.
- Changing the PDF in any way because of recognition. **OCR can never modify a document.**
- Any accuracy guarantee.

## The engine

Tesseract 5 through the `Tesseract` NuGet package, with `heb.traineddata` and `eng.traineddata` from
`tessdata_fast` (Apache-2.0, redistributable). Language data lives in a `tessdata` folder beside the
executable and is downloaded at build time by `build/fetch-assets`, never at run time.

The page is rasterised at 300 dpi by default (configurable, 150–600) and passed to the engine in
single-block segmentation mode.

## Measured accuracy

Measured in this project's environment with Tesseract 5.3.4 and `heb+eng`, on a page rendered at 300
dpi from the bundled font.

| Input line | Recognised |
| --- | --- |
| מסמך בדיקה לזיהוי תווים אופטי | exact |
| השורה הזאת כתובה בעברית תקנית ומכילה סימני פיסוק, נקודה. | exact |
| מספרים: 12345 ותאריך 26/08/2026 | exact, including the date |
| This line is written in English for mixed language testing. | exact |
| שורה מעורבת: הקובץ file.pdf נשמר בהצלחה | exact |
| רשימה: אלף, בית, גימל, דלת, הא, וו, זין, חית | exact |

That is a clean, born-digital render — the best case. A real scan is worse, and how much worse
depends on resolution, contrast, skew and the typeface. The application says so rather than implying
a number: "זיהוי הטקסט מתבצע במחשב זה בלבד. הדיוק תלוי באיכות הסריקה ואינו מובטח."

The same page rendered through a naive bidirectional implementation produced `2026/08/26` and
`pdf.file`. That was a rendering fault rather than a recognition one, and it is what motivated the
full UAX#9 implementation in `PdfEditor.Core.Text`.

## Searching Hebrew

A literal comparison is wrong for Hebrew twice over, so `HebrewTextNormalizer` folds both away
before matching:

- **Final letters.** ך ם ן ף ץ are folded to כ מ נ פ צ, so a word matches whether or not it ends the
  phrase.
- **Nikud.** Vowel points and cantillation marks (U+0591–U+05C7) are stripped from both the query
  and the text, so text with nikud matches a query typed without it.

Whitespace is collapsed, bidi control characters are dropped, and Latin text is compared case
insensitively. A match spanning several words returns the union of their bounding boxes, so the
highlight covers the whole phrase.

## The cache

Stored under `%LOCALAPPDATA%\PdfEditor\ocr-cache`, one JSON file per recognised page.

- The file name is a SHA-256 of the document fingerprint, page index, language and resolution.
  **No file name, path or document title is stored**, so the directory listing reveals nothing.
- The fingerprint covers the file length plus the first and last megabyte, so editing a document
  invalidates its entries.
- Entries older than the configured age (30 days by default) are pruned.
- "נקה מטמון זיהוי טקסט" in settings empties it.
- It is never synchronised: `LocalApplicationData` is used precisely so Windows roaming does not
  pick it up.

## Cancellation and progress

Recognition runs on the thread pool. The cancellation token is checked between lines, so cancelling
a document-wide run stops within a page rather than at the end. Progress is reported per page.

## Platform limitation

The `Tesseract` NuGet package ships native binaries for **Windows only** (`x64/tesseract50.dll` and
`x64/leptonica-1.82.0.dll`). Consequences:

- Recognition works in the shipped Windows application.
- It does **not** run on Linux or macOS, including this project's Linux CI job.
- The engine therefore reports `IsAvailable = false` with a Hebrew explanation rather than throwing,
  and the interface disables the OCR command and says why.
- Everything around the engine — the pixel-to-PDF geometry conversion, the search index, the cache —
  is pure and fully unit tested on every platform. Recognition itself is exercised by the Windows CI
  job and by the manual checklist.
