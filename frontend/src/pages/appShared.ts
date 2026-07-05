import type { EctdStructureNode } from '../workspaceTree'

export interface SequenceSummary {
  sequenceNumber: string
  submissionType?: string | null
  submissionSubType?: string | null
  description?: string | null
}

export interface Application {
  id: string
  applicationNumber: string
  ectdTemplateKey?: string | null
  ectdTemplateDisplayName?: string | null
  sponsorName: string
  workingDirectoryPath?: string | null
  createdUtc?: string | null
  sequences: SequenceSummary[]
}

export interface EctdStructureResponse {
  profileName: string
  region: string
  roots: EctdStructureNode[]
}

export const formatDate = (dateStr?: string) => {
  if (!dateStr) return '-'
  return new Date(dateStr).toLocaleString()
}

export const formatOptionalText = (value?: string | null) => value || '-'

export const formatBytes = (bytes: number) => {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}

export const formatOptionalBytes = (bytes?: number | null) => bytes == null ? '-' : formatBytes(bytes)

export const getApplicationTemplateLabel = (application?: Pick<Application, 'ectdTemplateDisplayName' | 'ectdTemplateKey'> | null) => {
  return application?.ectdTemplateDisplayName || application?.ectdTemplateKey || 'Unknown Template'
}

export type StatusColor = 'success' | 'error' | 'processing' | 'default'

export const getStatusColor = (status: string): StatusColor => {
  switch (status.toLowerCase()) {
    case 'completed': return 'success'
    case 'failed': return 'error'
    case 'running': return 'processing'
    case 'pending': return 'default'
    default: return 'default'
  }
}

export type LifecycleSummary = {
  replaceTargetNotFoundCount?: number | null
  deleteTargetNotFoundCount?: number | null
  appendTargetNotFoundCount?: number | null
  ambiguousCount?: number | null
  currentSequenceCount?: number | null
}

export const getLifecycleIssueCount = (summary?: LifecycleSummary | null) => {
  if (!summary) return 0
  return (summary.replaceTargetNotFoundCount || 0)
    + (summary.deleteTargetNotFoundCount || 0)
    + (summary.appendTargetNotFoundCount || 0)
    + (summary.ambiguousCount || 0)
    + (summary.currentSequenceCount || 0)
}

export type ReportAvailability = {
  reportAvailable?: boolean | null
  reportReadable?: boolean | null
}

export const getReportAvailabilityLabel = (entry: ReportAvailability) => {
  if (!entry?.reportAvailable) return 'Missing'
  if (!entry?.reportReadable) return 'Unreadable'
  return 'Available'
}

export const getErrorMessage = (error: unknown, fallback = 'Unknown error') => {
  if (error instanceof Error && error.message) return error.message
  if (typeof error === 'string' && error.trim().length > 0) return error
  return fallback
}

export const getSectionAncestorKeys = (sectionPath: string) => {
  const segments = sectionPath.split('.').filter(Boolean)
  const keys: string[] = []

  for (let index = 0; index < segments.length; index += 1) {
    keys.push(segments.slice(0, index + 1).join('.'))
  }

  return keys
}

export const addSectionExpansionKeys = (currentKeys: string[], sectionPath: string) => {
  return Array.from(new Set([...currentKeys, ...getSectionAncestorKeys(sectionPath)]))
}
