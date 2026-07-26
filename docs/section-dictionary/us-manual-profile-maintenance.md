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
dotnet test tests/RATools.Tests/RATools.Tests.csproj --filter "SectionProfileGuardTests|SequenceValidationLifecycleTargetTests|SequenceNumberValidationTests"
```

Expected outcome: every listed class matches at least one executed test and all pass.
`SectionProfileGuardTests` is the profile's structural guard (metadata completeness,
module coverage, element-name uniqueness, canonical folder anchoring for both the
US and EU dictionaries); if you rename it, update this checklist in the same commit —
a filter that matches zero tests exits green and silently voids this gate.

## Commit Message Checklist
- Why this profile update is required.
- Which sections and element names changed.
- Which test commands were run.
