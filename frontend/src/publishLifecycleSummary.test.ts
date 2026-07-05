import { describe, expect, it } from 'vitest'

import { getLifecycleMatches, summarizeLifecycleMatches } from './publishLifecycleSummary'

describe('summarizeLifecycleMatches', () => {
  it('reads lifecycle matches from optional validation reports', () => {
    const lifecycleMatches = [{ resultCode: 'MATCHED' }, { resultCode: 'DELETE_TARGET_NOT_FOUND' }]

    expect(getLifecycleMatches({ validationReport: { lifecycleMatches } })).toBe(lifecycleMatches)
    expect(getLifecycleMatches({ validationReport: {} })).toEqual([])
    expect(getLifecycleMatches({})).toEqual([])
    expect(getLifecycleMatches(null)).toEqual([])
    expect(getLifecycleMatches(undefined)).toEqual([])
  })

  it('counts lifecycle match result codes in one summary', () => {
    const summary = summarizeLifecycleMatches([
      { resultCode: 'MATCHED' },
      { resultCode: 'MATCHED' },
      { resultCode: 'REPLACE_TARGET_NOT_FOUND' },
      { resultCode: 'DELETE_TARGET_NOT_FOUND' },
      { resultCode: 'APPEND_TARGET_NOT_FOUND' },
      { resultCode: 'LIFECYCLE_TARGET_AMBIGUOUS' },
      { resultCode: 'LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE' },
      { resultCode: 'UNKNOWN' },
    ])

    expect(summary).toEqual({
      matchedCount: 2,
      replaceTargetNotFoundCount: 1,
      deleteTargetNotFoundCount: 1,
      appendTargetNotFoundCount: 1,
      ambiguousCount: 1,
      currentSequenceCount: 1,
      issueCount: 6,
    })
  })
})
