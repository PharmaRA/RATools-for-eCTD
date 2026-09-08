"""Extract the closed ICH node schema from the pinned DTD using libxml2."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import sys

from lxml import etree


ROOT = Path(__file__).resolve().parents[2]
DTD_PATH = ROOT / "reference/dtd/ich-ectd-3-2.dtd"
OUTPUT = ROOT / "reference/publisher/ich-3.2.2-nodes.json"
OCCURRENCES = {"once": (1, 1), "opt": (0, 1), "mult": (0, None), "plus": (1, None)}


def qualified_name(declaration) -> str:
    return f"{declaration.prefix}:{declaration.name}" if declaration.prefix else declaration.name


def section_path(element: str) -> str:
    parts = element.split("-")
    selected = parts[:1]
    for part in parts[1:]:
        if part.isdigit() or (len(part) == 1 and part.isascii() and part.isalpha()):
            selected.append(part)
        else:
            break
    return ".".join(selected)


def sequence_items(content) -> list:
    if content.type == "seq" and content.occur == "once":
        return sequence_items(content.left) + sequence_items(content.right)
    return [content]


def schema() -> dict:
    scope = json.loads((ROOT / "reference/publisher/standards-scope.json").read_text(encoding="utf-8"))
    digest = hashlib.sha256(DTD_PATH.read_bytes()).hexdigest()
    assert digest == next(asset["sha256"] for asset in scope["assets"] if asset["id"] == "ich-dtd-3.2")
    reviewed_attributes = {item["element"]: set(item["required"] + item["optional"])
                           for item in scope["businessAttributes"]}
    dtd = etree.DTD(str(DTD_PATH))
    declarations = {qualified_name(declaration): declaration for declaration in dtd.iterelements()}
    names = [name for name in declarations if name.startswith(("m1-", "m2-", "m3-", "m4-", "m5-"))]
    parents = {}
    cardinalities = {}
    for name in ["ectd:ectd", *names]:
        for item in sequence_items(declarations[name].content):
            if item.type == "element" and item.name in names:
                assert item.name not in parents
                parents[item.name] = None if name == "ectd:ectd" else name
                cardinalities[item.name] = OCCURRENCES[item.occur]

    definitions = []
    for name in [*names, "node-extension"]:
        declaration = declarations[name]
        is_extension = name == "node-extension"
        content = declaration.content
        mixed = content if not is_extension else content.right
        allows_extensions = mixed.type == "or"
        if allows_extensions:
            assert mixed.left.type == mixed.right.type == "element"
            assert {mixed.left.name, mixed.right.name} == {"leaf", "node-extension"}
            assert mixed.occur == ("plus" if is_extension else "mult")
        if is_extension:
            assert content.type == "seq" and content.occur == "once"
            assert content.left.name == "title" and content.left.occur == "once"
        children = []
        if not allows_extensions:
            items = sequence_items(content)
            assert items[0].name == "leaf" and items[0].occur == "mult"
            for item in items[1:]:
                assert item.type == "element" and item.name in names
                minimum, maximum = OCCURRENCES[item.occur]
                children.append({"definitionKey": item.name, "minimum": minimum, "maximum": maximum})
        attributes = []
        business_names = set()
        for attribute in declaration.iterattributes():
            attr_name = qualified_name(attribute)
            is_identity = attr_name not in {"ID", "xml:lang"}
            if is_identity:
                business_names.add(attr_name)
            assert attribute.type in {"id", "cdata", "enumeration"}
            assert attribute.default in {"required", "implied"}
            attributes.append({"name": attr_name, "valueType": {"id": "XmlId", "cdata": "CData", "enumeration": "Enumeration"}[attribute.type],
                               "required": attribute.default == "required", "identity": is_identity,
                               "allowedValues": attribute.values()})
        assert business_names == reviewed_attributes.get(name, set()), name
        definitions.append({
            "definitionKey": name, "elementName": name, "sectionPath": None if is_extension else section_path(name),
            "parentDefinitionKey": None if is_extension else parents[name],
            "repeatable": is_extension or cardinalities[name][1] is None,
            "kind": "Extension" if is_extension else "Standard",
            "allowsLeaves": True, "extensionPolicy": "Allowed" if allows_extensions else "Forbidden",
            "attributes": sorted(attributes, key=lambda attr: attr["name"]), "children": children,
        })
    return {"schemaVersion": 1, "definitionVersion": "ich-3.2.2-nodes-v1", "dtdVersion": "3.2",
            "dtdSha256": digest, "identityComparisonVersion": "exact-xml-values-v1", "definitions": definitions}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check", action="store_true", help="Fail on snapshot drift without writing files")
    options = parser.parse_args()
    snapshot = schema()
    rendered = json.dumps(snapshot, ensure_ascii=True, indent=2) + "\n"
    if options.check:
        if not OUTPUT.exists() or OUTPUT.read_text(encoding="utf-8") != rendered:
            print("ICH node schema differs from the pinned DTD; regenerate and review it", file=sys.stderr)
            return 1
    else:
        OUTPUT.write_text(rendered, encoding="utf-8", newline="\n")
    print(f"ICH node schema {'verified' if options.check else 'generated'}: {len(snapshot['definitions'])} definitions")
    return 0


if __name__ == "__main__":
    sys.exit(main())
