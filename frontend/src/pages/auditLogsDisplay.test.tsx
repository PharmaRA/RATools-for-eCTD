import { describe, expect, it } from 'vitest'

import { buildAuditLogColumns, getAuditLogEntityTypeColor, truncateAuditDetails } from './auditLogsDisplay'

describe('auditLogsDisplay', () => {
  it('assigns a distinct colour per known entity type', () => {
    expect(getAuditLogEntityTypeColor('PublishJob')).toBe('blue')
    expect(getAuditLogEntityTypeColor('SequenceValidation')).toBe('orange')
    expect(getAuditLogEntityTypeColor('PublishJobArtifact')).toBe('green')
  })

  it('falls back to the neutral colour for unknown entity types', () => {
    expect(getAuditLogEntityTypeColor('SomethingNew')).toBe('default')
  })

  it('leaves short details untouched', () => {
    expect(truncateAuditDetails('MatchedPrefixes=none')).toBe('MatchedPrefixes=none')
  })

  it('truncates long details with an ellipsis', () => {
    const details = 'x'.repeat(120)

    const truncated = truncateAuditDetails(details)

    expect(truncated).toHaveLength(81)
    expect(truncated.endsWith('…')).toBe(true)
  })

  it('renders empty details as an empty string', () => {
    expect(truncateAuditDetails(null)).toBe('')
    expect(truncateAuditDetails(undefined)).toBe('')
  })

  it('exposes the audit table columns in reading order', () => {
    expect(buildAuditLogColumns().map((column) => (column as { dataIndex?: string }).dataIndex)).toEqual([
      'createdUtc',
      'entityType',
      'entityId',
      'action',
      'actor',
      'details',
    ])
  })
})
