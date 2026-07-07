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
  buildEvidenceFindingColumn('Severity', 'severity', severityWidth, includeKeys, severityRenderer),
  buildEvidenceFindingColumn('Type', 'type', typeWidth, includeKeys),
  buildEvidenceFindingColumn('Path', 'path', pathWidth, includeKeys, formatOptionalText),
  buildEvidenceFindingColumn('Message', 'message', undefined, includeKeys),
]
