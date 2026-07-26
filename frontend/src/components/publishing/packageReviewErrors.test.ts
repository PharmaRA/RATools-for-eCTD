import { describe, expect, it } from 'vitest'

import { ApiRequestError } from '../../apiClient'
import { messages } from '../../i18n/messages'
import { getReviewErrorDescription, getReviewErrorTitle, normalizePackageReviewError } from './packageReviewErrors'

describe('packageReviewErrors', () => {
  it('preserves Error instances when normalizing package review failures', () => {
    const error = new Error('Report failed')

    expect(normalizePackageReviewError(error)).toBe(error)
  })

  it('converts non-error failures into Error instances', () => {
    expect(normalizePackageReviewError('Report failed')).toMatchObject({
      message: 'Report failed',
    })
  })

  it.each([
    [404, messages.packageReview.loadErrorNotFound],
    [409, messages.packageReview.loadErrorNotReady],
    [410, messages.packageReview.loadErrorGone],
    [422, messages.packageReview.loadErrorCorrupted],
    [500, `${messages.packageReview.loadErrorGeneric} (500)`],
  ])('maps API status %s to a package review title', (status, title) => {
    expect(getReviewErrorTitle(new ApiRequestError(status, 'Failed'))).toBe(title)
  })

  it('uses the generic package review title for non-API errors', () => {
    expect(getReviewErrorTitle(new Error('Failed'))).toBe(messages.packageReview.loadErrorGeneric)
  })

  it('uses the error message as the package review description', () => {
    expect(getReviewErrorDescription(new Error('Report failed'))).toBe('Report failed')
  })

  it('uses an empty package review description when no error is available', () => {
    expect(getReviewErrorDescription(null)).toBe('')
  })
})
