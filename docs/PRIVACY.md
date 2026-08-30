# Privacy

This application is built for one person working on their own machine. It has no account, no server
and no network code.

## What it does

- Opens PDF files you point it at, in memory, on your machine.
- Writes files only where you tell it to.
- Keeps a small amount of state under `%LOCALAPPDATA%\PdfEditor`.

## What it never does

- It never opens a network connection. There is no HTTP client, no socket and no networking package
  in the application. CI fails the build if one appears — see `docs/adr/0010-offline-guarantee.md`.
- It never uploads a document, a page, recognised text or a signature.
- It has no analytics, no telemetry and no usage counters.
- It has no update check and no crash reporting.
- It loads no font, icon or style from the internet. Everything is in the package.
- It sends nothing to a cloud OCR service. Recognition runs locally, from language data shipped
  inside the package.
- It never asks for administrator rights.

## What is stored on your machine

Everything lives under `%LOCALAPPDATA%\PdfEditor`. `LocalApplicationData` is used deliberately
instead of `ApplicationData`, so nothing here is picked up by Windows roaming profiles or copied to
a server.

| Location | Contents | How to clear it |
| --- | --- | --- |
| `settings.json` | Theme, zoom, autosave interval and similar preferences | Settings → reset |
| `recent.json` | Paths of recently opened files | "נקה רשימת קבצים אחרונים" |
| `signatures\` | Your stored signatures: a small metadata file and a protected image | "מחיקת כל החתימות" |
| `ocr-cache\` | Recognition results, so a page is not recognised twice | "נקה מטמון זיהוי טקסט" |
| `recovery\` | Unsaved annotations from an interrupted session | "נקה קובצי שחזור", and automatically after a successful save |
| `temp\` | Print jobs being prepared | Automatically after printing, and at the next start |
| `logs\` | Diagnostics | "נקה קבצים זמניים" |

Deleting the whole `PdfEditor` folder returns the application to a first-run state. The portable
build itself keeps nothing next to the executable.

## Signatures

A stored signature is personal data and is treated as such. It is written only under your local
application data, protected with Windows DPAPI in `CurrentUser` scope, and overwritten with random
bytes before it is deleted. DPAPI ties it to your Windows account on that machine: copying the
portable folder elsewhere will not carry your signatures with it. `docs/SIGNATURE_STORAGE.md` covers
the details and the limits of that protection.

The application always states that a signature placed on a page is a graphical signature and not a
verified digital signature.

## The OCR cache

Recognition results are cached so a page is not processed twice. A cache entry is named by a hash of
the document fingerprint, page number, language and resolution — never by a file name — so the
directory listing reveals nothing about which documents you have opened. Changing a document changes
its fingerprint, which invalidates its entries. Entries older than the configured age are pruned
automatically, and the cache can be emptied from settings.

## Logs

Logs contain operation names, durations and error codes. They never contain page text, recognised
text, signature data or the contents of a document. A crash log records the exception type, message
and stack trace of the application's own code.

## The repository

`.gitignore` blocks `*.pdf`, `signatures/`, `*.traineddata`, key material and cache directories, and
CI fails if any of them is ever tracked. No personal document is used as a test fixture: every
fixture is generated in code at test time.
