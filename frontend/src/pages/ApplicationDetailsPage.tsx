import { useCallback, useEffect, useState } from 'react'
import { Alert, Button, Form, Input, Modal, Radio, Select, Space, Table, Tabs, Tag, Tooltip, message, type TableColumnsType } from 'antd'
import { ArrowLeft, HardDrive, Plus, Trash2 } from 'lucide-react'

import { apiFetch } from '../apiClient'
import { performBatchDelete, performDelete, type BatchDeleteSummary, type DeleteMode } from '../deleteActions'
import { PublishHistoryTab } from '../components/publishing/PublishHistoryTab'
import { type Application, type SequenceSummary, formatDate, getApplicationTemplateLabel, getErrorMessage } from './appShared'

export const ApplicationDetailsPage = ({ appId, onBack, onOpenWorkspace }: { appId: string, onBack: () => void, onOpenWorkspace: (seq: string) => void }) => {
  const [appData, setAppData] = useState<Application | null>(null)
  const [loading, setLoading] = useState(false)
  const [deletingSequenceNumbers, setDeletingSequenceNumbers] = useState<Set<string>>(new Set())
  const [seqModalVisible, setSeqModalVisible] = useState(false)
  const [sequenceDeleteDialog, setSequenceDeleteDialog] = useState<{ open: boolean; sequenceNumber: string | null; mode: DeleteMode }>({
    open: false,
    sequenceNumber: null,
    mode: 'databaseOnly',
  })
  const [selectedSequenceKeys, setSelectedSequenceKeys] = useState<string[]>([])
  const [sequenceBatchDeleteDialog, setSequenceBatchDeleteDialog] = useState<{ open: boolean; mode: DeleteMode; running: boolean }>({
    open: false,
    mode: 'databaseOnly',
    running: false,
  })
  const [sequenceBatchSummary, setSequenceBatchSummary] = useState<BatchDeleteSummary | null>(null)
  const [sequenceBatchSummaryOpen, setSequenceBatchSummaryOpen] = useState(false)
  const [form] = Form.useForm()

  const fetchApp = useCallback(async () => {
    setLoading(true)
    try {
      const data = await apiFetch('/api/applications') as Application[]
      const target = data.find((application) => application.id === appId)
      setAppData(target || null)
    } catch {
      message.error('Failed to load application details.')
    } finally {
      setLoading(false)
    }
  }, [appId])

  useEffect(() => {
    void Promise.resolve().then(fetchApp)
  }, [fetchApp])

  useEffect(() => {
    setSelectedSequenceKeys([])
  }, [appId])

  useEffect(() => {
    const validSequenceKeys = new Set((appData?.sequences || []).map((sequence) => String(sequence.sequenceNumber)))
    setSelectedSequenceKeys((current) => {
      const next = current.filter((key) => validSequenceKeys.has(key))
      return next.length === current.length ? current : next
    })
  }, [appData?.sequences])

  const handleCreateSequence = async () => {
    try {
      const values = await form.validateFields()
      await apiFetch(`/api/applications/${appId}/sequences`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(values),
      })
      message.success('Sequence created successfully!')
      setSeqModalVisible(false)
      form.resetFields()
      fetchApp()
    } catch (e) { message.error('Failed to create sequence: ' + getErrorMessage(e)) }
  }

  const handleDeleteSequence = async (seqNumber: string, mode: DeleteMode) => {
    setDeletingSequenceNumbers((current) => new Set(current).add(seqNumber))

    try {
      const outcome = await performDelete('sequence', `/api/applications/${appId}/sequences/${seqNumber}`, mode)

      if (outcome.kind === 'success') {
        message.success(outcome.message)
      } else {
        message.error(outcome.message)
      }

      if (outcome.shouldRefresh) {
        await fetchApp()
      }
    } finally {
      setDeletingSequenceNumbers((current) => {
        const next = new Set(current)
        next.delete(seqNumber)
        return next
      })
    }
  }

  const openDeleteSequenceDialog = (sequenceNumber: string) => {
    setSequenceDeleteDialog({
      open: true,
      sequenceNumber,
      mode: 'databaseOnly',
    })
  }

  const confirmDeleteSequence = async () => {
    const sequenceNumber = sequenceDeleteDialog.sequenceNumber
    if (!sequenceNumber) {
      return
    }

    const mode = sequenceDeleteDialog.mode
    setSequenceDeleteDialog((current) => ({ ...current, open: false }))
    await handleDeleteSequence(sequenceNumber, mode)
  }

  const confirmBatchDeleteSequences = async () => {
    if (selectedSequenceKeys.length === 0 || deletingSequenceNumbers.size > 0) {
      if (deletingSequenceNumbers.size > 0) {
        message.warning('存在进行中的单条删除，请稍后再试批量删除。')
      }
      return
    }

    setSequenceBatchDeleteDialog((current) => ({ ...current, running: true }))

    try {
      const mode = sequenceBatchDeleteDialog.mode
      const items = selectedSequenceKeys.map((sequenceNumber) => ({
        key: sequenceNumber,
        label: sequenceNumber,
        url: `/api/applications/${appId}/sequences/${sequenceNumber}`,
      }))
      const summary = await performBatchDelete('sequence', mode, items)

      setSequenceBatchSummary(summary)
      setSequenceBatchSummaryOpen(true)
      setSequenceBatchDeleteDialog({ open: false, mode: 'databaseOnly', running: false })
    } catch (error) {
      message.error('批量删除失败: ' + getErrorMessage(error, '未知错误'))
      setSequenceBatchDeleteDialog((current) => ({ ...current, running: false }))
    }
  }

  const closeSequenceBatchSummary = async () => {
    setSequenceBatchSummaryOpen(false)
    setSequenceBatchSummary(null)
    setSelectedSequenceKeys([])
    await fetchApp()
  }

  const failedSequenceBatchResults = (sequenceBatchSummary?.results || []).filter((result) => result.outcome.kind === 'error')
  const hasSingleSequenceDeleteRunning = deletingSequenceNumbers.size > 0
  const canStartBatchDelete = selectedSequenceKeys.length > 0 && !sequenceBatchDeleteDialog.running && !hasSingleSequenceDeleteRunning
  const appTitle = appData ? `${appData.applicationNumber} (${appData.sponsorName})` : appId
  const sequenceColumns: TableColumnsType<SequenceSummary> = [
    { title: 'Sequence', dataIndex: 'sequenceNumber', render: (t) => <b>{t}</b> },
    { title: 'Submission Type', dataIndex: 'submissionType' },
    { title: 'Description', dataIndex: 'description' },
    {
      title: 'Actions', key: 'actions', render: (_, r) => (
        <Space>
          <Button type="link" size="small" disabled={sequenceBatchDeleteDialog.running} onClick={() => onOpenWorkspace(r.sequenceNumber)}>
            Enter Workspace
          </Button>
          <Button
            danger
            type="text"
            size="small"
            icon={<Trash2 size={14} />}
            title="Delete Sequence"
            loading={deletingSequenceNumbers.has(r.sequenceNumber)}
            disabled={deletingSequenceNumbers.has(r.sequenceNumber) || sequenceBatchDeleteDialog.running}
            onClick={() => openDeleteSequenceDialog(r.sequenceNumber)}
          />
        </Space>
      ),
    },
  ]
  const tabItems = [
    {
      key: 'sequences',
      label: 'Sequences',
      children: (
        <>
          <div className="mb-4 flex justify-end">
            <Space>
              <Button
                danger
                icon={<Trash2 size={14} className="mr-1" />}
                disabled={!canStartBatchDelete}
                loading={sequenceBatchDeleteDialog.running}
                onClick={() => {
                  if (hasSingleSequenceDeleteRunning) {
                    return
                  }
                  setSequenceBatchDeleteDialog({ open: true, mode: 'databaseOnly', running: false })
                }}
              >
                Batch Delete
              </Button>
              <Button type="primary" icon={<Plus size={16} className="mr-1" />} onClick={() => setSeqModalVisible(true)}>
                New Sequence
              </Button>
            </Space>
          </div>
          <Table<SequenceSummary>
            loading={loading}
            dataSource={appData?.sequences || []}
            rowKey="sequenceNumber"
            size="middle"
            rowSelection={{
              selectedRowKeys: selectedSequenceKeys,
              onChange: (nextSelectedRowKeys) => setSelectedSequenceKeys(nextSelectedRowKeys.map((key) => String(key))),
              getCheckboxProps: (record) => ({
                disabled: sequenceBatchDeleteDialog.running || deletingSequenceNumbers.has(String(record.sequenceNumber)),
              }),
            }}
            pagination={{
              onChange: () => setSelectedSequenceKeys([]),
            }}
            columns={sequenceColumns}
          />
        </>
      ),
    },
    {
      key: 'history',
      label: 'Publish History',
      children: <PublishHistoryTab appId={appId} />,
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-4 bg-white p-4 rounded shadow-sm border border-gray-200">
        <Button icon={<ArrowLeft size={16} />} onClick={onBack} disabled={sequenceBatchDeleteDialog.running}>Back to Applications</Button>
        <div className="flex-1">
          <div className="flex justify-between items-start">
            <h2 className="m-0 text-xl font-bold">{appTitle}</h2>
          </div>
          <Space className="mt-2 flex-wrap">
            <Tag color="blue">{getApplicationTemplateLabel(appData)}</Tag>
            <span className="text-gray-500 text-sm border-r pr-2">Created: {formatDate(appData?.createdUtc ?? undefined)}</span>
            {appData?.workingDirectoryPath && (
              <Tooltip title="Physical Working Directory Path">
                <span className="text-gray-600 text-sm flex items-center gap-1 bg-gray-100 px-2 py-0.5 rounded font-mono">
                  <HardDrive size={14} className="text-blue-500" />
                  {appData.workingDirectoryPath}
                </span>
              </Tooltip>
            )}
          </Space>
        </div>
      </div>

      <div className="bg-white p-4 rounded shadow-sm border border-gray-200">
        <Tabs defaultActiveKey="sequences" items={tabItems} />
      </div>

      <Modal title="Create New Sequence" open={seqModalVisible} onOk={handleCreateSequence} onCancel={() => setSeqModalVisible(false)} destroyOnHidden>
        <Form form={form} layout="vertical">
          <Form.Item name="sequenceNumber" label="Sequence Number" initialValue="0000" rules={[{ required: true }]}>
            <Input placeholder="0000" />
          </Form.Item>
          <Form.Item name="submissionType" label="Submission Type" initialValue="Original Application" rules={[{ required: true }]}>
            <Select options={[{ value: 'Original Application', label: 'Original Application' }, { value: 'Supplemental Application', label: 'Supplemental Application' }, { value: 'Amendment', label: 'Amendment' }]} />
          </Form.Item>
          <Form.Item name="submissionSubType" label="Submission Sub-Type" initialValue="Presubmission">
            <Input />
          </Form.Item>
          <Form.Item name="description" label="Description" rules={[{ required: true }, { min: 2, max: 512 }]}>
            <Input.TextArea placeholder="e.g. Initial eCTD Submission" rows={3} />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="删除 Sequence"
        open={sequenceDeleteDialog.open}
        okText="确认删除"
        cancelText="取消"
        onOk={confirmDeleteSequence}
        onCancel={() => setSequenceDeleteDialog({ open: false, sequenceNumber: null, mode: 'databaseOnly' })}
        confirmLoading={sequenceDeleteDialog.sequenceNumber !== null && deletingSequenceNumbers.has(sequenceDeleteDialog.sequenceNumber)}
      >
        <div className="flex flex-col gap-3">
          <div>
            即将删除 Sequence: <Tag>{sequenceDeleteDialog.sequenceNumber ?? '-'}</Tag>
          </div>
          <Radio.Group
            value={sequenceDeleteDialog.mode}
            onChange={(event) => setSequenceDeleteDialog((current) => ({
              ...current,
              mode: event.target.value as DeleteMode,
            }))}
          >
            <Space direction="vertical">
              <Radio value="databaseOnly">只删数据库记录</Radio>
              <Radio value="purgeWorkspace">删除数据库记录并递归删除对应工作目录/发布产物</Radio>
            </Space>
          </Radio.Group>
          {sequenceDeleteDialog.mode === 'purgeWorkspace' && (
            <Alert
              type="warning"
              showIcon
              title="purgeWorkspace 是破坏性操作，无法撤销。"
            />
          )}
        </div>
      </Modal>

      <Modal
        title="批量删除 Sequence"
        open={sequenceBatchDeleteDialog.open}
        okText="确认批量删除"
        cancelText="取消"
        onOk={confirmBatchDeleteSequences}
        onCancel={() => setSequenceBatchDeleteDialog({ open: false, mode: 'databaseOnly', running: false })}
        confirmLoading={sequenceBatchDeleteDialog.running}
        okButtonProps={{ disabled: !canStartBatchDelete }}
        cancelButtonProps={{ disabled: sequenceBatchDeleteDialog.running }}
      >
        <div className="flex flex-col gap-3">
          <div>
            已选择 <Tag>{selectedSequenceKeys.length}</Tag> 个 Sequence。
          </div>
          <Radio.Group
            value={sequenceBatchDeleteDialog.mode}
            onChange={(event) => setSequenceBatchDeleteDialog((current) => ({
              ...current,
              mode: event.target.value as DeleteMode,
            }))}
          >
            <Space direction="vertical">
              <Radio value="databaseOnly">只删数据库记录</Radio>
              <Radio value="purgeWorkspace">删除数据库记录并递归删除对应工作目录/发布产物</Radio>
            </Space>
          </Radio.Group>
          {sequenceBatchDeleteDialog.mode === 'purgeWorkspace' && (
            <Alert
              type="warning"
              showIcon
              title="purgeWorkspace 是破坏性操作，无法撤销。"
            />
          )}
        </div>
      </Modal>

      <Modal
        title="批量删除结果"
        open={sequenceBatchSummaryOpen}
        okText="关闭"
        cancelButtonProps={{ style: { display: 'none' } }}
        onOk={() => { void closeSequenceBatchSummary() }}
        onCancel={() => { void closeSequenceBatchSummary() }}
      >
        <div className="flex flex-col gap-3">
          <div>成功: <Tag color="green">{sequenceBatchSummary?.successCount ?? 0}</Tag></div>
          <div>失败: <Tag color="red">{sequenceBatchSummary?.failureCount ?? 0}</Tag></div>
          {failedSequenceBatchResults.length > 0 && (
            <div className="flex flex-col gap-2">
              {failedSequenceBatchResults.map((result) => (
                <Alert
                  key={result.key}
                  type="error"
                  showIcon
                  title={`${result.label}: ${result.outcome.message}`}
                />
              ))}
            </div>
          )}
        </div>
      </Modal>
    </div>
  )
}
