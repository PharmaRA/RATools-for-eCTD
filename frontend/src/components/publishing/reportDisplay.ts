import { createElement, type ReactNode } from 'react'
import { Tag } from 'antd'

import {
  buildLifecycleIssueCountItems,
  formatDate,
  formatOptionalBytes,
  formatOptionalCount,
  formatOptionalList,
  formatOptionalText,
  getOptionalArray,
} from '../../pages/appShared'
import { renderArtifactExistsStatus } from './artifactDisplay'
import { buildEvidenceFindingColumns } from './evidenceFindingDisplay'
import { renderEvidenceFindingSeverityStatus } from './findingSeverityDisplay'
import { buildPublishReadinessCategoryColumns, buildPublishReadinessFindingColumns } from './publishReadinessDisplay'
import { buildIntegrityRiskSummaryItems } from './riskSummaryDisplay'

export const formatReportList = (values?: unknown[]) => formatOptionalList(values)

export const formatReportCount = formatOptionalCount

export const getReportOutcomeDisplayMeta = (succeeded?: boolean) => (
  succeeded
    ? { title: '发布成功', iconClassName: 'text-green-500' }
    : { title: '发布失败', iconClassName: 'text-red-500' }
)

export const getReportValidationIssues = <T>(
  report?: { validationReport?: { issues?: T[] | null } | null } | null,
): T[] => getOptionalArray(report?.validationReport?.issues)

export const getReportIntegrityFindings = <T>(
  report?: { integrityEvidence?: { findings?: T[] | null } | null } | null,
): T[] => getOptionalArray(report?.integrityEvidence?.findings)

export const getReportIntegrityArtifacts = <T>(
  report?: { integrityEvidence?: { artifacts?: T[] | null } | null } | null,
): T[] => getOptionalArray(report?.integrityEvidence?.artifacts)

type ReportOverview = {
  validationProfile?: string
  durationMs?: number
  errorCount?: number
  warningCount?: number
}

export const buildReportOverviewItems = (
  report: ReportOverview,
  lifecycleIssueCount: number,
  integrityState: string,
) => [
  { key: 'profile', label: '配置', children: report.validationProfile },
  { key: 'duration', label: '耗时', children: `${report.durationMs} ms` },
  { key: 'errors', label: '错误', children: report.errorCount },
  { key: 'warnings', label: '警告', children: report.warningCount },
  { key: 'lifecycle-issues', label: '生命周期问题', children: lifecycleIssueCount },
  { key: 'integrity', label: '完整性', children: integrityState },
]

type ReportSummaryItemDefinition<TSummary> = {
  key: string
  label: string
  children: (summary: TSummary) => ReactNode
}

export const buildReportSummaryItems = <TSummary>(
  summary: TSummary,
  definitions: readonly ReportSummaryItemDefinition<TSummary>[],
) => definitions.map(({ key, label, children }) => ({
  key,
  label,
  children: children(summary),
}))

type ReportIntegritySummary = {
  isConsistent?: boolean | null
  missingFilesCount?: number | null
  missingZipEntriesCount?: number | null
  mismatchedArtifactsCount?: number | null
}

export const formatReportIntegrityState = (summary: ReportIntegritySummary | null | undefined) => {
  if (!summary) return '-'
  return summary.isConsistent ? '一致' : '不一致'
}

export const buildReportIntegrityIssueSummaryItems = buildIntegrityRiskSummaryItems

export const buildReportIntegritySummaryItems = (
  summary: ReportIntegritySummary | null | undefined,
  integrityState: string,
) => [
  { key: 'consistent', label: '一致', children: integrityState },
  ...buildReportIntegrityIssueSummaryItems(summary),
]

type ReportArtifactSummary = {
  fileCount?: number | null
  totalSizeBytes?: number | null
  packageSizeBytes?: number | null
}

export const buildReportArtifactSummaryItems = (
  summary: ReportArtifactSummary | null | undefined,
) => buildReportSummaryItems(summary, [
  { key: 'file-count', label: '文件数', children: (item) => formatReportCount(item?.fileCount) },
  { key: 'total-size', label: '总大小', children: (item) => formatOptionalBytes(item?.totalSizeBytes) },
  { key: 'package-size', label: '包大小', children: (item) => formatOptionalBytes(item?.packageSizeBytes) },
])

type ReportAuditSummary = {
  publishJobEventCount?: number | null
  validationEventCount?: number | null
  latestPublishJobAction?: string | null
  latestPublishJobEventUtc?: string | null
}

