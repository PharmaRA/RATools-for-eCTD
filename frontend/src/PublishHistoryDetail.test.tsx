import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it, vi } from 'vitest'

import App from './App'

const flushPromises = async () => {
  await act(async () => {
    await Promise.resolve()
  })
}

const waitForElement = async (getElement: () => HTMLElement | undefined, label: string) => {
  for (let attempt = 0; attempt < 10; attempt += 1) {
    await flushPromises()
    const element = getElement()
    if (element) return element
  }

  throw new Error(`Could not find ${label}`)
}

const renderApp = () => {
  window.history.replaceState(null, '', '/')

  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)

  act(() => {
    root.render(<App />)
  })

  return {
    unmount() {
      act(() => {
        root.unmount()
      })
      container.remove()
    },
  }
}

const clickByText = async (text: string) => {
  const element = await waitForElement(
    () => Array.from(document.querySelectorAll('button, [role="tab"], .ant-tabs-tab-btn')).find((candidate) => candidate.textContent?.includes(text)) as HTMLElement | undefined,
    `control with text ${text}`,
  )

  act(() => {
    element.click()
  })
}

const clickButtonByText = async (text: string) => {
  const button = await waitForElement(
    () => Array.from(document.querySelectorAll('button')).find((candidate) => candidate.textContent?.trim() === text) as HTMLElement | undefined,
    `button with text ${text}`,
  )

  act(() => {
    button.click()
  })
}

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

const readJsonBlob = async (blob: Blob) => {
  const text = await new Promise<string>((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error)
    reader.readAsText(blob)
  })

  return JSON.parse(text)
}

const expectControlDisabled = (text: string) => {
  const control = Array.from(document.querySelectorAll('button, a')).find((candidate) => candidate.textContent?.trim() === text) as HTMLElement | undefined
  expect(control).toBeTruthy()
  expect(
    control?.hasAttribute('disabled')
    || control?.getAttribute('aria-disabled') === 'true'
    || control?.classList.contains('ant-btn-disabled'),
  ).toBe(true)
}

const expectDescriptionItem = (label: string, expectedValue: string) => {
  const labelCell = Array.from(document.querySelectorAll('.ant-descriptions-item-label')).find((candidate) => candidate.textContent?.trim() === label)
  expect(labelCell).toBeTruthy()
  expect(labelCell?.nextElementSibling?.textContent?.trim()).toBe(expectedValue)
}

const normalizeText = (value?: string | null) => value?.replace(/\s+/g, ' ').trim() || ''

const getLifecycleTable = () => {
  const tables = Array.from(document.querySelectorAll('.ant-table'))
  const table = tables.find((candidate) => {
    const headers = Array.from(candidate.querySelectorAll('thead th')).map((header) => normalizeText(header.textContent))
    return headers.includes('Document ID') && headers.includes('Attempted Strategies')
  })
  expect(table).toBeTruthy()
  return table as HTMLElement
}

const getLifecycleColumnIndex = (columnTitle: string) => {
  const headers = Array.from(getLifecycleTable().querySelectorAll('thead th')).map((header) => normalizeText(header.textContent))
  const index = headers.findIndex((header) => header === columnTitle)
  expect(index).toBeGreaterThanOrEqual(0)
  return index
}

const getLifecycleRowByDocumentId = (documentId: string) => {
  const table = getLifecycleTable()
  const documentIdIndex = getLifecycleColumnIndex('Document ID')
  const row = Array.from(table.querySelectorAll('tbody tr.ant-table-row')).find((candidate) => {
    const cells = Array.from(candidate.querySelectorAll('td')).map((cell) => normalizeText(cell.textContent))
    return cells[documentIdIndex] === documentId
  })
  expect(row).toBeTruthy()
  return row as HTMLElement
}

const expectLifecycleCell = (documentId: string, columnTitle: string, expectedValue: string) => {
  const row = getLifecycleRowByDocumentId(documentId)
  const columnIndex = getLifecycleColumnIndex(columnTitle)
  const cells = Array.from(row.querySelectorAll('td')).map((cell) => normalizeText(cell.textContent))
  expect(cells[columnIndex]).toBe(expectedValue)
}

