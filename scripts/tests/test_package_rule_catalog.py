"""Independent provenance, fixture and canonical digest checks for P2 rules."""

from __future__ import annotations

import hashlib
import json
import re
import xml.etree.ElementTree as ET
from pathlib import Path

from test_publisher_reference_contract import ROOT, canonical_json, unique_object


def verify_package_rule_catalog() -> None:
    catalog_path = ROOT / "reference/publisher/package-rules-v1.json"
    catalog = json.loads(catalog_path.read_text(encoding="utf-8"), object_pairs_hook=unique_object)
    digest = hashlib.sha256(canonical_json(catalog).encode("utf-8")).hexdigest()
    assert digest == catalog_path.with_suffix(".sha256").read_text(encoding="ascii").strip()
    assert catalog["schemaVersion"] == 1 and catalog["catalogVersion"] == "package-rules-v1"
    scope = json.loads((ROOT / "reference/publisher/standards-scope.json").read_text(encoding="utf-8"))
    originals = {source["id"]: source for source in scope["sources"]}
    sources = {source["id"]: source for source in catalog["sources"]}
    assert len(sources) == len(catalog["sources"])
    for source in sources.values():
        assert source["url"].startswith("https://") and source["version"]
        if source["id"] == "fda-validation-criteria-4.7":
            inventory = json.loads((ROOT / "reference/publisher/fda/4.7/criteria-inventory.json").read_text(encoding="utf-8"))
            snapshot = json.loads((ROOT / "reference/publisher/official-sources/2026-11-09/source-snapshot.json").read_text(encoding="utf-8"))
            original = next(item for item in snapshot["sources"] if item["id"] == "fda-criteria")
            assert source["kind"] == "RegulatorySpecification" and source["verificationStatus"] == "Verified"
            assert source["version"] == inventory["version"] == "4.7"
            assert source["url"] == original["requestedUrl"]
            assert source["sha256"] == inventory["source"]["sha256"] == original["sha256"]
            assert hashlib.sha256((ROOT / original["repositoryPath"]).read_bytes()).hexdigest() == source["sha256"]
        elif source["kind"] == "RegulatorySpecification":
            original = originals[source["id"]]
            for field in ("url", "version", "sha256"):
                assert source[field] == original[field], (source["id"], field)
            assert source["verificationStatus"] == original["retrievalStatus"]
        else:
            project_sources = {"ratools-publisher-contract-v1": "reference/publisher/contracts-v1.json",
                               "ratools-package-xml-assets-v1": "reference/publisher/package-xml-assets-v1.json",
                               "ratools-fda-m1-attributes-v1": "reference/publisher/fda/4.7/m1-attribute-policy.json",
                               "ratools-fda-m1-conditional-v1": "reference/publisher/fda/4.7/m1-conditional-policy.json"}
            assert source["sha256"] == hashlib.sha256((ROOT / project_sources[source["id"]]).read_bytes()).hexdigest()

    xml_assets = json.loads((ROOT / "reference/publisher/package-xml-assets-v1.json").read_text(encoding="utf-8"))
    for asset in xml_assets["assets"]:
        assert hashlib.sha256((ROOT / asset["path"]).read_bytes()).hexdigest() == asset["sha256"], asset["id"]
    assert hashlib.sha256((ROOT / xml_assets["nodeSchemaPath"]).read_bytes()).hexdigest() == xml_assets["nodeSchemaSha256"]

    profiles = {profile["profileSnapshotId"]: profile for profile in catalog["profiles"]}
    assert len(profiles) == len(catalog["profiles"]) == 3
    # The current catalog intentionally declares development scope only.
    assert all(not profile["qualifiedForFinalization"] and profile["qualificationReason"] for profile in profiles.values())
    rules = {rule["internalRuleId"]: rule for rule in catalog["rules"]}
    assert len(rules) == len(catalog["rules"])
    cases_path = ROOT / "tests/RATools.Tests/Fixtures/Publisher/invalid-cases.json"
    case_ids = {case["id"] for case in json.loads(cases_path.read_text(encoding="utf-8"))["cases"]}
    for rule in rules.values():
        assert rule["sourceId"] in sources
        assert rule["sourceSection"] and rule["ruleVersion"] and rule["applicableScope"]
        assert rule["profileSnapshotIds"] and set(rule["profileSnapshotIds"]) <= profiles.keys()
        assert rule["implementationNote"]
        if rule["implementationStatus"] == "Implemented":
            assert rule["positiveFixtures"] and rule["negativeFixtures"]
        for field in ("positiveFixtures", "negativeFixtures", "componentEvidence", "externalComparisonEvidence"):
            assert len(rule[field]) == len(set(rule[field]))
            for reference in rule[field]:
                path, _, fragment = reference.partition("#")
                resolved = (ROOT / path).resolve()
                assert resolved.is_relative_to(ROOT) and resolved.exists(), reference
                if fragment:
                    assert resolved == cases_path and fragment in case_ids, reference
    for profile in profiles:
        assert any(profile in rule["profileSnapshotIds"] for rule in rules.values())
    policy = json.loads((ROOT / "reference/publisher/fda/4.7/m1-attribute-policy.json").read_text(encoding="utf-8"))
    assert policy["schemaVersion"] == 1 and policy["algorithm"] == "fda-m1-attributes-v1"
    assert policy["classification"] == "ProjectDerivedEvidence"
    originals = {item["id"]: item for item in json.loads((ROOT / "reference/publisher/official-sources/2026-11-09/source-snapshot.json").read_text(encoding="utf-8"))["sources"]}
    dtd_path = ROOT / "reference/dtd/us-regional-v3-3.dtd"
    dtd = dtd_path.read_text(encoding="utf-8")
    declarations = {}
    for element, block in re.findall(r"<!ATTLIST\s+(\S+)\s+([^>]+)>", dtd):
        for name, kind, presence, fixed in re.findall(r'(?m)^\s*(\S+)\s+(CDATA|ID|\([^\r\n)]+\))\s+(#REQUIRED|#IMPLIED|#FIXED\s+"([^"]+)")', block):
            declarations[element, name] = (kind, presence, fixed)
    required = {(element, name) for (element, name), (_, presence, _) in declarations.items() if presence == "#REQUIRED"}
    assert len(required) == 16
    assert required == {(item["element"], item["attribute"]) for item in policy["requiredAttributes"]}
    allowed = {}
    for pair, (kind, presence, fixed) in declarations.items():
        if kind.startswith("("):
            allowed[pair] = [value.strip() for value in kind[1:-1].split("|")]
        elif presence.startswith("#FIXED"):
            allowed[pair] = [fixed]
    assert len(allowed) == 11
    assert allowed == {(item["element"], item["attribute"]): item["values"] for item in policy["allowedAttributes"]}
    assert len(policy["dictionaries"]) == 10 and len(policy["combinations"]) == 11
    for source in policy["sources"] + policy["dictionaries"]:
        if source["key"] == "xml-us-dtd":
            source_path = dtd_path
        else:
            original = originals[source["key"].removeprefix("official-")]
            assert source["sha256"] == original["sha256"]
            source_path = ROOT / original["repositoryPath"]
        assert hashlib.sha256(source_path.read_bytes()).hexdigest() == source["sha256"]
        if "attribute" in source:
            assert set(source["elements"]) == {element for element, name in declarations if name == source["attribute"]}
            root = ET.parse(source_path).getroot()
            assert root.tag == source["root"] and root.attrib["version-number"] == source["version"]
    assert rules["US-CRITERIA-INVENTORY"]["requiredForReadiness"]
    assert rules["EU-CRITERIA-INVENTORY"]["requiredForReadiness"]
    assert rules["FILE-NAMING"]["authorityRuleId"] is None
    print(f"Package rule catalog passed: {len(rules)} rules, canonical SHA-256 {digest}")


if __name__ == "__main__":
    verify_package_rule_catalog()