export const buildReportAuditSummaryItems = (
  summary: ReportAuditSummary | null | undefined,
) => buildReportSummaryItems(summary, [
  { key: 'publish-job-events', label: '发布任务事件数', children: (item) => formatReportCount(item?.publishJobEventCount) },
  { key: 'validation-events', label: '校验事件数', children: (item) => formatReportCount(item?.validationEventCount) },
  { key: 'latest-action', label: '最近操作', children: (item) => item?.latestPublishJobAction ?? '-' },
  { key: 'latest-event', label: '最近事件', children: (item) => formatDate(item?.latestPublishJobEventUtc ?? undefined) },
])

type ReportLifecycleIssueSummary = {
  issueCount: number
  replaceTargetNotFoundCount: number
  deleteTargetNotFoundCount: number
  appendTargetNotFoundCount: number
  ambiguousCount: number
  currentSequenceCount: number
}

type ReportLifecycleSummary = ReportLifecycleIssueSummary & {
  matchedCount: number
}

export const buildReportLifecycleIssueSummaryItems = (
  summary: ReportLifecycleIssueSummary,
) => buildReportSummaryItems(summary, [
  { key: 'issues', label: '问题', children: (item) => item.issueCount },
  ...buildLifecycleIssueCountItems(summary).map(({ key, label, value }) => ({
    key,
    label,
    children: () => value,
  })),
])

export const buildReportLifecycleSummaryItems = (
  summary: ReportLifecycleSummary,
  warningSummary?: string | null,
) => [
  { key: 'matched', label: '已匹配', children: summary.matchedCount },
  ...buildReportLifecycleIssueSummaryItems(summary),
  { key: 'warning-summary', label: '警告摘要', children: formatOptionalText(warningSummary) },
]

export const renderReportSeverityStatus = renderEvidenceFindingSeverityStatus

export const buildReportValidationIssueColumns = () => [
  { title: '严重级别', dataIndex: 'severity', render: renderReportSeverityStatus, width: 100 },
  { title: '代码', dataIndex: 'code', width: 200 },
  { title: '消息', dataIndex: 'message' },
]

export const buildReportIntegrityFindingColumns = () => buildEvidenceFindingColumns({ includeKeys: false })

export const buildReportArtifactManifestColumns = () => [
  { title: '角色', dataIndex: 'role', width: 140 },
  { title: '相对路径', dataIndex: 'relativePath', width: 260, render: formatOptionalText },
  { title: '存在', dataIndex: 'exists', width: 120, render: renderArtifactExistsStatus },
  { title: '大小', dataIndex: 'sizeBytes', width: 120, render: formatOptionalBytes },
  { title: 'Zip 条目', dataIndex: 'zipEntryPresent', width: 150, render: renderZipEntryPresentStatus },
  { title: '来源', dataIndex: 'source', width: 160 },
]

export const buildReportLifecycleMatchColumns = () => [
  { title: '操作类型', dataIndex: 'operation', width: 120 },
  { title: '序列', dataIndex: 'sequenceNumber', width: 100 },
  { title: 'CTD 章节', dataIndex: 'ctdSection', width: 120 },
  { title: '文档 ID', dataIndex: 'documentId', width: 180 },
  { title: '结果代码', dataIndex: 'resultCode', width: 240 },
  { title: '匹配策略', dataIndex: 'matchStrategy', width: 180 },
  { title: '尝试的策略', dataIndex: 'attemptedStrategies', render: formatReportList, width: 220 },
  { title: '历史匹配数', dataIndex: 'historicalMatchCount', width: 140 },
  { title: '历史序列', dataIndex: 'historicalSequenceNumbers', render: formatReportList, width: 180 },
  { title: '历史放置 ID', dataIndex: 'historicalPlacementIds', render: formatReportList, width: 240 },
  { title: '最终状态', dataIndex: 'historicalFinalState', width: 140 },
]

export const buildReportPublishReadinessCategoryColumns = () => (
  buildPublishReadinessCategoryColumns({ categoryWidth: 220, includeKeys: false })
)

export const buildReportPublishReadinessFindingColumns = () => buildPublishReadinessFindingColumns({
  severityRenderer: renderReportSeverityStatus,
  includeKeys: false,
})

export const renderZipEntryPresentStatus = (present?: boolean | null) => {
  if (present === true) return createElement(Tag, { color: 'green' }, '存在')
  if (present === false) return createElement(Tag, { color: 'red' }, 'Zip 中缺失')
  return '-'
}
