# Package Review JSON Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a frontend-only `Download Review JSON` action to the Package Review drawer that exports the currently loaded review state as `package-review-export-v1` JSON.

**Architecture:** Keep the feature inside `PackageReviewPanel` and reuse the report, artifacts, checklist, risk summary, and integrity findings already computed by the drawer. The export handler builds a plain object from current React state, serializes it, creates a browser `Blob`, and triggers a temporary anchor download without making any new API request.

**Tech Stack:** React, TypeScript, Ant Design, Vitest, jsdom, existing `apiFetch`/`ApiRequestError` helpers.

---

## File Structure

- Modify: `frontend/src/components/publishing/PackageReviewPanel.tsx`
  - Add `message` import from Ant Design.
  - Add small export helpers near existing helpers: `buildErrorExport`, `downloadJson`, and any local export types needed for clarity.
  - Add `handleDownloadReviewJson` inside `PackageReviewPanel` so it can use current state without prop drilling.
  - Add `Download Review JSON` to the existing header `Space` before `Download Package` and `Download Report`.
- Modify: `frontend/src/PublishHistoryDetail.test.tsx`
  - Add focused tests for full JSON export, partial export with artifact error, and disabled export when both data sources fail.
  - Reuse current app rendering helpers and publish-history fixtures.
  - Mock browser download primitives and inspect generated `Blob` content.

Do not create a backend endpoint. Do not modify backend services, controllers, DTOs, or persistence.

---

### Task 1: Full Review JSON Export

**Files:**
- Modify: `frontend/src/PublishHistoryDetail.test.tsx`
- Modify: `frontend/src/components/publishing/PackageReviewPanel.tsx`

- [ ] **Step 1: Add test helpers for JSON download capture**

In `frontend/src/PublishHistoryDetail.test.tsx`, add these helpers after `clickButtonByText` and before `expectControlDisabled`:

```tsx
const setupDownloadCapture = () => {
  const createdBlobs: Blob[] = []
  const clickedDownloads: string[] = []
  const originalCreateElement = document.createElement.bind(document)

  vi.stubGlobal('URL', {
    ...URL,
    createObjectURL: vi.fn((blob: Blob | MediaSource) => {
      createdBlobs.push(blob as Blob)
      return 'blob:package-review'
    }),
    revokeObjectURL: vi.fn(),
  })
  vi.spyOn(document, 'createElement').mockImplementation(((tagName: string, options?: ElementCreationOptions) => {
    const element = originalCreateElement(tagName, options)
    if (tagName.toLowerCase() === 'a') {
      vi.spyOn(element, 'click').mockImplementation(() => {
        clickedDownloads.push((element as HTMLAnchorElement).download)
      })
    }
    return element
  }) as typeof document.createElement)

  return { createdBlobs, clickedDownloads }
}

const readJsonBlob = async (blob: Blob) => JSON.parse(await blob.text())
```

- [ ] **Step 2: Write the failing full-export test**

In the same file, update the existing test named `shows a strict not-ready package review with checklist, evidence, and downloads`.

Add download capture immediately before rendering the app:

```tsx
    const { createdBlobs, clickedDownloads } = setupDownloadCapture()
```

Add `Download Review JSON` to the existing text expectation array, before `Download Package`:

```tsx
      'Download Review JSON',
      'Download Package',
      'Download Report',
```

Add these assertions after the existing checklist row assertions and before `unmount()`:

