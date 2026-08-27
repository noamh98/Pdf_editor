# ADR-0010: How "no network" is enforced rather than promised

Status: accepted (milestone 2)

## Context

"Works offline" is easy to claim and easy to break by accident — a font loaded from a CDN, an
update check, a crash reporter, a NuGet package that phones home.

## Decision

Enforce it structurally:

1. No HTTP client, socket or networking package is referenced by any project. The dependency
   inventory in `docs/DEPENDENCIES.md` lists every package and its purpose.
2. All assets — fonts, icons, OCR language data — are embedded or copied into the package at build
   time. Nothing is fetched at runtime, and no remote font or stylesheet is referenced.
3. There is no update check, no analytics, no crash upload and no opt-in that could add one.
4. Logs contain operation names, durations and error codes only. Never file names, page text, OCR
   output or signature data.
5. CI runs a check that fails the build if `System.Net.Http`, `HttpClient`, `WebClient`,
   `Socket` or a `http://` / `https://` literal outside documentation and package metadata appears
   in application source.

## Consequences

- The claim is testable, and a regression fails the build rather than shipping.
- Nothing can be diagnosed remotely; support relies on the user reading a local log.
- Language data and fonts must be present in the package, which is why the artifact is tens of
  megabytes rather than a few.
