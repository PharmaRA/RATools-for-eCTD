import { describe, expect, it } from 'vitest'

import { getArtifactsFromResponse } from './packageReviewArtifacts'

describe('packageReviewArtifacts', () => {
  it('keeps only artifact-like rows from a direct array response', () => {
    expect(getArtifactsFromResponse([
      { name: 'PackageZip', exists: true, sizeBytes: 123 },
      { exists: true },
      null,
      { name: 'PublishReport', contentType: 'application/json' },
    ])).toEqual([
      { name: 'PackageZip', exists: true, sizeBytes: 123 },
      { name: 'PublishReport', contentType: 'application/json' },
    ])
  })

  it('keeps only artifact-like rows from a wrapped artifacts response', () => {
    expect(getArtifactsFromResponse({
      artifacts: [
        { name: 'BackboneXml', exists: true },
        'not-an-artifact',
        { name: 'PublishReport', exists: false },
      ],
    })).toEqual([
      { name: 'BackboneXml', exists: true },
      { name: 'PublishReport', exists: false },
    ])
  })

  it('returns an empty list when response shape is not usable', () => {
    expect(getArtifactsFromResponse(undefined)).toEqual([])
    expect(getArtifactsFromResponse({ artifacts: 'missing-list' })).toEqual([])
  })
})
