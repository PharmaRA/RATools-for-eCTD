import { getErrorMessage } from '../../pages/appShared'

export type ReportErrorState = {
  status: number
  message: string
}

type ReportErrorAlertMeta = {
  title: string
  type: 'error' | 'warning' | 'info'
}

export const toReportErrorState = (error: unknown): ReportErrorState => {
  const status = typeof (error as { status?: unknown })?.status === 'number'
    ? (error as { status: number }).status
    : 0

  return { status, message: getErrorMessage(error) }
}

export const getReportErrorAlertMeta = (status: number): ReportErrorAlertMeta => {
  switch (status) {
    case 404:
      return { title: '报告不存在 (404)', type: 'warning' }
    case 409:
      return { title: '任务未完成 (409)', type: 'info' }
    case 410:
      return { title: '报告文件已缺失 (410)', type: 'warning' }
    case 422:
      return { title: '报告已损坏 (422)', type: 'error' }
    default:
      return { title: '无法加载报告', type: 'error' }
  }
}
