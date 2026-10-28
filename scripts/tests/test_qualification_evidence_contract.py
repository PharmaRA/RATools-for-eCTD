"""Guard the remaining qualification-evidence debt so it cannot silently disappear.

Two facts must stay true until real external evidence exists:
1. The database refuses to rewrite committed finalization evidence.
2. CI retains a machine-readable receipt of what actually ran, and the checked-in
   evidence keeps Linux and external-validator qualification explicitly NotEvaluated.
"""

from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile

from test_publisher_reference_contract import ROOT


TRX_TEMPLATE = """<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
    {results}
  </Results>
</TestRun>
"""


def load_recorder():
    path = ROOT / "scripts/record_environment_receipt.py"
    spec = importlib.util.spec_from_file_location("record_environment_receipt", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def write_trx(directory: Path, name: str, outcomes: list[str]) -> Path:
    results = "\n".join(
        f'<UnitTestResult testName="test{i}" outcome="{outcome}" />'
        for i, outcome in enumerate(outcomes)
    )
    path = directory / name
    path.write_text(TRX_TEMPLATE.format(results=results), encoding="utf-8")
    return path


def verify_append_only_guard() -> None:
    migrations = sorted((ROOT / "src/RATools.Infrastructure/Persistence/EfCore/Migrations").glob(
        "*_GuardFinalizedEvidenceAppendOnly.cs"))
    guard = migrations[-1].read_text(encoding="utf-8")
    for table in ("finalized_sequences", "finalized_delivery_records", "finalized_delivery_receipts"):
        assert f"ON {table}" in guard, table
    # Row-level triggers also catch upserts and foreign-key cascades; TRUNCATE only
    # supports statement-level triggers, so it is guarded separately.
    assert "BEFORE UPDATE OR DELETE ON" in guard
    assert "FOR EACH ROW EXECUTE FUNCTION ratools_guard_finalized_evidence()" in guard
    assert "BEFORE TRUNCATE ON" in guard
    assert "FOR EACH STATEMENT EXECUTE FUNCTION ratools_guard_finalized_evidence()" in guard
    # The guard is re-runnable after an interrupted upgrade and reports a stable SQLSTATE.
    assert "CREATE OR REPLACE FUNCTION" in guard
    assert "ERRCODE = '55000'" in guard
    # Removing the guard destroys no rows.
    assert "DROP TABLE" not in guard


def verify_receipt_recorder() -> None:
    recorder = load_recorder()
    with tempfile.TemporaryDirectory() as raw:
        directory = Path(raw)
        complete = write_trx(directory, "complete.trx", ["Passed", "Passed"])
        output = directory / "complete.json"
        assert recorder.record([complete], output, "linux", "8.0.423", "abc", "1") == 0
        receipt = json.loads(output.read_text(encoding="utf-8"))
        assert receipt["tests"]["Passed"] == 2 and receipt["tests"]["Failed"] == 0
        assert receipt["status"] == "Pass"
        assert receipt["testFiles"][0]["name"] == "complete.trx"
        assert receipt["commit"] == "abc" and receipt["runId"] == "1"
        assert "NotEvaluated" in receipt["qualificationBoundary"]

        # A skipped test is not a passing qualification run; record() itself must say so,
        # so an importing caller cannot read success into the returned code.
        incomplete = write_trx(directory, "incomplete.trx", ["Passed", "NotExecuted"])
        incomplete_output = directory / "incomplete.json"
        assert recorder.record([incomplete], incomplete_output, "linux", "8.0.423", None, None) == 1
        assert json.loads(incomplete_output.read_text(encoding="utf-8"))["status"] == "Incomplete"
        failed = write_trx(directory, "failed.trx", ["Passed", "Failed"])
        assert recorder.record([failed], directory / "failed.json", "linux", "8.0.423", None, None) == 1
        assert recorder.record([write_trx(directory, "empty.trx", [])], directory / "empty.json",
                               "linux", "8.0.423", None, None) == 1


def verify_ci_wiring() -> None:
    ci = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
    assert "scripts/record_environment_receipt.py" in ci
    assert "--trx TestResults/backend.trx" in ci and "--trx TestResults/regional.trx" in ci
    assert '--platform linux' in ci
    # The regional regression must produce the TRX the receipt consumes.
    assert 'LogFileName=regional.trx' in ci


def verify_evidence_stays_honest() -> None:
    evidence = json.loads((ROOT / "reference/publisher/environment-evidence.json").read_text(encoding="utf-8"))
    # A workflow or a local run does not become Linux qualification evidence by itself.
    assert evidence["linux"]["status"] == "NotEvaluated"
    assert evidence["linux"]["reason"]
    decisions = json.loads((ROOT / "reference/publisher/decisions.json").read_text(encoding="utf-8"))
    validator = next(item for item in decisions["candidates"] if item["id"] == "lorenz-evalidator")
    assert validator["licenseStatus"] == "Unverified"
    assert validator["qualificationStatus"] == "NotEvaluated"
    assert "P2 external comparison" in decisions["pendingQualification"]


def verify_frontend_timeout_is_explicit() -> None:
    config = (ROOT / "frontend" / "vite.config.ts").read_text(encoding="utf-8")
    # Page-level tests wait for lazy chunks; the default 5 s flakes under worker
    # contention. The timeout is raised explicitly rather than masking failures with
    # retries, and retries stay disabled.
    assert "testTimeout:" in config
    assert "retry:" not in config and "retries" not in config


if __name__ == "__main__":
    verify_append_only_guard()
    verify_receipt_recorder()
    verify_ci_wiring()
    verify_evidence_stays_honest()
    verify_frontend_timeout_is_explicit()
    print("Qualification evidence contract passed")
