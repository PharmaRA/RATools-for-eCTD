"""Independent fixture-only lifecycle oracle; not a regulator validation tool."""

import json
from urllib.parse import urljoin, urlsplit, unquote

from test_package_xml_contract import ROOT, parse


def verify_package_lifecycle_contract() -> None:
    fixture = ROOT / "tests/RATools.Tests/Fixtures/Publisher"
    expected = json.loads((fixture / "expected.json").read_text(encoding="utf-8"))
    current = set()
    known = {}
    events = []
    for sequence in ("0000", "0001", "0002", "0003"):
        changes = []
        for relative in ("index.xml", "m1/us/us-regional.xml"):
            path = sequence + "/" + relative
            document = parse((fixture / "sequences" / path).read_bytes())
            for leaf in document.iter("leaf"):
                address = path + "#" + leaf.get("ID")
                assert address not in known
                known[address] = leaf
                operation = leaf.get("operation")
                href = leaf.get("{http://www.w3c.org/1999/xlink}href")
                regional_reference = leaf.getparent().tag == "m1-administrative-information-and-prescribing-information"
                if regional_reference:
                    assert operation == "new"
                    continue
                if operation == "new":
                    assert leaf.get("modified-file", "") == ""
                else:
                    uri = urlsplit(urljoin("https://fixture.invalid/" + path, leaf.get("modified-file")))
                    target = unquote(uri.path[1:]) + "#" + unquote(uri.fragment)
                    assert target in current and target in known
                    events.append({"leaf": address, "operation": operation, "target": target,
                                   "targetRemainsEffective": operation == "append"})
                    if operation in ("replace", "delete"):
                        changes.append(target)
                if operation == "delete":
                    assert not href and leaf.get("checksum") == ""
                else:
                    assert href
                    content = urlsplit(urljoin("https://fixture.invalid/" + path, href))
                    assert (fixture / "sequences" / unquote(content.path[1:])).is_file()
                    current.add(address)
        current.difference_update(changes)
        assert current == set(expected["effectivePayloadLeaves"][sequence]), sequence
    assert events == expected["events"]
    print("Independent lifecycle fixture oracle passed: 4 sequence states, 3 exact events, delete checksum and historical content")


if __name__ == "__main__":
    verify_package_lifecycle_contract()
