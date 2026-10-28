"""Record what a CI run actually executed as a machine-readable environment receipt.

This is evidence capture, not a qualification decision: the receipt says which platform
ran which tests with which toolchain, and it refuses to record a green result from a TRX
that contains failures or skips. A configured workflow alone is never a passing receipt;
the receipt is only written from an actual completed run.
"""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def test_counts(path: Path) -> dict[str, int]:
    results = ET.parse(path).findall(".//t:UnitTestResult", NS)
    counts = {
        outcome: sum(item.get("outcome") == outcome for item in results)
        for outcome in ("Passed", "Failed", "NotExecuted")
    }
    counts["Other"] = len(results) - sum(counts.values())
    counts["Total"] = len(results)
    return counts


def record(trx_paths: list[Path], output: Path, platform: str, dotnet_version: str,
           commit: str | None, run_id: str | None) -> int:
    """Write the receipt and return 0 only when the run it describes is complete."""
    if not trx_paths:
        raise SystemExit("At least one TRX file is required; a receipt cannot be invented")
    files: list[dict[str, object]] = []
    totals = {"Passed": 0, "Failed": 0, "NotExecuted": 0, "Other": 0, "Total": 0}
    for path in trx_paths:
        counts = test_counts(path)
        for key, value in counts.items():
            totals[key] += value
        files.append({
            "name": path.name,
            "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            "tests": counts,
        })
    # A receipt is only evidence of a complete run when every test that ran passed.
    # The counts are always recorded; the exit code carries the verdict so a caller
    # cannot read success into an incomplete run.
    complete = bool(totals["Passed"]) and not (totals["Failed"] or totals["NotExecuted"] or totals["Other"])
    receipt_status = "Pass" if complete else "Incomplete"

    receipt = {
        "schemaVersion": 1,
        "platform": platform,
        "dotnetVersion": dotnet_version,
        "commit": commit,
        "runId": run_id,
        "recordedUtc": datetime.now(timezone.utc).isoformat(),
        "status": receipt_status,
        "scope": ["build", "path-security", "postgresql", "backend-tests"],
        "tests": totals,
        "testFiles": files,
        # Explicitly not a qualification statement: this run covers one platform and one
        # toolchain. Regional, external-validator and other-platform gates stay separate.
        "qualificationBoundary": "Evidence for this run only; external-validator, regional and other-platform qualification remain NotEvaluated",
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(f"Receipt: {output} ({totals['Passed']} passed, {totals['Failed']} failed, "
          f"{totals['NotExecuted']} skipped, status={receipt_status})")
    if not complete:
        print("The run is incomplete; the receipt records the counts but must not be read as passing",
              file=sys.stderr)
        return 1
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--trx", type=Path, action="append", default=[],
                        help="TRX file produced by this run; repeat for multiple files")
    parser.add_argument("--output", type=Path, required=True, help="Receipt JSON path")
    parser.add_argument("--platform", required=True, help="Platform that actually ran the tests")
    parser.add_argument("--dotnet-version", required=True, help="SDK version that ran the build")
    parser.add_argument("--commit", default=None)
    parser.add_argument("--run-id", default=None)
    options = parser.parse_args()

    missing = [str(path) for path in options.trx if not path.is_file()]
    if missing:
        raise SystemExit(f"TRX evidence is missing: {', '.join(missing)}")
    return record(options.trx, options.output, options.platform, options.dotnet_version,
                  options.commit, options.run_id)


if __name__ == "__main__":
    sys.exit(main())
