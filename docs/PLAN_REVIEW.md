# Plan review — a critical reading

This is the independent critical review `docs/PLAN.md` asked for. It is deliberately adversarial:
its job is to attack the plan, not to confirm it. Where the plan is right, it says so briefly and
moves on.

It is written after three working sessions, with the benefit of knowing which parts of the plan
survived contact with the code. That is not hindsight bias — every failure below was predictable
from the plan's own text, and the review says which sentence should have caught it.

## The one-line summary

The plan is unusually strong on **not damaging the input** and unusually weak on **not losing the
user's output**. Almost every defect found across three sessions sits in that gap.

---

## 1. The asymmetry at the heart of the priorities

Section 1 orders the product's priorities, and the first is *"Never damaging a source document."*
That priority was taken seriously to an admirable degree: operations write new files, saves are
atomic, and tests hash the source before and after and assert it is untouched.

There is no corresponding priority for **the user's unsaved work**, and the consequences were
exactly what you would predict:

- The window's close button discarded unsaved annotations silently. Open and Close Document both
  prompted; the one control every user reaches for did not.
- Autosave — an explicit acceptance criterion — was an interface with no implementation and no call
  site, while `AppSettings.AutosaveEnabled` defaulted to `true` at a 45-second interval. The
  settings told the user their work was protected while nothing protected it.

Both were fixed in the third session. The point of raising them here is that they are not two
unrelated bugs. They are one missing sentence in section 1. A plan that had said *"never lose work
the user has done"* alongside *"never damage the source"* would have made both defects visible as
violations rather than as omissions.

**Recommendation.** Add the second half of the promise to the priority list, and give it the same
kind of test the first half has: a test that asserts unsaved work survives every exit path.

## 2. Acceptance criteria that can pass while the feature does not exist

This is the most serious structural problem in the plan. Several criteria are phrased so that a
unit test satisfies them while a user cannot reach the feature at all.

| # | Criterion | What was true when it was "met" |
| --- | --- | --- |
| 7 | "Autosave and crash recovery restore unsaved work after a forced termination" | No implementation existed. Nothing could have restored anything |
| 10 | "Signatures are stored locally, protected, and can be deleted permanently" | True of the storage layer, and still true today. There is no way for a user to store one — the library has no interface |
| 2 | "…and signatures can be added, moved, resized and deleted" | A signature annotation can be placed. It has no image behind it |
| 8 | "Merge, split, extract, delete, rotate and **reorder** produce valid new files" | Reorder is implemented and tested in the engine and is not reachable from the window |
| 14 | "A self-contained portable build runs on a clean Windows 10 or 11 machine" | Never attempted. The application has still never been run on Windows |

Criteria 7, 10, 2 and 8 share one defect: they describe **capabilities of the codebase** rather than
**things a person can do**. That phrasing lets a green test suite stand in for a working product.

**Recommendation.** Rewrite every acceptance criterion as a user action with an observable result,
and require each to be demonstrated through the interface. "Signatures are stored locally,
protected" becomes "a user imports a signature, places it on a page, closes the application, reopens
it and places the same signature again." That criterion cannot be satisfied by a unit test, which is
the entire point.

## 3. The output-verification list checks the wrong things

Section 5 specifies what to verify after every save or export: the file exists, is non-empty,
reopens, has the expected page count and page sizes, *"has annotations or none as the mode
requires"*, and the source's SHA-256 is unchanged.

Read that list again for what it does **not** check: whether the content that was already in the
document is still there and still correct. Every item is either about the source file or about a
coarse property of the output.

A defect found in the third session lands precisely in that hole: flattening destroyed annotations
created by other applications. The output file existed, was non-empty, reopened, had the right page
count and page sizes, and had annotations "as the mode requires" — and had silently dropped content
the user never asked to lose. The verification list passes that file.

**Recommendation.** Add fidelity checks to the list: pre-existing annotations survive a round trip;
page content is unchanged where the operation did not intend to change it; text extractable before
is extractable after. Verification that only looks at the source file and the output's shape is
verification that cannot see the most expensive class of bug.

## 4. The test strategy has no eyes

Section 5 assigns the App layer "Avalonia headless UI tests" and stops. Sixty-six such tests existed
at the end of the second session and every one of them passed while:

- the document canvas was **mirrored**, because the window's right-to-left flow direction flips
  custom-drawn content;
- the properties panel showed **`70 × 250`** for a 250 × 70 annotation, because the neutral `×` let
  the two numbers swap inside a Hebrew paragraph;
- the **dark theme did not apply at all**, because the brushes were declared outside the theme
  dictionaries and so never saw the variant change.

