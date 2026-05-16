import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it, vi } from 'vitest'

import App from './App'

const flushPromises = async () => {
  await act(async () => {
    await Promise.resolve()
  })
}

const renderApp = () => {
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

const clickByText = (text: string) => {
  const element = Array.from(document.querySelectorAll('button, [role="tab"], .ant-tabs-tab-btn')).find((candidate) => candidate.textContent?.includes(text)) as HTMLElement | undefined
  expect(element).toBeTruthy()
  act(() => {
    element!.click()
  })
}

if (!window.matchMedia) {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn(),
    removeEventListener: vi.fn(),
    dispatchEvent: vi.fn(),
  })) as unknown as typeof window.matchMedia
}

if (!globalThis.ResizeObserver) {
  class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
  }

  globalThis.ResizeObserver = ResizeObserverStub as unknown as typeof ResizeObserver
}

const publishHistoryResponse = {
  applicationId: 'app-1',
  applicationNumber: 'APP-1',
  region: 'US',
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
            region: 'US',
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
    clickByText('Manage App')
    await flushPromises()
    clickByText('Publish History')
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
            region: 'US',
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
    clickByText('Manage App')
    await flushPromises()
    clickByText('Publish History')
    await flushPromises()
    clickByText('Report')
    await flushPromises()

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
    expect(document.body.textContent).toContain('by-file-name')

    unmount()
  })
})
