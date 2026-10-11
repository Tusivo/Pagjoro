# Third-party notices

Pagjoro is licensed under the GNU Affero General Public License 3.0 or later. This file lists the other software it uses or plans to use, and the license of each. A component is added here **before** it is merged (see `LEGAL.md`).

## In use now
| Component | Used for | License |
|---|---|---|
| .NET 10 runtime and WPF (Microsoft) | The program's runtime and window | MIT |
| xUnit (tests only, not shipped) | Unit tests | Apache-2.0 |

## Planned (not yet shipped)
| Component | Planned use | License | Compatible with AGPL-3.0-or-later |
|---|---|---|---|
| MuPDF | Rendering, annotations, forms, redaction, page operations | AGPL-3.0 | Yes (same license) |
| qpdf | Repair, linearize, encrypt | Apache-2.0 | Yes |
| Tesseract OCR | Text recognition | Apache-2.0 | Yes |
| BouncyCastle (C#) | Signatures, timestamps, certificates | MIT-style | Yes |
| Open XML SDK | Writing Word, Excel and PowerPoint files | MIT | Yes |
| veraPDF (optional, run as a separate program) | Checking archival standards | GPL-3.0 / MPL-2.0 | Yes |

## Standards implemented
- ISO 32000-1 and ISO 32000-2 (PDF)
- ISO 19005 (PDF/A), ISO 15930 (PDF/X), ISO 14289 (PDF/UA)
- ETSI EN 319 142 (PAdES), RFC 5652 (CMS), RFC 3161 (timestamps)

The standards are public specifications; no text from them is reproduced here.
