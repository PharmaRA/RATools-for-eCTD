import { describe, expect, it } from 'vitest'

import { ApiRequestError } from '../../apiClient'
import {
  buildPackageReviewChecklistExportRows,
  buildPackageReviewExportErrors,
  buildPackageReviewExport,
  buildPackageReviewExportFilename,
  buildPackageReviewIntegrityRiskSummaryExport,
  buildPackageReviewRequiredArtifactExportRows,
  buildPackageReviewRiskSummaryExport,
  buildPublishReadinessCategorySummaryExports,
  buildPublishReadinessMissingMetadataFieldExports,
  buildPublishReadinessFindingExports,
} from './packageReviewExport'

describe('packageReviewExport', () => {
  it('builds package review export filenames with unknown sequence fallback', () => {
    expect(buildPackageReviewExportFilename('0007', 'job-42')).toBe('package-review-0007-job-42.json')
    expect(buildPackageReviewExportFilename(null, 'job-99')).toBe('package-review-unknown-job-99.json')
    expect(buildPackageReviewExportFilename('', 'job-100')).toBe('package-review-unknown-job-100.json')
  })

  it('builds risk summary exports from loaded and unavailable report data', () => {
    expect(buildPackageReviewRiskSummaryExport({
      reportLoaded: true,
      lifecycleIssueCount: 3,
      report: {
        errorCount: 2,
        warningCount: 1,
        integritySummary: {
          missingFilesCount: 4,
          missingZipEntriesCount: 5,
          mismatchedArtifactsCount: 6,
        },
      },
    })).toEqual({
      validationErrors: 2,
      warnings: 1,
      lifecycleIssues: 3,
      missingFiles: 4,
      missingZipEntries: 5,
      mismatchedArtifacts: 6,
    })

    expect(buildPackageReviewRiskSummaryExport({
      reportLoaded: false,
      lifecycleIssueCount: 7,
      report: null,
    })).toEqual({
      validationErrors: null,
      warnings: null,
      lifecycleIssues: null,
      missingFiles: null,
      missingZipEntries: null,
      mismatchedArtifacts: null,
    })
  })

  it('builds package review integrity risk summary exports from optional summary data', () => {
    expect(buildPackageReviewIntegrityRiskSummaryExport({
      missingFilesCount: 4,
      missingZipEntriesCount: 0,
      mismatchedArtifactsCount: null,
    })).toEqual({
      missingFiles: 4,
      missingZipEntries: 0,
      mismatchedArtifacts: null,
    })

    expect(buildPackageReviewIntegrityRiskSummaryExport(null)).toEqual({
      missingFiles: null,
      missingZipEntries: null,
      mismatchedArtifacts: null,
    })
  })

  it('builds required artifact export rows with normalized existence flags', () => {
    expect(buildPackageReviewRequiredArtifactExportRows([
      { name: 'BackboneXml', exists: true, sizeBytes: 123, contentType: 'application/xml' },
      { name: 'PackageZip', exists: false },
      { name: 'ValidationReport' },
    ])).toEqual([
      { name: 'BackboneXml', exists: true, sizeBytes: 123, contentType: 'application/xml' },
      { name: 'PackageZip', exists: false, sizeBytes: undefined, contentType: undefined },
      { name: 'ValidationReport', exists: false, sizeBytes: undefined, contentType: undefined },
    ])

    expect(buildPackageReviewRequiredArtifactExportRows([])).toEqual([])
  })

  it('builds checklist export rows with pass and fail statuses', () => {
    expect(buildPackageReviewChecklistExportRows([
      { key: 'publish-succeeded', check: 'Publish succeeded', pass: true, detail: 'Published' },
      { key: 'validation-errors', check: 'Validation errors', pass: false, detail: '1 error(s)' },
    ])).toEqual([
      { key: 'publish-succeeded', check: 'Publish succeeded', status: 'Pass', detail: 'Published' },
      { key: 'validation-errors', check: 'Validation errors', status: 'Fail', detail: '1 error(s)' },
    ])

    expect(buildPackageReviewChecklistExportRows([])).toEqual([])
  })

  it('builds package review export errors from optional load failures', () => {
    expect(buildPackageReviewExportErrors({
      reportError: new ApiRequestError(410, 'Report expired'),
      artifactsError: new Error('Artifacts unavailable'),
    })).toEqual({
      report: { message: 'Report expired', status: 410 },
      artifacts: { message: 'Artifacts unavailable' },
    })

    expect(buildPackageReviewExportErrors({
      reportError: null,
      artifactsError: null,
    })).toBeUndefined()
  })

  it('builds publish readiness finding exports from optional readiness data', () => {
    expect(buildPublishReadinessFindingExports({
      findings: [
        {
          severity: 'Error',
          code: 'MISSING_METADATA',
          category: 'metadata',
          fieldName: 'applicationNumber',
          recommendedAction: 'Complete metadata',
        },
        {
          severity: 'Warning',
          code: 'CHECK_VALUE',
          category: 'quality',
          recommendedAction: 'Review value',
        },
      ],
    })).toEqual([
      {
        severity: 'Error',
        code: 'MISSING_METADATA',
        category: 'metadata',
        fieldName: 'applicationNumber',
        recommendedAction: 'Complete metadata',
      },
      {
        severity: 'Warning',
        code: 'CHECK_VALUE',
        category: 'quality',
        fieldName: null,
        recommendedAction: 'Review value',
      },
    ])
    expect(buildPublishReadinessFindingExports({})).toEqual([])
    expect(buildPublishReadinessFindingExports(null)).toEqual([])
    expect(buildPublishReadinessFindingExports(undefined)).toEqual([])
  })

  it('reads publish readiness missing metadata field exports from optional readiness data', () => {
    const fields = ['applicationNumber', 'sponsorName']

    expect(buildPublishReadinessMissingMetadataFieldExports({ missingMetadataFields: fields })).toBe(fields)
    expect(buildPublishReadinessMissingMetadataFieldExports({})).toEqual([])
    expect(buildPublishReadinessMissingMetadataFieldExports(null)).toEqual([])
    expect(buildPublishReadinessMissingMetadataFieldExports(undefined)).toEqual([])
  })

  it('reads publish readiness category summary exports from optional readiness data', () => {
    const categorySummaries = [{ category: 'metadata', blockingErrorCount: 1, warningCount: 2, findingCount: 3 }]

    expect(buildPublishReadinessCategorySummaryExports({ categorySummaries })).toBe(categorySummaries)
    expect(buildPublishReadinessCategorySummaryExports({})).toEqual([])
    expect(buildPublishReadinessCategorySummaryExports(null)).toEqual([])
    expect(buildPublishReadinessCategorySummaryExports(undefined)).toEqual([])
  })

  it('builds the review export payload and filename from loaded review data', () => {
    const integrityFinding = {
      severity: 'Error',
      type: 'MissingFile',
      path: 'm1/us/file.pdf',
      message: 'File is missing',
    }

    const result = buildPackageReviewExport({
      jobId: 'job-42',
      generatedAtUtc: '2026-07-05T04:00:00.000Z',
      reportLoaded: true,
      readyForSubmission: false,
      lifecycleIssueCount: 3,
      reportError: new ApiRequestError(410, 'Report expired'),
      artifactsError: new Error('Artifacts unavailable'),
      checklistRows: [
        { key: 'publish-succeeded', check: 'Publish succeeded', pass: true, detail: 'Published' },
        { key: 'validation-errors', check: 'Validation errors', pass: false, detail: '2 error(s)' },
      ],
      report: {
        sequenceNumber: '0007',
        validationProfile: 'FDA',
        errorCount: 2,
        warningCount: 1,
        integritySummary: {
          missingFilesCount: 4,
          missingZipEntriesCount: 5,
          mismatchedArtifactsCount: 6,
        },
        publishReadiness: {
          isReady: false,
          status: 'Blocked',
          blockingErrorCount: 2,
          warningCount: 1,
          missingMetadataFields: ['applicationNumber'],
          categorySummaries: [{ category: 'metadata', blockingErrorCount: 2, warningCount: 1, findingCount: 3 }],
          findings: [{
            severity: 'Error',
            code: 'MISSING_METADATA',
            category: 'metadata',
            recommendedAction: 'Complete metadata',
          }],
        },
      },
      requiredArtifactRows: [
        { name: 'BackboneXml', exists: true, sizeBytes: 123, contentType: 'application/xml' },
        { name: 'PackageZip', exists: false },
      ],
      integrityFindings: [integrityFinding],
    })

    expect(result.filename).toBe('package-review-0007-job-42.json')
    expect(result.value).toEqual({
      reportVersion: 'package-review-export-v1',
      generatedAtUtc: '2026-07-05T04:00:00.000Z',
      publishJobId: 'job-42',
      sequenceNumber: '0007',
      validationProfile: 'FDA',
      verdict: 'NotReadyForSubmission',
      checklist: [
        { key: 'publish-succeeded', check: 'Publish succeeded', status: 'Pass', detail: 'Published' },
        { key: 'validation-errors', check: 'Validation errors', status: 'Fail', detail: '2 error(s)' },
      ],
      riskSummary: {
        validationErrors: 2,
        warnings: 1,
        lifecycleIssues: 3,
        missingFiles: 4,
        missingZipEntries: 5,
        mismatchedArtifacts: 6,
      },
      publishReadiness: {
        isReady: false,
        status: 'Blocked',
        blockingErrorCount: 2,
        warningCount: 1,
        missingMetadataFields: ['applicationNumber'],
        categorySummaries: [{ category: 'metadata', blockingErrorCount: 2, warningCount: 1, findingCount: 3 }],
        findings: [{
          severity: 'Error',
          code: 'MISSING_METADATA',
          category: 'metadata',
          fieldName: null,
          recommendedAction: 'Complete metadata',
        }],
      },
      requiredArtifacts: [
        { name: 'BackboneXml', exists: true, sizeBytes: 123, contentType: 'application/xml' },
        { name: 'PackageZip', exists: false, sizeBytes: undefined, contentType: undefined },
      ],
      integrityFindings: [integrityFinding],
      errors: {
        report: { message: 'Report expired', status: 410 },
        artifacts: { message: 'Artifacts unavailable' },
      },
    })
  })

  it('omits errors and uses the unknown sequence filename when review data is partial', () => {
    const result = buildPackageReviewExport({
      jobId: 'job-99',
      generatedAtUtc: '2026-07-05T04:01:00.000Z',
      report: null,
      reportLoaded: false,
      readyForSubmission: true,
      lifecycleIssueCount: 7,
      reportError: null,
      artifactsError: null,
      checklistRows: [],
      requiredArtifactRows: [],
      integrityFindings: [],
    })

    expect(result.filename).toBe('package-review-unknown-job-99.json')
    expect(result.value).not.toHaveProperty('errors')
    expect(result.value.riskSummary.lifecycleIssues).toBeNull()
    expect(result.value.verdict).toBe('ReadyForSubmission')
  })
})
