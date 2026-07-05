import { createElement } from 'react'
import { Tag } from 'antd'

export const getImportIssueSeverityDisplayMeta = (value: string) => {
  const isError = String(value).toLowerCase() === 'error'
  return {
    alertType: isError ? 'error' : 'warning',
    tagColor: isError ? 'red' : 'gold',
  } as const
}

export const getImportResultIssues = <T>(
  result?: { issues?: T[] | null } | null,
): T[] => result?.issues || []

type ImportIssueSummaryCounts = {
  totalIssueCount: number
  warningCount: number
  errorCount: number
  lifecycleWarningCount: number
}

export const buildImportIssueSummaryItems = ({
  totalIssueCount,
  warningCount,
  errorCount,
  lifecycleWarningCount,
}: ImportIssueSummaryCounts) => [
  { key: 'total', color: 'blue', label: `${totalIssueCount} total issues` },
  { key: 'warnings', color: 'gold', label: `${warningCount} warnings` },
  { key: 'errors', color: 'red', label: `${errorCount} errors` },
  {
    key: 'lifecycle-target-warnings',
    color: lifecycleWarningCount > 0 ? 'gold' : 'green',
    label: `${lifecycleWarningCount} lifecycle target warnings`,
  },
]

export const renderImportIssueSeverityTag = (value: string) => {
  return createElement(Tag, { color: getImportIssueSeverityDisplayMeta(value).tagColor }, value)
}

export const formatImportIssueSequenceNumber = (value?: string | null) => value || '-'

export const buildImportIssueColumns = () => [
  { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 110, render: renderImportIssueSeverityTag },
  { title: 'Code', dataIndex: 'code', key: 'code', width: 220 },
  { title: 'Sequence', dataIndex: 'sequenceNumber', key: 'sequenceNumber', width: 130, render: formatImportIssueSequenceNumber },
  { title: 'Message', dataIndex: 'message', key: 'message' },
]
