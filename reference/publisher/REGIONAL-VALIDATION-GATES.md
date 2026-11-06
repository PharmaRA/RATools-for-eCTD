# Regional validation and activation gates

P6-08 implements selected regional **project policies**, not a complete FDA/EU
criteria inventory. It inspects external package XML independently of the
authoring/import model. Formal admission remains blocked for bundled profiles.

## Selected checks

| Profile | Project policy | Input and result |
| --- | --- | --- |
| US M1 3.3 / criteria label 4.5 | US-ADMIN-METADATA | Explicit applicant/application/submission identifiers, descriptions, contact names/types, ordered phones/emails, and actual sequence identity |
| US | US-FORM-ATTRIBUTES | Nonblank form-type on supported form elements, with actual parent scope; CDATA is not promoted to a verified FDA code list |
| US | US-SUBMISSION-SCOPE | Promotional m1.15 branches remain NotEvaluated, outside selected administrative/forms trial scope |
| EU M1 3.1.1 / criteria 8.2 | EU-ENVELOPE-METADATA | Explicit envelope values and repeated names/tracking values, captured DTD codes, national DE/FR/BE policy combinations, actual sequence identity |
| EU | EU-PI-CONTEXT | Actual country, PI language/type and parent context; FDA form attributes do not apply |
| EU | EU-RELATED-SEQUENCE | Initial/reformat reference current sequence; other units reference earlier actual selected history, with matching parsed envelope business identity |

All six descriptors have ProjectPolicy provenance, source locators, explicit
profile applicability, severity, readiness requirements and positive/negative
evidence. Their IDs are internal IDs. Complete US-CRITERIA-INVENTORY and
EU-CRITERIA-INVENTORY remain required NotImplemented gaps. No official rule ID,
regional severity or regulatory business vocabulary is invented.

The inspection consumes bounded ParsedBackbone facts already produced by the
XML/lifecycle pipeline and the exact PackageInputSet. It does not reread package
XML, consult current authoring metadata or assume that a source registration's
application GUID establishes its XML business identity. Missing or partial
regional XML and absent/uninspected required history produce explicit Fail or
NotEvaluated results, with source file and element locations where available.

EU vocabulary comes from selected captured DTD bytes. Country/procedure
combinations outside the declared national trial matrix remain pending. Missing
history is unknown; malformed, future and foreign business references fail.
Required unknowns and failed checks remain blocking report coverage.

## Shared algorithms and different applicability

RegionalEctdRuleSetProvider returns separate versioned mappings with profile,
criteria version, ProjectPolicy source/locator, sections, parameters and finding
severity. Shared algorithms consume those mappings. FDA-NAMING-1 is selected
only for US. Unknown profiles fail selection. The historical EU criteria alias
is marked as a compatibility alias; it does not confer Formal qualification.

The shared PDF algorithm uses a declared common PDF 1.4 project acceptance
policy, with existing navigation recommendations explicitly labelled as project
parameters. Other PDF versions need regional acceptance evidence. Independent
package PDF findings take severity from each selected sequence's captured
catalog. Reuse of an algorithm does not establish equal regulatory coverage.

## Immutable selection and report binding

Every inspection uses the selected PackageStandardsContext, including each
historical sequence's own catalog/assets. A byte-exact pre-P6-08 catalog from
commit e087d3c is retained for replay tests; its SHA-256 is
`f75b4947a988f38c881a7f8d4d34d78f71925eb1f4d87078a1f168dd49a587f2`.
An archived catalog lacking the six new descriptors retains its own coverage.
Current rules are not inserted into that historical catalog.

Changed inspection behavior has new engine identities: package-delivery-v4,
package-regional-inspector-v2 and package-pdf-validator-v2. A new inspection
cannot claim the previous engine's cache identity. Existing archived reports
remain readable; a queued run with an incompatible engine must be recreated.

## Formal admission

Snapshot lifecycle Active is one condition. Exact snapshot/content identity,
selection date, template/business scope, versioned official source bytes and
verified Sources, ControlledVocabulary, MetadataMapping, Writer, Rules and
ExternalComparison evidence remain required. Trial writer/DTD/parser successes
do not manufacture these evidence states. Unconfirmed regional selection is
still unqualified for Formal admission; the independent input's bytes must be
bound to complete qualification evidence rather than inferred from current
workspace metadata.

POST /api/package-validations returns HTTP 400 with
`code: STANDARDS_SELECTION_BLOCKED`, a readable message and the individual
`reasons` for rejected standards selection. No run is created by that refusal.
Development enqueue remains possible and binds the exact snapshot and new engine
identity. It does not authorize finalization.

## Verification boundary

The independent acceptance uses actual snapshot/resource HTTP routes, actual
Formal/Development enqueue routes, PostgreSQL restoration through a new
connection, old literal binding/manifest digests and archived catalog replay.
Source files and immutable test inputs are retained. Tests with physical cleanup
are not executed without a retained/recycle-bin adapter.

Final analyzed layer is
`.artifacts/current-head-review-20261104/p608-backend-stage5/`; compiler output is
`../p608-round1-stage5-compile.log`. Four layers compile without warnings/errors
(one existing informational CS9236). `p608-round1-final-green.log` records
108 passed / 0 failed, including regional, catalog, report and provider cases.
The independent snapshot/HTTP/PostgreSQL run is
`p608-parent-final-stage5.log` (21 passed / 0 failed).

Task review found that old-catalog history could have a matching identifier but
declare the wrong sequence. The association now checks identifier and sequence
on the same parsed historical envelope. Wrong/noncanonical sequences fail;
blank or ambiguous values remain unknown. The focused RED had 45 passes and
five failures; the final run includes eight additional historical pairing and
ambiguity cases. This change also increments the regional component to v2.

OpenAPI export passed. Its semantic diff adds only the existing enqueue route's
HTTP 400 response, with no path or schema addition/removal. The two regional
frontend files passed 15 tests; direct TypeScript and ESLint checks passed.
These counts are separate scoped runs, with overlapping dependencies.

This is local Windows implementation evidence. It does not establish Linux,
the whole repository suite, a current external regional business validator or
regulatory activation. P6-09 records the declared local acceptance matrix and
all remaining qualification dependencies.
