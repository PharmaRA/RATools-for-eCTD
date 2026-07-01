# Sequence Workspace Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `SequenceWorkspacePage.tsx` into focused pure logic, data hooks, tree/drag-drop components, validation summary, and publish modal modules while preserving current workflow behavior.

**Architecture:** Move pure validation checklist logic first so later UI extractions can import a stable view model. Then extract display-only panels and form modals, followed by data-fetching and drag/drop hooks. Keep provider props on `SequenceWorkspacePage` so existing tests can keep injecting validation/readiness/publish behavior.

**Tech Stack:** React 19, TypeScript, Vite, Vitest, Testing Library, Ant Design.

---

## Scope Check

This plan implements only the sequence workspace refactor from `docs/superpowers/specs/2026-06-18-sequence-workspace-refactor-design.md`.

Do not change backend API contracts, `validationActions`, `publishActions`, or `workspaceActions` signatures. Do not introduce a state management library. Do not change validation semantics except for the explicitly required visible fetch errors, multi-file drop handling, and keyboard-accessible move support.

## File Structure Map

- Create: `frontend/src/prePublishChecklist.ts`
  - Owns `buildPrePublishChecklistSummary`, `normalizeValidationReport`, checklist constants, and summary types.
- Create: `frontend/src/prePublishChecklist.test.ts`
  - Direct unit coverage for checklist status, structural validation fallback, section/lifecycle behavior, and warning handling.
- Create: `frontend/src/pages/workspace/ValidationSummaryPanel.tsx`
  - Renders the existing validation summary markup and preserves existing `data-testid` values.
- Create: `frontend/src/pages/workspace/PublishModal.tsx`
  - Renders publish output path and publish metadata fields.
- Create: `frontend/src/pages/workspace/useWorkspaceData.ts`
  - Owns placement/document/eCTD structure fetching, derived `treeData`, visible fetch errors, and refresh helpers.
- Create: `frontend/src/pages/workspace/useWorkspaceData.test.tsx`
  - Uses `renderHook` to cover successful data load and visible error states.
- Create: `frontend/src/pages/workspace/useWorkspaceDragDrop.ts`
  - Owns drag state, drag payload parsing, drop orchestration, multi-file processing, and keyboard move orchestration.
- Create: `frontend/src/pages/workspace/useWorkspaceDragDrop.test.tsx`
  - Covers multi-file drop filtering, sequential upload calls, stale payload handling, and keyboard move.
- Create: `frontend/src/pages/workspace/WorkspaceTree.tsx`
  - Wraps antd `Tree`, renders accessible section/document nodes, delegates drag/drop to the hook.
- Modify: `frontend/src/pages/SequenceWorkspacePage.tsx`
  - Becomes the composition layer for workspace data, publish flow, validation summary, workspace tree, and leaf metadata panel.
- Modify: `frontend/src/pages/SequenceWorkspacePage.validation.test.tsx`
  - Add visible fetch-error assertions and keyboard/multi-file integration assertions while preserving current tests.

## Task 1: Extract Pre-Publish Checklist Logic

**Files:**
- Create: `frontend/src/prePublishChecklist.ts`
- Create: `frontend/src/prePublishChecklist.test.ts`
- Modify: `frontend/src/pages/SequenceWorkspacePage.tsx`

- [ ] **Step 1: Write failing checklist unit tests**

Create `frontend/src/prePublishChecklist.test.ts` with these tests:

