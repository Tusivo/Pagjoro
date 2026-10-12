# Architecture

## The shape of the program
Pagjoro is a single native Windows program written in C# on .NET 10 with WPF for the window.

```
Pagjoro.App    the window: menus, toolbar, viewer, dialogs, printing, Windows-only services (WPF)
   |
Pagjoro.Core   the program's logic with no user interface: documents, operations, settings
   |
engines        PDFium (rendering, parsing, page and mark operations), PDFsharp (structure, encryption, signing)
```

- **`src/Pagjoro.Core`** has no reference to WPF or to Windows-only APIs, so everything in it can be tested without a window, and a command-line tool can use it later.
- **`src/Pagjoro.App`** presents what Core can do, and holds the pieces that need Windows itself: text recognition, scanning, Office conversion, picture codecs.
- **`tests/Pagjoro.Tests`** runs against Core (and the repository's policy checks).

## Components
| Job | Component | License |
|---|---|---|
| Rendering, text, marks, forms, page objects, redaction | PDFium through PDFiumCore | BSD-3-Clause / Apache-2.0 |
| Encryption, bookmarks, attachments, signatures, form structure | PDFsharp | MIT |
| Signatures and time stamps | .NET cryptography (CMS) | MIT |
| Text recognition | Windows built-in engine | Part of Windows |
| Word and Excel files | Written directly as Office Open XML | n/a |

Every one of these was reviewed against `LEGAL.md` and is listed in `THIRD-PARTY-NOTICES.md`. A candidate engine, MuPDF.NET, was rejected because its .NET package is licensed for non-commercial use only.

## Notes on the engine
- **One engine at a time.** PDFium is not thread safe, so every call goes through `PdfEngine.Gate`.
- **Form fields are drawn separately.** PDFium's page renderer leaves them out; `PdfFile.Render` draws them with the form-fill layer.
- **Redaction.** Marks that touch an area are removed. A text line is cut at the covered letters and the rest is re-set in a built-in font. Pictures have only the covered pixels blanked. A page whose content sits inside an embedded form object cannot be edited in place, so it is turned into a picture with the areas blacked out.
- **Editing text.** The PDF format has no "paragraph", only runs of letters. Pagjoro replaces a run and re-sets it at the same place in a built-in font or an installed Windows font; it does not try to reflow a page. A Windows font is embedded in the document once, however much text uses it.
- **Two libraries, one file.** PDFium edits page content and marks; PDFsharp edits the document's structure (bookmarks, attachments, signatures). Each operation hands the other a freshly saved copy of the bytes, never a shared object.

## Updates
`Updater` (in Core) lists the project's GitHub releases, picks the newest one that has both `Pagjoro.exe` and `Pagjoro.exe.sig`, downloads it, checks its size, header and signature against the public key embedded from `src/Pagjoro.Core/release-key.txt`, and swaps it in with a backup. Nothing is installed unless the signature verifies.

## Rules for the code
1. **Core never shows a window.** Errors are returned or thrown with a plain-language message the window can show as it is.
2. **An operation never changes its input.** It reads a document and produces a new one; saving is a separate, explicit step.
3. **Untrusted input.** Every file is hostile until proven otherwise (see `SECURITY.md`): limits on size and nesting, and no active content.
4. **One version.** `Directory.Build.props` holds the version; nothing else repeats it.
5. **Original expression.** Names, wording, colors and icons are Pagjoro's own (see `LEGAL.md`).
6. **Nothing leaves the computer unasked.** The only network traffic is Check for Updates and an optional time stamp request, both started by the person.
7. **Files stay beside the program.** `AppPaths` is the one place that decides where settings live (`Pagjoro-Data` next to the exe, else the account's folder). The registry is only written by an explicit "Make Pagjoro the Default PDF App" request, under the person's own account.

## Packaging
`scripts/build.ps1` produces one self-contained `dist\Pagjoro.exe` (no installer, no separate .NET install) and its SHA-256. Releases are signed with `scripts/release-sign.js` (see `docs/RELEASING.md`).
