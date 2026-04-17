# US Manual Profile Maintenance (FdaEctd322)

## Change Triggers
- Add or modify nodes only when FDA/ICH guidance or controlled submission requirements change.
- Keep valid-but-not-listed paths as `NON_STANDARD` unless they must become explicit profile nodes.
- Edit profile nodes in `src/RATools.Application/Validation/Profiles/FdaEctd322.cs`.

## Required Metadata
- Every node with a section path must include stable `ElementName`, `SectionPath`, and `Title` values.
- Valid folder metadata means `FolderName` is present and non-empty for every node with a section path so canonical folder paths can be built.

## Required Test Checklist
Run all commands before merge:

```bash
dotnet test tests/RATools.Application.Tests/RATools.Application.Tests.csproj --filter "SectionDictionaryTests|SequenceValidationServiceTests|EctdStructureServiceTests|FdaEctd322ProfileGuardTests"
dotnet test tests/RATools.Api.Tests/RATools.Api.Tests.csproj --filter EctdStructureControllerTests
```

Expected outcome: all listed tests pass and `FdaEctd322ProfileGuardTests` has no skipped tests.

## Commit Message Checklist
- Why this profile update is required.
- Which sections and element names changed.
- Which test commands were run.
