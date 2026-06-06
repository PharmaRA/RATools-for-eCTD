import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'
import { afterEach, describe, expect, it, vi } from 'vitest'

vi.mock('antd', async (importOriginal) => {
  const actual = await importOriginal<typeof import('antd')>()

  return {
    ...actual,
    message: {
      success: vi.fn(),
      error: vi.fn(),
      info: vi.fn(),
      warning: vi.fn(),
      loading: vi.fn(),
    },
  }
})

import { SequenceWorkspacePage } from './SequenceWorkspacePage'
import { message } from 'antd'

const flushPromises = async () => {
  await act(async () => {
    await Promise.resolve()
  })
}

const renderSequenceWorkspacePage = (props: React.ComponentProps<typeof SequenceWorkspacePage>) => {
  const container = document.createElement('div')
  document.body.appendChild(container)
  const root = createRoot(container)

  act(() => {
    root.render(<SequenceWorkspacePage {...props} />)
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
  const element = Array.from(document.querySelectorAll('button')).find((button) => button.textContent?.includes(text)) as HTMLButtonElement | undefined
  expect(element).toBeTruthy()
  act(() => {
    element!.click()
  })
}

const clickAnyByText = (text: string) => {
  const element = Array.from(document.querySelectorAll('button, .ant-tree-node-content-wrapper, .ectd-tree-node')).find((candidate) => candidate.textContent?.includes(text)) as HTMLElement | undefined
  expect(element).toBeTruthy()
  act(() => {
    element!.click()
  })
}

const clickPrimaryModalButton = () => {
  const element = Array.from(document.querySelectorAll('.ant-modal .ant-btn-primary')).at(-1) as HTMLButtonElement | undefined
  expect(element).toBeTruthy()
  act(() => {
    element!.click()
  })
}

const getValidationSummary = () => document.querySelector('[data-testid="validation-summary"]')

const getValidationSummaryField = (field: string) => document.querySelector(`[data-testid="validation-summary-${field}"]`)

const getLocateButtons = (container: Element | null) => Array.from(container?.querySelectorAll('button') || [])
  .filter((button) => button.textContent?.includes('Locate')) as HTMLButtonElement[]

const clickLocateButton = (container: Element | null, index = 0) => {
  const button = getLocateButtons(container).at(index)
  expect(button).toBeTruthy()
  act(() => {
    button!.click()
  })
}

const stubWorkspaceFetch = () => {
  vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => {
    if (url === '/api/document-placements') {
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([
        {
          id: 'placement-1',
          documentId: 'document-1',
          applicationId: 'app-1',
          sequenceNumber: '0001',
          ctdSection: 'm1.1',
          operation: 'Replace',
          title: 'Protocol Leaf',
        },
      ]) })
    }

    if (url === '/api/documents') {
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([
        {
          id: 'document-1',
          fileName: 'protocol.pdf',
          storagePath: 'C:/workspace/app/0001/m1/us/11-forms/protocol.pdf',
          mediaType: 'application/pdf',
          sha256: 'abc123',
          sizeBytes: 1234,
        },
      ]) })
    }

    if (url === '/api/applications/app-1/ectd-structure') {
      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({
        profileName: 'US FDA eCTD 3.2.2',
        region: 'US',
        roots: [
          {
            elementName: 'm1-1-forms',
            sectionPath: 'm1.1',
            displayName: 'Forms',
            sourceProfile: 'US FDA eCTD 3.2.2',
            children: [],
          },
        ],
      }) })
    }

    return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
  }))
}

const setInputValue = (input: HTMLInputElement, value: string) => {
  const valueSetter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')?.set
  valueSetter?.call(input, value)
  input.dispatchEvent(new Event('input', { bubbles: true }))
}

const getInputByLabel = (label: string) => {
  const item = Array.from(document.querySelectorAll('.ant-form-item')).find((candidate) => candidate.textContent?.includes(label))
  const input = item?.querySelector('input') as HTMLInputElement | undefined
  expect(input).toBeTruthy()
  return input!
}

