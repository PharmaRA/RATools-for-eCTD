# Independent lifecycle references (P2-04)

`PackageLifecycleInspector.InspectAsync(PackageInputSet, profileSnapshotId)` reads
the explicitly captured current and historical packages. It invokes the independent
XML inspector using each baseline entry's profile, never a repository, placement,
writer model, filename match or automatically selected resend. Inputs are verified
before and after inspection. Results bind input, baseline, profile, rule catalog,
asset manifest, resource limits and engine version.

## Source decisions

The pinned ICH eCTD 3.2.2 PDF in `standards-scope.json` is the normative source for
these decisions; page numbers below are PDF pages, followed by printed pages.

| Source | Checked behavior |
| --- | --- |
| Appendix 6, p. 99 (6-2) | Each submission references a regional administrative backbone; that reference always has operation `new`. Presence is checked under `PROFILE-VERSION`; operation under `LIFECYCLE-OPERATION`. |
| pp. 100–101 (6-3–6-4) | `new` is independent. Empty `modified-file=""` is equivalent to omission. Other operations require a single exact target. |
| p. 100 (6-3) | Same-sequence append can be appropriate after consulting the authority. It is `NotEvaluated` until an applicable policy exists, rather than a universal prohibition. |
| p. 101 (6-4) | Delete supplies no new content and has `checksum=""`. ICH/US/EU writers and the hand-authored delete fixture were corrected; only affected byte manifests were refreshed. |
| p. 101 (6-4) | Replaced/deleted leaves cannot be targeted by later operations. An append preserves the target's effectiveness. |
| p. 102 (6-5) | A lifecycle change need not preserve filenames; append does not merge PDF bytes. |
| p. 107 (6-10) | A leaf can reuse content delivered in an earlier sequence. Its href is resolved from its own XML using the same explicit input set. |

Exact business-context comparison uses the project contract's node ancestry and
identity attributes. It does not use titles, filenames, or the last leaf in a
section. Regional business identity, profile transitions, authority consultation
and complex append combinations remain unqualified.

## Resolution and replay

References decode once relative to the containing backbone. URI query, external
scheme, application escape, future sequence and invalid fragment are rejected.
Diagnostics separately identify absent history, absent XML, uninspected XML,
missing ID, duplicate ID, non-leaf ID, DTD-invalid XML, inactive target, unresolved
context and mismatched context. Delete is inspected even without an href.
Historical leaf content is resolved to its exact selected file; byte checksums,
PDF contents and links are P2-05, so file existence here is not file qualification.

Each sequence's operations are resolved before any effectiveness changes are
applied. Multiple operations on one target have no order-dependent winner.
Same-sequence operations/cycles and subsequent operations on append branches
remain `NotEvaluated`; invalid and unresolved events remain in the result. Unknown
target states and unreadable intermediate sequences cannot establish a later
target's effectiveness. This is deliberately a limited replay for validation,
not the full P3 cumulative review policy or a record of regulatory submission.

`Events` preserve source/target addresses, historical content paths, findings and
status. `States` distinguish Current, Replaced, Deleted, DeleteEvent and Unknown;
duplicate source IDs remain separate events. `ResolutionComplete` only describes
these scoped checks relative to the supplied baseline. It is not finalization
readiness, proof that all historical submissions were selected, or qualification
of regional/PDF/extension rules.

Unverified/legacy history stays `NotEvaluated`; rejected history fails. A verified
baseline entry must carry a validation-run reference, but this database-free
component cannot authenticate that record. P2-06's service must authorize inputs
and verify persisted trust/evidence before constructing the baseline. Different
profiles never inherit compatibility automatically.

## Limits and evidence

One deadline covers input revalidation, XML inspection and replay. Backbone/node
counts and findings have aggregate caps using the bound read limits. Cancellation
and changed input propagate without producing a reusable successful result;
timeout/limit returns explicit incomplete coverage and any observations obtained.

`PackageLifecycleInspectorTests` compares each of the four sequence states and all
three historical events with hand-authored `expected.json`. It tests exact URI
failures, historical content reuse, delete checksums, regional-relative references,
trust/profile gaps, ambiguous or invalid XML, inactive targets, order-independent
conflicts, cycles, unsupported branches, limits, cancellation and changed history.
The independent Python/libxml fixture checks and ordinary publishing regressions
remain separate evidence. No external regulator-tool qualification or Linux run
is claimed by the Windows test receipts.
