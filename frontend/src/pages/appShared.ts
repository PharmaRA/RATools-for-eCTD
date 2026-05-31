import type { EctdStructureNode } from '../workspaceTree'

export interface Application {
  id: string
  applicationNumber: string
  ectdTemplateKey?: string
  ectdTemplateDisplayName?: string
  sponsorName: string
  workingDirectoryPath?: string
  createdUtc: string
  sequences: any[]
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

export const formatBytes = (bytes: number) => {
  if (bytes === 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i]
}

export const getApplicationTemplateLabel = (application?: Pick<Application, 'ectdTemplateDisplayName' | 'ectdTemplateKey'> | null) => {
  return application?.ectdTemplateDisplayName || application?.ectdTemplateKey || 'Unknown Template'
}

export const getStatusColor = (status: string) => {
  switch (status.toLowerCase()) {
    case 'completed': return 'success'
    case 'failed': return 'error'
    case 'running': return 'processing'
    case 'pending': return 'default'
    default: return 'default'
  }
}

export const getLifecycleIssueCount = (summary?: any) => {
  if (!summary) return 0
  return (summary.replaceTargetNotFoundCount || 0)
    + (summary.deleteTargetNotFoundCount || 0)
    + (summary.appendTargetNotFoundCount || 0)
    + (summary.ambiguousCount || 0)
    + (summary.currentSequenceCount || 0)
}

export const getReportAvailabilityLabel = (entry: any) => {
  if (!entry?.reportAvailable) return 'Missing'
  if (!entry?.reportReadable) return 'Unreadable'
  return 'Available'
}

export const getSectionAncestorKeys = (sectionPath: string) => {
  const segments = sectionPath.split('.').filter(Boolean)
  const keys: string[] = []

  for (let index = 0; index < segments.length; index += 1) {
    keys.push(segments.slice(0, index + 1).join('.'))
  }

  return keys
}