describe('SequenceWorkspacePage validation-first publish workflow', () => {
  afterEach(async () => {
    await flushPromises()
    document.body.innerHTML = ''
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
  })

  it('validates the sequence before opening the publish modal', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: vi.fn().mockResolvedValue([]) }))

    const callOrder: string[] = []
    const validateSequenceProvider = vi.fn().mockImplementation(async () => {
      callOrder.push('validate')
      return {
        applicationId: 'app-1',
        sequenceNumber: '0001',
        validationProfile: 'US FDA eCTD 3.2.2',
        isValid: true,
        issues: [],
        sectionMatches: [
          { sectionPath: 'm1.1', isValid: true, isStandard: true, matchedPrefix: 'm1.1', reason: null },
        ],
        lifecycleMatches: [],
      }
    })
    const createAndExecutePublishJobProvider = vi.fn().mockImplementation(async () => {
      callOrder.push('publish')
      return { id: 'job-1' }
    })

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0001',
      onBack: vi.fn(),
      validateSequenceProvider,
      createAndExecutePublishJobProvider,
    })

    await flushPromises()
    clickByText('Publish Sequence')
    await flushPromises()

    expect(validateSequenceProvider).toHaveBeenCalledWith({
      applicationId: 'app-1',
      sequenceNumber: '0001',
    })
    expect(createAndExecutePublishJobProvider).not.toHaveBeenCalled()
    expect(callOrder).toEqual(['validate'])
    const validationSummary = getValidationSummary()
    expect(validationSummary).toBeTruthy()
    expect(validationSummary?.getAttribute('data-severity')).toBe('success')
    expect(getValidationSummaryField('title')?.textContent).toContain('Validation passed')
    expect(getValidationSummaryField('profile')?.textContent).toContain('US FDA eCTD 3.2.2')
    expect(getValidationSummaryField('issue-count')?.textContent).toContain('0 issues')
    expect(getValidationSummaryField('has-api-error')?.textContent).toContain('No')
    expect(getValidationSummaryField('status-label')?.textContent).toContain('Validation passed')
    expect(getValidationSummaryField('details')?.textContent).toContain('No validation issues found.')
    expect(getValidationSummaryField('issues')?.textContent).toContain('No validation issues found.')
    expect(getValidationSummaryField('lifecycle')?.textContent).toContain('No lifecycle operations were checked.')
    expect(getValidationSummaryField('sections')?.textContent).toContain('1 checked | 0 invalid | 0 non-standard')
    expect(getValidationSummaryField('sections')?.textContent).toContain('All checked sections are valid standard matches.')

    const input = Array.from(document.querySelectorAll('input')).find((element) => element.placeholder === 'e.g. C:/eCTD/exports') as HTMLInputElement | undefined
    expect(input).toBeTruthy()

    act(() => {
      setInputValue(input!, 'E:/exports/submission-a')
    })

    await flushPromises()
    clickPrimaryModalButton()
    await flushPromises()

    expect(validateSequenceProvider).toHaveBeenCalledTimes(1)
    expect(createAndExecutePublishJobProvider).toHaveBeenCalledWith({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      outputDirectoryPath: 'E:/exports/submission-a',
    })
    expect(callOrder).toEqual(['validate', 'publish'])

    unmount()
  })

  it('keeps validation summary view model free of display labels', () => {
    const source = readFileSync(join(process.cwd(), 'src/pages/SequenceWorkspacePage.tsx'), 'utf8')

    expect(source).not.toContain('issueCountLabel')
    expect(source).not.toContain('statusLabel')
  })

  it('renders validation failures without opening the publish modal', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: vi.fn().mockResolvedValue([]) }))

    const validateSequenceProvider = vi.fn().mockResolvedValue({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      validationProfile: 'US FDA eCTD 3.2.2',
      isValid: false,
      issues: [
        {
          severity: 'Error',
          code: 'MISSING_DOCUMENT',
          message: 'Module 3 document is required.',
        },
      ],
      sectionMatches: [],
      lifecycleMatches: [],
    })
    const createAndExecutePublishJobProvider = vi.fn()

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0001',
      onBack: vi.fn(),
      validateSequenceProvider,
      createAndExecutePublishJobProvider,
    })

    await flushPromises()
    clickByText('Publish Sequence')
    await flushPromises()

    expect(validateSequenceProvider).toHaveBeenCalledWith({
      applicationId: 'app-1',
      sequenceNumber: '0001',
    })
    expect(createAndExecutePublishJobProvider).not.toHaveBeenCalled()
    expect(document.querySelector('.ant-modal')).toBeFalsy()
    const validationSummary = getValidationSummary()
    expect(validationSummary).toBeTruthy()
    expect(validationSummary?.getAttribute('data-severity')).toBe('error')
    expect(getValidationSummaryField('title')?.textContent).toContain('Validation failed')
    expect(getValidationSummaryField('profile')?.textContent).toContain('US FDA eCTD 3.2.2')
    expect(getValidationSummaryField('issue-count')?.textContent).toContain('1 issue')
    expect(getValidationSummaryField('has-api-error')?.textContent).toContain('No')
    expect(getValidationSummaryField('status-label')?.textContent).toContain('Validation failed')
    expect(getValidationSummaryField('details')?.textContent).toContain('MISSING_DOCUMENT')
    expect(getValidationSummaryField('details')?.textContent).toContain('Module 3 document is required.')

    unmount()
  })

  it('renders API validation errors without opening the publish modal', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: vi.fn().mockResolvedValue([]) }))

    const validateSequenceProvider = vi.fn().mockRejectedValue(new Error('Validation service unavailable.'))
    const createAndExecutePublishJobProvider = vi.fn()

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0001',
      onBack: vi.fn(),
      validateSequenceProvider,
      createAndExecutePublishJobProvider,
    })

    await flushPromises()
    clickByText('Publish Sequence')
    await flushPromises()

    expect(validateSequenceProvider).toHaveBeenCalledWith({
      applicationId: 'app-1',
      sequenceNumber: '0001',
    })
    expect(createAndExecutePublishJobProvider).not.toHaveBeenCalled()
    expect(document.querySelector('.ant-modal')).toBeFalsy()
    const validationSummary = getValidationSummary()
    expect(validationSummary).toBeTruthy()
    expect(validationSummary?.getAttribute('data-severity')).toBe('error')
    expect(getValidationSummaryField('title')?.textContent).toContain('Validation API error')
    expect(getValidationSummaryField('profile')?.textContent).toContain('Validation API')
    expect(getValidationSummaryField('issue-count')?.textContent).toContain('1 issue')
    expect(getValidationSummaryField('has-api-error')?.textContent).toContain('Yes')
    expect(getValidationSummaryField('status-label')?.textContent).toContain('Validation API error')
    expect(getValidationSummaryField('details')?.textContent).toContain('API_ERROR')
    expect(getValidationSummaryField('details')?.textContent).toContain('Validation service unavailable.')

    unmount()
  })

  it('closes a previously opened publish modal when revalidation fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: vi.fn().mockResolvedValue([]) }))

    const validateSequenceProvider = vi.fn()
      .mockResolvedValueOnce({
        applicationId: 'app-1',
        sequenceNumber: '0001',
        validationProfile: 'US FDA eCTD 3.2.2',
        isValid: true,
        issues: [],
        sectionMatches: [],
        lifecycleMatches: [],
      })
      .mockResolvedValueOnce({
        applicationId: 'app-1',
        sequenceNumber: '0001',
        validationProfile: 'US FDA eCTD 3.2.2',
        isValid: false,
        issues: [
          {
            severity: 'Error',
            code: 'STALE_EXPORT_BLOCKED',
            message: 'Revalidation found a blocking issue.',
          },
        ],
        sectionMatches: [],
        lifecycleMatches: [],
      })
    const createAndExecutePublishJobProvider = vi.fn()

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0001',
      onBack: vi.fn(),
      validateSequenceProvider,
      createAndExecutePublishJobProvider,
    })

    await flushPromises()
    clickByText('Publish Sequence')
    await flushPromises()

    expect(document.querySelector('.ant-modal')).toBeTruthy()

    clickByText('Publish Sequence')
    await flushPromises()

    expect(validateSequenceProvider).toHaveBeenCalledTimes(2)
    expect(document.querySelector('.ant-modal')).toBeFalsy()
    expect(createAndExecutePublishJobProvider).not.toHaveBeenCalled()
    expect(getValidationSummaryField('title')?.textContent).toContain('Validation failed')
    expect(getValidationSummaryField('details')?.textContent).toContain('STALE_EXPORT_BLOCKED')

    unmount()
  })

  it('renders editing-oriented validation report sections', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({ ok: true, json: vi.fn().mockResolvedValue([]) }))

    const validateSequenceProvider = vi.fn().mockResolvedValue({
      applicationId: 'app-1',
      sequenceNumber: '0002',
      validationProfile: 'US FDA eCTD 3.2.2',
      isValid: false,
      issues: [
        { severity: 'Error', code: 'LIFECYCLE_TARGET_INVALID', message: 'Replace target must be from an earlier sequence.' },
        { severity: 'Warning', code: 'SECTION_NON_STANDARD', message: 'Section uses a non-standard profile match.' },
      ],
      sectionMatches: [
        { sectionPath: 'm1.1', isValid: true, isStandard: true, matchedPrefix: 'm1.1', reason: null },
        { sectionPath: 'm3.2.p', isValid: true, isStandard: false, matchedPrefix: 'm3.2', reason: 'Matched parent section.' },
        { sectionPath: 'm9.9', isValid: false, isStandard: false, matchedPrefix: null, reason: 'Unknown section.' },
      ],
      lifecycleMatches: [
        {
          operation: 'Replace',
          sequenceNumber: '0002',
          ctdSection: 'm3.2.p',
          documentId: 'document-1',
          resultCode: 'INVALID_TARGET',
          matchStrategy: 'ExplicitPlacementId',
          attemptedStrategies: ['ExplicitPlacementId'],
          historicalMatchCount: 1,
          historicalSequenceNumbers: ['0001'],
          historicalPlacementIds: ['target-placement-1'],
          historicalFinalState: 'Current',
        },
      ],
    })

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0002',
      onBack: vi.fn(),
      validateSequenceProvider,
      createAndExecutePublishJobProvider: vi.fn(),
    })

    await flushPromises()
    clickByText('Publish Sequence')
    await flushPromises()

    expect(getValidationSummaryField('issues')?.textContent).toContain('Issues to fix')
    expect(getValidationSummaryField('issues')?.textContent).toContain('LIFECYCLE_TARGET_INVALID')
    expect(getValidationSummaryField('issues')?.textContent).toContain('Replace target must be from an earlier sequence.')
    expect(getValidationSummaryField('issues')?.textContent).toContain('Warning')

    expect(getValidationSummaryField('lifecycle')?.textContent).toContain('Lifecycle Targets')
    expect(getValidationSummaryField('lifecycle')?.textContent).toContain('Replace')
    expect(getValidationSummaryField('lifecycle')?.textContent).toContain('m3.2.p')
    expect(getValidationSummaryField('lifecycle')?.textContent).toContain('INVALID_TARGET')
    expect(getValidationSummaryField('lifecycle')?.textContent).toContain('ExplicitPlacementId')
    expect(getValidationSummaryField('lifecycle')?.textContent).toContain('0001')

    expect(getValidationSummaryField('sections')?.textContent).toContain('Section Matches')
    expect(getValidationSummaryField('sections')?.textContent).toContain('1 invalid')
    expect(getValidationSummaryField('sections')?.textContent).toContain('1 non-standard')
    expect(getValidationSummaryField('sections')?.textContent).toContain('m9.9')
    expect(getValidationSummaryField('sections')?.textContent).toContain('Unknown section.')

    unmount()
  })

  it('locates a validation issue document and does not render Locate for non-locatable issues', async () => {
    stubWorkspaceFetch()

    const validateSequenceProvider = vi.fn().mockResolvedValue({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      validationProfile: 'US FDA eCTD 3.2.2',
      isValid: false,
      issues: [
        {
          severity: 'Error',
          code: 'PLACEMENT_INVALID',
          message: 'Protocol placement needs review.',
          placementId: 'placement-1',
          documentId: 'document-1',
          sectionPath: 'm1.1',
        },
        {
          severity: 'Warning',
          code: 'GENERAL_WARNING',
          message: 'Review the validation report.',
        },
      ],
      sectionMatches: [],
      lifecycleMatches: [],
    })

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0001',
      onBack: vi.fn(),
      validateSequenceProvider,
      createAndExecutePublishJobProvider: vi.fn(),
    })

    await flushPromises()
    await flushPromises()
    clickByText('Publish Sequence')
    await flushPromises()

    const issuesArea = getValidationSummaryField('issues')
    expect(getLocateButtons(issuesArea)).toHaveLength(1)

    clickLocateButton(issuesArea)
    await flushPromises()

    expect(document.body.textContent).toContain('Leaf Metadata')
    expect(document.body.textContent).toContain('protocol.pdf')
    expect(document.body.textContent).toContain('Operation')

    unmount()
  })

  it('warns without crashing when a validation issue locator is stale', async () => {
    stubWorkspaceFetch()

    const validateSequenceProvider = vi.fn().mockResolvedValue({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      validationProfile: 'US FDA eCTD 3.2.2',
      isValid: false,
      issues: [
        {
          severity: 'Error',
          code: 'STALE_LOCATOR',
          message: 'This issue points at a removed document.',
          placementId: 'missing-placement',
        },
      ],
      sectionMatches: [],
      lifecycleMatches: [],
    })

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0001',
      onBack: vi.fn(),
      validateSequenceProvider,
      createAndExecutePublishJobProvider: vi.fn(),
    })

    await flushPromises()
    await flushPromises()
    clickAnyByText('Forms')
    await flushPromises()
    expect(document.body.textContent).toContain('Leaf Metadata Guide')

    clickByText('Publish Sequence')
    await flushPromises()

    const issuesArea = getValidationSummaryField('issues')
    expect(getLocateButtons(issuesArea)).toHaveLength(1)

    clickLocateButton(issuesArea)
    await flushPromises()

    expect(message.warning).toHaveBeenCalledWith('Could not locate this validation issue in the workspace tree.')
    expect(document.body.textContent).toContain('Leaf Metadata Guide')
    expect(document.body.textContent).not.toContain('Leaf Metadata\nOperation')

    unmount()
  })

  it('locates a section match abnormal row by section path', async () => {
    stubWorkspaceFetch()

    const validateSequenceProvider = vi.fn().mockResolvedValue({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      validationProfile: 'US FDA eCTD 3.2.2',
      isValid: false,
      issues: [],
      sectionMatches: [
        { sectionPath: 'm1.1', isValid: false, isStandard: false, matchedPrefix: null, reason: 'Unknown section.' },
      ],
      lifecycleMatches: [],
    })

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0001',
      onBack: vi.fn(),
      validateSequenceProvider,
      createAndExecutePublishJobProvider: vi.fn(),
    })

    await flushPromises()
    await flushPromises()
    clickByText('Publish Sequence')
    await flushPromises()

    clickLocateButton(getValidationSummaryField('sections'))
    await flushPromises()

    expect(document.body.textContent).toContain('Section')
    expect(document.body.textContent).toContain('m1.1')
    expect(document.body.textContent).toContain('Display')
    expect(document.body.textContent).toContain('Leaf Metadata Guide')

    unmount()
  })

  it('locates a lifecycle row document by current sequence document id', async () => {
    stubWorkspaceFetch()

    const validateSequenceProvider = vi.fn().mockResolvedValue({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      validationProfile: 'US FDA eCTD 3.2.2',
      isValid: false,
      issues: [],
      sectionMatches: [],
      lifecycleMatches: [
        {
          operation: 'Replace',
          sequenceNumber: '0001',
          ctdSection: 'm1.1',
          documentId: 'document-1',
          resultCode: 'INVALID_TARGET',
          matchStrategy: 'DocumentId',
          attemptedStrategies: ['DocumentId'],
          historicalMatchCount: 0,
          historicalSequenceNumbers: [],
          historicalPlacementIds: [],
          historicalFinalState: 'Missing',
        },
      ],
    })

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0001',
      onBack: vi.fn(),
      validateSequenceProvider,
      createAndExecutePublishJobProvider: vi.fn(),
    })

    await flushPromises()
    await flushPromises()
    clickByText('Publish Sequence')
    await flushPromises()

    clickLocateButton(getValidationSummaryField('lifecycle'))
    await flushPromises()

    expect(document.body.textContent).toContain('Leaf Metadata')
    expect(document.body.textContent).toContain('protocol.pdf')
    expect(document.body.textContent).toContain('Operation')

    unmount()
  })

  it('locates a lifecycle row document by document id and section when duplicate placements exist', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => {
      if (url === '/api/document-placements') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([
          {
            id: 'placement-1',
            documentId: 'document-1',
            applicationId: 'app-1',
            sequenceNumber: '0001',
            ctdSection: 'm1.1',
            operation: 'New',
            title: 'Forms Leaf',
          },
          {
            id: 'placement-2',
            documentId: 'document-1',
            applicationId: 'app-1',
            sequenceNumber: '0001',
            ctdSection: 'm1.2',
            operation: 'Replace',
            title: 'Cover Leaf',
          },
        ]) })
      }

      if (url === '/api/documents') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([
          {
            id: 'document-1',
            fileName: 'shared.pdf',
            storagePath: 'C:/workspace/app/0001/m1/us/shared.pdf',
            mediaType: 'application/pdf',
            sha256: 'abc123',
            sizeBytes: 1234,
          },
        ]) })
      }

      if (url === '/api/applications/app-1/ectd-structure') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({
          profileName: 'US FDA eCTD 3.2.2',
          region: 'US',
          roots: [
            {
              elementName: 'm1-1-forms',
              sectionPath: 'm1.1',
              displayName: 'Forms',
              sourceProfile: 'US FDA eCTD 3.2.2',
              children: [],
            },
            {
              elementName: 'm1-2-cover-letters',
              sectionPath: 'm1.2',
              displayName: 'Cover Letters',
              sourceProfile: 'US FDA eCTD 3.2.2',
              children: [],
            },
          ],
        }) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const validateSequenceProvider = vi.fn().mockResolvedValue({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      validationProfile: 'US FDA eCTD 3.2.2',
      isValid: false,
      issues: [],
      sectionMatches: [],
      lifecycleMatches: [
        {
          operation: 'Replace',
          sequenceNumber: '0001',
          ctdSection: 'm1.2',
          documentId: 'document-1',
          resultCode: 'INVALID_TARGET',
          matchStrategy: 'DocumentId',
          attemptedStrategies: ['DocumentId'],
          historicalMatchCount: 0,
          historicalSequenceNumbers: [],
          historicalPlacementIds: [],
          historicalFinalState: 'Missing',
        },
      ],
    })

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0001',
      onBack: vi.fn(),
      validateSequenceProvider,
      createAndExecutePublishJobProvider: vi.fn(),
    })

    await flushPromises()
    await flushPromises()
    clickByText('Publish Sequence')
    await flushPromises()

    clickLocateButton(getValidationSummaryField('lifecycle'))
    await flushPromises()

    expect(document.body.textContent).toContain('Placement ID')
    expect(document.body.textContent).toContain('placement-2')
    expect(document.body.textContent).toContain('m1.2')
    expect(getInputByLabel('Leaf Title').value).toBe('Cover Leaf')
    expect(document.body.textContent).not.toContain('placement-1')

    unmount()
  })

  it('replaces the reserved section placeholder with a leaf metadata guide', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => {
      if (url === '/api/document-placements') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([
          {
            id: 'target-placement-1',
            documentId: 'target-document-1',
            applicationId: 'app-1',
            sequenceNumber: '0000',
            ctdSection: 'm1.1',
            operation: 'New',
            title: 'Historical Leaf',
          },
          {
            id: 'placement-1',
            documentId: 'document-1',
            applicationId: 'app-1',
            sequenceNumber: '0001',
            ctdSection: 'm1.1',
            operation: 'New',
            title: 'Protocol Leaf',
          },
        ]) })
      }

      if (url === '/api/documents') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([
          {
            id: 'target-document-1',
            fileName: 'historical.pdf',
            storagePath: 'C:/workspace/app/0000/m1/us/11-forms/historical.pdf',
            mediaType: 'application/pdf',
            sha256: 'target123',
            sizeBytes: 1234,
          },
          {
            id: 'document-1',
            fileName: 'protocol.pdf',
            storagePath: 'C:/workspace/app/0001/m1/us/11-forms/protocol.pdf',
            mediaType: 'application/pdf',
            sha256: 'abc123',
            sizeBytes: 1234,
          },
        ]) })
      }

      if (url === '/api/applications/app-1/ectd-structure') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({
          profileName: 'US FDA eCTD 3.2.2',
          region: 'US',
          roots: [
            {
              elementName: 'm1-1-forms',
              sectionPath: 'm1.1',
              displayName: 'Forms',
              sourceProfile: 'US FDA eCTD 3.2.2',
              children: [],
            },
          ],
        }) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0001',
      onBack: vi.fn(),
    })

    await flushPromises()
    await flushPromises()
    clickAnyByText('Forms')
    await flushPromises()

    expect(document.body.textContent).not.toContain('Leaf Element Data Entry (Reserved)')
    expect(document.body.textContent).toContain('Leaf Metadata Guide')
    expect(document.body.textContent).toContain('Mapped Leaves')
    expect(document.body.textContent).toContain('1')

    unmount()
  })

  it('shows editable leaf metadata and preview for a selected mapped document', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => {
      if (url === '/api/document-placements') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([
          {
            id: 'target-placement-1',
            documentId: 'target-document-1',
            applicationId: 'app-1',
            sequenceNumber: '0000',
            ctdSection: 'm1.1',
            operation: 'New',
            title: 'Historical Leaf',
          },
          {
            id: 'placement-1',
            documentId: 'document-1',
            applicationId: 'app-1',
            sequenceNumber: '0001',
            ctdSection: 'm1.1',
            operation: 'Replace',
            title: 'Protocol Leaf',
          },
        ]) })
      }

      if (url === '/api/documents') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([
          {
            id: 'target-document-1',
            fileName: 'historical.pdf',
            storagePath: 'C:/workspace/app/0000/m1/us/11-forms/historical.pdf',
            mediaType: 'application/pdf',
            sha256: 'target123',
            sizeBytes: 1234,
          },
          {
            id: 'document-1',
            fileName: 'protocol.pdf',
            storagePath: 'C:/workspace/app/0001/m1/us/11-forms/protocol.pdf',
            mediaType: 'application/pdf',
            sha256: 'abc123',
            sizeBytes: 1234,
          },
        ]) })
      }

      if (url === '/api/applications/app-1/ectd-structure') {
        return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue({
          profileName: 'US FDA eCTD 3.2.2',
          region: 'US',
          roots: [
            {
              elementName: 'm1-1-forms',
              sectionPath: 'm1.1',
              displayName: 'Forms',
              sourceProfile: 'US FDA eCTD 3.2.2',
              children: [],
            },
          ],
        }) })
      }

      return Promise.resolve({ ok: true, json: vi.fn().mockResolvedValue([]) })
    }))

    const { unmount } = renderSequenceWorkspacePage({
      appId: 'app-1',
      seqNumber: '0001',
      onBack: vi.fn(),
    })

    await flushPromises()
    await flushPromises()
    clickAnyByText('protocol.pdf')
    await flushPromises()

    expect(document.body.textContent).toContain('Leaf Metadata')
    expect(document.body.textContent).toContain('Operation')
    expect(document.body.textContent).toContain('xlink:href')
    expect(document.body.textContent).toContain('Mime Type')
    expect(document.body.textContent).toContain('Checksum')
    expect(document.body.textContent).toContain('md5')
    expect(document.body.textContent).toContain('Computed at publish')
    expect(document.body.textContent).toContain('Lifecycle Target')
    expect(document.body.textContent).toContain('0000 | m1.1 | Historical Leaf | New')
    expect(document.body.textContent).toContain('modified-file')
    expect(document.body.textContent).toContain('Save Leaf Metadata')

    act(() => {
      setInputValue(getInputByLabel('File Prefix'), 'updated-protocol')
    })
    await flushPromises()

    expect(document.body.textContent).toContain('m1/us/11-forms/updated-protocol.pdf')

    unmount()
  })
})
