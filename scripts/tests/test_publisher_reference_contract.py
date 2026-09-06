"""Check the reviewed publisher scope against source bytes, without the writer."""

from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCOPE = ROOT / "reference/publisher/standards-scope.json"


def canonical_json(value: object) -> str:
    """Independent digest oracle for the deliberately restricted shared wire format."""
    if value is None or isinstance(value, bool):
        return json.dumps(value)
    if isinstance(value, int):
        if abs(value) > 9007199254740991:
            raise ValueError("Integer exceeds the shared safe range")
        return str(value)
    if isinstance(value, str):
        value.encode("utf-8", errors="strict")  # Reject unpaired surrogates.
        return json.dumps(value, ensure_ascii=False)
    if isinstance(value, list):
        return "[" + ",".join(canonical_json(item) for item in value) + "]"
    if isinstance(value, dict):
        keys = sorted(value, key=lambda key: key.encode("utf-16-be", errors="strict"))
        return "{" + ",".join(canonical_json(key) + ":" + canonical_json(value[key]) for key in keys) + "}"
    raise ValueError("Only JSON literals, strings, safe integers, arrays and objects are supported")


def unique_object(pairs: list[tuple[str, object]]) -> dict:
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("Duplicate object member")
        result[key] = value
    return result


def verify_contract_examples() -> None:
    contract = json.loads((ROOT / "reference/publisher/contracts-v1.json").read_text(encoding="utf-8"))
    examples = json.loads((ROOT / "reference/publisher/contract-examples.json").read_text(encoding="utf-8"))
    for vector in examples["canonicalVectors"]:
        encoded = canonical_json(vector["input"])
        assert encoded == vector["canonical"], vector["id"]
        assert hashlib.sha256(encoded.encode("utf-8")).hexdigest() == vector["sha256"], vector["id"]
    for vector in examples["invalidCanonicalInputs"]:
        try:
            canonical_json(json.loads(vector["json"], object_pairs_hook=unique_object))
        except (ValueError, UnicodeError):
            pass
        else:
            raise AssertionError(f"Invalid canonical input accepted: {vector['id']}")

    fixture = ROOT / "tests/RATools.Tests/Fixtures/Publisher/sequences"
    manifests = examples["packageInputs"]
    for manifest in manifests:
        content = manifest["content"]
        assert hashlib.sha256(canonical_json(content).encode("utf-8")).hexdigest() == manifest["digest"]
        actual_paths = sorted(path.relative_to(fixture).as_posix()
                              for path in (fixture / content["sequenceNumber"]).rglob("*") if path.is_file())
        assert [entry["logicalPath"] for entry in content["files"]] == actual_paths
        for entry in content["files"]:
            data = (fixture / entry["logicalPath"]).read_bytes()
            assert entry["length"] == len(data)
            assert entry["sha256"] == hashlib.sha256(data).hexdigest()
            assert entry["md5"] == hashlib.md5(data).hexdigest()
    baseline = examples["historyBaseline"]
    assert baseline["content"]["entries"][0]["contentDigest"] == manifests[0]["digest"]
    assert hashlib.sha256(canonical_json(baseline["content"]).encode("utf-8")).hexdigest() == baseline["digest"]
    for transition in contract["transitions"]:
        states = contract["states"][transition["entity"]]
        assert transition["from"] in states and transition["to"] in states

    from urllib.parse import urljoin
    for example in examples["leafReferences"]:
        resolved = urljoin("https://fixture.invalid/" + example["source"], example["modifiedFile"])
        assert resolved == "https://fixture.invalid/" + example["target"]


def verify_scope() -> None:
    scope = json.loads(SCOPE.read_text(encoding="utf-8"))
    sources = {source["id"]: source for source in scope["sources"]}
    for asset in scope["assets"]:
        data = (ROOT / asset["path"]).read_bytes()
        assert hashlib.sha256(data).hexdigest() == asset["sha256"], asset["path"]
        assert asset["sourceId"] in sources
        if official_copy := asset.get("byteIdenticalOfficialCopy"):
            assert data == (ROOT / official_copy).read_bytes(), asset["path"]

    dtd = (ROOT / "reference/dtd/ich-ectd-3-2.dtd").read_text(encoding="utf-8")
    # The source has historical declarations inside comments; they are not rules.
    dtd = re.sub(r"<!--.*?-->", "", dtd, flags=re.S)
    declarations = dict(re.findall(r"<!ELEMENT\s+(\S+)\s+(.*?)>", dtd, re.S))
    attributes = {}
    for element, body in re.findall(r"<!ATTLIST\s+(\S+)\s+(.*?)>", dtd, re.S):
        if not element.startswith(("m2-", "m3-", "m4-", "m5-")):
            continue
        business = re.findall(r"([\w-]+)\s+CDATA\s+#(REQUIRED|IMPLIED)", body)
        if business:
            attributes[element] = {
                "required": sorted(name for name, mode in business if mode == "REQUIRED"),
                "optional": sorted(name for name, mode in business if mode == "IMPLIED"),
            }

    inventory = scope["businessAttributes"]
    assert len({item["element"] for item in inventory}) == len(inventory)
    assert attributes == {
        item["element"]: {
            "required": sorted(item["required"]), "optional": sorted(item["optional"])
        }
        for item in inventory
    }, "The reviewed business-attribute inventory differs from the official DTD"
    repeated = {
        child: parent
        for parent, body in declarations.items()
        for child in re.findall(r"\b(m[2345][\w-]*)[*+]", body)
    }
    assert repeated == {
        item["element"]: item["parent"] for item in inventory if item["repeatable"]
    }, "The reviewed repeatability inventory differs from the official DTD"
    assert declarations["node-extension"] == "(title, (leaf | node-extension)+)"

    # Unavailable official evidence must stay visible; hash pinning is not acceptance.
    for source in sources.values():
        assert source["retrievalStatus"] in {"Verified", "Unverified"}
        if source["retrievalStatus"] == "Unverified":
            assert source["reason"]
            assert source["effectiveOn"] is None
    assert scope["qualificationScope"]["regulatoryReadiness"] == "NotEvaluated"


if __name__ == "__main__":
    verify_scope()
    verify_contract_examples()
    print("Publisher source and scope contract passed")
