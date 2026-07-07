import { describe, expect, it } from 'vitest'

import {
  addSectionExpansionKeys,
  formatOptionalBytes,
  formatOptionalCount,
  formatOptionalList,
  formatOptionalText,
  buildLifecycleIssueCountItems,
  getErrorSeverityTagColor,
  getOptionalArray,
  getLifecycleIssueCountValues,
} from './appShared'

describe('appShared tree helpers', () => {
  it('adds section ancestors without duplicating existing expanded keys', () => {
    const keys = addSectionExpansionKeys(['m1', 'm3'], 'm3.2.1')

    expect(keys).toEqual(['m1', 'm3', 'm3.2', 'm3.2.1'])
  })
})

describe('appShared display helpers', () => {
  it('uses a dash when optional text is missing', () => {
    expect(formatOptionalText('m1/us/file.pdf')).toBe('m1/us/file.pdf')
    expect(formatOptionalText('')).toBe('-')
    expect(formatOptionalText(null)).toBe('-')
    expect(formatOptionalText(undefined)).toBe('-')
  })

  it('formats optional byte values with a dash when missing', () => {
    expect(formatOptionalBytes(0)).toBe('0 B')
    expect(formatOptionalBytes(1536)).toBe('1.5 KB')
    expect(formatOptionalBytes(null)).toBe('-')
    expect(formatOptionalBytes(undefined)).toBe('-')
  })

  it('formats optional count values with a dash when missing', () => {
    expect(formatOptionalCount(3)).toBe(3)
    expect(formatOptionalCount(0)).toBe(0)
    expect(formatOptionalCount(null)).toBe('-')
    expect(formatOptionalCount(undefined)).toBe('-')
  })

  it('formats optional list values with a configurable fallback', () => {
    expect(formatOptionalList(['Lifecycle', 'Validation'])).toBe('Lifecycle, Validation')
    expect(formatOptionalList([])).toBe('-')
    expect(formatOptionalList(undefined)).toBe('-')
    expect(formatOptionalList(null, 'None')).toBe('None')
  })

  it.each([
    ['Error', 'red'],
    ['error', 'red'],
    ['Warning', 'gold'],
    ['Info', 'gold'],
  ])('maps %s severity to shared tag color %s', (severity, color) => {
    expect(getErrorSeverityTagColor(severity)).toBe(color)
  })
})

describe('appShared collection helpers', () => {
  it('reads optional arrays with an empty fallback', () => {
    const values = ['issue-1', 'issue-2']

    expect(getOptionalArray(values)).toBe(values)
    expect(getOptionalArray(null)).toEqual([])
    expect(getOptionalArray(undefined)).toEqual([])
  })
})

describe('appShared lifecycle helpers', () => {
  it('builds lifecycle issue count items with shared labels and zero defaults', () => {
    expect(buildLifecycleIssueCountItems({
      replaceTargetNotFoundCount: 2,
      deleteTargetNotFoundCount: null,
      appendTargetNotFoundCount: 3,
      ambiguousCount: undefined,
      currentSequenceCount: 1,
    })).toEqual([
      { key: 'replace-missing', label: 'Replace Missing', value: 2 },
      { key: 'delete-missing', label: 'Delete Missing', value: 0 },
      { key: 'append-missing', label: 'Append Missing', value: 3 },
      { key: 'ambiguous', label: 'Ambiguous', value: 0 },
      { key: 'current-sequence', label: 'Current Sequence', value: 1 },
    ])
  })

  it('reads lifecycle issue count values with zero defaults', () => {
    expect(getLifecycleIssueCountValues({
      replaceTargetNotFoundCount: 2,
      deleteTargetNotFoundCount: null,
      appendTargetNotFoundCount: 3,
      ambiguousCount: undefined,
      currentSequenceCount: 1,
    })).toEqual([2, 0, 3, 0, 1])

    expect(getLifecycleIssueCountValues(null)).toEqual([0, 0, 0, 0, 0])
    expect(getLifecycleIssueCountValues(undefined)).toEqual([0, 0, 0, 0, 0])
  })
})
