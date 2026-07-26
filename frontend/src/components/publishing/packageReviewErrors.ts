import { ApiRequestError } from '../../apiClient'
import { messages } from '../../i18n/messages'

export const normalizePackageReviewError = (error: unknown) => {
  if (error instanceof Error) return error
  return new Error(String(error))
}

export const getReviewErrorDescription = (error: Error | null) => error?.message || ''

export const getReviewErrorTitle = (error: Error) => {
  if (!(error instanceof ApiRequestError)) return messages.packageReview.loadErrorGeneric

  switch (error.status) {
    case 404:
      return messages.packageReview.loadErrorNotFound
    case 409:
      return messages.packageReview.loadErrorNotReady
    case 410:
      return messages.packageReview.loadErrorGone
    case 422:
      return messages.packageReview.loadErrorCorrupted
    default:
      return `${messages.packageReview.loadErrorGeneric} (${error.status})`
  }
}
