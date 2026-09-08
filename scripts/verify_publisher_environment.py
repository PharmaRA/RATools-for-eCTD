"""Run publisher build/path/database gates and retain an honest environment receipt."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
PROJECT = "tests/RATools.Tests/RATools.Tests.csproj"
MIGRATOR = "src/RATools.DatabaseMigrator/RATools.DatabaseMigrator.csproj"
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def test_counts(path: Path) -> dict[str, int]:
    results = ET.parse(path).findall(".//t:UnitTestResult", NS)
    counts = {outcome: sum(item.get("outcome") == outcome for item in results)
              for outcome in ("Passed", "Failed", "NotExecuted")}
    counts["Other"] = len(results) - sum(counts.values())
    return counts


def verify(output: Path, require_postgres: bool) -> int:
    output = output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    connection = os.environ.get("RATOOLS_TEST_POSTGRES", "").strip()
    environment = os.environ.copy()
    environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US"
    if connection:
        environment["ConnectionStrings__PostgreSql"] = connection
    receipt = {
        "schemaVersion": 1, "startedUtc": datetime.now(timezone.utc).isoformat(),
        "platform": platform.system(), "platformVersion": platform.release(),
        "pythonVersion": platform.python_version(), "postgresConfigured": bool(connection),
        "scope": ["ReleaseBuild", "PathSecurity", "PostgreSqlMigrationsAndConstraints"],
        "otherPlatforms": "NotEvaluated by this run; retain separate receipts for Windows and Linux",
        "steps": [],
    }
    dotnet = shutil.which("dotnet")
    if dotnet is None:
        receipt["steps"].append({"name": "toolchain", "status": "Fail", "reason": "dotnet is unavailable"})

    def run(name: str, arguments: list[str], trx_name: str | None = None) -> bool:
        completed = subprocess.run([dotnet, *arguments], cwd=ROOT, env=environment,
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        text = completed.stdout.decode("utf-8", errors="replace")
        if connection:
            text = text.replace(connection, "<redacted connection string>")
        text = re.sub(r"(?i)(password|pwd)\s*=\s*[^;\r\n]+", r"\1=<redacted>", text)
        log = output / f"{name}.log"
        log.write_text(text, encoding="utf-8")
        step = {"name": name, "arguments": arguments, "exitCode": completed.returncode,
                "status": "Pass" if completed.returncode == 0 else "Fail",
                "log": log.name, "logSha256": hashlib.sha256(log.read_bytes()).hexdigest()}
        if trx_name and (output / trx_name).is_file():
            step["tests"] = test_counts(output / trx_name)
            if step["tests"]["Passed"] == 0 or any(step["tests"][key] for key in ("Failed", "NotExecuted", "Other")):
                step["status"] = "Fail"
        elif trx_name:
            step["status"] = "Fail"
            step["reason"] = "No TRX evidence was produced"
        receipt["steps"].append(step)
        print(f"{name}: {step['status']} ({log})", flush=True)
        return step["status"] == "Pass"

    try:
        built = bool(dotnet) and run("build", ["build", PROJECT, "--configuration", "Release", "--no-restore"])
        if built:
            run("path-security", ["test", PROJECT, "--configuration", "Release", "--no-build", "--filter",
                                  "Category=PathSecurity", "--logger", "trx;LogFileName=path-security.trx",
                                  "--results-directory", str(output)], "path-security.trx")
        if not connection:
            receipt["steps"].append({"name": "postgres", "status": "NotEvaluated",
                                     "reason": "RATOOLS_TEST_POSTGRES is not configured; migrations and PostgreSQL constraints were not exercised"})
        elif built:
            migrated = run("migrate-first", ["run", "--project", MIGRATOR, "--configuration", "Release", "--no-build"])
            if migrated:
                migrated = run("migrate-repeat", ["run", "--project", MIGRATOR, "--configuration", "Release", "--no-build"])
            if migrated:
                run("postgres", ["test", PROJECT, "--configuration", "Release", "--no-build", "--filter",
                                 "FullyQualifiedName~RATools.Tests.Persistence.Postgres", "--logger",
                                 "trx;LogFileName=postgres.trx", "--results-directory", str(output)], "postgres.trx")
    except (OSError, ET.ParseError) as error:
        receipt["steps"].append({"name": "execution", "status": "Fail", "reason": type(error).__name__})
    finally:
        statuses = {step["status"] for step in receipt["steps"]}
        receipt["status"] = "Fail" if "Fail" in statuses else "NotEvaluated" if "NotEvaluated" in statuses else "Pass"
        receipt["finishedUtc"] = datetime.now(timezone.utc).isoformat()
        (output / "environment-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
        print(f"Environment scope: {receipt['status']}", flush=True)
    return 1 if receipt["status"] == "Fail" else 2 if require_postgres and not connection else 0


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True, help="New directory for logs, TRX files and the receipt")
    parser.add_argument("--require-postgres", action="store_true", help="Return a failing exit code when PostgreSQL is unavailable")
    options = parser.parse_args()
    sys.exit(verify(options.output, options.require_postgres))
