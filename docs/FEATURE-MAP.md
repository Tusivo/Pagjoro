# Feature map

What Pagjoro is meant to do, grouped by build phase. Feature names describe what the feature does; they are Pagjoro's own words.

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

## Phase 1: View and read
| Feature | State | Notes |
|---|---|---|
| Open and view pages; zoom, rotate, fit width and page | ○ | |
| Page layouts: single, two-up, continuous | ○ | |
| Thumbnails, bookmarks, layers, attachments panes | ○ | |
| Search text, search across several files | ○ | |
| Read aloud and high-contrast viewing | ○ | Windows speech APIs |
| Measure distance, area, perimeter | ○ | |
| Print, with scaling and booklet options | ○ | |
| Open password-protected files | ○ | |
| Recent files, reopen at the last page | ○ | |

## Phase 2: Organize and create
| Feature | State | Notes |
|---|---|---|
| Merge files into one, with reordering | ○ | |
| Split by pages, size or bookmarks | ○ | |
| Rotate, delete, insert, replace, extract, reorder pages | ○ | |
| Crop pages and set page boxes | ○ | |
| Page labels and numbering | ○ | |
| Create a PDF from images, text and a blank page | ○ | |
| Create from the clipboard or a scanner | ○ | |
| Print-to-PDF capture | ⚠ | Needs a printer driver; first version uses the Windows built-in one |
| Portfolios and packages of mixed files | ○ | |

## Phase 3: Annotate and review
| Feature | State | Notes |
|---|---|---|
| Highlight, underline, strike through | ○ | |
| Notes, text boxes, callouts | ○ | |
| Shapes, freehand drawing, arrows | ○ | |
| Stamps, including custom stamps | ○ | |
| Comment list, filter, reply, status | ○ | |
| Comment summary and export | ○ | |
| Collect and merge comments from several copies | ○ | |

## Phase 4: Forms
| Feature | State | Notes |
|---|---|---|
| Fill forms, with auto-complete | ○ | |
| Create fields: text, check box, radio, drop-down, list, button, signature | ○ | |
| Detect fields from the page layout | ○ | |
| Calculations and validation | ⚠ | Needs a safe, limited script engine; off by default |
| Import and export form data (FDF, XFDF, CSV) | ○ | |
| Barcode fields | ○ | |
| Flatten a form | ○ | |

## Phase 5: Edit content
| Feature | State | Notes |
|---|---|---|
| Edit text, with font matching | ⚠ | Reflowing existing text is the hardest edit problem |
| Add, replace, resize, crop images | ○ | |
| Move, add and remove objects | ○ | |
| Headers, footers, page numbers | ○ | |
| Watermarks and backgrounds | ○ | |
| Bates numbering | ○ | |
| Links and buttons with actions | ○ | |
| Embed audio and video | ○ | |
| Edit text in scanned pages after OCR | ⚠ | Depends on OCR quality |

## Phase 6: Protect and redact
| Feature | State | Notes |
|---|---|---|
| Open and permissions passwords, strong encryption | ○ | |
| Certificate-based encryption | ○ | |
| Restrict printing, copying, editing | ○ | |
| Redact by selection, with true removal | ○ | Verified by tests that search the output |
| Search and redact by pattern (numbers, dates, words) | ○ | |
| Sanitize: remove metadata, hidden layers, comments, scripts | ○ | Lists what it found before removing |

## Phase 7: Sign and certify
| Feature | State | Notes |
|---|---|---|
| Sign with a typed, drawn or image signature | ○ | |
| Digital signature with a certificate from the Windows store | ○ | PAdES / CMS |
| Timestamps | ○ | RFC 3161 |
| Certify a document (restrict later changes) | ○ | |
| Validate signatures and show who changed what | ○ | Uses the Windows trust store; the user can add trusted certificates |
| Long-term validation data | ⚠ | |
| Send for signature | ⚠ | Needs a service; out of scope for a local program |

## Phase 8: Scan and OCR
| Feature | State | Notes |
|---|---|---|
| Scan from a connected scanner | ○ | Windows Image Acquisition |
| Cleanup: deskew, despeckle, crop | ○ | |
| Recognize text so scans are searchable | ○ | Tesseract, and the Windows engine as an option |
| Choose recognition languages | ○ | |

## Phase 9: Convert and export
| Feature | State | Notes |
|---|---|---|
| Export pages to images (PNG, JPEG, TIFF) | ○ | |
| Export to text, HTML, RTF | ○ | |
| Office files to PDF | ⚠ | Uses the installed Office through its own export; optional free office suite as a fallback |
| PDF to Word, Excel, PowerPoint | ⚠ | Layout recovery; "good, not perfect" |
| Convert to archival and print standards (PDF/A, PDF/X) | ⚠ | Validated with an open-source checker |
| Batch conversion | ○ | |

## Phase 10: Compare, optimize, check
| Feature | State | Notes |
|---|---|---|
| Compare two files and summarize changes | ○ | |
| Reduce file size: image, font and structure optimization | ○ | |
| Space audit: what takes up room | ○ | |
| Fast web view (linearize) | ○ | |
| Print-readiness checks | ⚠ | Color, fonts, standards |
| Accessibility check, tag editing, reading order | ⚠ | Automatic tagging is difficult |

## Phase 11: Automation
| Feature | State | Notes |
|---|---|---|
| Action sequences ("do these steps to these files") | ○ | |
| Batch processing of folders | ○ | |
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
