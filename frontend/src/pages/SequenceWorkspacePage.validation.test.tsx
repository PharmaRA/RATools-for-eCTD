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
        sectionMatches: [],
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

  it('replaces the reserved section placeholder with a leaf metadata guide', async () => {
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
    expect(document.body.textContent).toContain('Save Leaf Metadata')

    act(() => {
      setInputValue(getInputByLabel('File Prefix'), 'updated-protocol')
    })
    await flushPromises()

    expect(document.body.textContent).toContain('m1/us/11-forms/updated-protocol.pdf')

    unmount()
  })
})
