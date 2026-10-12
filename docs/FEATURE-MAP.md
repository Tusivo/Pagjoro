# Feature map

What Pagjoro does and what it is meant to do, grouped by build phase. Feature names describe what the feature does; they are Pagjoro's own words.

**State:** ✔ done · ◐ partly done · ○ planned · ⚠ hard or depends on outside things (see Notes).

## Phase 0: Foundations
| Item | State | Notes |
|---|---|---|
| Solution, build, tests, version in one place | ✔ | `Directory.Build.props`, `scripts/build.ps1` |
| Clean-room policy and a test that enforces it | ✔ | `LEGAL.md`, `PolicyTests` |
| Original name, icon and colors | ✔ | `scripts/make-icon.ps1` |
| Release signing | ✔ | `scripts/release-sign.js` signs; the program embeds the public key |
| Check for Updates, accepting only signed releases | ✔ | `Updater` in `Pagjoro.Core`; offline-testable |
| Third-party notices | ✔ | `THIRD-PARTY-NOTICES.md` |
| Light, Dark and Night themes, remembered | ✔ | |
| Portable: settings kept in a `Pagjoro-Data` folder beside the program | ✔ | Falls back to the account's own folder when that cannot be written |
| Offer Pagjoro to Windows as a PDF program, with steps to choose it | ✔ | Windows does not allow a program to make itself the default |

## Phase 1: View and read
| Feature | State | Notes |
|---|---|---|
| Open and view pages; zoom, fit width and page | ✔ | |
| Page layouts: single, two pages side by side | ✔ | Continuous scrolling in both |
| Thumbnails and bookmarks panes | ✔ | |
| Search text, with match case and whole words | ✔ | One document at a time |
| Print | ✔ | |
| Open password-protected files | ✔ | |
| Recent files | ✔ | |
| Keyboard shortcuts (zoom, page, search, go to page) | ✔ | |
| Rotate the view without changing the file | ✔ | Rotating pages themselves is in Phase 2 |
| Read aloud and high-contrast viewing | ○ | Windows speech APIs |
| Measure distance, area, perimeter | ○ | |
| Reopen at the last page and zoom | ✔ | Remembers the last 100 files |
| Window opens where it was left | ✔ | |
| Layers pane | ○ | |

## Phase 2: Organize and create
| Feature | State | Notes |
|---|---|---|
| Merge files into one, with reordering | ✔ | |
| Split by page ranges or every N pages | ✔ | |
| Rotate, delete, insert, extract, reorder pages (drag thumbnails) | ✔ | |
| Crop pages and set page boxes | ✔ | |
| Page labels and numbering styles | ✔ | Roman, letters, prefixes |
| Create a PDF from pictures | ✔ | |
| Create from a scanner | ◐ | Uses the Windows scanner service; not tested on real hardware by the author |
| Office files to PDF | ◐ | Uses installed Microsoft Office, or the free LibreOffice; neither is bundled |
| Portfolios and packages of mixed files | ○ | |

## Phase 3: Annotate and review
| Feature | State | Notes |
|---|---|---|
| Highlight, underline, strike through | ✔ | |
| Notes | ✔ | Click a mark to choose it, edit its words or colour, move it, delete it |
| Shapes (box, oval) and freehand drawing | ✔ | |
| Stamps, with ready-made and custom words and the date | ✔ | |
| Marks list | ✔ | |
| Export the marks list (text or spreadsheet) | ✔ | Page, type, the words marked and the comment |
| Highlight colours (seven) and line thickness | ✔ | |
| Compare two documents | ✔ | Word by word, page by page |
| Collect and merge comments from several copies | ○ | |

## Phase 4: Forms
| Feature | State | Notes |
|---|---|---|
| Fill forms | ✔ | Text fields, check boxes, drop-downs |
| Create text fields and check boxes | ✔ | |
| Create radio buttons, drop-downs, list boxes | ○ | |
| Import and export form data | ✔ | JSON, and a CSV row |
| Flatten a form | ✔ | |
| Detect fields from the page layout | ○ | |
| Calculations and validation | ⚠ | Needs a safe, limited script engine; off by default |

