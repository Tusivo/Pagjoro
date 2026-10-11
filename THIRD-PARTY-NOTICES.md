# Third-party notices

Pagjoro is licensed under the GNU Affero General Public License 3.0 or later. This file lists the other software it uses, and the license of each. A component is added here **before** it is merged (see `LEGAL.md`).

## Shipped inside Pagjoro.exe
| Component | Used for | License |
|---|---|---|
| .NET 10 runtime and WPF (Microsoft) | The program's runtime and window | MIT |
| PDFium (Google, Foxit and contributors) | Reading, rendering, text, marks, forms and page operations | BSD-3-Clause |
| PDFiumCore (.NET bindings and native build) | Calling PDFium from C# | Apache-2.0 |
| PDFsharp | Encryption, document information, bookmarks, attachments, signatures and form structure | MIT |

## Used on the person's computer, not shipped
| Component | Used for | License |
|---|---|---|
| Windows text recognition (`Windows.Media.Ocr`) | Making scanned pages searchable | Part of Windows |
| Windows Image Acquisition | Scanning | Part of Windows |
| Microsoft Office (if installed) | Turning Word, Excel and PowerPoint files into PDF | The person's own license |
| LibreOffice (if installed) | The same, as a free alternative | MPL-2.0 |

Pagjoro starts these programs; it does not include or modify them.

## Tests only (not shipped)
| Component | Used for | License |
|---|---|---|
| xUnit | Unit tests | Apache-2.0 |

## Standards implemented
- ISO 32000-1 and ISO 32000-2 (PDF)
- RFC 5652 (CMS signatures), RFC 3161 (time stamps)
- ECMA-376 (Office Open XML: the Word and Excel files Pagjoro writes)

The standards are public specifications; no text from them is reproduced here.

## Considered and not used
| Component | Why not |
|---|---|
| MuPDF.NET | Its .NET package is licensed for non-commercial use only, which does not fit an AGPL project that anyone may reuse |
