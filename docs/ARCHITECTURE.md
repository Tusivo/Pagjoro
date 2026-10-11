# Architecture

## The shape of the program
Pagjoro is a single native Windows program written in C# on .NET 10 with WPF for the window.

```
Pagjoro.App    the window: screens, controls, dialogs, printing (WPF)
   |
Pagjoro.Core   the program's logic with no user interface: documents, operations, settings
   |
engines        PDF engine (rendering, parsing, page and annotation operations), OCR, cryptography
```

- **`src/Pagjoro.Core`** has no reference to WPF, so everything in it can be tested without a window, and a command-line tool can use it later.
- **`src/Pagjoro.App`** only presents what Core can do. Each tool is a small screen over a Core operation.
- **`tests/Pagjoro.Tests`** runs against Core (and the repository's policy checks).

## Planned components
| Job | Component | License |
|---|---|---|
| Rendering, text, annotations, forms, redaction, page operations | MuPDF, through .NET bindings (or its C interface) | AGPL-3.0 |
| Repair, linearize, encrypt, structure rewriting | qpdf | Apache-2.0 |
| Text recognition | Tesseract; Windows' built-in engine as an option | Apache-2.0 / OS |
| Signatures, timestamps, certificates | BouncyCastle and .NET cryptography | MIT-style |
| Word, Excel, PowerPoint files | Open XML SDK | MIT |
| Archival-standard checking | veraPDF (optional, external) | GPL-3.0 / MPL-2.0 |

Every one of these must be reviewed against `LEGAL.md` and listed in `THIRD-PARTY-NOTICES.md` before it is added. The first technical task of Phase 1 is a short spike that confirms the MuPDF binding exposes what Phases 3 to 6 need (annotations, forms, redaction, saving incrementally); if it does not, Core calls MuPDF's C interface directly.

## Updates
`Updater` (in Core) lists the project's GitHub releases, picks the newest one that has both `Pagjoro.exe` and `Pagjoro.exe.sig`, downloads it, checks its size, header and signature against the public key embedded from `src/Pagjoro.Core/release-key.txt`, and swaps it in with a backup. Nothing is installed unless the signature verifies.

## Rules for the code
1. **Core never shows a window.** Errors are returned or thrown with a plain-language message the window can show as it is.
2. **An operation never changes its input.** It reads a document and produces a new one; saving is a separate, explicit step.
3. **Untrusted input.** Every file is hostile until proven otherwise (see `SECURITY.md`): limits on size and nesting, and no active content.
4. **One version.** `Directory.Build.props` holds the version; nothing else repeats it.
5. **Original expression.** Names, wording, colors and icons are Pagjoro's own (see `LEGAL.md`).

## Packaging
`scripts/build.ps1` produces one self-contained `dist\Pagjoro.exe` (no installer, no separate .NET install) and its SHA-256. Releases are signed with `scripts/release-sign.js` (see `docs/RELEASING.md`).