```tsx
    await clickButtonByText('Download Review JSON')

    expect(createdBlobs).toHaveLength(1)
    expect(clickedDownloads).toEqual(['package-review-0001-job-1.json'])
    const exportJson = await readJsonBlob(createdBlobs[0])
    expect(exportJson).toMatchObject({
      reportVersion: 'package-review-export-v1',
      publishJobId: 'job-1',
      sequenceNumber: '0001',
      validationProfile: 'US FDA eCTD 3.2.2',
      verdict: 'NotReadyForSubmission',
      riskSummary: {
        validationErrors: 1,
        warnings: 2,
        lifecycleIssues: 6,
        missingFiles: 1,
        missingZipEntries: 0,
        mismatchedArtifacts: 2,
      },
      requiredArtifacts: [
        { name: 'BackboneXml', exists: true, sizeBytes: 512, contentType: 'application/xml' },
        { name: 'PublishReport', exists: true, sizeBytes: 1024, contentType: 'application/json' },
        { name: 'PackageZip', exists: true, sizeBytes: 2048, contentType: 'application/zip' },
      ],
      integrityFindings: [
        {
          severity: 'Error',
          type: 'MissingZipEntry',
          path: 'm1/us/11-forms/leaf.pdf',
          message: 'Output file is missing from package zip.',
        },
      ],
    })
    expect(exportJson.generatedAtUtc).toEqual(expect.any(String))
    expect(exportJson.checklist).toEqual([
      { key: 'publish-succeeded', check: 'Publish succeeded', status: 'Pass', detail: 'Publish completed successfully.' },
      { key: 'validation-errors', check: 'Validation errors', status: 'Fail', detail: '1 error(s)' },
      { key: 'lifecycle-issues', check: 'Lifecycle issues', status: 'Fail', detail: '6 issue(s)' },
      { key: 'integrity-consistent', check: 'Integrity consistent', status: 'Fail', detail: 'Inconsistent or unavailable' },
      { key: 'required-artifacts-present', check: 'Required artifacts present', status: 'Pass', detail: '3/3 present' },
    ])
    expect(fetchMock).not.toHaveBeenCalledWith('/api/publish-jobs/job-1/artifacts/PackageZip/download', expect.anything())
    expect(fetchMock).not.toHaveBeenCalledWith('/api/publish-jobs/job-1/artifacts/PublishReport/download', expect.anything())
```

- [ ] **Step 3: Run the test to verify it fails**

Run from `E:\02_GitHub\RATools-for-eCTD\frontend`:

```powershell
npm test -- --run src/PublishHistoryDetail.test.tsx
```

Expected: FAIL because `Download Review JSON` is not rendered and no JSON blob is created.

- [ ] **Step 4: Implement the minimal full-export behavior**

In `frontend/src/components/publishing/PackageReviewPanel.tsx`, change the Ant Design import:

```tsx
import { Alert, Button, Card, Descriptions, Drawer, Space, Spin, Table, Tag, message } from 'antd'
```

Add this type after `ChecklistRow`:

```tsx
type ChecklistExportRow = {
  key: string
  check: string
  status: 'Pass' | 'Fail'
  detail: string
}
```

Add this helper after `hasArtifact`:

```tsx
const downloadJson = (filename: string, value: unknown) => {
  const blob = new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' })
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
```

Inside `PackageReviewPanel`, add these derived values after `riskSummaryItems`:

```tsx
  const reviewExportAvailable = reportLoaded || (!artifactsError && artifacts.length > 0)
  const checklistExportRows: ChecklistExportRow[] = checklistRows.map((row) => ({
    key: row.key,
    check: row.check,
    status: row.pass ? 'Pass' : 'Fail',
    detail: row.detail,
  }))
```

Add this handler after `renderError`:

