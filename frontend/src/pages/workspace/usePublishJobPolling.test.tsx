import { act } from 'react'
import { createRoot, type Root } from 'react-dom/client'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { isTerminalPublishJobStatus, usePublishJobPolling, type PolledPublishJob } from './usePublishJobPolling'

type HookHarness = ReturnType<typeof usePublishJobPolling>

const renderPollingHook = (loadJob: (jobId: string) => Promise<PolledPublishJob>) => {
  const container = document.createElement('div')
  document.body.appendChild(container)
  const root: Root = createRoot(container)
  const latestRef: { current: HookHarness | null } = { current: null }

  const Probe = () => {
    const harness = usePublishJobPolling(loadJob)
    // 测试探针：把 hook 状态同步到组件外部以便断言（生产代码不会这样做）。
    // eslint-disable-next-line react-hooks/immutability
    latestRef.current = harness
    return null
  }

  act(() => {
    root.render(<Probe />)
  })

  return {
    get state() {
      return latestRef.current!
    },
    unmount() {
      act(() => {
        root.unmount()
      })
      container.remove()
    },
  }
}

const flushMicrotasks = async () => {
  await act(async () => {
    await Promise.resolve()
  })
}

describe('usePublishJobPolling', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    document.body.innerHTML = ''
  })

  it('polls until the job reaches a terminal status', async () => {
    const responses: PolledPublishJob[] = [
      { id: 'job-1', status: 'Pending' },
      { id: 'job-1', status: 'Running' },
      { id: 'job-1', status: 'Completed', packagePath: 'E:/out/pkg.zip' },
    ]
    let callIndex = 0
    const loadJob = vi.fn(async () => responses[Math.min(callIndex++, responses.length - 1)])
    const harness = renderPollingHook(loadJob)

    act(() => {
      harness.state.startPolling('job-1')
    })
    await flushMicrotasks()
    expect(harness.state.job?.status).toBe('Pending')
    expect(harness.state.isPolling).toBe(true)

    await act(async () => {
      vi.advanceTimersByTime(2000)
    })
    await flushMicrotasks()
    expect(harness.state.job?.status).toBe('Running')

    await act(async () => {
      vi.advanceTimersByTime(2000)
    })
    await flushMicrotasks()
    expect(harness.state.job?.status).toBe('Completed')
    expect(harness.state.isPolling).toBe(false)

    // 终态后不再发请求。
    const callsAtTerminal = loadJob.mock.calls.length
    await act(async () => {
      vi.advanceTimersByTime(10000)
    })
    expect(loadJob.mock.calls.length).toBe(callsAtTerminal)

    harness.unmount()
  })

  it('stops with an error after consecutive failures', async () => {
    const loadJob = vi.fn(async () => {
      throw new Error('network down')
    })
    const harness = renderPollingHook(loadJob)

    act(() => {
      harness.state.startPolling('job-1')
    })
    await flushMicrotasks()

    // 连续失败按指数退避重试，达到上限后停止并暴露错误。
    for (let round = 0; round < 6; round += 1) {
      await act(async () => {
        vi.advanceTimersByTime(10000)
      })
      await flushMicrotasks()
    }

    expect(harness.state.isPolling).toBe(false)
    expect(harness.state.error).toContain('network down')

    harness.unmount()
  })

  it('stops scheduling after unmount', async () => {
    const loadJob = vi.fn(async (): Promise<PolledPublishJob> => ({ id: 'job-1', status: 'Running' }))
    const harness = renderPollingHook(loadJob)

    act(() => {
      harness.state.startPolling('job-1')
    })
    await flushMicrotasks()
    const callsBeforeUnmount = loadJob.mock.calls.length

    harness.unmount()

    await act(async () => {
      vi.advanceTimersByTime(20000)
    })
    expect(loadJob.mock.calls.length).toBe(callsBeforeUnmount)
  })

  it('classifies terminal statuses', () => {
    expect(isTerminalPublishJobStatus('Completed')).toBe(true)
    expect(isTerminalPublishJobStatus('Failed')).toBe(true)
    expect(isTerminalPublishJobStatus('Running')).toBe(false)
    expect(isTerminalPublishJobStatus(null)).toBe(false)
  })
})