```ts
import { describe, expect, it } from 'vitest'

import {
  apiErrorCode,
  buildPrePublishChecklistSummary,
  normalizeValidationReport,
  validationApiProfile,
} from './prePublishChecklist'
import type { ValidationReport } from './validationActions'

const createReport = (overrides: Partial<ValidationReport> = {}): ValidationReport => ({
  applicationId: 'app-1',
  sequenceNumber: '0000',
  validationProfile: 'US FDA eCTD 3.2.2',
  isValid: true,
  issues: [],
  sectionMatches: [],
  lifecycleMatches: [],
  ...overrides,
})

describe('prePublishChecklist', () => {
  it('fails closed when a validation report has unusable structure', () => {
    const report = {
      applicationId: 'app-1',
      sequenceNumber: '0000',
      validationProfile: '',
      isValid: true,
      issues: [{ severity: '', code: 'BROKEN', message: 'Missing severity' }],
      sectionMatches: [],
      lifecycleMatches: [],
    } as ValidationReport

    const normalized = normalizeValidationReport(report)

    expect(normalized.validationProfile).toBe(validationApiProfile)
    expect(normalized.issues).toContainEqual({
      severity: 'Error',
      code: apiErrorCode,
      message: 'Validation service returned an unusable report.',
    })
  })

  it('blocks publish for validation errors and keeps warnings non-blocking', () => {
    const summary = buildPrePublishChecklistSummary(createReport({
      issues: [
        { severity: 'Error', code: 'ERR', message: 'Blocking' },
        { severity: 'Warning', code: 'WARN', message: 'Awareness' },
      ],
    }))

    expect(summary.canProceed).toBe(false)
    expect(summary.blockingIssueCount).toBe(1)
    expect(summary.warningCount).toBe(1)
    expect(summary.checklistRows.find((row) => row.key === 'blocking-errors')).toMatchObject({
      status: 'fail',
      blocking: true,
    })
  })

  it('marks lifecycle target errors as blocking lifecycle rows', () => {
    const summary = buildPrePublishChecklistSummary(createReport({
      issues: [{ severity: 'Error', code: 'REPLACE_TARGET_NOT_FOUND', message: 'Missing target' }],
      lifecycleMatches: [{
        operation: 'Replace',
        sequenceNumber: '0000',
        ctdSection: '1.2',
        documentId: 'doc-1',
        resultCode: 'REPLACE_TARGET_NOT_FOUND',
        matchStrategy: 'by-file-name',
        attemptedStrategies: ['by-file-name'],
        historicalMatchCount: 0,
        historicalSequenceNumbers: [],
        historicalPlacementIds: [],
        historicalFinalState: 'Missing',
      }],
    }))

    expect(summary.lifecycleIssueCount).toBe(1)
    expect(summary.checklistRows.find((row) => row.key === 'lifecycle-targets')).toMatchObject({
      status: 'fail',
      blocking: true,
    })
  })

  it('keeps non-standard section matches informational unless backed by blocking issues', () => {
    const summary = buildPrePublishChecklistSummary(createReport({
      sectionMatches: [{
        sectionPath: '1.2',
        matchedPrefix: '1',
        isValid: true,
        isStandard: false,
        reason: 'Non-standard but allowed',
      }],
    }))

    expect(summary.canProceed).toBe(true)
    expect(summary.nonStandardSectionCount).toBe(1)
    expect(summary.checklistRows.find((row) => row.key === 'section-paths')).toMatchObject({
      status: 'info',
      blocking: false,
    })
  })
})
```

- [ ] **Step 2: Run the new test and confirm RED**

Run:

```powershell
npm test -- src/prePublishChecklist.test.ts
```

Working directory: `frontend`

Expected: fail because `frontend/src/prePublishChecklist.ts` does not exist.

- [ ] **Step 3: Move pure checklist logic**

Create `frontend/src/prePublishChecklist.ts` by moving these existing declarations out of `SequenceWorkspacePage.tsx` without changing branch logic:

```ts
export type PrePublishChecklistRow = {
  key: string
  label: string
  status: 'pass' | 'fail' | 'info'
  detail: string
  blocking: boolean
}

export type NormalizedValidationReport = {
  validationProfile: string
  issues: ValidationIssue[]
  sectionMatches: ValidationSectionMatch[]
  lifecycleMatches: ValidationLifecycleMatch[]
}

export type PrePublishChecklistSummary = ReturnType<typeof buildPrePublishChecklistSummary>
```

