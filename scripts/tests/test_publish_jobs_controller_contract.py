#!/usr/bin/env python3
from pathlib import Path
import re


REPO_ROOT = Path(__file__).resolve().parents[2]
CONTROLLER = REPO_ROOT / "src" / "RATools.Api" / "Controllers" / "PublishJobsController.cs"
HTTP_EXAMPLES = REPO_ROOT / "RATools.Api.http"
README = REPO_ROOT / "README.md"


def method_block(source: str, method_name: str) -> str:
    match = re.search(
        rf"(?P<attributes>(?:\s*\[[^\n]+\]\n)+)\s*public(?:\s+async)?\s+(?:Task<IActionResult>|IActionResult)\s+{method_name}\(",
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

    legacy_attributes = method_block(source, "Create")
    execute_attributes = method_block(source, "Execute")

    assert '[HttpPost]' in legacy_attributes, "Legacy POST /api/publish-jobs route must remain discoverable during migration"
    assert '[HttpPost("execute")]' in execute_attributes, "Execute must remain POST /api/publish-jobs/execute"

    assert (
        '[ProducesResponseType(StatusCodes.Status410Gone)]' in legacy_attributes
    ), "Legacy POST /api/publish-jobs must document that it is gone"

    assert (
        "CreatePublishJobRequestBody" not in legacy_attributes
    ), "Legacy POST /api/publish-jobs must not accept the old synchronous request contract"

    assert (
        '[ProducesResponseType(typeof(PublishJobDto), StatusCodes.Status202Accepted)]'
        in execute_attributes
    ), "POST /api/publish-jobs/execute must document the async enqueue contract (202 Accepted + PublishJobDto)"

    assert (
        "PublishExecutionReportDto" not in execute_attributes
    ), "POST /api/publish-jobs/execute runs in the background; it must not advertise a synchronous report response"

    execute_example = http_example_block(http_examples, "Execute publish in the background")

    assert "Create publish job" not in http_examples, "RATools.Api.http must stop documenting the removed synchronous command"

    assert (
        "# Response: 202 Accepted PublishJobDto" in execute_example
    ), "RATools.Api.http must clarify that POST /api/publish-jobs/execute returns 202 with the queued PublishJobDto"

    assert (
        "The former synchronous `POST /api/publish-jobs` endpoint is deprecated and returns `410 Gone`"
        in readme
    ), "README must clarify that the legacy synchronous endpoint is gone"

    assert (
        "POST /api/publish-jobs/execute` enqueues background execution and returns `202 Accepted` with `PublishJobDto`"
        in readme
    ), "README must clarify that POST /api/publish-jobs/execute enqueues background execution"


if __name__ == "__main__":
    main()
