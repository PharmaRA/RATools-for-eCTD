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

globalThis.IS_REACT_ACT_ENVIRONMENT = true

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
})
