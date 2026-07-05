import { describe, expect, it } from 'vitest'

import { ApiRequestError } from '../../apiClient'
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
    [404, 'Report or artifacts were not found (404)'],
    [409, 'Publish job is not ready (409)'],
    [410, 'Publish data is unavailable (410)'],
    [422, 'Publish report is corrupted (422)'],
    [500, 'Unable to load package review data (500)'],
  ])('maps API status %s to a package review title', (status, title) => {
    expect(getReviewErrorTitle(new ApiRequestError(status, 'Failed'))).toBe(title)
  })

  it('uses the generic package review title for non-API errors', () => {
    expect(getReviewErrorTitle(new Error('Failed'))).toBe('Unable to load package review data')
  })

  it('uses the error message as the package review description', () => {
    expect(getReviewErrorDescription(new Error('Report failed'))).toBe('Report failed')
  })

  it('uses an empty package review description when no error is available', () => {
    expect(getReviewErrorDescription(null)).toBe('')
  })
})
