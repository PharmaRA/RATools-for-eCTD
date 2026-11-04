"""P5-05 fixed-fixture technical trial, not a production PDF processing endpoint.

qpdf does the transformation. MuPDF independently checks destinations, text and
rendered pixels. All outputs are retained as evidence; no temporary file deletion.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import platform
import subprocess
import time


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf"
SOURCE_SHA256 = "11a5534ee4b790e96e5953168ba68dd9810e48776afd2473cfad8a07e8dae705"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def command(qpdf: str, args: list[str], timeout: float) -> bytes:
    # subprocess.run kills and waits for the direct process on timeout. qpdf does
    # not spawn workers in this trial. This is not a production process sandbox.
    result = subprocess.run([qpdf, *args], check=True, timeout=timeout, capture_output=True)
    return result.stdout


def transform_trial(qpdf: str, source: Path, output: Path, timeout: float = 30) -> None:
    if output.exists() or output.is_symlink():
        raise FileExistsError(output)
    if source.is_symlink() or source.stat().st_size > 1024 * 1024:
        raise ValueError("Trial input must be a regular fixture no larger than 1 MiB")
    inspected = json.loads(command(qpdf, ["--json", "--json-stream-data=none", str(source)], timeout))
    if inspected["encrypt"]["encrypted"]:
        raise ValueError("Encrypted input is outside this navigation trial")
    objects = inspected["qpdf"][1]

    def has_signature(value):
        if isinstance(value, dict):
            return (value.get("/Type") == "/Sig" or value.get("/FT") == "/Sig"
                    or "/ByteRange" in value or any(has_signature(item) for item in value.values()))
        return isinstance(value, list) and any(has_signature(item) for item in value)

    if has_signature(objects):
        raise ValueError("Signature dictionary detected; modification is not qualified")
    if sha256(source) != SOURCE_SHA256:
        raise ValueError("This technical trial only accepts its frozen synthetic fixture")
    # Fixed object addresses are intentional: this qualifies the qpdf primitive,
    # not a general PDF editor. P5-06 must resolve objects from a bound source version.
    catalog = objects["obj:1 0 R"]["value"]
    catalog["/PageMode"] = "/UseOutlines"
    catalog["/Dests"]["/overview"] = ["4 0 R", "/Fit"]
    objects["obj:10 0 R"]["value"]["/Title"] = "u:总览"
    objects["obj:10 0 R"]["value"]["/Dest"] = ["4 0 R", "/Fit"]
    objects["obj:11 0 R"]["value"]["/Title"] = "u:详细资料"
    objects["obj:8 0 R"]["value"]["/A"]["/D"] = ["4 0 R", "/Fit"]
    objects["obj:12 0 R"]["value"]["/A"]["/F"] = "u:renamed.pdf"
    changed = {f"obj:{number} 0 R": objects[f"obj:{number} 0 R"] for number in (1, 8, 10, 11, 12)}
    update = output.with_suffix(".update.json")
    with update.open("x", encoding="utf-8") as handle:
        json.dump({"qpdf": [{"jsonversion": 2}, changed]}, handle, ensure_ascii=False, indent=2)
    # Write through an exclusively-created handle, so qpdf cannot overwrite a
    # path created between the initial existence check and process launch.
    with output.open("xb") as handle:
        subprocess.run([qpdf, str(source), f"--update-from-json={update}", "--deterministic-id", "-"],
                       stdout=handle, stderr=subprocess.PIPE, check=True, timeout=timeout)
    command(qpdf, ["--check", str(output)], timeout)
    if sha256(source) != SOURCE_SHA256:
        raise ValueError("Source changed during the trial; result is not qualified")


def run_trial(qpdf: str, directory: Path) -> dict:
    import pymupdf

    directory.mkdir(parents=True, exist_ok=False)
    version = command(qpdf, ["--version"], 10).decode("utf-8").splitlines()[0]
    if version != "qpdf version 12.4.1":
        raise ValueError("This trial is pinned to qpdf 12.4.1")
    if pymupdf.VersionBind != "1.28.2":
        raise ValueError("This trial is pinned to PyMuPDF 1.28.2")
    started = time.monotonic()
    source = directory / "source.pdf"
    source.write_bytes(SOURCE.read_bytes())
    target = directory / "renamed.pdf"
    target.write_bytes(SOURCE.read_bytes())
    result = directory / "navigation.pdf"
    transform_trial(qpdf, source, result)
    pages = []
    with pymupdf.open(source) as before, pymupdf.open(result) as after, pymupdf.open(target) as remote:
        checks = {
            "sourcePreserved": sha256(source) == SOURCE_SHA256,
            "nestedUnicodeBookmarks": after.get_toc() == [[1, "总览", 2], [2, "详细资料", 1]],
            "initialView": after.pagemode == "UseOutlines",
            "namedDestination": after.resolve_names()["overview"]["page"] == 1,
            "pageCount": before.page_count == after.page_count == 2,
        }
        internal, link = after[0].get_links()
        checks["internalLink"] = internal["kind"] == pymupdf.LINK_GOTO and internal["page"] == 1
        checks["remoteLink"] = (
            link["file"] == "renamed.pdf#nameddest=overview"
            and after.xref_get_key(link["xref"], "A/S") == ("name", "/GoToR")
            and after.xref_get_key(link["xref"], "A/F") == ("string", "renamed.pdf")
            and after.xref_get_key(link["xref"], "A/D") == ("string", "overview")
            and remote.resolve_names()["overview"]["page"] == 0)
        checks["uriPreserved"] = after[1].get_links()[0]["uri"] == "https://example.org/reference"
        for index, (old, new) in enumerate(zip(before, after), 1):
            old_render = hashlib.sha256(old.get_pixmap().samples).hexdigest()
            new_render = hashlib.sha256(new.get_pixmap().samples).hexdigest()
            unchanged_streams = ([before.xref_stream(x) for x in old.get_contents()]
                                 == [after.xref_stream(x) for x in new.get_contents()])
            pages.append({"page": index, "sourceRenderSha256": old_render, "outputRenderSha256": new_render,
                          "textUnchanged": old.get_text() == new.get_text(), "decodedStreamsUnchanged": unchanged_streams})
        checks["pageFidelity"] = len(pages) == 2 and all(
            page["sourceRenderSha256"] == page["outputRenderSha256"] and page["textUnchanged"]
            and page["decodedStreamsUnchanged"] for page in pages)
    report = {
        "schemaVersion": 1, "task": "P5-05", "platform": platform.system(),
        "recordedUtc": datetime.now(timezone.utc).isoformat(),
        "pythonVersion": platform.python_version(), "scriptSha256": sha256(Path(__file__)),
        "engineExecutableSha256": sha256(Path(qpdf)),
        "engine": version, "independentReader": f"PyMuPDF {pymupdf.VersionBind}",
        "sourceSha256": sha256(source), "outputSha256": sha256(result), "remoteTargetSha256": sha256(target),
        "updateSha256": sha256(result.with_suffix(".update.json")), "checks": checks, "pages": pages,
        "elapsedSeconds": round(time.monotonic() - started, 3),
        "status": "Passed" if all(checks.values()) else "Failed",
        "scope": "Frozen two-page navigation fixture only; no production, OCR, font or regulatory qualification",
    }
    with (directory / "receipt.json").open("x", encoding="utf-8") as handle:
        json.dump(report, handle, ensure_ascii=False, indent=2)
    if report["status"] != "Passed":
        raise ValueError("Navigation qualification failed; inspect receipt.json")
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--qpdf", required=True)
    parser.add_argument("--output-dir", type=Path, required=True, help="New retained evidence directory")
    arguments = parser.parse_args()
    print(json.dumps(run_trial(arguments.qpdf, arguments.output_dir), ensure_ascii=False, indent=2))