All three were found by rendering the window to a PNG and looking at it. None was found by a test,
and none *could* have been: every one of them is a property of the pixels, and the tests only ever
inspected the object graph.

This is not an argument for pixel-diff regression tests, which are brittle and were rightly not
planned. It is an argument that a plan whose fourth stated priority is *"a polished, genuinely
right-to-left Hebrew interface"* must specify how polish is going to be **observed**, and this one
does not. The word "polished" appears in the priorities and nowhere in the test strategy.

**Recommendation.** Make rendering-and-looking a required step in the definition of done, at named
widths and in both themes. The third session added `tools/PdfEditor.Shots` to make this cheap; the
plan should require its output to be reviewed before a release, and should say that RTL mirroring
and neutral-character reordering are the two specific things to look for, because both have already
happened once.

## 5. The plan never asks what the product is for

Section 1 describes the user as someone who needs to "mark up documents, sign them graphically,
reorganise pages and print them". Section 3 lists primary flows. Neither says which flow is *the*
flow.

The consequence showed up in a default. The text tool created annotations with a pale yellow fill
and a border — a sticky note. That is a perfectly reasonable default for "marking up a document",
which is what the plan describes. It is the wrong default for filling in a form, which is what the
product is actually most often wanted for: a name, an identity number, a date, typed onto a page so
that it looks like it was always there. A sticky note is unusable for that, and nothing in the plan
made the conflict visible, because the plan never ranked the flows.

The same gap explains a smaller bug found alongside it: the colour swatch in the properties panel
set the annotation's stroke colour, which a text box does not use, so choosing a colour for text did
nothing at all. Text was not the thing anyone was thinking about.

**Recommendation.** Name the primary flow explicitly and order the rest under it. Every default —
colours, sizes, which tool is selected at startup — should then be justified against that flow, and
the justification written down where the next person will see it.

## 6. Performance budgets that were never going to be measured

Section 8 specifies performance budgets. Nothing has been measured, and the documentation is honest
about that. But the plan gives no method, no fixture corpus, no machine specification and no point
in the schedule at which measurement happens. A budget with no measurement plan attached is a wish,
and it stayed a wish through three sessions.

**Recommendation.** Either attach a method and a milestone to the budgets, or move them out of the
plan and into a "future work" note. Both are defensible; leaving unmeasurable numbers in a plan
that is otherwise scrupulous about evidence is not.

## 7. Windows

The product targets Windows exclusively. It has been developed and tested entirely on Linux, and has
never been executed on Windows. Printing and DPAPI signature protection — two of the features the
plan treats as central — are Windows-only and therefore entirely unexercised, and the forced-duplex
sequence that motivates a substantial part of the design has never reached a printer.

The plan's risk register acknowledges related risks, and the packaging strategy explains why
cross-publishing is sound. Neither addresses the plain fact that the definition of done includes
"runs on a clean Windows machine" and the project has no access to one.

**Recommendation.** This is the single largest risk in the project and it should be stated at the
top of the plan, not distributed across a risk register and a limitations list. Until someone runs
the executable on Windows, every Windows-specific claim is a design intention.

---

## What the plan gets right

Briefly, because a review that only attacks is not useful:

- **Source-document safety** is genuinely well designed and genuinely well tested. Writing a new file
  for every destructive operation is the right call and it was followed consistently.
- **The offline guarantee is enforced rather than promised.** A CI job greps for networking APIs and
  fails the build. That is the correct way to hold a privacy claim.
- **Module boundaries are real.** `Core` depends on nothing, and that discipline held — which is why
  the bidirectional algorithm and the page-range parser are testable without a window.
- **The architecture decision records explain rejected alternatives**, which is what makes them worth
  having.
- **The documentation does not overclaim.** `KNOWN_LIMITATIONS.md` is honest to a degree that is rare,
  and the README states the project has never run on Windows rather than burying it.

## The pattern worth taking away

Every significant defect across three sessions was of one kind: **something asserted in a document
and never checked against reality.** The font's licence was asserted in `THIRD_PARTY_NOTICES.md` and
named a copyright holder who appears in no notice anywhere. Autosave was asserted in an acceptance
criterion and in a settings default, and did not exist. Signature storage was asserted as an
acceptance criterion and has no interface. Dark theme was asserted and did not apply.

The codebase's own quality bar is high; the tests are real and the comments are honest. The failure
mode is not carelessness in the code. It is that the documents and the code were allowed to drift
apart, and nothing in the plan's definition of done requires them to be reconciled.