const expectReviewChecklistRow = (label: string, expectedStatus: string) => {
  const checklistCard = Array.from(document.querySelectorAll('.ant-card')).find((candidate) => normalizeText(candidate.textContent).includes('Submission Readiness Checklist'))
  expect(checklistCard).toBeTruthy()
  const row = Array.from(checklistCard!.querySelectorAll('tbody tr')).find((candidate) => normalizeText(candidate.textContent).includes(label))
  expect(row).toBeTruthy()
  const cells = Array.from(row!.querySelectorAll('td')).map((cell) => normalizeText(cell.textContent))
  expect(cells[1]).toBe(expectedStatus)
}

const publishHistoryResponse = {
  applicationId: 'app-1',
  applicationNumber: 'APP-1',
  sponsorName: 'Sponsor',
  page: 1,
  pageSize: 20,
  totalCount: 1,
  statusSummary: {
    completedCount: 2,
    failedCount: 1,
    runningCount: 0,
  },
  lifecycleSummary: {
    matchedCount: 4,
    replaceTargetNotFoundCount: 1,
    deleteTargetNotFoundCount: 0,
    appendTargetNotFoundCount: 1,
    ambiguousCount: 1,
    currentSequenceCount: 0,
  },
  entries: [
    {
      publishJobId: 'job-1',
      sequenceNumber: '0001',
      status: 'Completed',
      createdUtc: '2024-01-02T12:00:00Z',
      completedUtc: '2024-01-02T12:05:00Z',
      reportAvailable: true,
      reportReadable: true,
      reportError: null,
      validationProfile: 'US FDA eCTD 3.2.2',
      errorCount: 1,
      warningCount: 2,
      warningSummary: 'Leaf title missing in one section',
      lifecycleSummary: {
        matchedCount: 2,
        replaceTargetNotFoundCount: 1,
        deleteTargetNotFoundCount: 0,
        appendTargetNotFoundCount: 0,
        ambiguousCount: 1,
        currentSequenceCount: 0,
      },
      lifecycleMatches: [],
      artifactSummary: {
        fileCount: 7,
        totalSizeBytes: 4096,
        packageSizeBytes: 2048,
      },
      reportPath: 'E:/exports/report.json',
      packagePath: 'E:/exports/output.zip',
    },
  ],
}

