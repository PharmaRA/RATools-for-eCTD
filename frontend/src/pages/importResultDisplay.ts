import { createElement } from 'react'
import { Tag } from 'antd'

import { formatOptionalText, getErrorSeverityTagColor } from './appShared'

export const getImportIssueSeverityDisplayMeta = (value: string) => {
  const isError = String(value).toLowerCase() === 'error'
  return {
    alertType: isError ? 'error' : 'warning',
    tagColor: getErrorSeverityTagColor(value),
  } as const
}

export const getImportResultIssues = <T>(
  result?: { issues?: T[] | null } | null,
): T[] => result?.issues || []

type ImportIssueTagSource = {
  severity: string
  code: string
  sequenceNumber?: string | null
}

type BuildImportIssueTagItemsOptions = {
  includeSeverity?: boolean
  codeColor?: string
}

type ImportIssueSummaryCounts = {
  totalIssueCount: number
  warningCount: number
  errorCount: number
  lifecycleWarningCount: number
}

export const getImportLifecycleWarningSummaryColor = (lifecycleWarningCount: number) => (
  lifecycleWarningCount > 0 ? 'gold' : 'green'
)

export const buildImportIssueSummaryItem = (
  key: string,
  count: number,
  label: string,
  color: string,
) => ({ key, color, label: `${count} ${label}` })

export const buildImportIssueSummaryItems = ({
  totalIssueCount,
  warningCount,
  errorCount,
  lifecycleWarningCount,
}: ImportIssueSummaryCounts) => [
  buildImportIssueSummaryItem('total', totalIssueCount, 'total issues', 'blue'),
  buildImportIssueSummaryItem('warnings', warningCount, 'warnings', 'gold'),
  buildImportIssueSummaryItem('errors', errorCount, 'errors', 'red'),
  buildImportIssueSummaryItem(
    'lifecycle-target-warnings',
    lifecycleWarningCount,
    'lifecycle target warnings',
    getImportLifecycleWarningSummaryColor(lifecycleWarningCount),
  ),
]

export const renderImportIssueSeverityTag = (value: string) => {
  return createElement(Tag, { color: getImportIssueSeverityDisplayMeta(value).tagColor }, value)
}

export const formatImportIssueSequenceNumber = formatOptionalText

export const buildImportIssueTagItems = (
  issue: ImportIssueTagSource,
  options: BuildImportIssueTagItemsOptions = {},
) => [
  { key: 'sequence', label: formatImportIssueSequenceNumber(issue.sequenceNumber) },
  ...(
    options.includeSeverity
      ? [{ key: 'severity', label: issue.severity, color: getImportIssueSeverityDisplayMeta(issue.severity).tagColor }]
      : []
  ),
  { key: 'code', label: issue.code, color: options.codeColor },
]

export const buildImportIssueColumns = () => [
  { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 110, render: renderImportIssueSeverityTag },
  { title: 'Code', dataIndex: 'code', key: 'code', width: 220 },
  { title: 'Sequence', dataIndex: 'sequenceNumber', key: 'sequenceNumber', width: 130, render: formatImportIssueSequenceNumber },
  { title: 'Message', dataIndex: 'message', key: 'message' },
]
