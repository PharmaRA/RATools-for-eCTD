"""Independent libxml DTD checks for the package inspector's source fixtures."""

from pathlib import Path

from lxml import etree


ROOT = Path(__file__).resolve().parents[2]


def parse(data: bytes):
    return etree.fromstring(data, etree.XMLParser(load_dtd=False, resolve_entities=False, no_network=True))


def verify_package_xml_contract() -> None:
    ich = etree.DTD(str(ROOT / "reference/dtd/ich-ectd-3-2.dtd"))
    us = etree.DTD(str(ROOT / "reference/dtd/us-regional-v3-3.dtd"))
    eu = etree.DTD(str(ROOT / "reference/eu-m1/3.1.1/util/dtd/eu-regional.dtd"))
    fixture = ROOT / "tests/RATools.Tests/Fixtures/Publisher/sequences"
    for sequence in ("0000", "0001", "0002", "0003"):
        assert ich.validate(parse((fixture / sequence / "index.xml").read_bytes())), ich.error_log
    assert us.validate(parse((fixture / "0000/m1/us/us-regional.xml").read_bytes())), us.error_log
    assert eu.validate(parse((ROOT / "tests/RATools.Tests/Fixtures/PackageValidation/eu-regional.xml").read_bytes())), eu.error_log

    original = (fixture / "0000/index.xml").read_bytes()
    mutations = {
        "missing-manufacturer": original.replace(b'<m3-2-s-drug-substance substance="Drug A" manufacturer="Alpha">',
                                                  b'<m3-2-s-drug-substance substance="Drug A">'),
        "duplicate-xml-id": original.replace(b'ID="qos-beta"', b'ID="qos-alpha"'),
        "wrong-dtd-version": original.replace(b'dtd-version="3.2"', b'dtd-version="3.1"'),
    }
    for name, changed in mutations.items():
        assert changed != original, name
        assert not ich.validate(parse(changed)), name
        assert all(error.line > 0 for error in ich.error_log), name
    assert ich.validate(parse(original.replace(b' ID="study-nc-001"', b''))), ich.error_log
    assert ich.validate(parse(original.replace(b' dtd-version="3.2"', b''))), ich.error_log
    print(f"Independent package XML contract passed: 6 positive backbones, 3 invalid variants, 2 optional/default cases; libxml {etree.LIBXML_VERSION}")


if __name__ == "__main__":
    verify_package_xml_contract()
