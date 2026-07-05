import { createElement } from 'react'
import { Tag } from 'antd'

export const formatReportList = (values?: unknown[]) => values?.length ? values.join(', ') : '-'

export const formatReportCount = (count?: number | null) => count ?? '-'

export const renderReportSeverityStatus = (severity: string) => {
  return createElement(Tag, { color: severity === 'Error' ? 'red' : 'orange' }, severity)
}

export const renderZipEntryPresentStatus = (present?: boolean | null) => {
  if (present === true) return createElement(Tag, { color: 'green' }, 'Present')
  if (present === false) return createElement(Tag, { color: 'red' }, 'Missing from zip')
  return '-'
}
