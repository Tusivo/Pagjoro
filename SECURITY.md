# Security model

PDF files are a common way to attack computers, so a PDF tool has to treat every file it opens as untrusted. This is the model Pagjoro is built to.

## Design principles
- **Local only.** Pagjoro does not upload documents and has no accounts. It makes no network connections of its own, apart from any the person starts on purpose (for example opening a link).
- **No active content by default.** Scripts, launch actions and embedded programs inside a PDF are not run. If a feature ever needs them, it is off until the person turns it on, per document.
- **Untrusted by default.** A file is parsed by a well-tested engine, and a damaged or hostile file must fail safely: a clear message, no crash, no change to the original.
- **Never overwrite the original silently.** Saving a changed file keeps the person in control of where it goes; risky operations (redaction, encryption, signing) ask first.
- **Redaction is real.** A redaction removes the underlying text and image data from the saved file, not just a black box on top, and is verified by tests that search the output.

## Controls (planned, tracked in docs/FEATURE-MAP.md)
| Area | Control |
|---|---|
| Parsing | Memory-safe handling where possible, size and nesting limits, fuzz testing of the importers |
| Passwords | Never stored; held in memory only while a file is open |
| Signatures | Private keys stay in the Windows certificate store; Pagjoro never exports them |
| Metadata | A "remove hidden information" step that lists what it found before deleting |
| Updates | Releases are signed with a key kept offline; the program accepts only updates that verify (see docs/RELEASING.md) |
| Dependencies | Reviewed for license and security before use; listed in THIRD-PARTY-NOTICES.md |

## Known limitations
- Pagjoro is in early development and has had no independent security review.
- PDF rendering and parsing code (including third-party engines) is complex; vulnerabilities in dependencies can affect Pagjoro. Keep Pagjoro up to date.

## Reporting a vulnerability
Please report security problems **privately**, not in a public issue: use GitHub's "Report a vulnerability" button on the repository's Security tab, or write to [info@tusivo.com](mailto:info@tusivo.com) with the subject "Pagjoro security". Include the version, what you did and what happened. Please do not attach private documents.
