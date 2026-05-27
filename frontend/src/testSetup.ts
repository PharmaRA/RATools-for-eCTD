import { beforeEach, vi } from 'vitest'

globalThis.IS_REACT_ACT_ENVIRONMENT = true

const ignoredConsoleWarnings = [
  'React Router Future Flag Warning:',
  'Warning: Instance created by `useForm` is not connected to any Form element.',
  'Warning: [antd: Table] `index` parameter of `rowKey` function is deprecated.',
]

const shouldIgnoreConsoleWarning = (args: unknown[]) => {
  const message = args.map((arg) => String(arg)).join(' ')
  return ignoredConsoleWarnings.some((warning) => message.includes(warning))
}

const consoleWarn = console.warn.bind(console)
const consoleError = console.error.bind(console)

console.warn = (...args: unknown[]) => {
  if (shouldIgnoreConsoleWarning(args)) return
  consoleWarn(...args)
}

console.error = (...args: unknown[]) => {
  if (shouldIgnoreConsoleWarning(args)) return
  consoleError(...args)
}

beforeEach(() => {
  window.history.replaceState(null, '', '/')
})

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

const getComputedStyle = window.getComputedStyle.bind(window)

window.getComputedStyle = ((element: Element, pseudoElement?: string | null) => {
  if (pseudoElement) {
    return getComputedStyle(element)
  }

  return getComputedStyle(element, pseudoElement)
}) as typeof window.getComputedStyle
