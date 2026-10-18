import type { PackageRunSummary, ValidationFinding } from '../../api/generated'
import type { ValidationEvidence } from '../../packageValidationActions'

export const runStatusLabels: Record<PackageRunSummary['status'], string> = {
  Pending: '等待执行', Running: '正在校验', Completed: '运行完成（不等于通过）', Failed: '执行失败',
  Cancelled: '已取消', InputChanged: '输入已变化',
}
export const checkLabels = { Pass: '通过', Fail: '失败', NotEvaluated: '未验证', NotApplicable: '不适用' }
export const severityLabels = { Error: '错误', Warning: '警告', Info: '信息' }
export const stageLabels: Record<string, string> = { Queued: '排队', ReadingInputs: '读取输入', Inspecting: '解析与规则检查', Archiving: '归档报告' }
export const projectionStatusLabels = {
  Current: '当前有效', AppendMember: '追加成员', Replaced: '已被替换', Deleted: '已被删除', Unknown: '无法确定',
}
export const projectionViewLabels = { history: '完整历史', current: '累计有效资料', sequence: '本次序列' }

export type FindingSource = { inputId: string; path: string; isSurrounding: boolean }

// A sequence-normalized path and a source-root path can name different bytes.
export const findingSources = (finding: ValidationFinding, evidence?: ValidationEvidence, targetSequence?: string): FindingSource[] => {
  const location = finding.location
  if (!evidence) return []
  const allowedIds = new Set([evidence.inputId, evidence.comparisonInputId, ...evidence.history.map(item => item.inputId)])
  const addresses = location.files
  if (addresses != null) {
    if (addresses.some(address => !allowedIds.has(address.sourceId))) return []
    return addresses.map(address => ({ inputId: address.sourceId, path: address.logicalPath, isSurrounding: address.isSurrounding }))
  }
  // Legacy parity/surrounding observations do not prove which input/address space
  // they refer to. Keep their report visible without guessing a file to display.
  if (finding.ruleId === 'ZIP-PARITY' || finding.code === 'SURROUNDING_INPUT_FILE' || finding.code === 'EMPTY_DELIVERY_DIRECTORY') return []
  const sequence = location.sequenceNumber || location.logicalPath?.split('/')[0]
  if (!sequence) return []
  const inputId = sequence === targetSequence ? evidence.inputId : evidence.historyBaseline.entries.find(entry => entry.sequenceNumber === sequence)?.sourceId
  let path = location.logicalPath
  if (!path && location.backboneRelativePath) path = `${sequence}/${location.backboneRelativePath}`
  return inputId && allowedIds.has(inputId) && path?.startsWith(`${sequence}/`) ? [{ inputId, path, isSurrounding: false }] : []
}
export const textFile = (path: string) => /\.(xml|xsl|txt|dtd|mod)$/i.test(path)
