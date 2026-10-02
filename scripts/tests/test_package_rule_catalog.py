"""Independent provenance, fixture and canonical digest checks for P2 rules."""

from __future__ import annotations

import hashlib
import json
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
        if source["kind"] == "RegulatorySpecification":
            original = originals[source["id"]]
            for field in ("url", "version", "sha256"):
                assert source[field] == original[field], (source["id"], field)
            assert source["verificationStatus"] == original["retrievalStatus"]
        else:
            assert source["id"] == "ratools-publisher-contract-v1"
            assert source["sha256"] == hashlib.sha256((ROOT / "reference/publisher/contracts-v1.json").read_bytes()).hexdigest()

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
    assert rules["US-CRITERIA-INVENTORY"]["requiredForReadiness"]
    assert rules["EU-CRITERIA-INVENTORY"]["requiredForReadiness"]
    assert rules["FILE-NAMING"]["authorityRuleId"] is None
    print(f"Package rule catalog passed: {len(rules)} rules, canonical SHA-256 {digest}")


if __name__ == "__main__":
    verify_package_rule_catalog()