Move the current `validationApiProfile`, `apiErrorCode`, `structurallyUnusableReportMessage`, `blockingSectionIssueCodes`, `stringEqualsIgnoreCase`, `isErrorIssue`, `hasUsableIssueSeverity`, `normalizeValidationReport`, `isBlockingSectionIssue`, and `buildPrePublishChecklistSummary` from `SequenceWorkspacePage.tsx` into this file. Import `ValidationIssue`, `ValidationLifecycleMatch`, `ValidationReport`, and `ValidationSectionMatch` from `./validationActions`.

In `SequenceWorkspacePage.tsx`, remove the moved definitions and add:

```ts
import {
  buildPrePublishChecklistSummary,
  type PrePublishChecklistRow,
} from '../prePublishChecklist'
```

Keep `getChecklistTagColor` and `getChecklistTagLabel` in the page until Task 2.

- [ ] **Step 4: Verify checklist tests GREEN**

Run:

```powershell
npm test -- src/prePublishChecklist.test.ts
```

Working directory: `frontend`

Expected: 4 tests pass.

- [ ] **Step 5: Verify existing workspace publish tests still pass**

Run:

```powershell
npm test -- src/pages/SequenceWorkspacePage.validation.test.tsx
```

Working directory: `frontend`

Expected: existing sequence workspace tests pass.

## Task 2: Extract ValidationSummaryPanel

**Files:**
- Create: `frontend/src/pages/workspace/ValidationSummaryPanel.tsx`
- Modify: `frontend/src/pages/SequenceWorkspacePage.tsx`
- Modify: `frontend/src/pages/SequenceWorkspacePage.validation.test.tsx`

- [ ] **Step 1: Add an assertion that existing summary test IDs remain stable**

In `frontend/src/pages/SequenceWorkspacePage.validation.test.tsx`, extend the existing validation summary tests to assert the panel still exposes:

```ts
expect(screen.getByTestId('validation-summary')).toBeInTheDocument()
expect(screen.getByTestId('validation-summary-checklist')).toBeInTheDocument()
expect(screen.getByTestId('validation-summary-issues')).toBeInTheDocument()
expect(screen.getByTestId('validation-summary-warnings')).toBeInTheDocument()
expect(screen.getByTestId('validation-summary-lifecycle')).toBeInTheDocument()
expect(screen.getByTestId('validation-summary-sections')).toBeInTheDocument()
```

- [ ] **Step 2: Run the targeted test before extraction**

Run:

```powershell
npm test -- src/pages/SequenceWorkspacePage.validation.test.tsx
```

Working directory: `frontend`

Expected: pass before refactor. This locks the render contract before moving markup.

- [ ] **Step 3: Create the panel component**

Create `frontend/src/pages/workspace/ValidationSummaryPanel.tsx` with props:

```ts
import { Alert, Button, Tag } from 'antd'

import type { PrePublishChecklistRow, PrePublishChecklistSummary } from '../../prePublishChecklist'
import type { ValidationIssue } from '../../validationActions'

type ValidationLocation = {
  placementId?: string | null
  documentId?: string | null
  sectionPath?: string | null
}

type ValidationSummaryPanelProps = {
  summary: PrePublishChecklistSummary
  statusText: string
  issueCountText: string
  hasValidationLocation: (location: ValidationLocation) => boolean
  locateValidationIssue: (location: ValidationLocation) => void
}
```

Move the current JSX block guarded by `{validationSummary && (...)}` from `SequenceWorkspacePage.tsx` into this component. Move `getChecklistTagColor` and `getChecklistTagLabel` into the component unchanged:

```ts
const getChecklistTagColor = (row: PrePublishChecklistRow) => {
  if (row.status === 'pass') return 'green'
  if (row.blocking) return 'red'
  return 'blue'
}

const getChecklistTagLabel = (row: PrePublishChecklistRow) => {
  if (row.status === 'pass') return 'Pass'
  if (row.status === 'fail') return 'Fail'
  return 'Awareness'
}
```

- [ ] **Step 4: Replace inline summary render**

In `SequenceWorkspacePage.tsx`, import and render:

