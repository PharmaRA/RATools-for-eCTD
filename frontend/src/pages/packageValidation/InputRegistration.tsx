import { useEffect, useRef, useState } from 'react'
import { Alert, Button, Form, Input, Modal, Radio, Select, Space } from 'antd'
import { ApiRequestError } from '../../apiClient'
import type { PackageInputDto } from '../../api/generated'
import { loadValidationInput, registerValidationInput, uploadValidationInput } from '../../packageValidationActions'

type Props = { applicationId: string; title: string; onRegistered: (input: PackageInputDto) => void; onClose: () => void }

export const InputRegistration = ({ applicationId, title, onRegistered, onClose }: Props) => {
  const [mode, setMode] = useState<'upload' | 'path' | 'existing'>('upload')
  const [path, setPath] = useState('')
  const [file, setFile] = useState<File>()
  const [inputId, setInputId] = useState('')
  const [sequence, setSequence] = useState('')
  const [root, setRoot] = useState<string>()
  const [candidates, setCandidates] = useState<ApiRequestError['candidates']>()
  const [error, setError] = useState<string>()
  const [busy, setBusy] = useState(false)
  const request = useRef<AbortController | null>(null)
  useEffect(() => () => request.current?.abort(), [])
  const resetSelection = () => { setCandidates(undefined); setRoot(undefined); setError(undefined) }

  const submit = async () => {
    if (busy) return
    const controller = new AbortController()
    request.current = controller
    setBusy(true)
    setError(undefined)
    try {
      if (mode !== 'existing' && sequence && !/^\d{4}$/.test(sequence)) throw new Error('序列号必须为四位数字。')
      if (mode !== 'existing' && candidates && root === undefined) throw new Error('请选择一个明确的序列根目录。')
      let input: PackageInputDto
      if (mode === 'existing') input = await loadValidationInput(inputId.trim(), controller.signal)
      else if (mode === 'path') input = await registerValidationInput({ applicationId, path, sequenceNumber: sequence || null, rootRelativePath: root ?? null }, controller.signal)
      else {
        if (!file) throw new Error('请选择 ZIP 文件。')
        input = await uploadValidationInput(applicationId, file, sequence || undefined, root, controller.signal)
      }
      if (input.applicationId.toLowerCase() !== applicationId.toLowerCase()) throw new Error('此输入属于另一申请标识；不能跨申请选用。')
      if (!controller.signal.aborted) onRegistered(input)
    } catch (caught) {
      if (controller.signal.aborted) return
      if (caught instanceof ApiRequestError && caught.code === 'SEQUENCE_SELECTION_REQUIRED') {
        setCandidates(caught.candidates)
        setRoot(undefined)
      }
      setError(caught instanceof Error ? caught.message : '输入登记失败')
    } finally { if (!controller.signal.aborted) setBusy(false) }
  }

  return <Modal open title={title} onCancel={() => { request.current?.abort(); onClose() }} footer={null} width={720}>
    <Alert type="info" showIcon title="只登记交付物，不导入业务工作区" description="同一申请的目标和历史序列必须使用相同申请标识。目录须处于服务端授权根目录；每次运行和文件查看都会重新核对摘要。" className="mb-4" />
    <Form layout="vertical" disabled={busy}>
      <Form.Item label="输入方式">
        <Radio.Group value={mode} onChange={event => { setMode(event.target.value); resetSelection() }} options={[
          { value: 'upload', label: '上传 ZIP' }, { value: 'path', label: '服务端目录 / ZIP' }, { value: 'existing', label: '已有输入 ID' },
        ]} />
      </Form.Item>
      {mode === 'upload' && <Form.Item label="ZIP 文件" htmlFor="validation-zip">
        <input id="validation-zip" type="file" accept=".zip,application/zip" disabled={busy} onChange={event => { setFile(event.target.files?.[0]); resetSelection() }} />
      </Form.Item>}
      {mode === 'path' && <Form.Item label="服务端路径" htmlFor="validation-path">
        <Input id="validation-path" value={path} onChange={event => { setPath(event.target.value); resetSelection() }} placeholder="已授权的序列目录、申请目录或 ZIP 完整路径" />
      </Form.Item>}
      {mode === 'existing' ? <Form.Item label="已登记输入 ID" htmlFor="validation-input-id">
        <Input id="validation-input-id" value={inputId} onChange={event => setInputId(event.target.value)} />
      </Form.Item> : <Form.Item label="目标序列（可选）" htmlFor="validation-sequence">
        <Input id="validation-sequence" value={sequence} maxLength={4} onChange={event => { setSequence(event.target.value); resetSelection() }} placeholder="例如 0001；留空时由内容识别，歧义时必须选择" />
      </Form.Item>}
      {candidates && <Form.Item label="选择序列根目录" htmlFor="validation-root" required>
        <Select id="validation-root" value={root} onChange={(value: string) => {
          setRoot(value); setError(undefined)
          setSequence(candidates.find(candidate => candidate.rootRelativePath === value)?.sequenceNumber || '')
        }} options={candidates.map(candidate => ({ value: candidate.rootRelativePath, label: `${candidate.sequenceNumber} / ${candidate.rootRelativePath || '(根目录)'}` }))} />
      </Form.Item>}
      {error && <Alert type="error" showIcon title="无法登记输入" description={error} className="mb-4" />}
      <Space><Button type="primary" loading={busy} onClick={() => void submit()}>登记并选择</Button><Button onClick={() => { request.current?.abort(); onClose() }}>关闭</Button></Space>
    </Form>
  </Modal>
}
