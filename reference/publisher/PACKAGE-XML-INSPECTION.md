# Independent XML inspection (P2-03)

`PackageXmlInspector.InspectAsync` reads only `ICapturedPackageInput`, an explicit
catalog profile and pinned standard assets. It does not use a publishing package
model, database document, placement or repository. The capture is reverified before
and after inspection. Changed input propagates `PackageInputChangedException`;
user cancellation propagates cancellation; the inspection deadline produces
incomplete coverage with an `XML_READ_TIMEOUT` finding.

## Pinned profiles and resources

`package-xml-assets-v1.json` pins the ICH DTD, US DTD, EU DTD and its two modules,
plus the ICH node schema. The rule catalog includes the exact asset-manifest hash,
so asset/schema changes also change the rule digest. Every resource is embedded
and its exact bytes are verified before use. The EU profile's M1 specification
version is 3.1.1; its DTD declares `dtd-version="3.1"`, as the source requires.

The offline resolver accepts only the selected profile's exact logical asset
addresses in the selected sequence. Relative spelling and percent escapes are
resolved by the same once-only path logic used by the package reader. Absolute
identifiers, unknown paths, queries, fragments and nonempty internal subsets are
rejected. An empty `[]` does not declare or override anything. Parameter entities
inside trusted, hash-verified DTDs are permitted under the entity expansion limit;
the EU modules are resolved only after their trusted parent asset was served.

The parser never fetches a URL, opens an arbitrary local system identifier, or
executes an XSL transformation. Package-provided DTD bytes cannot override the
embedded profile. Delivery-asset presence/byte verification remains P2-05.
Exact asset locations are an explicit development-profile resolution policy;
they do not turn all recommended ICH folder names into regulatory requirements.

The existing publishing validator remains separate. Its filename-based resolver
is not used by independent package inspection.

## Parsing and identities

The ICH index must match the selected DOCTYPE, root qualified name, namespace,
main DTD and effective DTD version. Known DTD defaults are accepted and marked
`IsDefault`, preserving the distinction from explicitly supplied attributes.
Source encoding is handled by the XML reader; parsed attribute case and whitespace
are preserved according to XML normalization.

ICH M1 leaf references identify regional XML files, including custom paths and
filenames with encoded spaces or literal `#` characters. The known default
regional file is also inspected when present. A missing referenced backbone is
an error. If a sequence contains no regional reference or default regional file,
its regional DTD check is NotApplicable; whether that absence violates regional
submission criteria remains an independent, unqualified regional inventory check.
An ICH-only selection with regional references reports `REGIONAL_PROFILE_REQUIRED`
instead of guessing a regional profile.

Each `ParsedBackbone` contains immutable raw elements/attributes, original
locations, ICH node contexts, leaves and an XML-document-scoped ID index. Duplicate
IDs retain all occurrences and both locations. ID attributes on titles and other
non-leaf elements participate in that same index. Equal IDs in different
backbones remain separate, valid addresses. `LeafAddress` stores the decoded XML
file component separately from the fragment and escapes literal filename `#`
characters when generating a URI.

`ReadComplete` means the XML stream was consumed with the selected declaration
checks. It is distinct from `DtdStatus`: a well-formed XML can be fully read and
still fail DTD validation. Malformed, truncated or declaration-rejected documents
never expose partially built node/leaf/ID indexes. Later lifecycle checks must
inspect completion, DTD status and ID ambiguity before trusting a target.

The ICH schema checks mandatory attributes, allowed values, parent relationships,
child order/cardinality and extension placement. Node context keys use exact
schema identity attributes and ancestry; absent and empty attributes differ.
Identical sibling contexts remain separate and ambiguous, and ambiguity propagates
to descendant leaves. Extension titles are metadata rather than identity. An
optional-ID extension without a stable XML ID remains unresolved, even when its
DTD structure is valid. Regional elements and their attributes are retained raw;
full regional business identity/schema modeling remains P6.

## Coverage, limits and qualification

The result records input digest, profile, engine version, rule digest, asset digest,
resource-limit digest and per-rule observations. Findings locate the sequence,
backbone, node, leaf and field where available, with original XML line/column.
Incomplete parsing cannot turn unexecuted checks into Pass. The report model
continues to distinguish engine completion, rule failures and finalization readiness.

`PackageReadLimits` now also binds XML depth, nodes (including attributes), document
characters, expanded entity characters, finding count and backbone count. XML
reads and identity processing observe cancellation; the whole inspection has an
elapsed-time deadline. Truncating findings retains an explicit blocking limit
finding and marks remaining checks incomplete.

Seven catalog entries now have implemented independent XML execution and fixture
evidence: offline resource resolution, XML limits, profile declaration consistency,
ICH/US/EU DTD checks and document-scoped XML IDs. ICH context and extension checks
record their implemented structural components while retaining Partial status for
unresolved historical mappings and regional acceptance. Current FDA sources,
complete regional criteria, external whole-package comparison and Linux execution
qualification remain incomplete. No development profile authorizes finalization.

## Evidence

Tests use the hand-authored four-sequence G0 fixtures and their independent business
oracle, plus a hand-authored EU regional XML. They exercise real directory/ZIP
captures, DTD modules, attributes/defaults, duplicate and invalid IDs, namespace and
version conflicts, custom paths, UTF-16/Unicode, missing backbones, ambiguous nodes,
forbidden extensions, malicious identifiers/internal entities, quotas, cancellation
and input changes. Passing XML checks still leave file/lifecycle/qualification
checks incomplete in a complete catalog report.

`scripts/tests/test_package_xml_contract.py` uses a separate libxml implementation
to verify six valid backbones, three invalid DTD variants and two optional/default
cases. CI runs it after installing the pinned standards-tool requirements. This
is structural corroboration, not an external regulatory validator report.

```powershell
python -m pip install -r scripts/standards/requirements.txt
python scripts/tests/test_package_xml_contract.py
python scripts/tests/test_publisher_reference_contract.py
dotnet test tests/RATools.Tests/RATools.Tests.csproj -c Release --filter 'FullyQualifiedName~Tests.PackageValidation|FullyQualifiedName~LeafAddressTests'
```

P2-04 next resolves the retained `modified-file` values to exact selected historical
backbones, fragments and effective lifecycle chains. P1-09 follows the minimum
P2-01–04 chain.