**The one change that would have prevented the most damage:** require every claim in the
documentation to name the test, the script, or the manual step that verifies it — and treat a claim
with nothing behind it as a defect, at the same severity as a failing test.

---

## Second review pass

Run independently, in a fresh session, against the state left by the review above. This pass found
four further defects, all confirmed against the running code rather than inferred from reading it,
and all four have since been fixed. It is a supplement to the review above, not a replacement.

| # | Severity | Finding | Evidence | Status |
| --- | --- | --- | --- | --- |
| S1 | Critical | Rendering a page a second time after it had already finished successfully threw `ObjectDisposedException` | `PageViewModel.EnsureRenderedAsync` published its cancellation source into `_pending`, then disposed it unconditionally via `using` on the way out without clearing the field. The next call that did not short-circuit on `IsSharp` — any zoom change — exchanged out that disposed source and called `.Cancel()` on it. Reproduced with a test that opens a document, renders a page, changes its scale, and renders again; confirmed the fix by reverting it and watching the same test fail | Fixed. The source is now only disposed via a `CompareExchange` that also clears the field, and only when a later call has not already superseded it. A regression test covers the exact sequence |
| S2 | Critical | The published package redistributed Microsoft's Segoe WP fonts under a licence this project does not hold | The PDFsharp NuGet package's `lib/` folder ships `PdfSharp.WPFonts.dll` alongside `PdfSharp.dll`; `strings` on the built package's copy of it shows six embedded Segoe WP font files and "© 2010 Microsoft Corporation... You may use this font as permitted by the EULA for the product in which this font is included" — a EULA this project is not a party to | Fixed. `build/package.sh` and `build/package.ps1` delete the file from the published output and refuse to produce a package if it is still present. Confirmed safe to remove — and confirmed that most of its seven sibling assemblies are *not* safe to remove — by deleting each from a built test output in turn and running the full suite; only `PdfSharp.WPFonts.dll` could be removed alone without a project depending on `PdfSharp.System.dll`'s logger failing at `PdfDocument`'s constructor |
| S3 | Major | A saved annotation's opacity could be applied twice by a fully compliant PDF viewer | `AnnotationRenderer` bakes the configured opacity into every colour it draws, and `AnnotationWriter` also wrote that same value into the annotation dictionary's `/CA`. Per the PDF specification, `/CA` tells a viewer to additionally composite the whole appearance at that alpha, so a viewer that implements it fully would render at roughly opacity squared | Fixed by no longer writing `/CA`, verified by a test asserting the key is absent. **Not independently verified as a visible difference**: PDFium's own basic annotation-rendering flag, the only renderer available in this environment and the one behind every page preview and thumbnail in the application, does not implement `/CA` compositing at all, so the bug could not be observed by rendering before or after the fix. The correctness argument rests on the PDF specification, not on a passing pixel comparison |
| S4 | Minor | This document did not exist under this name when the second pass started | The independent-reviewer agent that produced it wrote to `docs/PLAN_REVIEW.md` in a working tree that a separate, concurrent session had already fast-forwarded past — merging its own first-pass review, autosave, the signature interface, and the flattening fix now recorded above as already resolved. The two reviews were reconciled by hand rather than one overwriting the other | Not a code defect. Recorded so a future reader understands why this file has two passes with different authors and different dates rather than one continuous document |

### What this pass did not find wrong

The first pass's most serious findings — no autosave implementation, the signature library with no
interface, flattening deleting annotations it could not redraw, the font's copyright notice naming
nobody who appears in any notice — were already fixed in the commits this pass started from, and
this pass re-checked each one against the current code rather than trusting the commit messages:
`FileSystemAutosaveService` exists and is wired up, `SignaturePickerViewModel` gives the library an
interface, `AnnotationFlattener` now removes only the annotations it actually redrew or burned in,
and `THIRD_PARTY_NOTICES.md` names empira for PDFsharp rather than the Assistant font's authors.

### Left open

Two things this pass noticed but did not chase, for lack of time rather than lack of concern, and
neither is new — both were already implied by the first pass's finding that Windows-only features are
entirely unexercised:

- Printing decodes each page into memory as a full-resolution raster for the whole job at once;
  nothing bounds how many pages are held simultaneously. On a very long document this could exhaust
  memory before it exhausts the printer. Untested, because printing itself is untested end to end.
- Every render, thumbnail and blank-page check re-parses the source PDF from bytes rather than
  reusing an open document handle, because PDFtoImage 4.1's public API offers no persistent handle to
  reuse. This is a real cost on a large document and is already implicitly covered by
  `docs/KNOWN_LIMITATIONS.md`'s performance-budget entries, which state plainly that nothing has been
  measured.
