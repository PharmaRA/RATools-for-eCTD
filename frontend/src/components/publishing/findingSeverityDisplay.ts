import { createElement } from 'react'
import { Tag } from 'antd'

export const getEvidenceFindingSeverityTagColor = (severity: string) => (
  severity === 'Error' ? 'red' : 'orange'
)

export const renderEvidenceFindingSeverityStatus = (severity: string) => {
  return createElement(Tag, { color: getEvidenceFindingSeverityTagColor(severity) }, severity)
}