```tsx
{validationSummary && (
  <ValidationSummaryPanel
    summary={validationSummary}
    statusText={validationStatusText}
    issueCountText={validationIssueCountText}
    hasValidationLocation={hasValidationLocation}
    locateValidationIssue={locateValidationIssue}
  />
)}
```

- [ ] **Step 5: Verify summary extraction**

Run:

```powershell
npm test -- src/pages/SequenceWorkspacePage.validation.test.tsx
npm run lint
```

Working directory: `frontend`

Expected: workspace tests pass and lint stays clean.

## Task 3: Extract PublishModal

**Files:**
- Create: `frontend/src/pages/workspace/PublishModal.tsx`
- Modify: `frontend/src/pages/SequenceWorkspacePage.tsx`

- [ ] **Step 1: Run current publish-modal tests as baseline**

Run:

```powershell
npm test -- src/pages/SequenceWorkspacePage.validation.test.tsx -- -t "validates the sequence before opening the publish modal|runs publish readiness before allowing publish|allows publishing when validation only returns warnings"
```

Working directory: `frontend`

Expected: selected tests pass before extraction.

- [ ] **Step 2: Create PublishModal component**

Create `frontend/src/pages/workspace/PublishModal.tsx` with these props:

```ts
import { Alert, Form, Input, Modal } from 'antd'
import type { FormInstance } from 'antd'

import { PathPicker } from '../../PathPicker'
import type { PrePublishChecklistSummary } from '../../prePublishChecklist'
import type { PublishReadinessReport } from '../../validationActions'

export type MetadataFormValues = {
  applicationType?: string
  submissionType: string
  submissionSubtype?: string
  sequenceDescription: string
  applicantName: string
  formType?: string
  applicantContactName?: string
  applicantContactType?: string
  telephone?: string
  telephoneNumberType?: string
  email?: string
}

type PublishModalProps = {
  open: boolean
  publishing: boolean
  validationSummary: PrePublishChecklistSummary | null
  publishReadiness: PublishReadinessReport | null
  publishForm: FormInstance
  publishMetadataForm: FormInstance<MetadataFormValues>
  onOk: () => void
  onCancel: () => void
}
```

Move the existing `<Modal title="Publish Sequence"...>` block and its metadata `<Form.Item>` fields into the component. Preserve the `PathPicker`, field names, labels, and validation rules exactly.

- [ ] **Step 3: Replace inline modal**

In `SequenceWorkspacePage.tsx`, remove the local `MetadataFormValues` type, import it from `PublishModal`, and render:

```tsx
<PublishModal
  open={isPublishModalOpen}
  publishing={publishing}
  validationSummary={validationSummary}
  publishReadiness={publishReadiness}
  publishForm={publishForm}
  publishMetadataForm={publishMetadataForm}
  onOk={triggerPublish}
  onCancel={handlePublishModalCancel}
/>
```

- [ ] **Step 4: Verify publish modal extraction**

Run:

```powershell
npm test -- src/pages/SequenceWorkspacePage.validation.test.tsx
npm run lint
```

Working directory: `frontend`

Expected: workspace tests pass and lint stays clean.

## Task 4: Extract useWorkspaceData With Visible Errors

**Files:**
- Create: `frontend/src/pages/workspace/useWorkspaceData.ts`
- Create: `frontend/src/pages/workspace/useWorkspaceData.test.tsx`
- Modify: `frontend/src/pages/SequenceWorkspacePage.tsx`
- Modify: `frontend/src/pages/SequenceWorkspacePage.validation.test.tsx`

- [ ] **Step 1: Write failing hook tests for visible fetch errors**

Create `frontend/src/pages/workspace/useWorkspaceData.test.tsx`:

