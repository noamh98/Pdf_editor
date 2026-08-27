# ADR-0008: Two explicit save modes and atomic writes

Status: accepted (milestone 2)

## Context

Users need both "keep working on this later" and "send a final version nobody can un-mark". Merging
those into one Save button is how documents get silently flattened and work gets lost.

## Decision

Two clearly separated commands:

- **Save / Save As** writes annotations as standard, re-editable PDF annotation objects with
  appearance streams (ADR-0003).
- **Export Final Copy** draws the same content into the page content stream, removes `/Annots`,
  and always writes a **new** file. It warns first that the result cannot be re-edited, and it
  refuses to overwrite the source path without an explicit confirmation.

Every write goes through `AtomicFileWriter`: content is written to a sibling temporary file, flushed
to disk, and only then moved onto the target with `File.Replace` (falling back to `File.Move` where
`Replace` is unsupported, such as across volumes). A failure or cancellation deletes the temporary
file and leaves the original untouched.

## Consequences

- An interrupted save cannot truncate an existing document.
- Editable and flattened output are produced by the same drawing routine, so what the user sees
  after flattening is what they saw before it.
- The test suite asserts the source file's SHA-256 is unchanged after Save As, Export, merge and
  split. This is a hard requirement, not a nicety.
- `File.Replace` leaves a backup file behind on some filesystems; the writer deletes it and
  `CleanupOrphans` removes any stragglers from an earlier crash.
