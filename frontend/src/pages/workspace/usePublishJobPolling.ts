import { useCallback, useEffect, useRef, useState } from 'react'

import { apiFetch } from '../../apiClient'
import { buildPublishJobUrl } from '../../publishActions'

export type PolledPublishJob = {
  id: string
  status: string
  failureReason?: string | null
  outputPath?: string | null
  packagePath?: string | null
}

export type PublishJobPollingState = {
  job: PolledPublishJob | null
  isPolling: boolean
  error: string | null
}

const TERMINAL_STATUSES = new Set(['Completed', 'Failed'])
const BASE_INTERVAL_MS = 2000
const MAX_INTERVAL_MS = 10000
const MAX_CONSECUTIVE_ERRORS = 5

export const isTerminalPublishJobStatus = (status?: string | null) =>
  !!status && TERMINAL_STATUSES.has(status)

/**
 * 发布作业状态轮询：2s 起步，请求失败按指数退避（上限 10s），连续 5 次失败停止；
 * 到达 Completed/Failed 终态即停。组件卸载时清理定时器，避免对已卸载组件 setState。
 */
export const usePublishJobPolling = (
  loadJob: (jobId: string) => Promise<PolledPublishJob> = async (jobId) =>
    (await apiFetch(buildPublishJobUrl(jobId))) as PolledPublishJob,
) => {
  const [state, setState] = useState<PublishJobPollingState>({ job: null, isPolling: false, error: null })
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const activeJobIdRef = useRef<string | null>(null)
  const errorCountRef = useRef(0)
  const loadJobRef = useRef(loadJob)
  loadJobRef.current = loadJob

  const clearTimer = () => {
    if (timerRef.current !== null) {
      clearTimeout(timerRef.current)
      timerRef.current = null
    }
  }

  const stopPolling = useCallback(() => {
    activeJobIdRef.current = null
    clearTimer()
    setState((previous) => ({ ...previous, isPolling: false }))
  }, [])

  const startPolling = useCallback((jobId: string) => {
    clearTimer()
    activeJobIdRef.current = jobId
    errorCountRef.current = 0
    setState({ job: null, isPolling: true, error: null })

    const poll = async () => {
      if (activeJobIdRef.current !== jobId) return

      try {
        const job = await loadJobRef.current(jobId)
        if (activeJobIdRef.current !== jobId) return
        errorCountRef.current = 0

        if (isTerminalPublishJobStatus(job.status)) {
          activeJobIdRef.current = null
          setState({ job, isPolling: false, error: null })
          return
        }

        setState({ job, isPolling: true, error: null })
        timerRef.current = setTimeout(poll, BASE_INTERVAL_MS)
      } catch (error) {
        if (activeJobIdRef.current !== jobId) return
        errorCountRef.current += 1

        if (errorCountRef.current >= MAX_CONSECUTIVE_ERRORS) {
          activeJobIdRef.current = null
          setState((previous) => ({
            ...previous,
            isPolling: false,
            error: error instanceof Error ? error.message : String(error),
          }))
          return
        }

        const backoff = Math.min(BASE_INTERVAL_MS * 2 ** errorCountRef.current, MAX_INTERVAL_MS)
        timerRef.current = setTimeout(poll, backoff)
      }
    }

    void poll()
  }, [])

  useEffect(() => () => {
    activeJobIdRef.current = null
    clearTimer()
  }, [])

  return { ...state, startPolling, stopPolling }
}
