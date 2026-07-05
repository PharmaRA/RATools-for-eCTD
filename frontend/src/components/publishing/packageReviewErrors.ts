import { ApiRequestError } from '../../apiClient'

export const normalizePackageReviewError = (error: unknown) => {
  if (error instanceof Error) return error
  return new Error(String(error))
}

export const getReviewErrorTitle = (error: Error) => {
  if (!(error instanceof ApiRequestError)) return 'Unable to load package review data'

  switch (error.status) {
    case 404:
      return 'Report or artifacts were not found (404)'
    case 409:
      return 'Publish job is not ready (409)'
    case 410:
      return 'Publish data is unavailable (410)'
    case 422:
      return 'Publish report is corrupted (422)'
    default:
      return `Unable to load package review data (${error.status})`
  }
}
