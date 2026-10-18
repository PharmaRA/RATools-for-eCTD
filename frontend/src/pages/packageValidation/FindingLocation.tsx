import { useState } from 'react'
import { Alert, Button, Descriptions, Drawer, Select, Space, Spin } from 'antd'
import { useQuery } from '@tanstack/react-query'
import type { ValidationFinding } from '../../api/generated'
import { downloadValidationFile, loadInputText, type ValidationEvidence } from '../../packageValidationActions'
import { findingSources, textFile, type FindingSource } from './validationDisplay'

const addressKey = (source: FindingSource) => JSON.stringify([source.inputId, source.path, source.isSurrounding])

export const FindingLocation = ({ finding, evidence, sequence, onClose }: {
  finding: ValidationFinding; evidence?: ValidationEvidence; sequence: string; onClose: () => void
}) => {
  const sources = findingSources(finding, evidence, sequence)
  const [selected, setSelected] = useState<string>()
  const source = sources.find(item => addressKey(item) === selected) || sources[0]
  const location = finding.location
  return <Drawer open title={`问题定位 · ${finding.ruleId}`} onClose={onClose} size={800}>
    <Space orientation="vertical" size="middle" className="w-full">
      <Alert type={finding.checkStatus === 'Fail' ? 'error' : 'warning'} showIcon title={finding.code} description={finding.message} />
      {sources.length > 1 && <Select aria-label="选择差异文件来源" className="w-full" value={addressKey(source)} onChange={setSelected}
        options={sources.map(item => ({ value: addressKey(item), label: `${item.inputId === evidence?.inputId ? '目标输入' : item.inputId === evidence?.comparisonInputId ? '对照输入' : '历史输入'} · ${item.path}` }))} />}
      <Descriptions column={1} bordered size="small" className="break-all" items={[
        { key: 'source', label: '明确输入 ID', children: source?.inputId || '没有选中此来源' },
        { key: 'scope', label: '地址范围', children: source?.isSurrounding ? '源根目录外围文件（不是序列交付文件）' : '选中序列内的逻辑文件' },
        { key: 'path', label: '文件地址', children: source?.path || location.logicalPath || '-' },
        { key: 'backbone', label: 'Backbone / leaf', children: `${location.backboneRelativePath || '-'} # ${location.leafId || '-'}` },
        { key: 'node', label: '章节 / XML 节点', children: location.nodePath || '-' },
        { key: 'line', label: '行 / 列', children: `${location.line || '-'} / ${location.column || '-'}` },
        { key: 'field', label: '字段 / PDF 对象与页码', children: location.fieldPath || '-' },
        { key: 'expected', label: '期望', children: finding.expected || '-' },
        { key: 'actual', label: '实际', children: finding.actual || '-' },
        { key: 'action', label: '修复建议', children: finding.recommendedAction || '请按规则定位核对原始交付物。' },
      ]} />
      {!source && <Alert type="warning" title="没有可安全打开的明确文件地址" description="旧版外围/对照问题、未选择的历史、目录或仅规则级问题不会按同名文件猜测来源。" />}
      {source && <FilePreview key={`${addressKey(source)}:${location.line || 1}`} source={source} line={location.line || 1} />}
    </Space>
  </Drawer>
}

const FilePreview = ({ source, line }: { source: FindingSource; line: number }) => {
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const text = useQuery({ queryKey: ['validation-text', source.inputId, source.path, source.isSurrounding, line],
    queryFn: ({ signal }) => loadInputText(source.inputId, source.path, line, signal, source.isSurrounding),
    enabled: textFile(source.path), retry: false, staleTime: 0, gcTime: 0,
  })
  const download = async () => {
    if (busy) return
    setBusy(true)
    try {
      await downloadValidationFile(source.inputId, source.path, source.isSurrounding)
      // A successful download does not revalidate the cached text window.
      if (textFile(source.path)) await text.refetch({ throwOnError: true })
      setError(undefined)
    }
    catch (caught) { setError(caught instanceof Error ? caught.message : '下载失败') }
    finally { setBusy(false) }
  }
  return <Space orientation="vertical" size="middle" className="w-full">
      <Button loading={busy} onClick={() => void download()}>下载定位文件原始字节</Button>
      {(error || text.error) && <Alert type="error" showIcon title="文件不可用；旧报告不会替换为新文件内容" description={error || (text.error instanceof Error ? text.error.message : '读取失败')} />}
      {text.isFetching && <Spin aria-label="正在读取原始文本" />}
      {text.data && !text.isError && !text.isFetching && !busy && !error && <div>
        <p className="text-xs break-all">文件 SHA-256：{text.data.sha256}</p>
        <p className="text-slate-500">读取时校验一致的只读原文（不执行 XML、DTD 或样式表）。{text.data.truncated ? '仅显示有限文本窗口；完整内容请下载。' : ''}</p>
        <pre className="bg-slate-50 border border-slate-200 rounded p-3 text-xs overflow-auto max-h-96" data-testid="validation-source-text">
          {text.data.lines.map((value, index) => <div key={index} className={text.data!.startLine + index === line ? 'bg-amber-100' : ''} style={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>
            <span className="text-slate-400 select-none mr-3">{text.data!.startLine + index}</span>{value}
          </div>)}
        </pre>
      </div>}
    </Space>
}
