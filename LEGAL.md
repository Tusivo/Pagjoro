# Legal posture and clean-room policy

*This is a project policy, not legal advice. Have an IP lawyer review it before a public 1.0 release or any commercial use.*

## Why open source and free does not by itself protect us
Copyright, trademark, patent and contract claims do not depend on charging money. The goal is to **not do the things that create claims**, and to be able to show that we did not.

## What Pagjoro may do
- Implement **functionality**. Ideas, methods and functions (merging pages, redacting text, filling forms, comparing documents) are not protected by copyright, and many compatible programs already exist.
- Read and write **PDF files** by the published standards: ISO 32000-1 and ISO 32000-2, the PDF/A, PDF/X and PDF/UA family, and the signature standards (PAdES, CMS). PDF is an open ISO standard, and the standard's patents are licensed royalty-free for conforming implementations.
- Use **generic interface patterns** common to all document software (a page list, a toolbar, a properties pane).
- Say truthfully that Pagjoro is an **alternative** to commercial subscription PDF software, and that it **opens and saves PDF files** (nominative use of a trademark to describe what a product is compared to).

## What Pagjoro must not do
| Don't | Why |
|---|---|
| Use another company's product names, tool names, logos, icons, illustrations, sounds, screenshots, sample files, help text or marketing copy in the program, documents or website | Trademark and copyright |
| Copy another program's screens, layout or wording pixel for pixel, or reproduce its icon set | Copyright and trade dress |
| Decompile, disassemble or otherwise inspect another company's program to learn how it works | License terms usually forbid it; legal risk |
| Bundle another company's fonts, color profiles or data files | Copyright and license |
| Use another company's online services, trust lists or SDKs in ways their terms forbid, or imply a tie to them | Contract and trademark |
| Claim affiliation, certification or endorsement | False association |
| Copy code from a program or project whose license is incompatible with AGPL-3.0-or-later | Copyright and license conflict |

## Clean-room practice
1. **Specs only.** Implement from public specifications and from our own synthetic test files. Do not paste another company's text, samples or files into the repository.
2. **Describe by what it does.** Our documents and our interface name features by their function (merge, split, redact), and our tool names are our own. The automated test in `tests/Pagjoro.Tests` fails if a name we have promised not to use appears in the program's source.
3. **Original look.** Our own name, colors, icon, wording and layout. Where a screen resembles a common industry pattern, say in the pull request that the pattern is generic and the expression is ours.
4. **Record provenance.** Each feature that implements a standard names the standard it follows (see `docs/FEATURE-MAP.md`). Contributors confirm that they did not use confidential or license-restricted material.
5. **Third-party code.** Every dependency needs a license review (AGPL-compatible) and goes into `THIRD-PARTY-NOTICES.md` before it is merged.
6. **Patents.** We cannot rule out third-party software patents. Before any commercial distribution, commission a freedom-to-operate review.
7. **Takedown plan.** If anyone objects, respond promptly (through counsel for anything formal) and be ready to rename or restyle. This is why branding and interface are original and separable from the engine.

## Test files
Use only files made for the project (generated, or created by the contributor) or files whose license allows redistribution. Never commit a real person's or company's document.

## License
AGPL-3.0-or-later. Contributors license their work under the same terms.
