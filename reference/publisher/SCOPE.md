# Publisher qualification baseline

G0-01 records the sources and scope for the September 2026 publisher roadmap.
`standards-scope.json` is the reviewable inventory. It is deliberately separate
from a standards provider's claim that a workflow is ready for submission.

ICH eCTD 3.2.2 (16 July 2008) was retrieved from ICH on 5 September 2026. The
bundled ICH DTD is byte-identical to the copy in EMA's pinned EU M1 package.
Pinned DTD/XSL assets disable Git line-ending conversion so their recorded
SHA-256 values are identical on Windows and Linux checkouts.
The eCTD specification version **3.2.2** and the DTD version **3.2** are distinct.
EMA's current source page was also retrieved: it states that EU M1 3.1.1 and
validation criteria 8.2 were accepted from 1 October 2025 and mandatory from
1 December 2025. This source check does not qualify all EU procedures or languages.

The FDA URLs in the existing provider could not be retrieved (Not found).
US M1 3.3, technical guide 1.9 and validation criteria 4.5 therefore remain
**unverified code-baseline labels**. No effective date is inferred from those
labels. The US DTD hash pins the current input but does not prove its provenance.
Resolve this source gap before an FDA readiness claim or regional qualification.

## Node inventory

All nine M2–M5 elements with business attributes are enumerated in the JSON.
They are also the nine standard elements allowed to repeat under their parent.
Requiredness comes from the DTD, not from an example or the workspace UI.

| Sections | Required attributes | Optional attributes |
| --- | --- | --- |
| 2.3.S, 3.2.S | substance, manufacturer | — |
| 2.3.P, 3.2.P | — | product-name, dosageform, manufacturer |
| 2.7.3, 5.3.5 | indication | — |
| 3.2.P.4 | — | excipient |
| 3.2.A.1, 3.2.A.2 | — | manufacturer, substance, dosageform, product-name |

The technical attributes `ID` and `xml:lang` are separate from business identity.
All business attributes here have DTD type CDATA; there is no arbitrary-attribute
escape hatch. Application-level completeness rules must be identified separately
from DTD requiredness. Missing optional identity attributes are not permission
to merge otherwise ambiguous imported sibling nodes.

ICH Appendix 3 Tables 3-2 through 3-5 define the directory examples. In particular,
the substance/manufacturer directory belongs **inside** `32s-drug-sub`, before
the subject subdirectories. ICH Appendix 6 Example 6-4 describes multiple
substances, manufacturers and products. Display labels are not directory keys.

M4/M5 study grouping uses `node-extension` only where the DTD permits it. ICH
Appendix 6 Example 6-5 discourages unnecessary extensions and calls for regional
guidance. Regional acceptability is still open. A DTD-valid extension is not
automatically a regionally qualified study structure.

## Lifecycle and evidence boundaries

The four ICH operations are in scope. `modified-file` identifies an XML document
and fragment in sequence context. An append preserves an association; it does
not rewrite the target PDF. The ICH specification also discusses same-sequence
append; this and subsequent operations on append chains need explicit P3-01
qualification. Region-specific restrictions must not be inferred from the DTD.

Run `python3 scripts/tests/test_publisher_reference_contract.py` (Windows:
`py -3.14 -X utf8 scripts/tests/test_publisher_reference_contract.py`). It checks
the inventory independently against the pinned DTD and detects changed source
bytes or omitted attributed/repeatable elements. G0-02 adds hand-authored XML,
independent expectations and corrupted variants. External validator evidence
remains a distinct P2 deliverable.