```tsx
import { renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useWorkspaceData } from './useWorkspaceData'

describe('useWorkspaceData', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  it('loads placements, documents, structure, and derived tree data', async () => {
    const apiFetch = vi.fn()
      .mockResolvedValueOnce([
        { id: 'placement-1', applicationId: 'app-1', sequenceNumber: '0000', documentId: 'doc-1', ctdSection: '1.2', operation: 'New' },
      ])
      .mockResolvedValueOnce([
        { id: 'doc-1', fileName: 'cover.pdf', storagePath: '/tmp/cover.pdf' },
      ])
      .mockResolvedValueOnce({
        roots: [{ elementName: 'm1', sectionPath: '1.2', displayName: 'Cover', sourceProfile: 'FDA', children: [] }],
      })

    const { result } = renderHook(() => useWorkspaceData({ appId: 'app-1', seqNumber: '0000', apiFetch }))

    await waitFor(() => expect(result.current.treeData).toHaveLength(1))
    expect(result.current.placements).toHaveLength(1)
    expect(result.current.documentsById['doc-1'].fileName).toBe('cover.pdf')
    expect(result.current.treeError).toBeNull()
    expect(result.current.placementsError).toBeNull()
    expect(result.current.documentsError).toBeNull()
  })

  it('stores visible placement and document load errors', async () => {
    const apiFetch = vi.fn()
      .mockRejectedValueOnce(new Error('placements unavailable'))
      .mockRejectedValueOnce(new Error('documents unavailable'))
      .mockResolvedValueOnce({ roots: [] })

    const { result } = renderHook(() => useWorkspaceData({ appId: 'app-1', seqNumber: '0000', apiFetch }))

    await waitFor(() => expect(result.current.placementsError).toBe('placements unavailable'))
    expect(result.current.documentsError).toBe('documents unavailable')
  })
})
```

- [ ] **Step 2: Run hook test and confirm RED**

Run:

```powershell
npm test -- src/pages/workspace/useWorkspaceData.test.tsx
```

Working directory: `frontend`

Expected: fail because `useWorkspaceData.ts` does not exist.

- [ ] **Step 3: Implement useWorkspaceData**

Create `frontend/src/pages/workspace/useWorkspaceData.ts` with:

```ts
type UseWorkspaceDataOptions = {
  appId: string
  seqNumber: string
  apiFetch?: typeof defaultApiFetch
}
```

Move `placements`, `applicationPlacements`, `documentsById`, `treeLoading`, `treeError`, `ectdRoots`, `expandedKeys`, `treeData`, `fetchPlacements`, `fetchDocuments`, `fetchEctdStructure`, and `refreshWorkspaceData` out of `SequenceWorkspacePage.tsx`.

Add state:

```ts
const [placementsError, setPlacementsError] = useState<string | null>(null)
const [documentsError, setDocumentsError] = useState<string | null>(null)
```

When placement/document fetches fail, set those messages with `getErrorMessage(error)` instead of only logging. Clear each error before the corresponding successful request.

- [ ] **Step 4: Render visible fetch errors in the page**

In `SequenceWorkspacePage.tsx`, consume the hook return values and render alerts above the tree row:

```tsx
{placementsError && <Alert type="error" showIcon title="Failed to load workspace placements" description={placementsError} />}
{documentsError && <Alert type="error" showIcon title="Failed to load workspace documents" description={documentsError} />}
```

Keep `treeError` inside the tree card until `WorkspaceTree` is extracted.

- [ ] **Step 5: Add page-level visible error assertion**

In `SequenceWorkspacePage.validation.test.tsx`, add a test that mocks `/api/document-placements` failure and asserts:

```ts
expect(await screen.findByText('Failed to load workspace placements')).toBeInTheDocument()
expect(screen.getByText('placements unavailable')).toBeInTheDocument()
```

- [ ] **Step 6: Verify data hook extraction**

Run:

```powershell
npm test -- src/pages/workspace/useWorkspaceData.test.tsx
npm test -- src/pages/SequenceWorkspacePage.validation.test.tsx
npm run lint
```

Working directory: `frontend`

Expected: hook tests and page tests pass; lint stays clean.

## Task 5: Extract useWorkspaceDragDrop and WorkspaceTree

**Files:**
- Create: `frontend/src/pages/workspace/useWorkspaceDragDrop.ts`
- Create: `frontend/src/pages/workspace/WorkspaceTree.tsx`
- Create: `frontend/src/pages/workspace/useWorkspaceDragDrop.test.tsx`
- Modify: `frontend/src/pages/SequenceWorkspacePage.tsx`

