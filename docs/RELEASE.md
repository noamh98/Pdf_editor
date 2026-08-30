# Releasing

## Before anything else: the licence

**The repository has no `LICENSE` file yet, and that is deliberate.** Choosing the licence for your
own code is your decision, not one to be made on your behalf. Nothing should be published until it
is made.

The dependencies impose no constraint — every one of them is MIT, Apache-2.0, BSD-3-Clause or OFL,
and none requires this project's source to be published. The choice is therefore free:

| If you want | Choose |
| --- | --- |
| Anyone to use it, including commercially, with attribution | **MIT** — simplest and most common for a project like this |
| The same, plus an explicit patent grant | **Apache-2.0** |
| Changes published if someone distributes a modified version | **MPL-2.0** (per-file) or **GPL-3.0** (whole work) |
| To keep all rights and share nothing | **No licence at all**, stated explicitly in the README |

Once decided, add `LICENSE`, set the year and holder, and reference it from `README.md`.

## Release checklist

Nothing here is optional. A box that cannot be ticked is a reason not to release.

### Quality gates

- [ ] `./build/clean.sh && ./build/fetch-assets.sh && ./build/build.sh` succeeds from a fresh clone
- [ ] `./build/test.sh` passes with zero failures on Windows **and** Linux
- [ ] CI is green: both build jobs, the offline-guarantee check, the secret scan, the dependency audit
- [ ] `dotnet list PdfEditor.sln package --vulnerable --include-transitive` reports nothing high or critical
- [ ] `docs/KNOWN_LIMITATIONS.md` matches reality, including which items are still unverified
- [ ] `docs/DEPENDENCIES.md` and `THIRD_PARTY_NOTICES.md` match `Directory.Packages.props`

### Verification that cannot be automated

- [ ] The manual smoke checklist in `docs/TESTING.md` has been run on Windows against the package
- [ ] The printing protocol in `docs/PRINTING.md` has been run, at least section A
- [ ] **The package has been started on a clean Windows machine** with no .NET, no Visual Studio and
      no developer tools, and it opened a PDF
- [ ] **The native dependency question is settled**: confirm whether `pdfium.dll`, `libSkiaSharp.dll`,
      `tesseract50.dll` and `leptonica-1.82.0.dll` need the Visual C++ redistributable on a bare
      Windows install. If they do, ship the runtime DLLs beside the executable. Until this is
      answered, "self-contained" is not proven

### Building the artifact

```bash
./build/clean.sh
./build/fetch-assets.sh
./build/test.sh
./build/package.sh
```

Produces:

```
artifacts/PdfEditor-<version>-win-x64-portable/
artifacts/PdfEditor-<version>-win-x64-portable.zip
artifacts/SHA256SUMS.txt
```

`package.sh` verifies the package contains the executable, `pdfium.dll`, `libSkiaSharp.dll`, the
font, both language files and the Tesseract natives, and fails if any is missing.

### Publishing

- [ ] Bump `<Version>` in `Directory.Build.props`
- [ ] Tag: `git tag -a v<version> -m "..." && git push origin v<version>`
- [ ] Create the GitHub release, attach the zip and `SHA256SUMS.txt`
- [ ] Paste the SHA-256 into the release notes
- [ ] Release notes state, plainly: what is new; that the executable is unsigned and SmartScreen will
      warn; the current limitations; and what has not been verified

## Code signing

There is no certificate, so the executable is unsigned. On first run Windows SmartScreen shows
"Windows protected your PC".

This is documented rather than worked around. **Do not tell users to disable SmartScreen.** Publish
the SHA-256 with every artifact so a download can be verified, and if signing becomes important,
obtain an OV or EV certificate and sign in `package.ps1` — the script is the right place for it.

## Versioning

`<Major>.<Minor>.<Patch>` in `Directory.Build.props`.

- **Patch** — fixes only.
- **Minor** — new features, no change to how documents are written.
- **Major** — a change to the annotation payload format, or anything that makes files written by a
  newer version unreadable by an older one.

The annotation payload carries `AnnotationSerializer.SchemaVersion`. Increment it when the shape
changes, and make the reader migrate rather than fail: an unknown newer version currently causes the
annotation to be read as foreign, which preserves it but loses editability.

## After a release

- [ ] Confirm the published SHA-256 matches the attached file
- [ ] Download the zip on a different machine and start it
- [ ] Update `docs/HANDOFF.md` with the release location and checksum
