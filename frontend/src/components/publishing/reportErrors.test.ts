import { describe, expect, it } from 'vitest'

import { ApiRequestError } from '../../apiClient'
import { getReportErrorAlertMeta, toReportErrorState } from './reportErrors'

describe('reportErrors', () => {
  it('keeps HTTP status and message when converting report load errors', () => {
    expect(toReportErrorState(new ApiRequestError(409, 'Publish job is still running'))).toEqual({
      status: 409,
      message: 'Publish job is still running',
    })
  })

  it('uses status 0 and a fallback message for unknown report load errors', () => {
    expect(toReportErrorState(null)).toEqual({
      status: 0,
      message: 'Unknown error',
    })
  })

  it.each([
    [404, { title: '报告不存在 (404)', type: 'warning' }],
    [409, { title: '任务未完成 (409)', type: 'info' }],
    [410, { title: '报告文件已缺失 (410)', type: 'warning' }],
    [422, { title: '报告已损坏 (422)', type: 'error' }],
    [500, { title: '无法加载报告', type: 'error' }],
  ] as const)('maps status %s to report alert metadata', (status, meta) => {
    expect(getReportErrorAlertMeta(status)).toEqual(meta)
  })
})
