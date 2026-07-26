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

const renderApp = (initialPath = '/') => {
  window.history.replaceState(null, '', initialPath)

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

const selectOptionByInputId = async (inputId: string, optionText: string) => {
  const input = await waitForElement(
    () => document.getElementById(inputId) as HTMLInputElement | undefined,
    `select input ${inputId}`,
  )
  const select = input.closest('.ant-select') as HTMLElement | null
  expect(select).toBeTruthy()

  act(() => {
    select!.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }))
  })

  const option = await waitForElement(
    () => Array.from(document.querySelectorAll('.ant-select-item-option')).find((candidate) => candidate.textContent?.includes(optionText)) as HTMLElement | undefined,
    `select option ${optionText}`,
  )

  act(() => {
    option.click()
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
    return headers.includes('文档 ID') && headers.includes('尝试的策略')
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
  const documentIdIndex = getLifecycleColumnIndex('文档 ID')
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
  const checklistCard = Array.from(document.querySelectorAll('.ant-card')).find((candidate) => normalizeText(candidate.textContent).includes('提交就绪检查清单'))
  expect(checklistCard).toBeTruthy()
  const row = Array.from(checklistCard!.querySelectorAll('tbody tr')).find((candidate) => normalizeText(candidate.textContent).includes(label))
  expect(row).toBeTruthy()
  const cells = Array.from(row!.querySelectorAll('td')).map((cell) => normalizeText(cell.textContent))
  expect(cells[1]).toBe(expectedStatus)
}

const getPublishHistorySequenceOrder = () => {
  const tables = Array.from(document.querySelectorAll('.ant-table'))
  const table = tables.find((candidate) => {
    const headers = Array.from(candidate.querySelectorAll('thead th')).map((header) => normalizeText(header.textContent))
    return headers.includes('序列') && headers.includes('就绪度') && headers.includes('操作')
  })
  expect(table).toBeTruthy()
  return Array.from(table!.querySelectorAll('tbody tr.ant-table-row')).map((row) => normalizeText(row.querySelector('td')?.textContent))
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
  readinessSummary: {
    readyCount: 0,
    blockedCount: 1,
    unknownCount: 0,
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
      publishReadiness: {
        isReady: false,
        status: 'Blocked',
        blockingErrorCount: 1,
        warningCount: 0,
        missingMetadataFields: ['ApplicantContactName'],
      },
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
    {
      publishJobId: 'job-2',
      sequenceNumber: '0002',
      status: 'Completed',
      createdUtc: '2024-01-02T12:10:00Z',
      completedUtc: '2024-01-02T12:15:00Z',
      reportAvailable: true,
      reportReadable: true,
      reportError: null,
      validationProfile: 'US FDA eCTD 3.2.2',
      errorCount: 0,
      warningCount: 1,
      warningSummary: null,
      publishReadiness: {
        isReady: true,
        status: 'Ready',
        blockingErrorCount: 0,
        warningCount: 1,
        missingMetadataFields: [],
      },
      lifecycleSummary: {
        matchedCount: 1,
        replaceTargetNotFoundCount: 0,
        deleteTargetNotFoundCount: 0,
        appendTargetNotFoundCount: 0,
        ambiguousCount: 0,
        currentSequenceCount: 0,
      },
      lifecycleMatches: [],
      artifactSummary: {
        fileCount: 6,
        totalSizeBytes: 3072,
        packageSizeBytes: 1536,
      },
      reportPath: 'E:/exports/report-2.json',
      packagePath: 'E:/exports/output-2.zip',
    },
    {
      publishJobId: 'job-3',
      sequenceNumber: '0003',
      status: 'Completed',
      createdUtc: '2024-01-02T12:20:00Z',
      completedUtc: '2024-01-02T12:25:00Z',
      reportAvailable: false,
      reportReadable: false,
      reportError: null,
      validationProfile: null,
      errorCount: null,
      warningCount: null,
      warningSummary: null,
      publishReadiness: null,
      lifecycleSummary: {
        matchedCount: 0,
        replaceTargetNotFoundCount: 0,
        deleteTargetNotFoundCount: 0,
        appendTargetNotFoundCount: 0,
        ambiguousCount: 0,
        currentSequenceCount: 0,
      },
      lifecycleMatches: [],
      artifactSummary: null,
      reportPath: null,
      packagePath: null,
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
        historicalFinalState: '缺失',
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
        historicalFinalState: '缺失',
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
        historicalFinalState: '缺失',
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
    await clickByText('管理')
    await clickByText('发布历史')
    await flushPromises()

    expect(document.body.textContent).toContain('已匹配')
    expect(document.body.textContent).toContain('替换目标缺失')
    expect(document.body.textContent).toContain('追加目标缺失')
    expect(document.body.textContent).toContain('校验')
    expect(document.body.textContent).toContain('错误: 1')
    expect(document.body.textContent).toContain('警告: 2')
    expect(document.body.textContent).toContain('Leaf title missing in one section')
    expect(document.body.textContent).toContain('就绪度')
    expect(document.body.textContent).toContain('Blocked')
    expect(document.body.textContent).toContain('ApplicantContactName')
    expect(document.body.textContent).toContain('产物')
    expect(document.body.textContent).toContain('7 files')
    expect(document.body.textContent).toContain('报告')
    expect(document.body.textContent).toContain('可用')
    expect(document.body.textContent).toContain('就绪序列')
    expect(document.body.textContent).toContain('受阻序列')
    expect(document.body.textContent).toContain('就绪度未知')

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
    await clickByText('管理')
    await clickByText('发布历史')
    await clickButtonByText('报告')
    await flushPromises()

    expect(document.body.textContent).toContain('发布成功')
    expect(document.body.textContent).toContain('Publish completed successfully.')
    expect(document.body.textContent).toContain('配置')
    expect(document.body.textContent).toContain('US FDA eCTD 3.2.2')
    expect(document.body.textContent).toContain('耗时')
    expect(document.body.textContent).toContain('1534 ms')
    expect(document.body.textContent).toContain('错误')
    expect(document.body.textContent).toContain('警告')
    expect(document.body.textContent).toContain('生命周期问题')
    expectDescriptionItem('生命周期问题', '6')
    expect(document.body.textContent).toContain('完整性')
    expectDescriptionItem('完整性', '不一致')
    expect(document.body.textContent).toContain('替换目标缺失')
    expectDescriptionItem('已匹配', '1')
    expectDescriptionItem('替换目标缺失', '1')
    expectDescriptionItem('删除目标缺失', '1')
    expectDescriptionItem('追加目标缺失', '1')
    expectDescriptionItem('存在歧义', '1')
    expectDescriptionItem('当前序列', '1')
    expect(document.body.textContent).toContain('删除目标缺失')
    expect(document.body.textContent).toContain('追加目标缺失')
    expect(document.body.textContent).toContain('存在歧义')
    expect(document.body.textContent).toContain('当前序列')
    expect(document.body.textContent).toContain('完整性摘要')
    expect(document.body.textContent).toContain('一致')
    expect(document.body.textContent).toContain('缺失文件')
    expect(document.body.textContent).toContain('不匹配的产物')
    expect(document.body.textContent).toContain('产物摘要')
    expect(document.body.textContent).toContain('包大小')
    expect(document.body.textContent).toContain('审计摘要')
    expect(document.body.textContent).toContain('最近操作')
    expect(document.body.textContent).toContain('发布就绪度')
    expect(document.body.textContent).toContain('Blocked')
    expect(document.body.textContent).toContain('ApplicantContactName')
    expect(document.body.textContent).toContain('Populate the required US Regional publishing metadata field before publishing.')
    expect(document.body.textContent).toContain('生命周期')
    expect(document.body.textContent).toContain('REPLACE_TARGET_NOT_FOUND')
    expect(document.body.textContent).toContain('DELETE_TARGET_NOT_FOUND')
    expect(document.body.textContent).toContain('LIFECYCLE_TARGET_INVALID')
    expect(document.body.textContent).toContain('by-file-name')
    expect(document.body.textContent).toContain('序列')
    expect(document.body.textContent).toContain('历史匹配数')
    expect(document.body.textContent).toContain('历史序列')
    expect(document.body.textContent).toContain('文档 ID')
    expect(document.body.textContent).toContain('尝试的策略')
    expect(document.body.textContent).toContain('历史放置 ID')
    expect(document.body.textContent).toContain('最终状态')
    expectLifecycleCell('doc-2', '操作类型', 'Append')
    expectLifecycleCell('doc-2', '序列', '0001')
    expectLifecycleCell('doc-2', 'CTD 章节', '1.3.5')
    expectLifecycleCell('doc-2', '结果代码', 'APPEND_TARGET_NOT_FOUND')
    expectLifecycleCell('doc-2', '匹配策略', 'explicit-placement-id')
    expectLifecycleCell('doc-2', '尝试的策略', 'explicit-placement-id, document-id')
    expectLifecycleCell('doc-2', '历史匹配数', '0')
    expectLifecycleCell('doc-2', '历史序列', '-')
    expectLifecycleCell('doc-2', '历史放置 ID', '-')
    expectLifecycleCell('doc-2', '最终状态', '缺失')
    expectLifecycleCell('doc-3', '操作类型', 'Replace')
    expectLifecycleCell('doc-3', '序列', '0001')
    expectLifecycleCell('doc-3', 'CTD 章节', '1.4.1')
    expectLifecycleCell('doc-3', '结果代码', 'LIFECYCLE_TARGET_AMBIGUOUS')
    expectLifecycleCell('doc-3', '匹配策略', 'document-id')
    expectLifecycleCell('doc-3', '尝试的策略', 'document-id')
    expectLifecycleCell('doc-3', '历史匹配数', '2')
    expectLifecycleCell('doc-3', '历史序列', '0000, 0001')
    expectLifecycleCell('doc-3', '历史放置 ID', 'placement-1, placement-2')
    expectLifecycleCell('doc-3', '最终状态', 'Current')
    await clickByText('证据')
    expect(document.body.textContent).toContain('完整性发现')
    expect(document.body.textContent).toContain('MissingZipEntry')
    expect(document.body.textContent).toContain('m1/us/11-forms/leaf.pdf')
    expect(document.body.textContent).toContain('Output file is missing from package zip.')
    expect(document.body.textContent).toContain('产物清单')
    expect(document.body.textContent).toContain('BackboneXml')
    expect(document.body.textContent).toContain('OutputFile')
    expect(document.body.textContent).toContain('2 KB')
    expect(document.body.textContent).toContain('Zip 中缺失')

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
    await clickByText('管理')
    await clickByText('发布历史')
    await clickButtonByText('审阅')
    await flushPromises()

    expect(fetchMock).toHaveBeenCalledWith('/api/publish-jobs/job-1/report', expect.anything())
    expect(fetchMock).toHaveBeenCalledWith('/api/publish-jobs/job-1/artifacts', expect.anything())

    for (const text of [
      '包审阅',
      '不可提交',
      '提交就绪检查清单',
      '发布就绪度快照',
      'ApplicantContactName',
      'Populate the required US Regional publishing metadata field before publishing.',
      '发布成功',
      '校验错误',
      '生命周期问题',
      '完整性一致',
      '必需产物齐全',
      '风险摘要',
      'MissingZipEntry',
      'Output file is missing from package zip.',
      '必需产物',
      'BackboneXml',
      'PublishReport',
      'PackageZip',
      '下载审阅 JSON',
      '下载包',
      '下载报告',
    ]) {
      expect(document.body.textContent).toContain(text)
    }
    expectReviewChecklistRow('发布成功', '通过')
    expectReviewChecklistRow('校验错误', '未通过')
    expectReviewChecklistRow('生命周期问题', '未通过')
    expectReviewChecklistRow('完整性一致', '未通过')
    expectReviewChecklistRow('必需产物齐全', '通过')
    const { createdBlobs, clickedDownloads } = setupDownloadCapture()
    await clickButtonByText('下载审阅 JSON')

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
      { key: 'publish-succeeded', check: '发布成功', status: 'Pass', detail: 'Publish completed successfully.' },
      { key: 'validation-errors', check: '校验错误', status: 'Fail', detail: '1 个错误' },
      { key: 'lifecycle-issues', check: '生命周期问题', status: 'Fail', detail: '6 个问题' },
      { key: 'integrity-consistent', check: '完整性一致', status: 'Fail', detail: '不一致或不可用' },
      { key: 'required-artifacts-present', check: '必需产物齐全', status: 'Pass', detail: '3/3 已就绪' },
    ])
    expect(fetchMock).not.toHaveBeenCalledWith('/api/publish-jobs/job-1/artifacts/PackageZip/download', expect.anything())
    expect(fetchMock).not.toHaveBeenCalledWith('/api/publish-jobs/job-1/artifacts/PublishReport/download', expect.anything())

    unmount()
  })

  it('sends the readiness filter when publish history is filtered to blocked readiness', async () => {
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

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    })
    vi.stubGlobal('fetch', fetchMock)

    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('管理')
    await clickByText('发布历史')
    await selectOptionByInputId('readinessStatus', '受阻')
    await clickButtonByText('筛选')
    await flushPromises()

    expect(
      fetchMock.mock.calls.some((call) => String(call[0]).includes('/api/applications/app-1/publish-history?page=1&pageSize=20&readinessStatus=Blocked')),
    ).toBe(true)

    unmount()
  })

  it('sorts publish history by readiness priority when blocked first is selected', async () => {
    const responseWithUnknownStatus = {
      ...publishHistoryResponse,
      entries: publishHistoryResponse.entries.map((entry) => (
        entry.publishJobId === 'job-3'
          ? {
            ...entry,
            publishReadiness: {
              isReady: false,
              status: 'Unknown',
              blockingErrorCount: 0,
              warningCount: 0,
              missingMetadataFields: [],
            },
          }
          : entry
      )),
    }

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
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue(responseWithUnknownStatus) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderApp()

    await flushPromises()
    await clickByText('管理')
    await clickByText('发布历史')
    await selectOptionByInputId('readinessSort', '受阻优先')
    await clickButtonByText('筛选')
    await flushPromises()

    expect(getPublishHistorySequenceOrder()).toEqual(['0001', '0003', '0002'])

    unmount()
  })

  it('sorts publish history by readiness priority when ready first is selected', async () => {
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
    await clickByText('管理')
    await clickByText('发布历史')
    await selectOptionByInputId('readinessSort', '就绪优先')
    await clickButtonByText('筛选')
    await flushPromises()

    expect(getPublishHistorySequenceOrder()).toEqual(['0002', '0003', '0001'])

    unmount()
  })

  it('restores and persists publish history filter state in the browser query', async () => {
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

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    })
    vi.stubGlobal('fetch', fetchMock)

    const { unmount } = renderApp('/applications/app-1?publishReadinessStatus=Blocked&publishReadinessSort=ready-first')

    await flushPromises()
    await clickByText('发布历史')
    await flushPromises()

    expect(
      fetchMock.mock.calls.some((call) => String(call[0]).includes('/api/applications/app-1/publish-history?page=1&pageSize=20&readinessStatus=Blocked')),
    ).toBe(true)
    expect(getPublishHistorySequenceOrder()).toEqual(['0002', '0003', '0001'])

    await selectOptionByInputId('readinessSort', '受阻优先')
    await clickButtonByText('筛选')
    await flushPromises()

    expect(window.location.search).toContain('publishReadinessStatus=Blocked')
    expect(window.location.search).toContain('publishReadinessSort=blocked-first')
    expect(window.location.search).not.toContain('readinessSort=')
    expect(getPublishHistorySequenceOrder()).toEqual(['0001', '0003', '0002'])

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
    await clickByText('管理')
    await clickByText('发布历史')
    await clickButtonByText('审阅')
    await flushPromises()

    expect(document.body.textContent).toContain('可提交')
    expect(document.body.textContent).toContain('警告不影响就绪度')
    expect(document.body.textContent).toContain('发布就绪度快照')
    expect(document.body.textContent).toContain('Ready')
    expect(document.body.textContent).toContain('TITLE_FALLBACK_USED')
    expect(document.body.textContent).toContain('未记录任何完整性发现项')
    expectReviewChecklistRow('发布成功', '通过')
    expectReviewChecklistRow('校验错误', '通过')
    expectReviewChecklistRow('生命周期问题', '通过')
    expectReviewChecklistRow('完整性一致', '通过')
    expectReviewChecklistRow('必需产物齐全', '通过')

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
    await clickByText('管理')
    await clickByText('发布历史')
    await clickButtonByText('审阅')
    await flushPromises()

    expect(document.body.textContent).toContain('包审阅')
    expect(document.body.textContent).toContain('不可提交')
    expect(document.body.textContent).toContain('发布数据不可用 (410)')
    expect(document.body.textContent).toContain('必需产物齐全')
    expectReviewChecklistRow('必需产物齐全', '未通过')
    expectControlDisabled('下载包')
    expectControlDisabled('下载报告')
    const { createdBlobs, clickedDownloads } = setupDownloadCapture()
    await clickButtonByText('下载审阅 JSON')

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
    await clickByText('管理')
    await clickByText('发布历史')
    await clickButtonByText('审阅')
    await flushPromises()

    const { createdBlobs } = setupDownloadCapture()

    expect(document.body.textContent).toContain('包审阅')
    expect(document.body.textContent).toContain('发布报告已损坏 (422)')
    expect(document.body.textContent).toContain('发布数据不可用 (410)')
    expectControlDisabled('下载审阅 JSON')
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
    await clickByText('管理')
    await clickByText('发布历史')
    await clickButtonByText('审阅')
    await flushPromises()

    const { createdBlobs, clickedDownloads } = setupDownloadCapture()
    await clickButtonByText('下载审阅 JSON')

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
    await clickByText('管理')
    await clickByText('发布历史')
    await clickButtonByText('审阅')
    await flushPromises()

    expect(document.body.textContent).toContain('包审阅')
    expect(document.body.textContent).toContain('不可提交')
    expect(document.body.textContent).toContain('必需产物')
    expectReviewChecklistRow('必需产物齐全', '未通过')
    expectControlDisabled('下载包')

    unmount()
  })

  it('shows old-report compatibility message when integrity evidence is absent', async () => {
    const oldReportResponse: Omit<typeof publishReportResponse, 'integrityEvidence'> & {
      integrityEvidence?: typeof publishReportResponse.integrityEvidence
    } = { ...publishReportResponse }
    delete oldReportResponse.integrityEvidence

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
    await clickByText('管理')
    await clickByText('发布历史')
    await clickButtonByText('报告')
    await flushPromises()
    await clickByText('证据')

    expect(document.body.textContent).toContain('未记录该报告的详细完整性证据。')

    unmount()
  })
})
