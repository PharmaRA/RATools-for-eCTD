import { describe, expect, it } from 'vitest'

import { isAllowedEctdFileName, splitFileName } from './ectdFileTypes'

describe('ectdFileTypes', () => {
  it('accepts configured eCTD whitelist extensions case-insensitively', () => {
    expect(isAllowedEctdFileName('study-report.PDF')).toBe(true)
    expect(isAllowedEctdFileName('structure.xml')).toBe(true)
    expect(isAllowedEctdFileName('tabulation.sas7bdat')).toBe(true)
    expect(isAllowedEctdFileName('analysis.XPT')).toBe(true)
    expect(isAllowedEctdFileName('image.tiff')).toBe(true)
  })

  it('rejects missing, invalid, and path-like file names', () => {
    expect(isAllowedEctdFileName('')).toBe(false)
    expect(isAllowedEctdFileName('no-extension')).toBe(false)
    expect(isAllowedEctdFileName('payload.exe')).toBe(false)
    expect(isAllowedEctdFileName('../payload.pdf')).toBe(false)
    expect(isAllowedEctdFileName('folder/payload.pdf')).toBe(false)
  })

  it('splits file name into prefix and extension', () => {
    expect(splitFileName('quality-summary.pdf')).toEqual({ prefix: 'quality-summary', extension: '.pdf' })
    expect(splitFileName('filename-without-extension')).toEqual({ prefix: 'filename-without-extension', extension: '' })
  })
})
