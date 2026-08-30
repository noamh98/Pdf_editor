# How signatures are stored

## What a signature is here

A **graphical** signature: an image of a handwritten signature, drawn with a mouse, pen or touch
screen, or imported from a file. It is placed on a page as a picture.

It is **not** a cryptographic digital signature. It proves nothing about who placed it and can be
copied by anyone who has the file. The application says so wherever a signature is used:

> זוהי חתימה גרפית ואינה חתימה דיגיטלית מאומתת.

## Where it is stored

`%LOCALAPPDATA%\PdfEditor\signatures`, two files per signature:

| File | Contents |
| --- | --- |
| `<id>.json` | Name, creation time, pixel dimensions, whether the payload is protected |
| `<id>.bin` | The PNG image, DPAPI-protected on Windows |

`LocalApplicationData` is deliberate. The roaming folder would be synchronised to a domain server or
a Microsoft account, which is exactly what must not happen to a signature.

## How it is protected

On Windows the payload is encrypted with
`ProtectedData.Protect(bytes, entropy, DataProtectionScope.CurrentUser)` — the Windows Data
Protection API, keyed to the logged-in Windows account, with an application-specific entropy value
so another application's DPAPI blob cannot be read as this one's.

No home-grown cryptography is used, and no key is stored beside the data.

### What that does protect against

- Another Windows account on the same machine reading the file. It cannot decrypt it.
- The file being copied to another machine and read there.
- Casual inspection of the application data folder.

### What it does not protect against

Stated plainly, because DPAPI is often assumed to do more than it does:

- **Anything running as you.** Malware or another program under your Windows account can call
  `Unprotect` exactly as this application does.
- **An attacker with your Windows password**, who can log in as you.
- **Offline access to an unencrypted disk** combined with credentials, which is what BitLocker is
  for.
- **A process with debug privileges** reading the decrypted bytes from memory.

## Consequences of DPAPI you will actually notice

- **Signatures do not travel with the portable folder.** Copy the application to another machine, or
  log in as a different Windows user, and stored signatures cannot be decrypted. They are shown as
  unavailable rather than silently vanishing. This is the intended trade-off: portability of the
  application, not of your personal data.
- **A Windows profile reset loses them.** DPAPI keys are tied to the user profile.
- On a platform without DPAPI — the Linux machines this project is developed and tested on — the
  payload is stored unprotected and `SignatureEntry.IsProtected` is `false`, so the interface can be
  honest instead of implying a protection that is not there.

## Deleting

Deleting a signature overwrites the payload with random bytes before unlinking it, then removes the
metadata file. "מחיקת כל החתימות" does the same for every entry.

**This is not secure erasure.** On an SSD with wear levelling the overwrite may land on a different
physical block than the original, leaving the old data recoverable by a forensic tool. Full-disk
encryption is the real answer; the documentation does not claim more than the code does.

## What is never done

- A signature is never uploaded, transmitted or backed up anywhere.
- A signature is never written to a log.
- A signature is never committed to the repository: `signatures/`, `*.signature` and `*.sig.json`
  are in `.gitignore`, and CI fails if one becomes tracked.
- No signature is captured from a camera or a scanner by the application.

## "זכור את החתימה במחשב זה"

Turning this off keeps the signature in memory for the session only; nothing is written to disk.
Turning it back on stores signatures created from then on. It does not delete what is already
stored — "מחיקת כל החתימות" does that.

## Windows Hello, and why it is not here

Requiring a biometric or PIN unlock before using a stored signature was considered and rejected for
version 1:

- It would not survive a copy of the portable folder, defeating the point of a portable build.
- It fails on machines with no enrolled biometric or PIN, which needs a fallback that weakens it.
- Most importantly, it would invite the belief that the signature is cryptographically bound to the
  person, which it is not. A graphical signature that is hard to unlock is still a picture once it
  is on the page.

It is recorded as possible future work in `docs/KNOWN_LIMITATIONS.md`, not as an existing protection.
