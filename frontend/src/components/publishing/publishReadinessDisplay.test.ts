import { describe, expect, it } from 'vitest'

import {
  formatMissingMetadataFields,
  getPublishReadinessCategoryKey,
  getPublishReadinessFindingKey,
} from './publishReadinessDisplay'

describe('publishReadinessDisplay', () => {
  it('formats missing metadata fields as a comma-separated list', () => {
    expect(formatMissingMetadataFields(['Applicant', 'Submission Type'])).toBe('Applicant, Submission Type')
  })

  it('uses None when no metadata fields are missing', () => {
    expect(formatMissingMetadataFields([])).toBe('None')
    expect(formatMissingMetadataFields(undefined)).toBe('None')
  })

  it('uses category as the publish readiness category row key', () => {
    expect(getPublishReadinessCategoryKey({ category: 'Metadata' })).toBe('Metadata')
  })

  it('builds a stable publish readiness finding row key', () => {
    expect(getPublishReadinessFindingKey({ code: 'MISSING_FIELD', fieldName: 'Applicant' }, 2))
      .toBe('MISSING_FIELD-Applicant-2')
    expect(getPublishReadinessFindingKey({ code: 'GLOBAL' }, 0)).toBe('GLOBAL-none-0')
  })
})