const publishReportResponse = {
  succeeded: true,
  sequenceNumber: '0001',
  message: 'Publish completed successfully.',
  validationProfile: 'US FDA eCTD 3.2.2',
  durationMs: 1534,
  errorCount: 1,
  warningCount: 2,
  integritySummary: {
    isConsistent: false,
    missingFilesCount: 1,
    missingZipEntriesCount: 0,
    mismatchedArtifactsCount: 2,
  },
  integrityEvidence: {
    findings: [
      { severity: 'Error', type: 'MissingZipEntry', path: 'm1/us/11-forms/leaf.pdf', message: 'Output file is missing from package zip.' },
    ],
    artifacts: [
      { role: 'BackboneXml', relativePath: 'index.xml', path: 'E:/exports/index.xml', exists: true, sizeBytes: 512, zipEntryPresent: true, source: 'TopLevelArtifact' },
      { role: 'OutputFile', relativePath: 'm1/us/11-forms/leaf.pdf', path: 'E:/exports/m1/us/11-forms/leaf.pdf', exists: true, sizeBytes: 2048, zipEntryPresent: false, source: 'OutputDirectory' },
    ],
  },
  artifactSummary: {
    fileCount: 7,
    totalSizeBytes: 4096,
    packageSizeBytes: 2048,
  },
  auditSummary: {
    publishJobEventCount: 3,
    validationEventCount: 2,
    latestPublishJobAction: 'Completed',
    latestPublishJobEventUtc: '2024-01-02T12:05:00Z',
  },
  publishReadiness: {
    isReady: false,
    status: 'Blocked',
    blockingErrorCount: 1,
    warningCount: 0,
    missingMetadataFields: ['ApplicantContactName'],
    categorySummaries: [
      {
        category: 'RegionalMetadata',
        blockingErrorCount: 1,
        warningCount: 0,
        findingCount: 1,
      },
    ],
    findings: [
      {
        source: 'PublishPreflight',
        severity: 'Error',
        code: 'US_REGIONAL_METADATA_MISSING',
        message: "metadata field 'ApplicantContactName' is required.",
        category: 'RegionalMetadata',
        recommendedAction: 'Populate the required US Regional publishing metadata field before publishing.',
        fieldName: 'ApplicantContactName',
        sectionPath: null,
        documentId: null,
        placementId: null,
      },
    ],
  },
  validationReport: {
    issues: [
      { severity: 'Error', code: 'ERR-1', message: 'One validation error.' },
    ],
    lifecycleMatches: [
      {
        operation: 'Replace',
        sequenceNumber: '0001',
        ctdSection: '1.2.3',
        documentId: 'doc-1',
        resultCode: 'REPLACE_TARGET_NOT_FOUND',
        matchStrategy: 'by-file-name',
        attemptedStrategies: ['by-file-name'],
        historicalMatchCount: 0,
        historicalSequenceNumbers: [],
        historicalPlacementIds: [],
        historicalFinalState: 'Missing',
      },
      {
        operation: 'Append',
        sequenceNumber: '0001',
        ctdSection: '1.3.5',
        documentId: 'doc-2',
        resultCode: 'APPEND_TARGET_NOT_FOUND',
        matchStrategy: 'explicit-placement-id',
        attemptedStrategies: ['explicit-placement-id', 'document-id'],
        historicalMatchCount: 0,
        historicalSequenceNumbers: [],
        historicalPlacementIds: [],
        historicalFinalState: 'Missing',
      },
      {
        operation: 'Delete',
        sequenceNumber: '0001',
        ctdSection: '1.3.7',
        documentId: 'doc-delete',
        resultCode: 'DELETE_TARGET_NOT_FOUND',
        matchStrategy: 'document-id',
        attemptedStrategies: ['document-id'],
        historicalMatchCount: 0,
        historicalSequenceNumbers: [],
        historicalPlacementIds: [],
        historicalFinalState: 'Missing',
      },
      {
        operation: 'Replace',
        sequenceNumber: '0001',
        ctdSection: '1.4.1',
        documentId: 'doc-3',
        resultCode: 'LIFECYCLE_TARGET_AMBIGUOUS',
        matchStrategy: 'document-id',
        attemptedStrategies: ['document-id'],
        historicalMatchCount: 2,
        historicalSequenceNumbers: ['0000', '0001'],
        historicalPlacementIds: ['placement-1', 'placement-2'],
        historicalFinalState: 'Current',
      },
      {
        operation: 'Delete',
        sequenceNumber: '0001',
        ctdSection: '1.5.1',
        documentId: 'doc-4',
        resultCode: 'LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE',
        matchStrategy: 'document-id',
        attemptedStrategies: ['document-id'],
        historicalMatchCount: 1,
        historicalSequenceNumbers: ['0001'],
        historicalPlacementIds: ['placement-3'],
        historicalFinalState: 'Current',
      },
      {
        operation: 'Replace',
        sequenceNumber: '0001',
        ctdSection: '1.6.1',
        documentId: 'doc-5',
        resultCode: 'MATCHED',
        matchStrategy: 'document-id',
        attemptedStrategies: ['document-id'],
        historicalMatchCount: 1,
        historicalSequenceNumbers: ['0000'],
        historicalPlacementIds: ['placement-4'],
        historicalFinalState: 'Superseded',
      },
      {
        operation: 'Replace',
        sequenceNumber: '0001',
        ctdSection: '1.7.1',
        documentId: 'doc-invalid',
        resultCode: 'LIFECYCLE_TARGET_INVALID',
        matchStrategy: 'explicit-placement-id',
        attemptedStrategies: ['explicit-placement-id'],
        historicalMatchCount: 1,
        historicalSequenceNumbers: ['0000'],
        historicalPlacementIds: ['placement-invalid'],
        historicalFinalState: 'Invalid',
      },
    ],
  },
}

