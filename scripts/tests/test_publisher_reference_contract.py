"""Check the reviewed publisher scope against source bytes, without the writer."""

from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCOPE = ROOT / "reference/publisher/standards-scope.json"


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
    print("Publisher source and scope contract passed")
