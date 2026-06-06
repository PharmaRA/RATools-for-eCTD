# Validation Issue Workspace Linking Design

## Goal

Make most validation issues actionable from the sequence workspace by letting reviewers jump from a validation row to the relevant eCTD tree node. The first version focuses on reliable selection and details-panel linkage rather than visual issue badges or virtual-tree scrolling.

## Current State

The sequence workspace already renders validation results in three sections: issues, lifecycle targets, and section matches. The eCTD tree selection is controlled by `selectedTreeKey`, `selectedSectionPath`, and `expandedKeys`, and document nodes use keys in the form `placement:<placementId>`.

Backend validation issues currently expose only `severity`, `code`, and `message`. That is not enough for stable UI navigation. Existing validation side channels already carry some structured location data: `ValidationSectionMatchDto` has `sectionPath`, and `ValidationLifecycleMatchDto` has `ctdSection`, `documentId`, and historical placement ids.

## Backend Design

Extend `ValidationIssueDto` with optional location fields:

```csharp
public sealed record ValidationIssueDto(
    string Severity,
    string Code,
    string Message,
    string? SectionPath = null,
    Guid? DocumentId = null,
    Guid? PlacementId = null);
```

Validation code should populate these fields at the issue source, not by parsing messages. Placement-specific issues should include `placement.Id`, `placement.DocumentId`, and `placement.CtdSection` when available. Section-only issues should include `sectionPath`. Application-level issues should intentionally leave all location fields empty.

Targeted issue coverage:

- `DUPLICATE_PLACEMENT`: include the duplicate placement location, preferably one issue per duplicate placement so each row can locate a concrete node.
- `DUPLICATE_PUBLISHED_DOCUMENT_PATH`: include the affected placement/document location when the duplicate path comes from a placed document.
- `UNSUPPORTED_OPERATION_VALUE`: include placement, document, and section.
- `DOCUMENT_NOT_FOUND`: include placement and section, with document id when available.
- Lifecycle resolver failures such as missing or ambiguous historical matches: include the current lifecycle placement, document, and section.
- `MISSING_LEAF_CORE_METADATA`: include placement, document, and section.
- `SECTION_MISSING`: include placement and document.
- `INVALID_SECTION_PATH`, `SECTION_DEPTH_SHALLOW`, and `NON_STANDARD_SECTION_PATTERN`: include placement, document, and section.
- `TITLE_FALLBACK_USED`: include placement, document, and section.
- `MEDIA_TYPE_MISMATCH`: include placement, document, and section.
- `FILE_MISSING`: include placement, document, and section.
- `APP_NOT_FOUND`, `SEQ_NOT_FOUND`, `NO_PLACEMENTS`, and `SEQUENCE_NOT_LATEST`: do not expose a workspace locator.

The API remains backward-compatible for consumers because the new fields are optional JSON properties. Existing clients can ignore them.

## Frontend Design

Update `ValidationIssue` in `validationActions.ts` with optional `sectionPath`, `documentId`, and `placementId` fields.

Add a small locator helper in `SequenceWorkspacePage.tsx` that resolves a validation location to a tree key:

1. If `placementId` is present, try `placement:<placementId>`.
2. If `documentId` is present, search current-sequence `placements` for the document and resolve `placement:<placement.id>`.
3. If `sectionPath` is present, try the section node key directly.
4. If none resolve, the issue is not locatable.

When a locator resolves, selecting it should:

- Set `selectedTreeKey` to the resolved section or document node key.
- Set `selectedSectionPath` from the resolved tree node.
- Expand ancestors with `getSectionAncestorKeys(resolvedNode.sectionPath)` and include the selected section path.
- Let the existing Selection Details panel update from the selected node.

If a location field exists but the target node cannot be found, show `message.warning('Could not locate this validation issue in the workspace tree.')` and leave the current selection unchanged.

## UI Behavior

In the `Issues to fix` list, each locatable issue row shows a small `Locate` button. Non-locatable issues remain plain text.

Add the same behavior to abnormal rows in `Section Matches` and lifecycle rows when their location data can be resolved:

- Section match rows locate to `sectionPath`.
- Lifecycle rows locate first by current sequence `documentId`, then by `ctdSection`.

This keeps the feature useful even for reports that contain structured section or lifecycle data but not enriched issue fields.

## Error Handling

Location fields are hints, not a guarantee. The frontend should tolerate stale validation reports, deleted placements, missing documents, invalid sections, and API errors. A failed locate action should produce a warning message and must not clear the current tree selection.

## Testing

Backend tests should verify that representative issue sources populate the expected locator fields and that application-level issues do not include a locator. Tests should focus on issue DTO output rather than message text.

Frontend tests should verify:

- Locatable validation issues render a `Locate` button.
- Clicking `Locate` selects the matching document node and updates Selection Details.
- Section-only rows select the section node.
- Non-locatable issues do not render a locate button.
- Stale locator fields show a warning and do not crash.

## Out Of Scope

- Issue filtering, grouping, or severity facets.
- Tree node issue-count badges.
- Exact virtual-tree scroll positioning.
- Multi-node highlighting for one issue.
- Editing validation issues directly from the issue row.

## Self-Review

The design avoids message parsing, defines optional API fields, identifies which issue codes are locatable, describes fallback behavior, and keeps the first implementation scoped to stable selection and details-panel linkage.
