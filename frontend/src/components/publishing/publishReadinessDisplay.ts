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

export const formatReadinessHistoryCountHint = (
  readiness: PublishReadinessHistoryCountHint,
  missingMetadataHint?: string | null,
) => {
  if (readiness.isReady && (readiness.warningCount ?? 0) > 0) {
    return `Warnings: ${readiness.warningCount}`
  }

  if (!readiness.isReady && !missingMetadataHint && (readiness.blockingErrorCount ?? 0) > 0) {
    return `Blocking errors: ${readiness.blockingErrorCount}`
  }

  return null
}

export const formatReadinessReadyStatus = (isReady?: boolean | null) => isReady ? 'Yes' : 'No'

export const formatReadinessStatus = (status?: string | null) => status || '-'

type PublishReadinessStatus = {
  isReady?: boolean | null
  status?: string | null
}

export const getPublishReadinessStatusTagProps = (readiness: PublishReadinessStatus) => ({
  color: readiness.isReady ? 'green' : 'red',
  label: readiness.status || (readiness.isReady ? 'Ready' : 'Blocked'),
})

export const formatReadinessFieldName = (fieldName?: string | null) => fieldName || '-'

export const formatReadinessCount = (count?: number | null) => count ?? '-'

type PublishReadinessCategoryRow = {
  category: string
}

type PublishReadinessFindingRow = {
  code: string
  fieldName?: string | null
}

export const getPublishReadinessCategoryKey = (row: PublishReadinessCategoryRow) => row.category

export const getPublishReadinessFindingKey = (row: PublishReadinessFindingRow, index?: number) => {
  return `${row.code}-${row.fieldName || 'none'}-${index}`
}
