#!/usr/bin/env python3
from pathlib import Path
import re


REPO_ROOT = Path(__file__).resolve().parents[2]
CONTROLLER = REPO_ROOT / "src" / "RATools.Api" / "Controllers" / "PublishJobsController.cs"
HTTP_EXAMPLES = REPO_ROOT / "RATools.Api.http"
README = REPO_ROOT / "README.md"


def method_block(source: str, method_name: str) -> str:
    match = re.search(
        rf"(?P<attributes>(?:\s*\[[^\n]+\]\n)+)\s*public\s+async\s+Task<IActionResult>\s+{method_name}\(",
        source,
    )
    if not match:
        raise AssertionError(f"Could not find controller method {method_name}")

    return match.group("attributes")


def http_example_block(source: str, title: str) -> str:
    match = re.search(
        rf"### {re.escape(title)}\n(?P<block>.*?)(?=\n### |\Z)",
        source,
        re.S,
    )
    if not match:
        raise AssertionError(f"Could not find HTTP example section {title}")

    return match.group("block")


def main() -> None:
    source = CONTROLLER.read_text(encoding="utf-8")
    http_examples = HTTP_EXAMPLES.read_text(encoding="utf-8")
    readme = README.read_text(encoding="utf-8")

    create_attributes = method_block(source, "Create")
    execute_attributes = method_block(source, "Execute")

    assert '[HttpPost]' in create_attributes, "Create must remain POST /api/publish-jobs"
    assert '[HttpPost("execute")]' in execute_attributes, "Execute must remain POST /api/publish-jobs/execute"

    assert (
        '[ProducesResponseType(typeof(PublishJobDto), StatusCodes.Status201Created)]'
        in create_attributes
    ), "POST /api/publish-jobs must document that it creates a job resource, not a publish report"

    assert (
        "PublishExecutionReportDto" not in create_attributes
    ), "POST /api/publish-jobs must not advertise the execute/report response contract"

    assert (
        '[ProducesResponseType(typeof(PublishExecutionReportDto), StatusCodes.Status200OK)]'
        in execute_attributes
    ), "POST /api/publish-jobs/execute must document the create+execute+return-report contract"

    create_example = http_example_block(http_examples, "Create publish job")
    execute_example = http_example_block(http_examples, "Execute publish and return unified report")

    assert (
        "# Response: 201 Created PublishJobDto" in create_example
    ), "RATools.Api.http must clarify that POST /api/publish-jobs returns a created PublishJobDto"

    assert (
        "PublishExecutionReportDto" not in create_example
    ), "RATools.Api.http must not imply POST /api/publish-jobs returns the execution report contract"

    assert (
        "# Response: 200 OK PublishExecutionReportDto" in execute_example
    ), "RATools.Api.http must clarify that POST /api/publish-jobs/execute returns PublishExecutionReportDto"

    assert (
        "POST /api/publish-jobs` creates a publish job resource and returns `201 Created` with `PublishJobDto`"
        in readme
    ), "README must clarify that POST /api/publish-jobs creates a job resource"

    assert (
        "POST /api/publish-jobs/execute` creates and executes a publish job and returns `200 OK` with `PublishExecutionReportDto`"
        in readme
    ), "README must clarify that POST /api/publish-jobs/execute creates+executes and returns the execution report"


if __name__ == "__main__":
    main()
