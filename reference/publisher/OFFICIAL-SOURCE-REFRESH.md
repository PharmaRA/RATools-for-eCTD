# QA-07 official source retrieval — 2026-11-07

This record advances source collection only. It does not qualify a regional
snapshot, activate Formal mode, establish a complete criteria inventory, or
replace a version-fixed external validator comparison.

Evidence is retained locally under
`.artifacts/qa-07-official-sources/20261107T220356-ff9eebb20e0d4a17bfec18f1d94723a0/`.
The download receipts record the exact URLs, retrieval UTC timestamps, HTTP
status, byte lengths and SHA-256 values. Original files remain available.

## EMA materials obtained

The [official EU Module 1 index](https://esubmission.ema.europa.eu/eumodule1/index.htm)
states that EU M1 3.1.1 and Validation Criteria 8.2 were accepted from
**2025-10-01**, with mandatory use from **2025-12-01**. These are the index's
applicability dates, not the retrieval date. The workbook's title identifies
Version 8.2, June 2025.

| Retained file | HTTP | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `ema-module1.html` | 200 | 43026 | `72b9a7b2d720b2f48f3f4f18805d4b768215a8414601a7655be8867b049aa116` |
| `ema-validation-criteria-v8.2.xlsx` | 200 | 71307 | `0f64339496564d89abe9e96376f29805dce2302a56f54ce12858d083315d1fd9` |
| `ema-implementation-guide-v3.1.1.pdf` | 200 | 1002589 | `ae27b9a2258ac396071234539f2bbd88491dec03521c1b12ded135bdf49b2492` |

Direct material URLs, as linked by the retained index:

- [Validation Criteria 8.2](https://esubmission.ema.europa.eu/eumodule1/eCTD%203.2.2%20-%20EU%20M1%20Validation%20Criteria%20v8.2.xlsx)
- [Implementation Guide 3.1.1](https://esubmission.ema.europa.eu/eumodule1/eCTD%203.2.2%20-%20EU%20M1%20Implementation%20Guide%20v3.1.1.pdf)

`extract-ema-workbook.ps1` reads the downloaded OOXML archive without extracting
filesystem entries, follows the workbook's internal sheet relationships, rejects
DTDs/external XML resolution, expands shared strings and preserves cell addresses,
raw values, types, styles and cached formula values. It creates a new
`ema-workbook-rows.json` and records the workbook and extraction-script hashes.
The result's SHA-256 is
`011fca345a8f3769a4ac6edb7cd919a97fa9fc0dabb2e12af70c369744ae8a4d`.

| Sheet | Stored rows, including headings and notes |
| --- | ---: |
| Document Change Control | 38 |
| eCTD Validation Criteria | 117 |
| File-Folder Structure & Names | 430 |
| Files and Folders Q&A | 16 |
| Picklist Values | 73 |

This extraction does not render Excel number formats or recalculate formulas.
Row counts are not qualified rule counts. The picklist sheet title still says
EU M1 3.1; reconciling that title and the actual values with the 3.1.1 guide is
part of the pending semantic review.

## FDA retrieval remains unresolved

The two attempted official URLs both returned HTTP 404 in this environment:

- `https://www.fda.gov/drugs/electronic-regulatory-submission-and-review/ectd-submission-standards-ectd-v322-and-regional-m1`
- `https://www.fda.gov/media/87056/download`

No FDA document bytes were obtained. Earlier conversational references to
“4.7 / 2026-10-16” lack reproducible retrieval/search evidence in this run and
are withdrawn. A 404 response does not establish FDA's current version or the
reason for the retrieval failure.

## Remaining qualification work

1. Obtain the applicable FDA originals and identify publication, transition and
   effective dates from retained evidence.
2. Review every applicable EU/US criterion and vocabulary value against the
   declared procedure, language, submission type and document scope. Record
   implementation mapping, not-applicable reasons and gaps against an exact
   standards snapshot and digest.
3. Validate the same retained input with an authorized, version-fixed external
   business validator, preserve its raw report and explain each discrepancy.
4. Complete the Sources, ControlledVocabulary, MetadataMapping, Writer, Rules
   and ExternalComparison gates in `REGIONAL-VALIDATION-GATES.md` before Formal
   qualification. This retrieval record changes none of those statuses.

The QA-07 execution ledger remains `QA-07-ACCEPTANCE.md`; current execution order
is maintained in `docs/plans/quality-improvement.md`.


## FDA retry on 2026-11-08

Evidence: `.artifacts/qa-07-official-sources/20261108T112504Z-07fb0ab5a4384a81bcd690631a3f8b26/receipts.json` and four retained `.response` files. Both URLs above were requested directly and through the local port 7890 proxy. All four requests ended at `https://www.fda.gov/apology_objects/abuse-detection-apology.html`, returned HTTP 404 and the same 10 non-PDF bytes, SHA-256 `493fda53120050f85836032324409be6c6484f90a0755ae0c6a673ba7626818b`. Requested/final URLs, UTC, response size, status and PDF magic checks are recorded.

No FDA original was obtained. Repeated commentary claiming an actual search receipt for “4.7 / 2026-10-16” had no corresponding search result and is withdrawn. This checkpoint relies only on the retained download receipts, makes no current-version assertion, and leaves the official-source and Formal gates open. The redirect identifies the observed response endpoint; it does not independently establish why access failed.


## Remote Linux retry after environment recovery

On 2026-11-08 the same two FDA URLs were also requested from the recovered Linux machine. Both returned HTTP404 at the same abuse-detection endpoint, with the same10 non-PDF bytes and SHA-256 `493fda53120050f85836032324409be6c6484f90a0755ae0c6a673ba7626818b`. Receipts and responses were downloaded under `.artifacts/qa-07-official-sources/remote-fda-20261108T231809-570bfd87bb3c42039e27513d676c2fc1/`. This establishes an additional actual network attempt, not a current FDA version or original-source acquisition. The source and external Formal gates remain open.
