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
  if (row.status === 'pass') return 'Pass'
  if (row.status === 'fail') return 'Fail'
  return 'Awareness'
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
            <span data-testid="validation-summary-has-api-error">{summary.hasApiError ? 'Yes' : 'No'}</span>
            <span data-testid="validation-summary-status-label">{statusText}</span>
          </div>
          <div data-testid="validation-summary-details" className="flex flex-col gap-3">
            <div data-testid="validation-summary-checklist" className="rounded border border-gray-200 bg-white/70 p-3">
              <div className="mb-2 font-semibold">Pre-publish Checklist</div>
              <div className="flex flex-col gap-2">
                {summary.checklistRows.map((row) => (
                  <div key={row.key} data-testid={`validation-summary-checklist-${row.key}`}>
                    <Tag color={getChecklistTagColor(row)}>{getChecklistTagLabel(row)}</Tag>
                    <span>{row.label}</span>
                    <span> | {row.detail}</span>
                    {!row.blocking && <Tag color="blue" className="ml-2">Non-blocking</Tag>}
                  </div>
                ))}
              </div>
            </div>

            <div data-testid="validation-summary-issues" className="rounded border border-gray-200 bg-white/70 p-3">
              <div className="mb-2 font-semibold">Blocking Issues</div>
              {summary.blockingIssues.length === 0 ? (
                <div>No blocking validation errors found.</div>
              ) : (
                <div className="flex flex-col gap-2">
                  {summary.blockingIssues.map((issue) => (
                    <div key={`blocking-${issue.severity}-${issue.code}-${issue.message}`}>
                      <Tag color="red">{issue.severity}</Tag>
                      <Tag color="red">{issue.code}</Tag>
                      {issue.message}
                      {hasValidationLocation(issue) && (
                        <Button size="small" className="ml-2" onClick={() => locateValidationIssue(issue)}>Locate</Button>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div data-testid="validation-summary-warnings" className="rounded border border-gray-200 bg-white/70 p-3">
              <div className="mb-2 font-semibold">Warnings</div>
              {summary.warningIssues.length === 0 ? (
                <div>No validation warnings found.</div>
              ) : (
                <div className="flex flex-col gap-2">
                  {summary.warningIssues.map((issue) => (
                    <div key={`warning-${issue.severity}-${issue.code}-${issue.message}`}>
                      <Tag color="gold">{issue.severity}</Tag>
                      <Tag color="gold">{issue.code}</Tag>
                      {issue.message}
                      {hasValidationLocation(issue) && (
                        <Button size="small" className="ml-2" onClick={() => locateValidationIssue(issue)}>Locate</Button>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div data-testid="validation-summary-lifecycle" className="rounded border border-gray-200 bg-white/70 p-3">
              <div className="mb-2 font-semibold">Lifecycle Targets</div>
              {summary.lifecycleMatches.length === 0 ? (
                <div>No lifecycle operations were checked.</div>
              ) : (
                <div className="flex flex-col gap-2">
                  {summary.lifecycleMatches.map((match) => (
                    <div key={`${match.operation}-${match.sequenceNumber}-${match.ctdSection}-${match.documentId}`}>
                      <Tag color={match.resultCode === 'MATCHED' ? 'green' : 'red'}>{match.resultCode}</Tag>
                      <span>{match.operation} in {match.ctdSection}</span>
                      <span> | sequence {match.sequenceNumber}</span>
                      <span> | strategy {match.matchStrategy}</span>
                      <span> | {match.historicalMatchCount} historical match{match.historicalMatchCount === 1 ? '' : 'es'}</span>
                      {match.historicalSequenceNumbers.length > 0 && <span> | historical sequences {match.historicalSequenceNumbers.join(', ')}</span>}
                      <span> | final state {match.historicalFinalState}</span>
                      {hasValidationLocation({ documentId: match.documentId, sectionPath: match.ctdSection }) && (
                        <Button size="small" className="ml-2" onClick={() => locateValidationIssue({ documentId: match.documentId, sectionPath: match.ctdSection })}>Locate</Button>
                      )}
                    </div>
                  ))}
                </div>
              )}
            </div>

            <div data-testid="validation-summary-sections" className="rounded border border-gray-200 bg-white/70 p-3">
              <div className="mb-2 font-semibold">Section Matches</div>
              {summary.sectionMatches.length === 0 ? (
                <div>No section matches were checked.</div>
              ) : (
                <div className="flex flex-col gap-2">
                  <div>
                    {summary.sectionMatches.length} checked | {summary.invalidSectionCount} invalid | {summary.nonStandardSectionCount} non-standard
                  </div>
                  {summary.sectionRows.length === 0 ? (
                    <div>All checked sections are valid standard matches.</div>
                  ) : (
                    summary.sectionRows.map((match) => (
                      <div key={`${match.sectionPath}-${match.reason || 'ok'}`}>
                        <Tag color={match.isValid ? 'gold' : 'red'}>{match.isValid ? 'Non-standard' : 'Invalid'}</Tag>
                        <span>{match.sectionPath}</span>
                        {match.matchedPrefix && <span> | matched {match.matchedPrefix}</span>}
                        {match.reason && <span> | {match.reason}</span>}
                        {hasValidationLocation({ sectionPath: match.sectionPath }) && (
                          <Button size="small" className="ml-2" onClick={() => locateValidationIssue({ sectionPath: match.sectionPath })}>Locate</Button>
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
