import { createElement } from 'react'
import { Tag } from 'antd'

export const renderImportIssueSeverityTag = (value: string) => {
  return createElement(Tag, { color: String(value).toLowerCase() === 'error' ? 'red' : 'gold' }, value)
}

export const formatImportIssueSequenceNumber = (value?: string | null) => value || '-'

export const buildImportIssueColumns = () => [
  { title: 'Severity', dataIndex: 'severity', key: 'severity', width: 110, render: renderImportIssueSeverityTag },
  { title: 'Code', dataIndex: 'code', key: 'code', width: 220 },
  { title: 'Sequence', dataIndex: 'sequenceNumber', key: 'sequenceNumber', width: 130, render: formatImportIssueSequenceNumber },
  { title: 'Message', dataIndex: 'message', key: 'message' },
]
