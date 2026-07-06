export const formatMissingMetadataFields = (fields?: string[] | null) => {
  return fields?.length ? fields.join(', ') : 'None'
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
    return `Warnings: ${readiness.warningCount}`
  }

  return null
}

export const formatReadinessBlockingErrorCountHint = (
  readiness: Pick<PublishReadinessHistoryCountHint, 'isReady' | 'blockingErrorCount'>,
  missingMetadataHint?: string | null,
) => {
  if (!readiness.isReady && !missingMetadataHint && (readiness.blockingErrorCount ?? 0) > 0) {
    return `Blocking errors: ${readiness.blockingErrorCount}`
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

export const formatReadinessReadyStatus = (isReady?: boolean | null) => isReady ? 'Yes' : 'No'

export const formatReadinessOptionalText = (value?: string | null) => value || '-'

export const formatReadinessStatus = (status?: string | null) => formatReadinessOptionalText(status)

type PublishReadinessStatus = {
  isReady?: boolean | null
  status?: string | null
}

export const getPublishReadinessStatusTagProps = (readiness: PublishReadinessStatus) => ({
  color: readiness.isReady ? 'green' : 'red',
  label: readiness.status || (readiness.isReady ? 'Ready' : 'Blocked'),
})

export const getPublishReadinessFindingSeverityTagColor = (severity: string) => {
  return String(severity).toLowerCase() === 'error' ? 'red' : 'gold'
}

export const formatReadinessFieldName = (fieldName?: string | null) => formatReadinessOptionalText(fieldName)

export const formatReadinessCount = (count?: number | null) => count ?? '-'

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
  { key: 'readiness-status', label: 'Status', children: formatReadinessStatus(readiness.status) },
  { key: 'readiness-ready', label: 'Ready', children: formatReadinessReadyStatus(readiness.isReady) },
  { key: 'readiness-blocking-errors', label: 'Blocking Errors', children: formatReadinessCount(readiness.blockingErrorCount) },
  { key: 'readiness-warnings', label: 'Warnings', children: formatReadinessCount(readiness.warningCount) },
  {
    key: 'readiness-missing-fields',
    label: 'Missing Metadata Fields',
    children: formatMissingMetadataFields(readiness.missingMetadataFields),
    ...(options.missingMetadataFieldsSpan ? { span: options.missingMetadataFieldsSpan } : {}),
  },
]

export const buildPublishReadinessCategoryColumns = () => [
  { title: 'Category', dataIndex: 'category', key: 'category' },
  { title: 'Blocking Errors', dataIndex: 'blockingErrorCount', key: 'blockingErrorCount', width: 140 },
  { title: 'Warnings', dataIndex: 'warningCount', key: 'warningCount', width: 120 },
  { title: 'Findings', dataIndex: 'findingCount', key: 'findingCount', width: 120 },
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
): T[] => readiness?.categorySummaries || []

export const getPublishReadinessFindings = <T extends PublishReadinessFindingRow>(
  readiness?: { findings?: T[] | null } | null,
): T[] => readiness?.findings || []

export const getPublishReadinessFromReport = <T>(
  report?: { publishReadiness?: T | null } | null,
): T | null => report?.publishReadiness || null

export const getPublishReadinessCategoryKey = (row: PublishReadinessCategoryRow) => row.category

export const getPublishReadinessFindingKey = (row: PublishReadinessFindingRow, index?: number) => {
  return `${row.code}-${row.fieldName || 'none'}-${index}`
}
