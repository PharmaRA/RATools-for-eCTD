# Publish Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden the current eCTD publish pipeline so generated artifacts remain historically correct, resilient to corruption, scalable for filtered history queries, and closer to real eCTD backbone structure.

**Architecture:** Keep the existing layered design, but move more publish-history work into repository-backed queries and isolate publish artifact naming/path logic so each publish job produces immutable outputs. Add tolerant read paths for persisted reports and incrementally tighten backbone/document path generation to avoid collisions and malformed submissions.

**Tech Stack:** .NET 8, ASP.NET Core, EF Core, PostgreSQL/InMemory repositories, xUnit, PowerShell smoke test.

---

## File Map

- Modify: `src/RATools.Infrastructure/Publishing/LocalBackboneFileWriter.cs`
  - Make package file names unique per publish job and generate collision-safe document output names.
- Modify: `src/RATools.Application/Publishing/BackboneService.cs`
  - Emit stable publish-relative paths that match renamed copied documents.
- Modify: `src/RATools.Application/Publishing/PublishJobService.cs`
  - Carry package/report naming metadata, tolerate report corruption on read, and support richer history/report behavior.
- Modify: `src/RATools.Application/Applications/ApplicationPublishHistoryService.cs`
  - Use repository-backed filtering/paging and tolerate unreadable reports.
- Modify: `src/RATools.Application/Abstractions/Persistence/IPublishJobRepository.cs`
  - Add application-scoped filtered/paged query methods.
- Modify: `src/RATools.Infrastructure/Persistence/InMemory/InMemoryPublishJobRepository.cs`
- Modify: `src/RATools.Infrastructure/Persistence/EfCore/EfCorePublishJobRepository.cs`
  - Implement repository-side paging/filtering.
- Modify: `src/RATools.Application/Applications/Dtos/ApplicationPublishHistoryDto.cs`
  - Add history-readability flags and any additional summary metadata if needed.
- Modify: `src/RATools.Application/Validation/SequenceValidationService.cs`
  - Expand real eCTD validation rules only after artifact correctness issues are fixed.
- Modify: `scripts/smoke-test.ps1`
  - Keep smoke test aligned with artifact immutability, history queries, and tolerant reads.
- Test: `tests/RATools.Infrastructure.Tests/Publishing/LocalBackboneFileWriterTests.cs`
- Test: `tests/RATools.Application.Tests/Publishing/PublishJobServiceTests.cs`
- Test: `tests/RATools.Application.Tests/Applications/ApplicationPublishHistoryServiceTests.cs`

## Recommended Implementation Order

1. Fix package overwrite and document name collisions.
2. Add tolerant report/history reading.
3. Move publish-history filtering/paging into repository queries.
4. Improve backbone/eCTD structural correctness.

## Review Findings Addressed By This Plan

1. Package path is still sequence-based and gets overwritten by later publishes of the same sequence.
2. Published documents still use raw file names, so same-name inputs can overwrite each other in `documents/`.
3. Persisted report reads can still fail hard if a report JSON is corrupted.
4. Publish-history filtering and paging still happens in memory after loading all jobs.
5. Backbone structure is still minimal and not yet close enough to real eCTD structure for serious validation.
