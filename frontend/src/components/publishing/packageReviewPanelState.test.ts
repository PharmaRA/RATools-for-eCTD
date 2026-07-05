import { describe, expect, it } from 'vitest'

import { buildPackageReviewPanelState } from './packageReviewPanelState'

describe('packageReviewPanelState', () => {
  it('shows loading while a job has no loaded report, artifacts, or errors', () => {
    expect(buildPackageReviewPanelState({
      jobId: 'job-1',
      loading: false,
      report: null,
      reportError: null,
      artifacts: [],
      artifactsLoaded: false,
      artifactsError: null,
    })).toEqual({
      reportLoaded: false,
      reviewLoading: true,
      reviewExportAvailable: false,
    })
  })

  it('uses loaded report and artifacts availability for review actions', () => {
    expect(buildPackageReviewPanelState({
      jobId: 'job-1',
      loading: false,
      report: { sequenceNumber: '0001' },
      reportError: null,
      artifacts: [],
      artifactsLoaded: false,
      artifactsError: null,
    })).toEqual({
      reportLoaded: true,
      reviewLoading: false,
      reviewExportAvailable: true,
    })

    expect(buildPackageReviewPanelState({
      jobId: 'job-1',
      loading: false,
      report: null,
      reportError: new Error('Report unavailable'),
      artifacts: [],
      artifactsLoaded: true,
      artifactsError: null,
    })).toEqual({
      reportLoaded: false,
      reviewLoading: false,
      reviewExportAvailable: true,
    })
  })
})
