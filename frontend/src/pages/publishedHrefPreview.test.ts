import { describe, expect, it } from 'vitest'

import { buildPublishedHrefPreview, findPublishedSequenceSegmentIndex } from './publishedHrefPreview'

describe('buildPublishedHrefPreview', () => {
  it('finds the last matching sequence segment case-insensitively', () => {
    expect(findPublishedSequenceSegmentIndex(['repo', '0001', 'archive', '0001', 'm1'], '0001')).toBe(3)
    expect(findPublishedSequenceSegmentIndex(['repo', 'ABCD', 'm1'], 'abcd')).toBe(1)
    expect(findPublishedSequenceSegmentIndex(['repo', '0002', 'm1'], '0001')).toBe(-1)
  })

  it('uses the fallback file name when storage path is missing', () => {
    expect(buildPublishedHrefPreview(undefined, '0001', 'protocol.pdf')).toBe('protocol.pdf')
    expect(buildPublishedHrefPreview('', '0001', undefined)).toBe('-')
  })

  it('builds a relative href below the matching sequence folder', () => {
    expect(buildPublishedHrefPreview('C:\\ectd\\0001\\m1\\us\\old.pdf', '0001', 'new.pdf')).toBe('m1/us/new.pdf')
    expect(buildPublishedHrefPreview('/repo/0001/m2/32p-drug-prod/old.pdf', '0001', 'renamed.pdf')).toBe('m2/32p-drug-prod/renamed.pdf')
  })

  it('falls back to the file name when the sequence folder is absent or terminal', () => {
    expect(buildPublishedHrefPreview('/repo/0002/m1/us/old.pdf', '0001', 'new.pdf')).toBe('new.pdf')
    expect(buildPublishedHrefPreview('/repo/0001', '0001', 'new.pdf')).toBe('new.pdf')
  })
})