```tsx
  const handleDownloadReviewJson = () => {
    if (!jobId || !reviewExportAvailable) return

    try {
      const sequenceNumber = report?.sequenceNumber ?? null
      const exportObject = {
        reportVersion: 'package-review-export-v1',
        generatedAtUtc: new Date().toISOString(),
        publishJobId: jobId,
        sequenceNumber,
        validationProfile: report?.validationProfile ?? null,
        verdict: readyForSubmission ? 'ReadyForSubmission' : 'NotReadyForSubmission',
        checklist: checklistExportRows,
        riskSummary: {
          validationErrors: report?.errorCount ?? null,
          warnings: report?.warningCount ?? null,
          lifecycleIssues: reportLoaded ? lifecycleIssueCount : null,
          missingFiles: report?.integritySummary?.missingFilesCount ?? null,
          missingZipEntries: report?.integritySummary?.missingZipEntriesCount ?? null,
          mismatchedArtifacts: report?.integritySummary?.mismatchedArtifactsCount ?? null,
        },
        requiredArtifacts: requiredArtifactRows.map((artifact) => ({
          name: artifact.name,
          exists: artifact.exists === true,
          sizeBytes: artifact.sizeBytes,
          contentType: artifact.contentType,
        })),
        integrityFindings: findings,
      }

      downloadJson(`package-review-${sequenceNumber || 'unknown'}-${jobId}.json`, exportObject)
    } catch {
      message.error('Failed to export package review.')
    }
  }
```

Add the new button at the start of the existing `<Space>` before `Download Package`:

```tsx
            <Button
              icon={<Download size={16} className="mr-1" />}
              onClick={handleDownloadReviewJson}
              disabled={!reviewExportAvailable}
            >
              Download Review JSON
            </Button>
```

- [ ] **Step 5: Run the focused test to verify it passes**

Run from `E:\02_GitHub\RATools-for-eCTD\frontend`:

```powershell
npm test -- --run src/PublishHistoryDetail.test.tsx
```

Expected: PASS for `PublishHistoryDetail.test.tsx`. Existing React/Ant Design warnings may still appear.

- [ ] **Step 6: Commit Task 1**

Run from repo root `E:\02_GitHub\RATools-for-eCTD`:

```powershell
git status --short
git diff -- frontend/src/components/publishing/PackageReviewPanel.tsx frontend/src/PublishHistoryDetail.test.tsx
git add frontend/src/components/publishing/PackageReviewPanel.tsx frontend/src/PublishHistoryDetail.test.tsx
git commit -m "feat(publishing): export package review json"
```

Expected: commit includes only the component and frontend test changes.

---

### Task 2: Partial Export Error Fields

**Files:**
- Modify: `frontend/src/PublishHistoryDetail.test.tsx`
- Modify: `frontend/src/components/publishing/PackageReviewPanel.tsx`

- [ ] **Step 1: Extend the artifact-failure test to export partial review JSON**

In `frontend/src/PublishHistoryDetail.test.tsx`, update the test named `keeps package review open and fails artifacts check when artifacts cannot load`.

Add download capture before rendering the app:

```tsx
    const { createdBlobs, clickedDownloads } = setupDownloadCapture()
```

Add these assertions after `expectControlDisabled('Download Report')` and before `unmount()`:

```tsx
    await clickButtonByText('Download Review JSON')

    expect(createdBlobs).toHaveLength(1)
    expect(clickedDownloads).toEqual(['package-review-0001-job-1.json'])
    const exportJson = await readJsonBlob(createdBlobs[0])
    expect(exportJson.reportVersion).toBe('package-review-export-v1')
    expect(exportJson.verdict).toBe('NotReadyForSubmission')
    expect(exportJson.requiredArtifacts).toEqual([
      { name: 'BackboneXml', exists: false },
      { name: 'PublishReport', exists: false },
      { name: 'PackageZip', exists: false },
    ])
    expect(exportJson.errors).toEqual({
      artifacts: {
        message: 'Artifacts unavailable.',
        status: 410,
      },
    })
```

- [ ] **Step 2: Run the test to verify it fails**

Run from `E:\02_GitHub\RATools-for-eCTD\frontend`:

```powershell
npm test -- --run src/PublishHistoryDetail.test.tsx
```

Expected: FAIL because the export JSON does not include `errors.artifacts` yet.

- [ ] **Step 3: Add error export helpers**

In `frontend/src/components/publishing/PackageReviewPanel.tsx`, add this type after `ChecklistExportRow`:

