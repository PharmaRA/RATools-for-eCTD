# Independent delivered bytes and PDF links (P2-05)

`PackageDeliveryInspector.InspectAsync(PackageDeliveryInput, profileSnapshotId)`
runs independent XML/lifecycle, file and PDF inspections against explicitly
captured directory/ZIP inputs. It needs no business database or publisher model.
The caller owns and disposes the captures. No adjacent ZIP or history is adopted
automatically; no stylesheet, PDF action or external URL is executed or fetched.

## Binding and result

`PackageDeliveryInput` accepts a target/history `PackageInputSet` and an optional
comparison capture. A comparison must be the opposite source kind (directory or
ZIP), with the same application, sequence and limits. Without a comparison the
input digest is the primary capture digest. With a comparison it is SHA-256 of
canonical JSON containing `schemaVersion: 1`, `primaryInputDigest` and
`comparisonInputDigest`. Raw ZIP bytes already participate in each capture digest,
so repacking identical files changes the binding. Reports use the combined digest.

Inputs are reverified around phases and before returning. Results retain history,
profile, rule/asset catalog, limits and actual parser engine versions. Changed
inputs and user cancellation propagate without a completed reusable report.
`CreateReport(mode)` aggregates observations and preserves catalog coverage gaps;
Completed means the run finished, not that the package passed or is ready to file.
All current development profiles remain unqualified for finalization.

## Sources and implemented scope

The normative source is the pinned ICH eCTD 3.2.2 PDF in `standards-scope.json`.
Page references below give PDF page and printed page. The machine-readable rule
catalog retains regional and external-qualification gaps separately.

| Source | Behavior |
| --- | --- |
| p. 10 (2-2), Appendix 5 | Resolve relative file references against explicit current/history captures; compare each declared MD5 with actual captured bytes, including regional XML, and validate `index-md5.txt`. |
| pp. 11–12 (2-3–2-4) | Check lowercase name tokens, one file extension, 64-character names and 230-character paths. Regional exceptions and acceptance remain unqualified; recommended folder spellings are not hard failures. |
| p. 94 (4-65), Appendix 8 | Inspect declared stylesheets and delivered pinned DTD/module/XSL bytes. Creation-time stylesheet identity and complete regional style policy remain incomplete. |
| p. 107 (6-10) | Resolve historical content reuse from explicitly supplied packages. Missing history cannot borrow an adjacent physical directory. |
| p. 114 (7-1) | PDF 1.4 is jointly accepted; other versions require regional evidence. Common-font exemptions are retained. Nested resources, full/subset embedding and regional font exceptions remain incomplete. |
| pp. 116–117 (7-3–7-4) | Inspect PDF destinations, relative links and security restrictions. Four bookmark levels is a recommendation. TOC/bookmark correspondence, page labels and initial views remain content-review gaps. |
| Project delivery binding contract | Compare directory/ZIP path sets, lengths, SHA-256 and empty directories; reject additional content outside the selected final ZIP sequence. |

File checks distinguish missing files, malformed references, application escape,
future sequence, missing history, checksum mismatch and unresolved checksums.
Delete checks remain in lifecycle inspection and do not require a new file.
Queries and external content references remain explicitly unevaluated. XML PDF
fragments share the PDF destination evaluator.

The inventory records empty directories and surrounding files. Reachability starts
from backbones and known utility assets, then follows inspected references; a
disconnected PDF link cycle does not make its files referenced. Unreferenced content
is reported for profile review rather than universally prohibited.

Stylesheet pseudo-attributes and XSL import/include dependencies are parsed with
DTD/entity processing prohibited and XML limits enforced. XSL assets are separate
from the DTD resolver allowlist. Unknown assets and generated `href`/`src` resources
remain unevaluated because the intended presentation base is not established. The
pinned ICH XSL emits a CSS reference absent from this source snapshot; inspection
does not invent a filesystem base or execute the transformation to resolve it.

## PDF facts, destinations and limits

`IPackagePdfInspector` separates extraction from rule evaluation.
`PdfPigPackageInspector` uses PdfPig 0.1.15 with strict parsing and reads raw page
trees, annotations, old destination dictionaries, destination name trees, outlines
and OpenAction. GoTo, GoToR, URI, page and named destinations are retained. Duplicate
names, invalid page references and unsupported/chained/additional actions remain
visible; URI-only extraction cannot silently drop them.

PDF file specifications are literal filenames, not URIs. Their segments are encoded
before the shared once-decoding resolver: literal `%20` remains `%20`, whereas a
URI `%2520` refers to that same literal filename. GoToR zero-based page indices become
one-based report pages. URI fragments support a bare name, `page=` or `nameddest=`;
conflicting/invalid destinations fail, other parameters remain unevaluated.
Uninspected or unreadable targets cannot pass destination checks.

Every PDF in the selected target/history set is inspected, including unreferenced
PDFs. Limits bind file bytes, pages (10,000), visited navigation objects (200,000),
depth (64), links (50,000), findings and elapsed time. Pages/links also have aggregate
caps. The outer delivery deadline covers all phases. Limit exhaustion preserves
explicit incomplete checks; the outer deadline raises `DELIVERY_INSPECTION_TIMEOUT`.

Cancellation is cooperative and checked during stream operations and traversal.
PdfPig runs in process: these bounds do not provide hard CPU/decompression or
process-memory isolation. A parser inner operation can delay cancellation. Durable
worker/tool isolation remains service work; no hard-preemption claim is made here.

## Evidence

`PackageFileInspectorTests` uses the hand-authored four-sequence fixture, physical
mutations and real ZIPs. It covers same-path/different-byte comparisons, changed
captures, hashes, historical references and inventory. `PdfPigPackageInspectorTests`
constructs raw PDF object graphs without the publisher or PdfPig writer.
`PackagePdfValidatorTests` checks exact historical file/page/name resolution and
unknown states. `PackageDeliveryInspectorTests` covers combined report binding,
unreachable cycles, stylesheets, aggregate limits and the overall deadline.

The source/catalog/manifest digests, libxml parser and lifecycle business oracle
remain independent Python checks. These are local implementation evidence, not an
external regulatory validator report. Linux and regional/tool qualification remain
open under the roadmap.
