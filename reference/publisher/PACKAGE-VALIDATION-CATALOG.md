# Independent package validation catalog (P2-01)

`package-rules-v1.json` is the versioned development inventory for independent
package validation. Its 37 checks cover controlled inputs, XML, node identities,
lifecycle references, delivered bytes, PDF inspection and qualification gaps.
It is not a complete regional regulatory checklist. In particular, the FDA and
EU criteria inventory entries are explicit blockers until individual regional
criteria are sourced, implemented and qualified in P2/P6.

The three profile snapshot IDs are explicit and immutable within this catalog:

| Snapshot | Scope |
| --- | --- |
| `ich-3.2.2-development-v1` | ICH independent validation development scope |
| `us-fda-3.2.2-m1-3.3-development-v1` | Existing US M1 development scope; current FDA requirements unverified |
| `eu-3.2.2-m1-3.1.1-development-v1` | EU M1 3.1.1 / criteria 8.2 development scope |

The ICH template key is reserved for independent inspection; it does not add a
new publishing template. None of these profiles authorizes finalization.

## Traceability and evidence

Each rule has an internal ID, nullable official authority ID, source reference,
source section, version, profile scope, applicability, severity and readiness
policy. Source records carry HTTPS provenance, version, verification status and
a content hash when verified. Project safety/identity policies are labeled
`ProjectPolicy` separately from regulatory specifications. A qualified profile
cannot require a rule from an unverified source.

Fixture lists identify available positive inputs and negative variants, not
successful independent executions. `invalid-cases.json#case-id` addresses the
named mutation; `scripts/fixtures/materialize_publisher_fixture.py` creates its
actual directory or ZIP. Empty lists expose fixture gaps. `componentEvidence`
records reusable implementation assets without claiming an independent check is
complete. All external comparison lists are currently empty.

Existing PDF, naming and lifecycle checks depend on publishing models or business
repositories. Their presence only supports `Partial` inventory status. The
legacy `FDA-NAMING-1` label is not an official rule ID. Existing PDF thresholds
are not proof of current regional requirements. ICH recommended directory names
must not become mandatory exact spellings without profile evidence. The pinned
ICH 3.2.2 change history permits same-sequence append; later lifecycle inspection
must evaluate that rule independently of conservative editor restrictions.

## Coverage and readiness

Implementation status is `Implemented`, `Partial`, `Manual`, `NotImplemented`
or `NotApplicable`. Actual execution status is separately `Pass`, `Fail`,
`NotEvaluated` or `NotApplicable`. Implemented declarations require positive and
negative evidence references; promoting a catalog entry still requires review of
the actual tests and qualification evidence.

`ValidationReport.Create` expands the entire selected profile inventory. Missing
checks become `NotEvaluated`. Passing component observations from incomplete
implementations remain `NotEvaluated`. Required failures and required unevaluated
checks block readiness regardless of severity; an Error failure also blocks
when advisory policy would otherwise allow the rule. Conditional rules may be
`NotApplicable` only with an explicit rationale; always-applicable rules cannot.
The evaluator is responsible for establishing that rationale from actual input.

Manual closure requires explicit rule permission and accountable evidence with
reviewer, UTC timestamp, reason and matching pass/fail outcome. The evidence must
match all seven binding fields: input digest, history digest, profile snapshot,
catalog digest, engine version, mode and resource-limit digest. The current
development catalog permits no manual closures. Evidence artifact storage and
reviewer authorization will be implemented with durable runs in P2-06/08.

`ExecutionCompleted` means the engine finished. `IsReadyForFinalization` additionally
requires no blocking coverage, a qualified profile and Formal mode. Failed,
cancelled, changed, pending and running inputs never authorize finalization.
These models do not yet add an API endpoint or change publishing behavior.

## Digest and validation

The full JSON document is canonicalized using `ratools-canonical-json-v1`; its
SHA-256 is pinned in `package-rules-v1.sha256` and binds every report. Source,
scope, evidence and policy edits all invalidate report reuse. The strict embedded
loader rejects duplicate/unknown/missing members, numeric or unknown enums,
duplicate IDs and unresolved references.

`scripts/tests/test_package_rule_catalog.py` independently verifies canonical
digest, source hashes and every evidence path/variant ID. The existing publisher
reference CI check invokes it. After an intentional catalog edit, recompute the
digest with the independent canonical serializer and review both changes.

Run from the repository root:

```powershell
py -3.11 -X utf8 scripts/tests/test_publisher_reference_contract.py
dotnet test tests/RATools.Tests/RATools.Tests.csproj -c Release --filter FullyQualifiedName~Tests.PackageValidation
```

The tests cover embedded provenance, exact digest binding, strict parsing,
immutable rule lists, coverage gates, all seven manual binding dimensions and
portable report serialization. P2-02 adds database-independent directory/ZIP and
history reading, including an always-applicable `INPUT-LIMITS` check in addition
to ZIP metadata limits. These input entries now link to the actual reader and
rejection tests, while per-run report integration and Linux qualification remain
pending. P2-03/04 add XML inspection and lifecycle resolution.

P2-03 now implements seven independent XML checks and pins their embedded assets
through the catalog's `ratools-package-xml-assets-v1` source. Node context and
extension criteria retain their outstanding mapping/regional qualification gaps.
See [PACKAGE-XML-INSPECTION.md](PACKAGE-XML-INSPECTION.md) for execution boundaries
and the independent libxml fixture checks.
