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
           "platform": platform.platform(), "scope": config.get("scope", "frontend")}
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
    status = Path("/proc/self/status").read_text()
    if "Seccomp:\t2" not in status:
        raise RuntimeError("Required inherited no-delete seccomp filter is absent")
    summary["noDeleteSeccomp"] = True
    if config.get("scope") == "linux-resources":
        run("backend-build", [sys.executable, "scripts/remote-check/backend-build.py"], root, "build-failed")
        if config['task'] == 'test':
            delegation = Path('/sys/fs/cgroup/user.slice/user-1000.slice/user@1000.service')
            owned_group = delegation / ('ratools-' + root.name)
            owned_group.mkdir()
            (owned_group / 'cgroup.subtree_control').write_text('+cpu +memory +pids')
            os.environ['RATOOLS_PDF_CGROUP_ROOT'] = str(owned_group)
            for name, probe in [('linux-resources', 'LinuxResourceProbe'), ('process-tree', 'ProcessTreeProbe')]:
                run(name, ['/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423/dotnet', f'backend/{probe}.dll', f'results/{name}'], root, 'test-failed')
            run('manager-fault', [sys.executable, 'scripts/remote-check/manager-launch.test.py'], root, 'test-failed')
        summary['category'] = 'passed'
        summary['exitCode'] = 0
        raise SystemExit(0)
    if config.get("scope") == "postgres-platform":
        try:
            run("postgres-platform", [sys.executable, "scripts/remote-check/postgres-platform.py"], root, "runner-failed")
        finally:
            receipt = results / "postgres-platform.json"
            if receipt.exists():
                record = json.loads(receipt.read_text())
                category = record["category"]
                if category not in {"toolchain-unavailable", "build-failed", "test-failed", "dependency-install-failed",
                                    "test-timeout", "operation-timeout", "cleanup-failed", "passed"}:
                    raise RuntimeError("Unrecognized PostgreSQL phase receipt")
                if (category == "passed") != (record.get("passed") is True):
                    raise RuntimeError("Inconsistent PostgreSQL phase receipt")
                summary["category"] = category
        summary["category"] = "passed"
        summary["exitCode"] = 0
        raise SystemExit(0)
    if config.get("scope") == "linux-platform":
        try:
            run("linux-platform", [sys.executable, "scripts/remote-check/linux-platform.py", config["task"]], root, "runner-failed")
        finally:
            receipt = results / "linux-phase.json"
            if receipt.exists():
                category = json.loads(receipt.read_text())["category"]
                if category not in {"toolchain-unavailable", "dependency-install-failed", "build-failed", "test-failed", "test-timeout"}:
                    raise RuntimeError("Unrecognized Linux phase receipt")
                summary["category"] = category
        summary["category"] = "passed"
        summary["exitCode"] = 0
        raise SystemExit(0)
    node = subprocess.check_output(["node", "--version"], text=True).strip()
    summary["node"] = node
    version = tuple(int(part) for part in node.lstrip("v").split("."))
    requirement = json.loads((root / "frontend/package.json").read_text())["engines"]["node"]
    minimum = re.fullmatch(r">=(\d+)\.(\d+)\.(\d+)", requirement)
    if not minimum or version < tuple(map(int, minimum.groups())):
        raise RuntimeError("Remote Node does not satisfy the supported engine rule: " + requirement)
    summary["nodeRequirement"] = requirement
    if config.get('verifiedFrontend'):
        run('frontend-mismatch', [sys.executable, 'scripts/remote-check/verified-frontend.test.py'], root, 'test-failed')
        run('verified-frontend', [sys.executable, 'scripts/remote-check/verified-frontend.py'], root, 'frontend-evidence-mismatch')
    else:
        run("dependencies", [sys.executable, "scripts/remote-check/install-lock.py"], root, "dependency-install-failed")
    frontend = root / "frontend"
    node_command = ["node", "--import", "../scripts/remote-check/no-delete.mjs"]
    if config.get("scope") in {"backend-browser", "backend-pg-browser"}:
        for case in ["spawn", "fetch"]:
            run("browser-process-" + case, node_command + ["../scripts/remote-check/browser-process.test.mjs", case], frontend, "test-failed")
    if config.get("lintFiles"):
        run("lint", node_command + ["node_modules/eslint/bin/eslint.js", *config["lintFiles"]], frontend, "lint-failed")
    if config["task"] == "test" and config.get("scope", "frontend") == "frontend":
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
        if not config.get('verifiedFrontend'):
            run("types-app", node_command + ["node_modules/typescript/bin/tsc", "--noEmit", "-p", "tsconfig.app.json"], frontend, "build-failed")
            run("types-config", node_command + ["node_modules/typescript/bin/tsc", "--noEmit", "-p", "tsconfig.node.json"], frontend, "build-failed")
            run("vite", node_command + ["node_modules/vite/bin/vite.js", "build", "--configLoader", "runner", "--emptyOutDir", "false"], frontend, "build-failed")
            run("budget", node_command + ["scripts/check-bundle-budget.mjs"], frontend, "build-failed")
        if config.get("browserProbe"):
            run("browser", node_command + ["../scripts/remote-check/browser-finalization.mjs"], frontend, "browser-test-failed")
        if config.get("scope") in {"backend-browser", "backend-pg-browser"}:
            run("backend-build", [sys.executable, "scripts/remote-check/backend-build.py"], root, "build-failed")
            if config["task"] == "test":
                delegation = Path('/sys/fs/cgroup/user.slice/user-1000.slice/user@1000.service')
                owned_group = delegation / ('ratools-' + root.name)
                owned_group.mkdir()
                (owned_group / 'cgroup.subtree_control').write_text('+cpu +memory +pids')
                os.environ['RATOOLS_PDF_CGROUP_ROOT'] = str(owned_group)
                run("linux-resources", ["/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423/dotnet", "backend/LinuxResourceProbe.dll", "results/linux-resources"], root, "test-failed")
                run("capture-recycle", ["/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423/dotnet", "backend/CaptureRecycleProbe.dll", "results/capture-recycle"], root, "test-failed")
                run("process-tree", ["/data/home/ratools-qa-76555a3-20261107/dotnet-8.0.423/dotnet", "backend/ProcessTreeProbe.dll", "results/process-tree"], root, "test-failed")
                if config.get("scope") == "backend-pg-browser":
                    run("postgres-browser", [sys.executable, "scripts/remote-check/postgres-platform.py", "--product-browser"], root, "postgres-browser-failed")
                else:
                    run("backend-browser", node_command + ["../scripts/remote-check/browser-backend.mjs"], frontend, "browser-test-failed")
                run("download-pdf", [sys.executable, "scripts/remote-check/verify-pdf-download.py"], root, "test-failed")
    summary["category"] = "passed"
    summary["exitCode"] = 0
except Exception as error:
    summary["error"] = str(error)
    (results / "worker-error.log").write_text(traceback.format_exc())
finally:
    (results / "summary.json").write_text(json.dumps(summary, indent=2))
    print(json.dumps(summary), flush=True)
sys.exit(summary["exitCode"])
