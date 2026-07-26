import { ApiRequestError } from '../../apiClient'

export const normalizePackageReviewError = (error: unknown) => {
  if (error instanceof Error) return error
  return new Error(String(error))
}

export const getReviewErrorDescription = (error: Error | null) => error?.message || ''

export const getReviewErrorTitle = (error: Error) => {
  if (!(error instanceof ApiRequestError)) return '无法加载包审阅数据'

  switch (error.status) {
    case 404:
      return '未找到报告或产物 (404)'
    case 409:
      return '发布任务尚未就绪 (409)'
    case 410:
      return '发布数据不可用 (410)'
    case 422:
      return '发布报告已损坏 (422)'
    default:
      return `无法加载包审阅数据 (${error.status})`
  }
}