- [ ] **Step 1: Write failing drag/drop hook tests**

Create `frontend/src/pages/workspace/useWorkspaceDragDrop.test.tsx` with tests that call the hook through `renderHook` and invoke returned handlers. Cover:

```ts
expect(uploadFile).toHaveBeenCalledTimes(2)
expect(uploadFile).toHaveBeenNthCalledWith(1, expect.objectContaining({ name: 'one.pdf' }), '1.2')
expect(uploadFile).toHaveBeenNthCalledWith(2, expect.objectContaining({ name: 'two.xml' }), '1.2')
expect(message.error).toHaveBeenCalledWith(expect.stringContaining('Unsupported file extension'))
```

Use real `File` objects:

```ts
const files = [
  new File(['one'], 'one.pdf', { type: 'application/pdf' }),
  new File(['two'], 'two.xml', { type: 'text/xml' }),
  new File(['bad'], 'bad.exe', { type: 'application/octet-stream' }),
]
```

- [ ] **Step 2: Run drag/drop hook test and confirm RED**

Run:

```powershell
npm test -- src/pages/workspace/useWorkspaceDragDrop.test.tsx
```

Working directory: `frontend`

Expected: fail because `useWorkspaceDragDrop.ts` does not exist.

- [ ] **Step 3: Implement useWorkspaceDragDrop**

Create a hook with this shape:

```ts
type UseWorkspaceDragDropOptions = {
  placements: DocumentPlacementRecord[]
  selectedSectionPath: string | null
  movePlacement: (placementId: string, fromSection: string, toSection: string) => Promise<void>
  uploadFile: (file: File, targetNodeKey: string) => Promise<void>
  setDraggingPlacementId?: (placementId: string | null) => void
}

export const useWorkspaceDragDrop = (options: UseWorkspaceDragDropOptions) => {
  const [dragOverNode, setDragOverNode] = useState<string | null>(null)
  const [draggingPlacementId, setDraggingPlacementId] = useState<string | null>(null)
  // return state plus handlers used by WorkspaceTree
}
```

Move `getPlacementPayloadFromDataTransfer` and the drop orchestration out of `SequenceWorkspacePage.tsx`. Iterate all `Array.from(e.dataTransfer.files)` and process valid files sequentially with `await uploadFile(file, nodeData.sectionPath)`. Reject invalid extensions with one aggregated `message.error`.

- [ ] **Step 4: Create WorkspaceTree component**

Create `frontend/src/pages/workspace/WorkspaceTree.tsx` that receives the current tree props:

```ts
type WorkspaceTreeProps = {
  treeData: WorkspaceTreeNode[]
  expandedKeys: string[]
  selectedTreeKey: string | null
  loading: boolean
  treeLoading: boolean
  treeError: string | null
  dragOverNode: string | null
  draggingPlacementId: string | null
  setExpandedKeys: (keys: string[]) => void
  selectTreeNode: (node: WorkspaceTreeNode) => void
  dragDrop: ReturnType<typeof useWorkspaceDragDrop>
}
```

Move the `Tree` JSX and `titleRender` into this component. Preserve CSS class names. Add `tabIndex={0}` to document nodes and droppable section nodes. Add `role="treeitem"` to the custom title div. Add `aria-grabbed={nodeData.nodeType === 'document' && draggingPlacementId === nodeData.placementId}` for document nodes.

- [ ] **Step 5: Replace inline tree render**

In `SequenceWorkspacePage.tsx`, keep `handleMovePlacement` and `handleDirectDrop` as page-level functions for now, pass them into `useWorkspaceDragDrop`, and render `WorkspaceTree`.

- [ ] **Step 6: Verify extraction**

Run:

```powershell
npm test -- src/pages/workspace/useWorkspaceDragDrop.test.tsx
npm test -- src/pages/SequenceWorkspacePage.validation.test.tsx
npm run lint
```

Working directory: `frontend`

