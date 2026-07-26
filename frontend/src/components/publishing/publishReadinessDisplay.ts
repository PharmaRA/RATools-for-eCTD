import type { ReactNode } from 'react'

import {
  formatOptionalCount,
  formatOptionalList,
  formatOptionalText,
  getErrorSeverityTagColor,
  getOptionalArray,
} from '../../pages/appShared'

export const formatMissingMetadataFields = (fields?: string[] | null) => {
  return formatOptionalList(fields, '无')
}

export const formatReadinessMissingMetadataHint = (fields?: string[] | null) => {
  if (!fields?.length) return null

  const additionalCount = Math.max(0, fields.length - 1)
  return `${fields[0]}${additionalCount > 0 ? ` +${additionalCount}` : ''}`
}

type PublishReadinessHistoryCountHint = {
  isReady?: boolean | null
  blockingErrorCount?: number | null
  warningCount?: number | null
}

export const formatReadinessWarningCountHint = (
  readiness: Pick<PublishReadinessHistoryCountHint, 'isReady' | 'warningCount'>,
) => {
  if (readiness.isReady && (readiness.warningCount ?? 0) > 0) {
    return `警告：${readiness.warningCount}`
  }

  return null
}

export const formatReadinessBlockingErrorCountHint = (
  readiness: Pick<PublishReadinessHistoryCountHint, 'isReady' | 'blockingErrorCount'>,
  missingMetadataHint?: string | null,
) => {
  if (!readiness.isReady && !missingMetadataHint && (readiness.blockingErrorCount ?? 0) > 0) {
    return `阻断性错误：${readiness.blockingErrorCount}`
  }

  return null
}

export const formatReadinessHistoryCountHint = (
  readiness: PublishReadinessHistoryCountHint,
  missingMetadataHint?: string | null,
) => {
  const warningHint = formatReadinessWarningCountHint(readiness)
  if (warningHint) return warningHint

  return formatReadinessBlockingErrorCountHint(readiness, missingMetadataHint)
}

export const formatReadinessReadyStatus = (isReady?: boolean | null) => isReady ? '是' : '否'

export const formatReadinessOptionalText = formatOptionalText

export const formatReadinessStatus = (status?: string | null) => formatReadinessOptionalText(status)

type PublishReadinessStatus = {
  isReady?: boolean | null
  status?: string | null
}

export const getPublishReadinessStatusTagProps = (readiness: PublishReadinessStatus) => ({
  color: readiness.isReady ? 'green' : 'red',
  label: readiness.status || (readiness.isReady ? '就绪' : '受阻'),
})

export const getPublishReadinessFindingSeverityTagColor = getErrorSeverityTagColor

export const formatReadinessFieldName = (fieldName?: string | null) => formatReadinessOptionalText(fieldName)

export const formatReadinessCount = formatOptionalCount

type PublishReadinessSnapshot = {
  status?: string | null
  isReady?: boolean | null
  blockingErrorCount?: number | null
  warningCount?: number | null
  missingMetadataFields?: string[] | null
}

type PublishReadinessSnapshotItemOptions = {
  missingMetadataFieldsSpan?: number
}

export const buildPublishReadinessSnapshotItems = (
  readiness: PublishReadinessSnapshot,
  options: PublishReadinessSnapshotItemOptions = {},
) => [
  { key: 'readiness-status', label: '状态', children: formatReadinessStatus(readiness.status) },
  { key: 'readiness-ready', label: '就绪', children: formatReadinessReadyStatus(readiness.isReady) },
  { key: 'readiness-blocking-errors', label: '阻断性错误', children: formatReadinessCount(readiness.blockingErrorCount) },
  { key: 'readiness-warnings', label: '警告', children: formatReadinessCount(readiness.warningCount) },
  {
    key: 'readiness-missing-fields',
    label: '缺失的元数据字段',
    children: formatMissingMetadataFields(readiness.missingMetadataFields),
    ...(options.missingMetadataFieldsSpan ? { span: options.missingMetadataFieldsSpan } : {}),
  },
]

type PublishReadinessCategoryColumnOptions = {
  categoryWidth?: number
  includeKeys?: boolean
}

const buildPublishReadinessCategoryColumn = (
  title: string,
  dataIndex: string,
  width: number | undefined,
  includeKeys: boolean,
) => ({
  title,
  dataIndex,
  ...(includeKeys ? { key: dataIndex } : {}),
  ...(width ? { width } : {}),
})

export const buildPublishReadinessCategoryColumns = (
  options: PublishReadinessCategoryColumnOptions = {},
) => {
  const includeKeys = options.includeKeys ?? true

  return [
    buildPublishReadinessCategoryColumn('类别', 'category', options.categoryWidth, includeKeys),
    buildPublishReadinessCategoryColumn('阻断性错误', 'blockingErrorCount', 140, includeKeys),
    buildPublishReadinessCategoryColumn('警告', 'warningCount', 120, includeKeys),
    buildPublishReadinessCategoryColumn('发现项', 'findingCount', 120, includeKeys),
  ]
}

type PublishReadinessFindingColumnOptions = {
  severityRenderer: (value: string) => ReactNode
  severityWidth?: number
  includeKeys?: boolean
}

const buildPublishReadinessFindingColumn = (
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

export const buildPublishReadinessFindingColumns = ({
  severityRenderer,
  severityWidth = 100,
  includeKeys = true,
}: PublishReadinessFindingColumnOptions) => [
  buildPublishReadinessFindingColumn('严重级别', 'severity', severityWidth, includeKeys, severityRenderer),
  buildPublishReadinessFindingColumn('代码', 'code', 220, includeKeys),
  buildPublishReadinessFindingColumn('类别', 'category', 180, includeKeys),
  buildPublishReadinessFindingColumn('字段', 'fieldName', 180, includeKeys, formatReadinessFieldName),
  buildPublishReadinessFindingColumn('建议措施', 'recommendedAction', undefined, includeKeys),
]

type PublishReadinessCategoryRow = {
  category: string
}

type PublishReadinessFindingRow = {
  code: string
  fieldName?: string | null
}

export const getPublishReadinessCategorySummaries = <T extends PublishReadinessCategoryRow>(
  readiness?: { categorySummaries?: T[] | null } | null,
): T[] => getOptionalArray(readiness?.categorySummaries)

export const getPublishReadinessFindings = <T extends PublishReadinessFindingRow>(
  readiness?: { findings?: T[] | null } | null,
): T[] => getOptionalArray(readiness?.findings)

export const getPublishReadinessFromReport = <T>(
  report?: { publishReadiness?: T | null } | null,
): T | null => report?.publishReadiness || null

export const getPublishReadinessCategoryKey = (row: PublishReadinessCategoryRow) => row.category

export const getPublishReadinessFindingKey = (row: PublishReadinessFindingRow, index?: number) => {
  return `${row.code}-${row.fieldName || 'none'}-${index}`
}