const publishArtifactsResponse = {
  artifacts: [
    { name: 'BackboneXml', type: 'file', path: 'E:/exports/index.xml', exists: true, sizeBytes: 512, contentType: 'application/xml' },
    { name: 'PublishReport', type: 'file', path: 'E:/exports/publish-report.json', exists: true, sizeBytes: 1024, contentType: 'application/json' },
    { name: 'PackageZip', type: 'file', path: 'E:/exports/package.zip', exists: true, sizeBytes: 2048, contentType: 'application/zip' },
  ],
}

const readyPublishReportResponse = {
  ...publishReportResponse,
  errorCount: 0,
  warningCount: 1,
  publishReadiness: {
    isReady: true,
    status: 'Ready',
    blockingErrorCount: 0,
    warningCount: 1,
    missingMetadataFields: [],
    categorySummaries: [
      {
        category: 'Validation',
        blockingErrorCount: 0,
        warningCount: 1,
        findingCount: 1,
      },
    ],
    findings: [
      {
        source: 'Validation',
        severity: 'Warning',
        code: 'TITLE_FALLBACK_USED',
        message: 'Placement has no explicit title, so the file name will be used.',
        category: 'Validation',
        recommendedAction: 'Resolve the validation issue before publishing.',
        fieldName: null,
        sectionPath: null,
        documentId: null,
        placementId: null,
      },
    ],
  },
  integritySummary: {
    isConsistent: true,
    missingFilesCount: 0,
    missingZipEntriesCount: 0,
    mismatchedArtifactsCount: 0,
  },
  integrityEvidence: {
    findings: [],
    artifacts: [
      { role: 'BackboneXml', relativePath: 'index.xml', path: 'E:/exports/index.xml', exists: true, sizeBytes: 512, zipEntryPresent: true, source: 'TopLevelArtifact' },
      { role: 'OutputFile', relativePath: 'm1/us/11-forms/leaf.pdf', path: 'E:/exports/m1/us/11-forms/leaf.pdf', exists: true, sizeBytes: 2048, zipEntryPresent: true, source: 'OutputDirectory' },
    ],
  },
  validationReport: {
    ...publishReportResponse.validationReport,
    issues: [
      { severity: 'Warning', code: 'WARN-1', message: 'One warning remains.' },
    ],
    lifecycleMatches: [
      {
        operation: 'Replace',
        sequenceNumber: '0001',
        ctdSection: '1.6.1',
        documentId: 'doc-ready',
        resultCode: 'MATCHED',
        matchStrategy: 'document-id',
        attemptedStrategies: ['document-id'],
        historicalMatchCount: 1,
        historicalSequenceNumbers: ['0000'],
        historicalPlacementIds: ['placement-ready'],
        historicalFinalState: 'Superseded',
      },
    ],
  },
}