Expected: drag/drop hook tests and page tests pass; lint stays clean.

## Task 6: Add Multi-File Drag/Drop and Accessibility Integration Tests

**Files:**
- Modify: `frontend/src/pages/SequenceWorkspacePage.validation.test.tsx`
- Modify: `frontend/src/pages/workspace/WorkspaceTree.tsx`
- Modify: `frontend/src/pages/workspace/useWorkspaceDragDrop.ts`

- [ ] **Step 1: Add page-level multi-file drop test**

In `SequenceWorkspacePage.validation.test.tsx`, add an integration test that drops two valid files and one invalid file on a leaf section. Assert two upload requests are made and the invalid file error is visible:

```ts
expect(uploadCalls.map((call) => call.fileName)).toEqual(['one.pdf', 'two.xml'])
expect(await screen.findByText(/Unsupported file extension/)).toBeInTheDocument()
```

- [ ] **Step 2: Add keyboard move test**

Add a test that renders one document node and one target section, focuses the document node, triggers the keyboard move path exposed by `WorkspaceTree`, and asserts the section move endpoint is called with the target section.

Use visible labels and roles where possible:

```ts
const documentNode = await screen.findByRole('treeitem', { name: /cover\.pdf/i })
documentNode.focus()
expect(documentNode).toHaveFocus()
```

- [ ] **Step 3: Implement any missing keyboard bridge**

If the Step 2 test fails because no keyboard bridge exists, add a minimal keyboard path in `WorkspaceTree`: when a document node has focus and Enter is pressed, mark it as the keyboard-selected placement; when a droppable section node has focus and Enter is pressed, call the same move handler used by drop.

- [ ] **Step 4: Verify integration behavior**

Run:

```powershell
npm test -- src/pages/SequenceWorkspacePage.validation.test.tsx
npm run lint
```

Working directory: `frontend`

Expected: all sequence workspace tests pass; lint stays clean.

## Task 7: Full Frontend Gate and Commit

**Files:**
- Modify/create all Task 6 files listed above.

- [ ] **Step 1: Run full frontend verification**

Run:

```powershell
npm run lint
npm run build
npm test
```

Working directory: `frontend`

Expected:
- `npm run lint`: exit 0
- `npm run build`: exit 0; Vite may keep the existing large chunk warning
- `npm test`: 107+ tests pass, including new Task 6 tests

- [ ] **Step 2: Inspect refactor boundary**

Run:

```powershell
rg -n "buildPrePublishChecklistSummary|titleRender|fetchPlacements|fetchDocuments|fetchEctdStructure|<Modal title=\"Publish Sequence\"" frontend/src/pages/SequenceWorkspacePage.tsx
```

Expected:
- No local definitions of `buildPrePublishChecklistSummary`, `fetchPlacements`, `fetchDocuments`, or `fetchEctdStructure`.
- No inline `titleRender` implementation.
- No inline publish modal JSX.
- Imports/usages of extracted modules are acceptable.

- [ ] **Step 3: Commit sequence workspace refactor**

Run:

```powershell
git add frontend\src docs\superpowers\plans\2026-07-01-sequence-workspace-refactor-execution.md
git commit -m "refactor: split sequence workspace page"
```

If the docs path is ignored, use:

```powershell
git add frontend\src
git add -f docs\superpowers\plans\2026-07-01-sequence-workspace-refactor-execution.md
git commit -m "refactor: split sequence workspace page"
```

Expected: commit succeeds and contains only the sequence workspace refactor, new focused tests, and this execution plan.

## Self-Review Notes

- Spec coverage: tasks cover pure checklist extraction, validation summary panel, publish modal, data hook with visible errors, drag/drop hook, workspace tree, multi-file drop, keyboard accessibility, and full gates.
- Placeholder scan: no step relies on unspecified future decisions; each new file has an explicit responsibility and verification command.
- Type consistency: extracted modules use existing `ValidationReport`, `DocumentPlacementRecord`, `DocumentRecord`, `EctdStructureNode`, and `WorkspaceTreeNode` types rather than redefining backend shapes.
