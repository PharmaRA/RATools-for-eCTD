import { formatOptionalCount } from '../../pages/appShared'

type IntegrityRiskSummary = {
  missingFilesCount?: number | null
  missingZipEntriesCount?: number | null
  mismatchedArtifactsCount?: number | null
}

export const formatRiskSummaryCount = formatOptionalCount

export const buildIntegrityRiskSummaryItems = (
  summary?: IntegrityRiskSummary | null,
) => [
  { key: 'missing-files', label: 'Missing Files', children: formatRiskSummaryCount(summary?.missingFilesCount) },
  { key: 'missing-zip-entries', label: 'Missing Zip Entries', children: formatRiskSummaryCount(summary?.missingZipEntriesCount) },
  { key: 'mismatched-artifacts', label: 'Mismatched Artifacts', children: formatRiskSummaryCount(summary?.mismatchedArtifactsCount) },
]
