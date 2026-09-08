# ICH section definitions (P1-01)

`ich-3.2.2-nodes.json` is the first closed node schema. It covers all 158 M2-M5
section elements, the ICH M1 backbone-reference container and `node-extension`.
The snapshot is embedded in the Application assembly and read by
`IchSectionDefinitions`; it is not a user-editable dictionary of arbitrary XML.
Domain definitions expose immutable attributes, children and validation results.

`scripts/standards/generate_ich_node_schema.py` uses lxml/libxml2's DTD declaration
API to read actual content models and expanded attribute declarations. Its
`--check` mode compares the entire generated snapshot without writing files.
CI installs the pinned dependency from `scripts/standards/requirements.txt`.
Only the reviewed, hash-locked ICH DTD is supported; unsupported grammar or
unreviewed business attributes fail generation. No regular-expression DTD
parser or runtime network resolver supplies this schema.

The nine attributed/repeatable groups are in `standards-scope.json`. Their
attributes are all identity attributes for this comparison version. `ID` and
`xml:lang` are permitted technical attributes and do not replace business
identity. Raw values remain unchanged; validation never trims or case-folds
manufacturer, substance or other values. XML attribute names are ordinal and
unknown names are rejected even if the caller's dictionary ignores case.

DTD requiredness and business completeness are distinct: the DTD requires the
presence of substance/manufacturer and indication attributes. The node schema
also requires those values to be nonblank for editing/publishing completeness.
Optional product/excipient attributes remain optional. Import may retain an
incomplete value with a diagnostic; the subsequent instance layer must not
invent one to satisfy completeness.

Defined child order, minimum/maximum occurrence, leaf position and extension
positions are fixed. Leaves precede defined child sections. At permitted terminal
sections, leaves and extensions may interleave. Extensions contain a title and
at least one leaf/extension and cannot contain standard section elements. The
schema's permission to extend does not assert regional acceptability; the G0
regional-policy qualification remains required.

Definition keys are the exact XML element names within the versioned set.
`CtdSection` is a compatibility/display path, not a unique key. In particular,
the official `m2-3-introduction` and `m2-3-quality-overall-summary` both have the
numeric context `m2.3`. They are separate definitions with an explicit parent
relation. Instance persistence, routing and output must use the definition key
and node ID, never regroup these elements solely by section number.

The G0 package fixtures and official ICH excerpts check M1-M5, repeated substance
nodes and study extensions. `Fixtures/CtdNodes/attribute-matrix.xml` independently
exercises all nine business-attribute groups, including optional omissions and
multiple indications/excipients/appendices. It is a grammar specimen with empty
sections, not a deliverable package. Both the runtime schema and the original
DTD validate it. Tests separately reject missing/unknown/invalid attributes,
wrong parents, cardinality/order violations and invalid extension content.

This task introduces definitions and their validation. P1-02 adds business
instances, P1-03 persists them, P1-06 imports context and P1-07 makes the publisher
consume the instance tree. The existing section writer and frontend are not
yet an implementation of the full multi-instance workflow. Regional M1 node
attributes and allowed values remain assigned to P6.
