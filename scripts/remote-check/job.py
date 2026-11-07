"""A single foreground worker inside an Instabox background job."""
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import traceback

root = Path.cwd()
results = root / "results"
results.mkdir()
config = json.loads((root / "job-config.json").read_text(encoding="utf-8-sig"))
summary = {"task": config["task"], "sourceHead": config["sourceHead"], "steps": [],
           "sourceSha256": sys.argv[1],
           "category": "toolchain-unavailable", "exitCode": 1,
           "platform": platform.platform(), "scope": "frontend only"}
os.environ["PYTHONDONTWRITEBYTECODE"] = "1"
os.environ["CI"] = "true"


def run(name, command, cwd, category):
    summary["category"] = category
    print(f"STEP {name}: {' '.join(command)}", flush=True)
    with (results / (name + ".log")).open("w") as log:
        process = subprocess.run(command, cwd=cwd, stdout=log, stderr=subprocess.STDOUT, check=False)
    summary["steps"].append({"name": name, "command": command, "exitCode": process.returncode,
                             "log": name + ".log"})
    if process.returncode:
        summary["exitCode"] = process.returncode if process.returncode > 0 else 1
        raise RuntimeError(f"{name} failed, exit {process.returncode}; see {name}.log")


try:
    node = subprocess.check_output(["node", "--version"], text=True).strip()
    summary["node"] = node
    version = tuple(int(part) for part in node.lstrip("v").split("."))
    requirement = json.loads((root / "frontend/package.json").read_text())["engines"]["node"]
    minimum = re.fullmatch(r">=(\d+)\.(\d+)\.(\d+)", requirement)
    if not minimum or version < tuple(map(int, minimum.groups())):
        raise RuntimeError("Remote Node does not satisfy the supported engine rule: " + requirement)
    summary["nodeRequirement"] = requirement
    status = Path("/proc/self/status").read_text()
    if "Seccomp:\t2" not in status:
        raise RuntimeError("Required inherited no-delete seccomp filter is absent")
    summary["noDeleteSeccomp"] = True
    run("dependencies", [sys.executable, "scripts/remote-check/install-lock.py"], root, "dependency-install-failed")
    frontend = root / "frontend"
    node_command = ["node", "--import", "../scripts/remote-check/no-delete.mjs"]
    if config.get("lintFiles"):
        run("lint", node_command + ["node_modules/eslint/bin/eslint.js", *config["lintFiles"]], frontend, "lint-failed")
    if config["task"] == "test":
        run("tests", node_command + ["../scripts/remote-check/test.mjs"], frontend, "test-failed")
        report = json.loads((results / "tests.json").read_text())
        expected = {str(frontend / name) for name in config["testFiles"]}
        suites = report["testResults"]
        if {suite["name"] for suite in suites} != expected or len(suites) != len(expected):
            raise RuntimeError("Executed test files differ from the requested selection")
        counts = {"src/ErrorBoundary.test.tsx": 1, "src/pages/SequenceWorkspacePage.validation.test.tsx": 24}
        for suite in suites:
            assertions = suite["assertionResults"]
            relative = str(Path(suite["name"]).relative_to(frontend))
            if not assertions or any(item["status"] != "passed" for item in assertions):
                raise RuntimeError("Requested tests include failed, skipped or unexecuted cases")
            if relative in counts and len(assertions) != counts[relative]:
                raise RuntimeError("Default QA07 regression case count changed: " + relative)
        if report["numFailedTests"] or report["numPendingTests"] or report["numPassedTests"] != sum(len(s["assertionResults"]) for s in suites):
            raise RuntimeError("Test totals do not establish a complete selected run")
        summary["tests"] = {"passed": report["numPassedTests"], "failed": 0, "skipped": 0, "files": config["testFiles"]}
    else:
        run("types-app", node_command + ["node_modules/typescript/bin/tsc", "--noEmit", "-p", "tsconfig.app.json"], frontend, "build-failed")
        run("types-config", node_command + ["node_modules/typescript/bin/tsc", "--noEmit", "-p", "tsconfig.node.json"], frontend, "build-failed")
        run("vite", node_command + ["node_modules/vite/bin/vite.js", "build", "--configLoader", "runner", "--emptyOutDir", "false"], frontend, "build-failed")
        run("budget", node_command + ["scripts/check-bundle-budget.mjs"], frontend, "build-failed")
        if config.get("browserProbe"):
            run("browser", node_command + ["../scripts/remote-check/browser-finalization.mjs"], frontend, "browser-test-failed")
    summary["category"] = "passed"
    summary["exitCode"] = 0
except Exception as error:
    summary["error"] = str(error)
    (results / "worker-error.log").write_text(traceback.format_exc())
finally:
    (results / "summary.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps(summary), flush=True)
sys.exit(summary["exitCode"])
