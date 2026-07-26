import type { ReactNode } from 'react'

import { formatOptionalText } from '../../pages/appShared'
import { renderEvidenceFindingSeverityStatus } from './findingSeverityDisplay'

type EvidenceFindingColumnOptions = {
  includeKeys?: boolean
  severityRenderer?: (value: string) => ReactNode
  severityWidth?: number
  typeWidth?: number
  pathWidth?: number
}

const buildEvidenceFindingColumn = (
  title: string,
  dataIndex: string,
  width: number | undefined,
  includeKeys: boolean,
  render?: (value: string) => ReactNode,
) => ({
  title,
  dataIndex,
  ...(includeKeys ? { key: dataIndex } : {}),
  ...(width ? { width } : {}),
  ...(render ? { render } : {}),
})

export const buildEvidenceFindingColumns = ({
  includeKeys = true,
  severityRenderer = renderEvidenceFindingSeverityStatus,
  severityWidth = 100,
  typeWidth = 200,
  pathWidth = 260,
}: EvidenceFindingColumnOptions = {}) => [
  buildEvidenceFindingColumn('严重级别', 'severity', severityWidth, includeKeys, severityRenderer),
  buildEvidenceFindingColumn('类型', 'type', typeWidth, includeKeys),
  buildEvidenceFindingColumn('路径', 'path', pathWidth, includeKeys, formatOptionalText),
  buildEvidenceFindingColumn('消息', 'message', undefined, includeKeys),
]
