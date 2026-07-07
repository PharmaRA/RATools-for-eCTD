import { describe, expect, it, vi } from 'vitest'

import {
  buildExecutePublishJobUrl,
  buildPublishHistoryRequestUrl,
  buildPublishJobArtifactsUrl,
  buildPublishJobArtifactDownloadUrl,
  buildPublishJobReportUrl,
  buildPublishJobsUrl,
  buildPublishJobUrl,
  executePublishJob,
  loadPublishHistory,
  loadPublishJobArtifacts,
  loadPublishJobReport,
} from './publishActions'

describe('publishActions', () => {
  it('builds publish job endpoint URLs', () => {
    expect(buildPublishJobsUrl()).toBe('/api/publish-jobs')
    expect(buildExecutePublishJobUrl()).toBe('/api/publish-jobs/execute')
    expect(buildPublishJobUrl('job-1')).toBe('/api/publish-jobs/job-1')
    expect(buildPublishJobReportUrl('job-1')).toBe('/api/publish-jobs/job-1/report')
    expect(buildPublishJobArtifactsUrl('job-1')).toBe('/api/publish-jobs/job-1/artifacts')
  })

  it('executes a publish job using only application, sequence, and output directory', async () => {
    const request = vi
      .fn()
      .mockResolvedValueOnce({ succeeded: true })

    await executePublishJob({
      applicationId: 'app-1',
      sequenceNumber: '0001',
      outputDirectoryPath: 'E:/exports/submission-a',
    }, request)

    expect(request).toHaveBeenCalledOnce()
    expect(request).toHaveBeenCalledWith('/api/publish-jobs/execute', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        applicationId: 'app-1',
        sequenceNumber: '0001',
        outputDirectoryPath: 'E:/exports/submission-a',
      }),
    })
  })

  it('loads a publish job report by job id', async () => {
    const report = { succeeded: true }
    const request = vi.fn().mockResolvedValueOnce(report)

    const result = await loadPublishJobReport('job-1', request)

    expect(request).toHaveBeenCalledWith('/api/publish-jobs/job-1/report')
    expect(result).toEqual(report)
  })

  it('loads publish job artifacts by job id', async () => {
    const artifacts = { artifacts: [{ name: 'PublishReport', exists: true }] }
    const request = vi.fn().mockResolvedValueOnce(artifacts)

    const result = await loadPublishJobArtifacts('job-1', request)

    expect(request).toHaveBeenCalledWith('/api/publish-jobs/job-1/artifacts')
    expect(result).toEqual(artifacts)
  })

  it('builds publish job artifact download urls', () => {
    expect(buildPublishJobArtifactDownloadUrl('job-1', 'PublishReport'))
      .toBe('/api/publish-jobs/job-1/artifacts/PublishReport/download')
  })

  it('builds a publish history request URL with pagination and filters', () => {
    expect(buildPublishHistoryRequestUrl('app-1', 2, 50, {
      sequenceNumber: '0002',
      status: 'Completed',
      readinessStatus: 'Ready',
    })).toBe('/api/applications/app-1/publish-history?page=2&pageSize=50&sequenceNumber=0002&status=Completed&readinessStatus=Ready')
  })

  it('omits empty publish history request filters', () => {
    expect(buildPublishHistoryRequestUrl('app-1', 1, 20, {
      sequenceNumber: '',
      status: undefined,
      readinessStatus: null,
    })).toBe('/api/applications/app-1/publish-history?page=1&pageSize=20')
  })

  it('loads publish history with pagination and filters', async () => {
    const history = { entries: [], totalCount: 0 }
    const request = vi.fn().mockResolvedValueOnce(history)

    const result = await loadPublishHistory({
      applicationId: 'app-1',
      page: 1,
      pageSize: 20,
      filters: { readinessStatus: 'Blocked' },
    }, request)

    expect(request).toHaveBeenCalledWith('/api/applications/app-1/publish-history?page=1&pageSize=20&readinessStatus=Blocked')
    expect(result).toEqual(history)
  })
})