```tsx
type ReviewExportError = {
  message: string
  status?: number
}
```

Add this helper after `downloadJson`:

```tsx
const buildErrorExport = (error: Error | null): ReviewExportError | undefined => {
  if (!error) return undefined

  return error instanceof ApiRequestError
    ? { message: error.message, status: error.status }
    : { message: error.message }
}
```

Inside `handleDownloadReviewJson`, add `errors` before `exportObject`:

```tsx
      const errors = {
        report: buildErrorExport(reportError),
        artifacts: buildErrorExport(artifactsError),
      }
```

Then add an `errors` property to `exportObject` after `integrityFindings`:

```tsx
        ...(errors.report || errors.artifacts ? { errors } : {}),
```

The completed tail of `exportObject` should be:

```tsx
        requiredArtifacts: requiredArtifactRows.map((artifact) => ({
          name: artifact.name,
          exists: artifact.exists === true,
          sizeBytes: artifact.sizeBytes,
          contentType: artifact.contentType,
        })),
        integrityFindings: findings,
        ...(errors.report || errors.artifacts ? { errors } : {}),
```

- [ ] **Step 4: Run the focused test to verify it passes**

Run from `E:\02_GitHub\RATools-for-eCTD\frontend`:

```powershell
npm test -- --run src/PublishHistoryDetail.test.tsx
```

Expected: PASS for `PublishHistoryDetail.test.tsx`. Existing React/Ant Design warnings may still appear.

- [ ] **Step 5: Commit Task 2**

Run from repo root `E:\02_GitHub\RATools-for-eCTD`:

```powershell
git status --short
git diff -- frontend/src/components/publishing/PackageReviewPanel.tsx frontend/src/PublishHistoryDetail.test.tsx
git add frontend/src/components/publishing/PackageReviewPanel.tsx frontend/src/PublishHistoryDetail.test.tsx
git commit -m "fix(publishing): include package review export errors"
```

Expected: commit includes only the partial-export error test and component error export changes.

---

### Task 3: Disabled State When Both Sources Fail

**Files:**
- Modify: `frontend/src/PublishHistoryDetail.test.tsx`
- Modify: `frontend/src/components/publishing/PackageReviewPanel.tsx` only if the test exposes a bug

- [ ] **Step 1: Add the both-unavailable disabled-state test**

In `frontend/src/PublishHistoryDetail.test.tsx`, add this test after `keeps package review open and fails artifacts check when artifacts cannot load`:

```tsx
  it('disables review json export when report and artifacts cannot load', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => {
      if (url === '/health') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ status: 'ok' }) })
      }

      if (url === '/api/applications') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([
          {
            id: 'app-1',
            applicationNumber: 'APP-1',
            sponsorName: 'Sponsor',
            ectdTemplateKey: 'us-fda-ectd-3.2.2',
            ectdTemplateDisplayName: 'US FDA eCTD 3.2.2',
            createdUtc: '2024-01-01T00:00:00Z',
            sequences: [],
          },
        ]) })
      }

      if (String(url).startsWith('/api/applications/app-1/publish-history?')) {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(publishHistoryResponse) })
      }

      if (url === '/api/publish-jobs/job-1/report') {
        return Promise.resolve({ ok: false, status: 422, json: vi.fn().mockResolvedValue({ message: 'Publish report is corrupted.' }) })
      }

      if (url === '/api/publish-jobs/job-1/artifacts') {
        return Promise.resolve({ ok: false, status: 410, json: vi.fn().mockResolvedValue({ message: 'Artifacts unavailable.' }) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { createdBlobs } = setupDownloadCapture()
    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('Manage App')
    await clickByText('Publish History')
    await clickButtonByText('Review')
    await flushPromises()

    expect(document.body.textContent).toContain('Package Review')
    expect(document.body.textContent).toContain('Publish report is corrupted.')
    expect(document.body.textContent).toContain('Artifacts unavailable.')
    expectControlDisabled('Download Review JSON')
    expect(createdBlobs).toHaveLength(0)

    unmount()
  })
```

