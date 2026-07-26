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
  { key: 'missing-files', label: '缺失文件', children: formatRiskSummaryCount(summary?.missingFilesCount) },
  { key: 'missing-zip-entries', label: '缺失 Zip 条目', children: formatRiskSummaryCount(summary?.missingZipEntriesCount) },
  { key: 'mismatched-artifacts', label: '不匹配的产物', children: formatRiskSummaryCount(summary?.mismatchedArtifactsCount) },
]
