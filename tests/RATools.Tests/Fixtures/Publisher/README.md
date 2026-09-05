# Independent publisher fixtures (G0-02)

`sequences/0000` through `0003` are frozen, hand-authored eCTD XML inputs. No
RATools package model, writer, importer or lifecycle projector generated them.
`expected.json` and `expected-tree.txt` were authored separately as business
oracles. The file manifest is mechanical byte evidence, not the semantic oracle.

The synthetic application is provisionally FDA IND 000001. This is a development
fixture, not an actual application or a regionally qualified submission. ICH DTD
structure, file references and simple lifecycle semantics are checked. Current
FDA controlled terms, regional extension/append permission and external-tool
qualification remain open in the source scope and P2/P6 plans.

The PDFs contain only the public test sentences listed in `payloads.json`.
They were produced with PdfPig 0.1.15 using an embedded subset of Arial, contain
one searchable page each, and are frozen here with their hashes. No production
document or patient information was used. Changing a payload requires a reviewed
fixture revision, updated leaf/index checksums and a new file manifest.

`external/` contains transcriptions of ICH's own examples (ICH eCTD 3.2.2,
16 July 2008, Appendix 6). Their authorship is independent of this project.
Typographic quotation marks and PDF line wrapping were normalized; an eCTD root,
namespace declarations and DTD reference were added around each excerpt.
The leaf IDs, business attributes, titles, hrefs and checksums come from ICH.
The examples' referenced PDFs are not available, so these are explicitly
**structure-only controls**, not complete validated external packages. Attribution
is to ICH; these excerpts are not relicensed as original project content.

`invalid-cases.json` describes deliberate corruptions. Use
`scripts/fixtures/materialize_publisher_fixture.py --output <new-directory>` to
copy the baseline and create directory/ZIP variants, including a repeat deletion
in sequence 0004. The tool refuses an existing destination and never alters the
frozen source. Result manifests name their expected defects; later P2 rules must
detect them independently. This tool only copies fixtures and injects defects.

The regression checks compare all hashes, parse every backbone against the
pinned DTD, verify the hand-authored node/leaf context and XML-addressed targets,
and check that every expected document is represented. Repeated IDs in separate
backbones are intentional (`overview`); duplicate IDs in one XML are a defect.
