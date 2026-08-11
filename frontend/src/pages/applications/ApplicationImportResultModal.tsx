import { Alert, Card, Col, Modal, Row, Statistic, Table, Tag } from 'antd'

import { summarizeImportIssues, type ImportApplicationResult } from '../../importActions'
import {
  buildImportIssueColumns,
  buildImportIssueSummaryItems,
  buildImportIssueTagItems,
  getImportIssueSeverityDisplayMeta,
  getImportResultIssues,
} from '../importResultDisplay'

type ApplicationImportResultModalProps = {
  open: boolean
  result: ImportApplicationResult | null
  onClose: () => void
}

export const ApplicationImportResultModal = ({
  open,
  result,
  onClose,
}: ApplicationImportResultModalProps) => {
  const issues = getImportResultIssues(result)
  const issueSummary = summarizeImportIssues(issues)
  const summaryItems = buildImportIssueSummaryItems({
    totalIssueCount: issues.length,
    warningCount: issueSummary.warningCount,
    errorCount: issueSummary.errorCount,
    lifecycleWarningCount: issueSummary.lifecycleIssues.length,
  })

  return (
    <Modal
      title="导入结果"
      open={open}
      okText="关闭"
      cancelButtonProps={{ style: { display: 'none' } }}
      onOk={onClose}
      onCancel={onClose}
      width={860}
    >
      {result && (
        <div className="flex flex-col gap-4">
          <Row gutter={12}>
            <Col span={8}><Card size="small"><Statistic title="已导入序列" value={result.importedSequenceCount} /></Card></Col>
            <Col span={8}><Card size="small"><Statistic title="已导入文档" value={result.importedDocumentCount} /></Card></Col>
            <Col span={8}><Card size="small"><Statistic title="已导入放置" value={result.importedPlacementCount} /></Card></Col>
          </Row>
          <Row gutter={12}>
            <Col span={12}><Card size="small"><Statistic title="已跳过序列" value={result.skippedSequenceCount} /></Card></Col>
            <Col span={12}><Card size="small"><Statistic title="失败序列" value={result.failedSequenceCount} /></Card></Col>
          </Row>

          <div data-testid="import-result-summary" className="flex flex-wrap gap-2">
            {summaryItems.map((item) => (
              <Tag key={item.key} color={item.color}>{item.label}</Tag>
            ))}
          </div>

          <Card size="small" title="生命周期目标需审阅" data-testid="import-result-lifecycle-issues">
            {issueSummary.lifecycleIssues.length === 0 ? (
              <Alert type="success" showIcon title="没有生命周期目标警告。" />
            ) : (
              <div className="flex flex-col gap-2">
                {issueSummary.lifecycleIssues.map((issue, index) => (
                  <Alert
                    key={`lifecycle-import-issue-${index}`}
                    type="warning"
                    showIcon
                    title={(
                      <span>
                        {buildImportIssueTagItems(issue, { codeColor: 'gold' }).map((tag) => (
                          <Tag key={tag.key} color={tag.color}>{tag.label}</Tag>
                        ))}
                        {issue.message}
                      </span>
                    )}
                  />
                ))}
              </div>
            )}
          </Card>

          <Card size="small" title="其他导入问题" data-testid="import-result-other-issues">
            {issueSummary.otherIssues.length === 0 ? (
              <Alert type="success" showIcon title="没有其他导入问题。" />
            ) : (
              <div className="flex flex-col gap-2">
                {issueSummary.otherIssues.map((issue, index) => {
                  const severityMeta = getImportIssueSeverityDisplayMeta(issue.severity)
                  return (
                    <Alert
                      key={`other-import-issue-${index}`}
                      type={severityMeta.alertType}
                      showIcon
                      title={(
                        <span>
                          {buildImportIssueTagItems(issue, { includeSeverity: true }).map((tag) => (
                            <Tag key={tag.key} color={tag.color}>{tag.label}</Tag>
                          ))}
                          {issue.message}
                        </span>
                      )}
                    />
                  )
                })}
              </div>
            )}
          </Card>

          {issues.length === 0 ? (
            <Alert type="success" showIcon title="导入完成，无警告或错误。" />
          ) : (
            <div data-testid="import-result-all-issues" className="flex flex-col gap-2">
              <div className="font-semibold">全部导入问题</div>
              <Table
                size="small"
                pagination={{ pageSize: 8 }}
                rowKey={(_, index) => `issue-${index}`}
                dataSource={issues}
                columns={buildImportIssueColumns()}
              />
            </div>
          )}
        </div>
      )}
    </Modal>
  )
}