- [ ] **Step 2: Run the test to verify the current implementation**

Run from `E:\02_GitHub\RATools-for-eCTD\frontend`:

```powershell
npm test -- --run src/PublishHistoryDetail.test.tsx
```

Expected: PASS if `reviewExportAvailable` is already correct. If it fails because the button is enabled, continue to Step 3. If it passes, skip Step 3 and continue to Step 4.

- [ ] **Step 3: Fix disabled-state logic if needed**

In `frontend/src/components/publishing/PackageReviewPanel.tsx`, ensure the export availability expression is exactly:

```tsx
  const reviewExportAvailable = reportLoaded || (!artifactsError && artifacts.length > 0)
```

This keeps the button disabled when both `reportError` and `artifactsError` are present, and enabled when either the report loaded or at least one normalized artifact row loaded.

- [ ] **Step 4: Run the focused frontend test again**

Run from `E:\02_GitHub\RATools-for-eCTD\frontend`:

```powershell
npm test -- --run src/PublishHistoryDetail.test.tsx
```

Expected: PASS for `PublishHistoryDetail.test.tsx`. Existing React/Ant Design warnings may still appear.

- [ ] **Step 5: Commit Task 3**

Run from repo root `E:\02_GitHub\RATools-for-eCTD`:

```powershell
git status --short
git diff -- frontend/src/components/publishing/PackageReviewPanel.tsx frontend/src/PublishHistoryDetail.test.tsx
git add frontend/src/components/publishing/PackageReviewPanel.tsx frontend/src/PublishHistoryDetail.test.tsx
git commit -m "test(publishing): cover unavailable review export"
```

Expected: commit includes the disabled-state test and any required component fix.

---

### Task 4: Final Verification

**Files:**
- Verify: `frontend/src/components/publishing/PackageReviewPanel.tsx`
- Verify: `frontend/src/PublishHistoryDetail.test.tsx`

- [ ] **Step 1: Run the full frontend test suite**

Run from `E:\02_GitHub\RATools-for-eCTD\frontend`:

```powershell
npm test
```

Expected: PASS for all frontend tests. Existing warnings about React `act`, Ant Design deprecated props, or Vite chunk size are acceptable only if there are zero test failures.

- [ ] **Step 2: Run the frontend production build**

Run from `E:\02_GitHub\RATools-for-eCTD\frontend`:

```powershell
npm run build
```

Expected: exit code 0. Existing Vite chunk-size warning is acceptable.

- [ ] **Step 3: Inspect the final diff and commits**

Run from repo root `E:\02_GitHub\RATools-for-eCTD`:

```powershell
git status --short
git log --oneline -5
```

Expected: no unstaged or staged tracked changes. Recent commits include the JSON export implementation commits.

- [ ] **Step 4: Report verification evidence**

Report the exact commands run and whether each passed. Include any warnings that appeared but did not fail the command.

---

## Spec Coverage Checklist

- `Download Review JSON` in the Package Review header: Task 1.
- Uses currently loaded report/artifact data and performs no API request: Task 1 test assertion and implementation.
- Existing `Download Package` and `Download Report` remain: Task 1 keeps current buttons and tests existing labels.
- Full export fields: Task 1.
- Partial export with `errors.artifacts`: Task 2.
- Disabled when report and artifacts both fail: Task 3.
- JSON generation failure message: Task 1 implementation wraps export in `try/catch` with `message.error('Failed to export package review.')`.
- Frontend tests in `frontend/src/PublishHistoryDetail.test.tsx`: Tasks 1-3.
- No PDF, Markdown, persistence, signature, API endpoint, upload, or archive behavior: File structure and implementation notes restrict changes to frontend component/test only.