## Phase 5: Edit content
| Feature | State | Notes |
|---|---|---|
| Edit text on the page | ◐ | A line is replaced and re-set at the same place in a font of your choice; the original font is not reused |
| Replace, delete and move pictures and text | ✔ | |
| Add text (font, size, colour, several lines) and pictures | ✔ | The twelve built-in fonts, and about twenty families installed in Windows, with Latin, Greek and Cyrillic letters |
| Headers, footers, page numbers | ✔ | |
| Watermarks | ✔ | |
| Bates numbering | ✔ | |
| Links to web addresses and pages | ✔ | |
| Embed audio and video | ○ | |

## Phase 6: Protect and redact
| Feature | State | Notes |
|---|---|---|
| Open and owner passwords, AES-256 encryption | ✔ | |
| Restrict printing, copying, editing | ✔ | |
| Certificate-based encryption | ○ | |
| Redact by selection, with true removal | ✔ | Only the covered letters and picture pixels are removed; verified by tests that search the output |
| Search and redact by pattern (numbers, emails, words) | ✔ | |
| Pages that cannot be edited in place are turned into a picture | ✔ | Safe fallback, so nothing under a redaction survives |
| Sanitize: remove metadata, hidden layers, scripts, attachments | ✔ | Lists what it found |

## Phase 7: Sign and certify
| Feature | State | Notes |
|---|---|---|
| Digital signature with a certificate from the Windows store | ✔ | CMS, SHA-256, visible or invisible |
| Trusted timestamps | ✔ | RFC 3161, optional, the authority is the person's choice |
| Validate signatures | ✔ | Shows if the file changed after signing |
| Sign with a typed, drawn or image signature | ○ | |
| Certify a document (restrict later changes) | ○ | |
| Long-term validation data | ⚠ | |
| Send for signature | ⚠ | Needs a service; out of scope for a local program |

## Phase 8: Scan and OCR
| Feature | State | Notes |
|---|---|---|
| Scan from a connected scanner | ◐ | See Phase 2 |
| Recognize text so scans are searchable | ✔ | The text recognition built into Windows; languages are the ones installed in Windows |
| Cleanup: deskew, despeckle, crop | ○ | |

## Phase 9: Convert and export
| Feature | State | Notes |
|---|---|---|
| Export pages to images | ✔ | PNG |
| Export text | ✔ | |
| Export to a web page, Word document, Excel workbook, CSV | ◐ | Headings, paragraphs and text columns are recovered; complex layouts are "good, not perfect" |
| Office files to PDF | ◐ | See Phase 2 |
| Convert to archival and print standards (PDF/A, PDF/X) | ⚠ | Needs a validator |
| Batch conversion | ✔ | Through action sequences |

## Phase 10: Compare, optimize, check
| Feature | State | Notes |
|---|---|---|
| Compare two files and summarize changes | ✔ | |
| Reduce file size: picture recompression and repacking | ✔ | Choice of how much |
| Print and sharing check: fonts, protection, hidden data, page sizes | ✔ | |
| Accessibility check: title, language, tags, text, bookmarks, form labels | ◐ | Cannot yet check alternative text, contrast or table markup, and cannot add tags |
| Space audit: what takes up room | ○ | |
| Fast web view (linearize) | ○ | |
| Tag editing and reading order | ⚠ | Automatic tagging is difficult |

## Phase 11: Automation
| Feature | State | Notes |
|---|---|---|
| Action sequences ("do these steps to these files") | ✔ | Watermark, header/footer, Bates numbers, rotate, flatten, sanitize, reduce size, information, language, password; saved as a file |
| Batch processing of many files into a folder | ✔ | |
| Safety copy of unsaved work, offered after a crash | ✔ | |
| Command line | ○ | |
| Custom tool sets and toolbars | ○ | |

## Later or never
| Feature | State | Notes |
|---|---|---|
| Cloud storage and sync | ○ | Not a goal: Pagjoro is local-first |
| Mobile apps | ○ | |
| Three-dimensional and rich-media content | ○ | |
| AI summaries and questions | ⚠ | Only as an optional, local, off-by-default feature |
| Enterprise deployment and policy control | ○ | |
