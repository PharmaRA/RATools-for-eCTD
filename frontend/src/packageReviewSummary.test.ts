import { describe, expect, it } from 'vitest'

import { summarizeRequiredArtifacts } from './packageReviewSummary'

describe('summarizeRequiredArtifacts', () => {
  it('summarizes required artifact presence and display rows together', () => {
    const summary = summarizeRequiredArtifacts(
      [
        { name: 'BackboneXml', exists: true, sizeBytes: 100 },
        { name: 'PackageZip', exists: false, sizeBytes: 200 },
        { name: 'PackageZip', exists: true, sizeBytes: 300 },
        { name: 'ExtraArtifact', exists: true },
      ],
      ['BackboneXml', 'PublishReport', 'PackageZip'],
    )

    expect(summary.presentCount).toBe(2)
    expect(summary.existsByName).toEqual({
      BackboneXml: true,
      PublishReport: false,
      PackageZip: true,
    })
    expect(summary.rows).toEqual([
      { name: 'BackboneXml', exists: true, sizeBytes: 100 },
      { name: 'PublishReport', exists: false },
      { name: 'PackageZip', exists: false, sizeBytes: 200 },
    ])
  })
})
