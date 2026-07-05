import { describe, expect, it } from 'vitest'

import { addSectionExpansionKeys, formatOptionalBytes, formatOptionalText } from './appShared'

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
})
