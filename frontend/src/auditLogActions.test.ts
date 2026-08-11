import { describe, expect, it, vi } from 'vitest'

import {
  AUDIT_LOG_ENTITY_TYPES,
  buildAuditLogsRequestUrl,
  buildAuditLogsUrl,
  loadAuditLogs,
} from './auditLogActions'

describe('auditLogActions', () => {
  it('builds the audit logs endpoint URL', () => {
    expect(buildAuditLogsUrl()).toBe('/api/audit-logs')
  })

  it('builds a request URL with only pagination when no filter is set', () => {
    expect(buildAuditLogsRequestUrl(1, 20, {})).toBe('/api/audit-logs?page=1&pageSize=20')
  })

  it('appends every provided filter to the query string', () => {
    const url = buildAuditLogsRequestUrl(2, 50, {
      entityType: 'PublishJob',
      entityId: 'job-1',
      action: 'Completed',
      createdFromUtc: '2026-07-01T00:00:00.000Z',
      createdToUtc: '2026-07-31T23:59:59.000Z',
    })

    const params = new URLSearchParams(url.split('?')[1])
    expect(params.get('page')).toBe('2')
    expect(params.get('pageSize')).toBe('50')
    expect(params.get('entityType')).toBe('PublishJob')
    expect(params.get('entityId')).toBe('job-1')
    expect(params.get('action')).toBe('Completed')
    expect(params.get('createdFromUtc')).toBe('2026-07-01T00:00:00.000Z')
    expect(params.get('createdToUtc')).toBe('2026-07-31T23:59:59.000Z')
  })

  it('omits empty and null filter values', () => {
    const url = buildAuditLogsRequestUrl(1, 20, {
      entityType: '',
      entityId: null,
      action: undefined,
    })

    expect(url).toBe('/api/audit-logs?page=1&pageSize=20')
  })

  it('loads a page of audit logs through the injected request function', async () => {
    const page = {
      page: 1,
      pageSize: 20,
      totalCount: 1,
      items: [{
        id: 'log-1',
        entityType: 'PublishJob',
        entityId: 'job-1',
        action: 'Completed',
        actor: 'system',
        details: null,
        createdUtc: '2026-07-26T08:00:00Z',
      }],
    }
    const request = vi.fn().mockResolvedValueOnce(page)

    const result = await loadAuditLogs({ page: 1, pageSize: 20, filters: { entityType: 'PublishJob' } }, request)

    expect(request).toHaveBeenCalledWith('/api/audit-logs?page=1&pageSize=20&entityType=PublishJob')
    expect(result).toEqual(page)
  })

  it('forwards an abort signal to the request function', async () => {
    const controller = new AbortController()
    const request = vi.fn().mockResolvedValueOnce({ page: 1, pageSize: 20, totalCount: 0, items: [] })

    await loadAuditLogs({ page: 1, pageSize: 20, filters: {}, signal: controller.signal }, request)

    expect(request).toHaveBeenCalledWith(
      '/api/audit-logs?page=1&pageSize=20',
      { signal: controller.signal },
    )
  })

  it('exposes the entity types written by the backend audit producers', () => {
    expect(AUDIT_LOG_ENTITY_TYPES).toEqual(['PublishJob', 'SequenceValidation', 'PublishJobArtifact'])
  })
})