describe('Publish history detail frontend', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    vi.useRealTimers()
  })

  it('shows summary-first lifecycle, validation, artifact, and report information in publish history', async () => {
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

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('Manage App')
    await clickByText('Publish History')
    await flushPromises()

    expect(document.body.textContent).toContain('Matched')
    expect(document.body.textContent).toContain('Replace Missing')
    expect(document.body.textContent).toContain('Append Missing')
    expect(document.body.textContent).toContain('Validation')
    expect(document.body.textContent).toContain('Errors: 1')
    expect(document.body.textContent).toContain('Warnings: 2')
    expect(document.body.textContent).toContain('Leaf title missing in one section')
    expect(document.body.textContent).toContain('Artifacts')
    expect(document.body.textContent).toContain('7 files')
    expect(document.body.textContent).toContain('Report')
    expect(document.body.textContent).toContain('Available')

    unmount()
  })

  it('shows integrity, artifact, audit, and lifecycle details in the publish report drawer', async () => {
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
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(publishReportResponse) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('Manage App')
    await clickByText('Publish History')
    await clickButtonByText('Report')
    await flushPromises()

    expect(document.body.textContent).toContain('Publish Succeeded')
    expect(document.body.textContent).toContain('Publish completed successfully.')
    expect(document.body.textContent).toContain('Profile')
    expect(document.body.textContent).toContain('US FDA eCTD 3.2.2')
    expect(document.body.textContent).toContain('Duration')
    expect(document.body.textContent).toContain('1534 ms')
    expect(document.body.textContent).toContain('Errors')
    expect(document.body.textContent).toContain('Warnings')
    expect(document.body.textContent).toContain('Lifecycle Issues')
    expectDescriptionItem('Lifecycle Issues', '6')
    expect(document.body.textContent).toContain('Integrity')
    expectDescriptionItem('Integrity', 'Inconsistent')
    expect(document.body.textContent).toContain('Replace Missing')
    expectDescriptionItem('Matched', '1')
    expectDescriptionItem('Replace Missing', '1')
    expectDescriptionItem('Delete Missing', '1')
    expectDescriptionItem('Append Missing', '1')
    expectDescriptionItem('Ambiguous', '1')
    expectDescriptionItem('Current Sequence', '1')
    expect(document.body.textContent).toContain('Delete Missing')
    expect(document.body.textContent).toContain('Append Missing')
    expect(document.body.textContent).toContain('Ambiguous')
    expect(document.body.textContent).toContain('Current Sequence')
    expect(document.body.textContent).toContain('Integrity Summary')
    expect(document.body.textContent).toContain('Consistent')
    expect(document.body.textContent).toContain('Missing Files')
    expect(document.body.textContent).toContain('Mismatched Artifacts')
    expect(document.body.textContent).toContain('Artifact Summary')
    expect(document.body.textContent).toContain('Package Size')
    expect(document.body.textContent).toContain('Audit Summary')
    expect(document.body.textContent).toContain('Latest Action')
    expect(document.body.textContent).toContain('Lifecycle')
    expect(document.body.textContent).toContain('REPLACE_TARGET_NOT_FOUND')
    expect(document.body.textContent).toContain('DELETE_TARGET_NOT_FOUND')
    expect(document.body.textContent).toContain('LIFECYCLE_TARGET_INVALID')
    expect(document.body.textContent).toContain('by-file-name')
    expect(document.body.textContent).toContain('Sequence')
    expect(document.body.textContent).toContain('Historical Matches')
    expect(document.body.textContent).toContain('Historical Sequences')
    expect(document.body.textContent).toContain('Document ID')
    expect(document.body.textContent).toContain('Attempted Strategies')
    expect(document.body.textContent).toContain('Historical Placement IDs')
    expect(document.body.textContent).toContain('Final State')
    expectLifecycleCell('doc-2', 'Operation', 'Append')
    expectLifecycleCell('doc-2', 'Sequence', '0001')
    expectLifecycleCell('doc-2', 'CTD Section', '1.3.5')
    expectLifecycleCell('doc-2', 'Result Code', 'APPEND_TARGET_NOT_FOUND')
    expectLifecycleCell('doc-2', 'Match Strategy', 'explicit-placement-id')
    expectLifecycleCell('doc-2', 'Attempted Strategies', 'explicit-placement-id, document-id')
    expectLifecycleCell('doc-2', 'Historical Matches', '0')
    expectLifecycleCell('doc-2', 'Historical Sequences', '-')
    expectLifecycleCell('doc-2', 'Historical Placement IDs', '-')
    expectLifecycleCell('doc-2', 'Final State', 'Missing')
    expectLifecycleCell('doc-3', 'Operation', 'Replace')
    expectLifecycleCell('doc-3', 'Sequence', '0001')
    expectLifecycleCell('doc-3', 'CTD Section', '1.4.1')
    expectLifecycleCell('doc-3', 'Result Code', 'LIFECYCLE_TARGET_AMBIGUOUS')
    expectLifecycleCell('doc-3', 'Match Strategy', 'document-id')
    expectLifecycleCell('doc-3', 'Attempted Strategies', 'document-id')
    expectLifecycleCell('doc-3', 'Historical Matches', '2')
    expectLifecycleCell('doc-3', 'Historical Sequences', '0000, 0001')
    expectLifecycleCell('doc-3', 'Historical Placement IDs', 'placement-1, placement-2')
    expectLifecycleCell('doc-3', 'Final State', 'Current')
    await clickByText('Evidence')
    expect(document.body.textContent).toContain('Integrity Findings')
    expect(document.body.textContent).toContain('MissingZipEntry')
    expect(document.body.textContent).toContain('m1/us/11-forms/leaf.pdf')
    expect(document.body.textContent).toContain('Output file is missing from package zip.')
    expect(document.body.textContent).toContain('Artifact Manifest')
    expect(document.body.textContent).toContain('BackboneXml')
    expect(document.body.textContent).toContain('OutputFile')
    expect(document.body.textContent).toContain('2 KB')
    expect(document.body.textContent).toContain('Missing from zip')

    unmount()
  })

  it('shows a strict not-ready package review with checklist, evidence, and downloads', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) => {
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
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(publishReportResponse) })
      }

      if (url === '/api/publish-jobs/job-1/artifacts') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(publishArtifactsResponse) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    })
    vi.stubGlobal('fetch', fetchMock)

    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('Manage App')
    await clickByText('Publish History')
    await clickButtonByText('Review')
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith('/api/publish-jobs/job-1/report', expect.anything())
    expect(fetchMock).toHaveBeenCalledWith('/api/publish-jobs/job-1/artifacts', expect.anything())

    for (const text of [
      'Package Review',
      'Not Ready for Submission',
      'Submission Readiness Checklist',
      'Publish Readiness Snapshot',
      'ApplicantContactName',
      'Populate the required US Regional publishing metadata field before publishing.',
      'Publish succeeded',
      'Validation errors',
      'Lifecycle issues',
      'Integrity consistent',
      'Required artifacts present',
      'Risk Summary',
      'MissingZipEntry',
      'Output file is missing from package zip.',
      'Required Artifacts',
      'BackboneXml',
      'PublishReport',
      'PackageZip',
      'Download Review JSON',
      'Download Package',
      'Download Report',
    ]) {
      expect(document.body.textContent).toContain(text)
    }
    expectReviewChecklistRow('Publish succeeded', 'Pass')
    expectReviewChecklistRow('Validation errors', 'Fail')
    expectReviewChecklistRow('Lifecycle issues', 'Fail')
    expectReviewChecklistRow('Integrity consistent', 'Fail')
    expectReviewChecklistRow('Required artifacts present', 'Pass')
    const { createdBlobs, clickedDownloads } = setupDownloadCapture()
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
      publishReadiness: {
        isReady: false,
        status: 'Blocked',
        blockingErrorCount: 1,
        warningCount: 0,
        missingMetadataFields: ['ApplicantContactName'],
        categorySummaries: [
          {
            category: 'RegionalMetadata',
            blockingErrorCount: 1,
            warningCount: 0,
            findingCount: 1,
          },
        ],
        findings: [
          {
            severity: 'Error',
            code: 'US_REGIONAL_METADATA_MISSING',
            category: 'RegionalMetadata',
            fieldName: 'ApplicantContactName',
            recommendedAction: 'Populate the required US Regional publishing metadata field before publishing.',
          },
        ],
      },
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

    unmount()
  })

  it('shows ready for submission when every strict package review check passes', async () => {
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
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(readyPublishReportResponse) })
      }

      if (url === '/api/publish-jobs/job-1/artifacts') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(publishArtifactsResponse) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('Manage App')
    await clickByText('Publish History')
    await clickButtonByText('Review')
    await flushPromises()

    expect(document.body.textContent).toContain('Ready for Submission')
    expect(document.body.textContent).toContain('Warnings do not block readiness')
    expect(document.body.textContent).toContain('Publish Readiness Snapshot')
    expect(document.body.textContent).toContain('Ready')
    expect(document.body.textContent).toContain('TITLE_FALLBACK_USED')
    expect(document.body.textContent).toContain('No integrity findings were recorded')
    expectReviewChecklistRow('Publish succeeded', 'Pass')
    expectReviewChecklistRow('Validation errors', 'Pass')
    expectReviewChecklistRow('Lifecycle issues', 'Pass')
    expectReviewChecklistRow('Integrity consistent', 'Pass')
    expectReviewChecklistRow('Required artifacts present', 'Pass')

    unmount()
  })

  it('keeps package review open and fails artifacts check when artifacts cannot load', async () => {
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
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(readyPublishReportResponse) })
      }

      if (url === '/api/publish-jobs/job-1/artifacts') {
        return Promise.resolve({ ok: false, status: 410, json: vi.fn().mockResolvedValue({ message: 'Artifacts unavailable.' }) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('Manage App')
    await clickByText('Publish History')
    await clickButtonByText('Review')
    await flushPromises()

    expect(document.body.textContent).toContain('Package Review')
    expect(document.body.textContent).toContain('Not Ready for Submission')
    expect(document.body.textContent).toContain('Artifacts unavailable.')
    expect(document.body.textContent).toContain('Required artifacts present')
    expectReviewChecklistRow('Required artifacts present', 'Fail')
    expectControlDisabled('Download Package')
    expectControlDisabled('Download Report')
    const { createdBlobs, clickedDownloads } = setupDownloadCapture()
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

    unmount()
  })

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

    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('Manage App')
    await clickByText('Publish History')
    await clickButtonByText('Review')
    await flushPromises()

    const { createdBlobs } = setupDownloadCapture()

    expect(document.body.textContent).toContain('Package Review')
    expect(document.body.textContent).toContain('Publish report is corrupted.')
    expect(document.body.textContent).toContain('Artifacts unavailable.')
    expectControlDisabled('Download Review JSON')
    expect(createdBlobs).toHaveLength(0)

    unmount()
  })

  it('exports review json when only an empty artifact list is available', async () => {
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
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({ artifacts: [] }) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('Manage App')
    await clickByText('Publish History')
    await clickButtonByText('Review')
    await flushPromises()

    const { createdBlobs, clickedDownloads } = setupDownloadCapture()
    await clickButtonByText('Download Review JSON')

    expect(createdBlobs).toHaveLength(1)
    expect(clickedDownloads).toEqual(['package-review-unknown-job-1.json'])
    const exportJson = await readJsonBlob(createdBlobs[0])
    expect(exportJson).toMatchObject({
      reportVersion: 'package-review-export-v1',
      publishJobId: 'job-1',
      sequenceNumber: null,
      validationProfile: null,
      verdict: 'NotReadyForSubmission',
      riskSummary: {
        validationErrors: null,
        warnings: null,
        lifecycleIssues: null,
        missingFiles: null,
        missingZipEntries: null,
        mismatchedArtifacts: null,
      },
      requiredArtifacts: [
        { name: 'BackboneXml', exists: false },
        { name: 'PublishReport', exists: false },
        { name: 'PackageZip', exists: false },
      ],
      integrityFindings: [],
    })

    unmount()
  })

  it('handles malformed artifact rows without crashing the package review', async () => {
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
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(readyPublishReportResponse) })
      }

      if (url === '/api/publish-jobs/job-1/artifacts') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({
          artifacts: [
            null,
            { name: 'PublishReport', type: 'file', path: 'E:/exports/publish-report.json', exists: true, sizeBytes: 1024, contentType: 'application/json' },
            { name: 'PackageZip', type: 'file', path: 'E:/exports/package.zip', exists: false, contentType: 'application/zip' },
          ],
        }) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('Manage App')
    await clickByText('Publish History')
    await clickButtonByText('Review')
    await flushPromises()

    expect(document.body.textContent).toContain('Package Review')
    expect(document.body.textContent).toContain('Not Ready for Submission')
    expect(document.body.textContent).toContain('Required Artifacts')
    expectReviewChecklistRow('Required artifacts present', 'Fail')
    expectControlDisabled('Download Package')

    unmount()
  })

  it('shows old-report compatibility message when integrity evidence is absent', async () => {
    const { integrityEvidence: _, ...oldReportResponse } = publishReportResponse

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
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(oldReportResponse) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('Manage App')
    await clickByText('Publish History')
    await clickButtonByText('Report')
    await flushPromises()
    await clickByText('Evidence')

    expect(document.body.textContent).toContain('No detailed integrity evidence was recorded for this report.')

    unmount()
  })
})
