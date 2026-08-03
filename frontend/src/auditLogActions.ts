import { apiFetch } from './apiClient'

export type AuditLogEntry = {
  id: string
  entityType: string
  entityId: string
  action: string
  actor: string
  details?: string | null
  createdUtc: string
}

export type AuditLogPage = {
  page: number
  pageSize: number
  totalCount: number
  items: AuditLogEntry[]
}

export type AuditLogFilterValues = {
  entityType?: string | null
  entityId?: string | null
  action?: string | null
  createdFromUtc?: string | null
  createdToUtc?: string | null
}

export type LoadAuditLogsRequest = {
  page: number
  pageSize: number
  filters: AuditLogFilterValues
}

export const buildAuditLogsUrl = () => '/api/audit-logs'

export const buildAuditLogsRequestUrl = (
  page: number,
  pageSize: number,
  values: AuditLogFilterValues,
) => {
  const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
  if (values.entityType) params.append('entityType', values.entityType)
  if (values.entityId) params.append('entityId', values.entityId)
  if (values.action) params.append('action', values.action)
  if (values.createdFromUtc) params.append('createdFromUtc', values.createdFromUtc)
  if (values.createdToUtc) params.append('createdToUtc', values.createdToUtc)

  return `${buildAuditLogsUrl()}?${params.toString()}`
}

export const loadAuditLogs = async (
  request: LoadAuditLogsRequest,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<AuditLogPage> => {
  return executeRequest(buildAuditLogsRequestUrl(request.page, request.pageSize, request.filters))
}

// 后端已知的实体类型（审计写入方：PublishJobService / SequenceValidationService /
// 发布产物记录），供过滤下拉使用。未知类型仍可经 entityId 精确查询。
export const AUDIT_LOG_ENTITY_TYPES = ['PublishJob', 'SequenceValidation', 'PublishJobArtifact'] as const
