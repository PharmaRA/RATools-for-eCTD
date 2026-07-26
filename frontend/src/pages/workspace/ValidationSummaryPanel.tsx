import { Alert, Button, Tag } from 'antd'

import type { PrePublishChecklistRow, PrePublishChecklistSummary } from '../../prePublishChecklist'

type ValidationLocation = {
  placementId?: string | null
  documentId?: string | null
  sectionPath?: string | null
}

type ValidationSummaryPanelProps = {
  summary: PrePublishChecklistSummary
  statusText: string
  issueCountText: string
  hasValidationLocation: (location: ValidationLocation) => boolean
  locateValidationIssue: (location: ValidationLocation) => void
}

const getChecklistTagColor = (row: PrePublishChecklistRow) => {
  if (row.status === 'pass') return 'green'
  if (row.blocking) return 'red'
  return 'blue'
}

const getChecklistTagLabel = (row: PrePublishChecklistRow) => {
  if (row.status === 'pass') return '通过'
  if (row.status === 'fail') return '未通过'
  return '需知悉'
}

export const ValidationSummaryPanel = ({
  summary,
  statusText,
  issueCountText,
  hasValidationLocation,
  locateValidationIssue,
}: ValidationSummaryPanelProps) => (
  <div data-testid="validation-summary" data-severity={summary.severity}>
    <Alert
      type={summary.severity}
      showIcon
      title={<span data-testid="validation-summary-title">{statusText}</span>}
      description={(
        <div className="flex flex-col gap-1">
          <div className="flex flex-wrap gap-2">
            <span data-testid="validation-summary-profile">{summary.profile}</span>
            <span data-testid="validation-summary-issue-count">{issueCountText}</span>
            <span data-testid="validation-summary-has-api-error">{summary.hasApiError ? '是' : '否'}</span>
            <span data-testid="validation-summary-status-label">{statusText}</span>
          </div>
          <div data-testid="validation-summary-details" className="flex flex-col gap-3">
            <div data-testid="validation-summary-checklist" className="rounded border border-gray-200 bg-white/70 p-3">
              <div className="mb-2 font-semibold">发布前检查清单</div>
              <div className="flex flex-col gap-2">
                {summary.checklistRows.map((row) => (
                  <div key={row.key} data-testid={`validation-summary-checklist-${row.key}`}>
                    <Tag color={getChecklistTagColor(row)}>{getChecklistTagLabel(row)}</Tag>
                    <span>{row.label}</span>
                    <span> | {row.detail}</span>
                    {!row.blocking && <Tag color="blue" className="ml-2">非阻断</Tag>}
                  </div>
                ))}
              </div>
            </div>

            <div data-testid="validation-summary-issues" className="rounded border border-gray-200 bg-white/70 p-3">
              <div className="mb-2 font-semibold">阻断性问题</div>
              {summary.blockingIssues.length === 0 ? (
                <div>未发现阻断性校验错误。</div>
              ) : (
                <div className="flex flex-col gap-2">
                  {summary.blockingIssues.map((issue) => (
                    <div key={`blocking-${issue.severity}-${issue.code}-${issue.message}`}>
                      <Tag color="red">{issue.severity}</Tag>
                      <Tag color="red">{issue.code}</Tag>
                      {issue.message}
                      {hasValidationLocation(issue) && (
                        <Button size="small" className="ml-2" onClick={() => locateValidationIssue(issue)}>定位</Button>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div data-testid="validation-summary-warnings" className="rounded border border-gray-200 bg-white/70 p-3">
              <div className="mb-2 font-semibold">警告</div>
              {summary.warningIssues.length === 0 ? (
                <div>未发现校验警告。</div>
              ) : (
                <div className="flex flex-col gap-2">
                  {summary.warningIssues.map((issue) => (
                    <div key={`warning-${issue.severity}-${issue.code}-${issue.message}`}>
                      <Tag color="gold">{issue.severity}</Tag>
                      <Tag color="gold">{issue.code}</Tag>
                      {issue.message}
                      {hasValidationLocation(issue) && (
                        <Button size="small" className="ml-2" onClick={() => locateValidationIssue(issue)}>定位</Button>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div data-testid="validation-summary-lifecycle" className="rounded border border-gray-200 bg-white/70 p-3">
              <div className="mb-2 font-semibold">生命周期目标</div>
              {summary.lifecycleMatches.length === 0 ? (
                <div>未检查任何生命周期操作。</div>
              ) : (
                <div className="flex flex-col gap-2">
                  {summary.lifecycleMatches.map((match) => (
                    <div key={`${match.operation}-${match.sequenceNumber}-${match.ctdSection}-${match.documentId}`}>
                      <Tag color={match.resultCode === 'MATCHED' ? 'green' : 'red'}>{match.resultCode}</Tag>
                      <span>{match.operation} 位于 {match.ctdSection}</span>
                      <span> | 序列 {match.sequenceNumber}</span>
                      <span> | 策略 {match.matchStrategy}</span>
                      <span> | {match.historicalMatchCount} 条历史匹配</span>
                      {match.historicalSequenceNumbers.length > 0 && <span> | 历史序列 {match.historicalSequenceNumbers.join(', ')}</span>}
                      <span> | 最终状态 {match.historicalFinalState}</span>
                      {hasValidationLocation({ documentId: match.documentId, sectionPath: match.ctdSection }) && (
                        <Button size="small" className="ml-2" onClick={() => locateValidationIssue({ documentId: match.documentId, sectionPath: match.ctdSection })}>定位</Button>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div data-testid="validation-summary-sections" className="rounded border border-gray-200 bg-white/70 p-3">
              <div className="mb-2 font-semibold">章节匹配</div>
              {summary.sectionMatches.length === 0 ? (
                <div>未检查任何章节匹配。</div>
              ) : (
                <div className="flex flex-col gap-2">
                  <div>
                    已检查 {summary.sectionMatches.length} 个 | {summary.invalidSectionCount} 个无效 | {summary.nonStandardSectionCount} 个非标准
                  </div>
                  {summary.sectionRows.length === 0 ? (
                    <div>所有已检查章节均为有效的标准匹配。</div>
                  ) : (
                    summary.sectionRows.map((match) => (
                      <div key={`${match.sectionPath}-${match.reason || 'ok'}`}>
                        <Tag color={match.isValid ? 'gold' : 'red'}>{match.isValid ? '非标准' : '无效'}</Tag>
                        <span>{match.sectionPath}</span>
                        {match.matchedPrefix && <span> | 匹配到 {match.matchedPrefix}</span>}
                        {match.reason && <span> | {match.reason}</span>}
                        {hasValidationLocation({ sectionPath: match.sectionPath }) && (
                          <Button size="small" className="ml-2" onClick={() => locateValidationIssue({ sectionPath: match.sectionPath })}>定位</Button>
                        )}
                      </div>
                    ))
                  )}
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    />
  </div>
)
