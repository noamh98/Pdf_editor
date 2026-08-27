# ADR-0006: Signature storage, protection and its honest limits

Status: accepted (milestone 2)

## Context

A stored graphical signature is personal data. It must survive between sessions when the user asks
for it, must never leave the machine, and must be deletable for good. It is not a cryptographic
signature and must never be presented as one.

## Threat model summary

In scope: another local user reading the signature file; the signature being synchronised to a cloud
profile; the signature leaking through a log, a temporary file or the repository; the file surviving
a "delete" as recoverable data.

Out of scope: an attacker who already has code execution as this Windows user, physical access with
full-disk access to an unencrypted drive, and malware with debugger privileges. DPAPI in
`CurrentUser` scope does not defend against those and this ADR does not claim it does.

## Decision

- Store under `%LOCALAPPDATA%\PdfEditor\signatures`, deliberately **Local** rather than **Roaming**,
  so the data is never picked up by profile synchronisation.
- Split each entry into a small metadata JSON file and a separate binary payload.
- On Windows, protect the payload with `ProtectedData.Protect(..., DataProtectionScope.CurrentUser)`
  and a fixed application entropy. No home-grown cryptography and no key stored next to the data.
- On any other platform, store unprotected and report `IsProtected = false` so the UI can be honest
  rather than implying protection that is not there.
- Deleting overwrites the payload with random bytes before unlinking.
- The UI always shows: "זוהי חתימה גרפית ואינה חתימה דיגיטלית מאומתת."
- `*.signature`, `signatures/` and `*.sig.json` are in `.gitignore`.

## Consequences

- DPAPI `CurrentUser` ties the payload to the Windows user account on that machine. Copying the
  portable folder to another machine, or to another user, makes stored signatures unreadable. This
  is the intended trade-off and it is stated in the UI and in `docs/SIGNATURE_STORAGE.md`.
- Overwrite-before-delete reduces but does not eliminate recoverability on SSDs with wear levelling;
  the documentation says so rather than promising secure erasure.
- Windows Hello / `KeyCredentialManager` was considered and **not** implemented. It would not survive
  a portable copy, adds a failure mode when no biometric device is enrolled, and would invite users
  to believe the signature is cryptographically bound to them. It is recorded as possible future
  work, not as an existing protection.
