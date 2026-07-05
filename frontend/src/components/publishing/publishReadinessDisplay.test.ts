import { describe, expect, it } from 'vitest'

import {
  formatMissingMetadataFields,
  formatReadinessFieldName,
  formatReadinessHistoryCountHint,
  formatReadinessMissingMetadataHint,
  formatReadinessCount,
  formatReadinessReadyStatus,
  formatReadinessStatus,
  getPublishReadinessStatusTagProps,
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

  it('formats the primary missing metadata hint for history rows', () => {
    expect(formatReadinessMissingMetadataHint(['Applicant'])).toBe('Applicant')
    expect(formatReadinessMissingMetadataHint(['Applicant', 'Submission Type'])).toBe('Applicant +1')
    expect(formatReadinessMissingMetadataHint([])).toBeNull()
    expect(formatReadinessMissingMetadataHint(undefined)).toBeNull()
  })

  it('formats readiness history count hints after metadata hints are handled', () => {
    expect(formatReadinessHistoryCountHint({ isReady: true, warningCount: 2 }, null)).toBe('Warnings: 2')
    expect(formatReadinessHistoryCountHint({ isReady: false, blockingErrorCount: 3 }, null)).toBe('Blocking errors: 3')
    expect(formatReadinessHistoryCountHint({ isReady: false, blockingErrorCount: 3 }, 'Applicant')).toBeNull()
    expect(formatReadinessHistoryCountHint({ isReady: true, warningCount: 0 }, null)).toBeNull()
  })

  it('formats publish readiness ready status', () => {
    expect(formatReadinessReadyStatus(true)).toBe('Yes')
    expect(formatReadinessReadyStatus(false)).toBe('No')
    expect(formatReadinessReadyStatus(undefined)).toBe('No')
  })

  it('uses a dash when publish readiness status is missing', () => {
    expect(formatReadinessStatus('Blocked')).toBe('Blocked')
    expect(formatReadinessStatus('')).toBe('-')
    expect(formatReadinessStatus(undefined)).toBe('-')
  })

  it('builds history readiness status tag props with status fallbacks', () => {
    expect(getPublishReadinessStatusTagProps({ isReady: true, status: 'Ready' })).toEqual({
      color: 'green',
      label: 'Ready',
    })
    expect(getPublishReadinessStatusTagProps({ isReady: true, status: '' })).toEqual({
      color: 'green',
      label: 'Ready',
    })
    expect(getPublishReadinessStatusTagProps({ isReady: false, status: 'Blocked' })).toEqual({
      color: 'red',
      label: 'Blocked',
    })
    expect(getPublishReadinessStatusTagProps({ isReady: false, status: '' })).toEqual({
      color: 'red',
      label: 'Blocked',
    })
  })

  it('uses a dash when publish readiness finding field is missing', () => {
    expect(formatReadinessFieldName('Applicant')).toBe('Applicant')
    expect(formatReadinessFieldName(null)).toBe('-')
    expect(formatReadinessFieldName(undefined)).toBe('-')
  })

  it('uses a dash only when a publish readiness count is missing', () => {
    expect(formatReadinessCount(2)).toBe(2)
    expect(formatReadinessCount(0)).toBe(0)
    expect(formatReadinessCount(null)).toBe('-')
    expect(formatReadinessCount(undefined)).toBe('-')
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
