import { useCallback, useEffect, useState } from 'react'
import { Alert, Button, Form, Input, Modal, Radio, Select, Space, Table, Tabs, Tag, Tooltip, message } from 'antd'
import { ArrowLeft, HardDrive, Plus, Trash2 } from 'lucide-react'

import { loadApplication } from '../applicationActions'
import { ApiRequestError } from '../apiClient'
import { buildSequenceBatchDeleteItems, buildSequenceDeleteUrl, getFailedBatchDeleteResults, performBatchDelete, performDelete, type BatchDeleteSummary, type DeleteMode } from '../deleteActions'
import { PublishHistoryTab } from '../components/publishing/PublishHistoryTab'
import { createSequence } from '../sequenceActions'
import { type Application, type SequenceSummary, formatDate, getApplicationTemplateLabel, getErrorMessage } from './appShared'
import { buildSequenceColumns, formatApplicationDetailsTitle, getApplicationSequences } from './applicationDetailsDisplay'
import { buildBatchDeleteSummaryItems } from './batchDeleteDisplay'
import { buildBatchDeleteState } from './batchDeleteState'
import { buildSelectionKeySet, keepKnownSelectionKeys, normalizeSelectionKeys } from './selectionKeys'

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
      // 直查单个申请：详情页此前拉全量列表再前端 find，数据量增长后既慢又浪费。
      const data = await loadApplication(appId)
      setAppData(data)
    } catch (error) {
      if (error instanceof ApiRequestError && error.status === 404) {
        setAppData(null)
      } else {
        message.error('加载申请详情失败。')
      }
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

  const appSequences = appData?.sequences

  useEffect(() => {
    const validSequenceKeys = buildSelectionKeySet(getApplicationSequences({ sequences: appSequences }), (sequence) => sequence.sequenceNumber)
    setSelectedSequenceKeys((current) => keepKnownSelectionKeys(current, validSequenceKeys))
  }, [appSequences])

  const handleCreateSequence = async () => {
    try {
      const values = await form.validateFields()
      await createSequence({
        applicationId: appId,
        sequenceNumber: values.sequenceNumber,
        submissionType: values.submissionType,
        submissionSubType: values.submissionSubType,
        description: values.description,
      })
      message.success('序列创建成功！')
      setSeqModalVisible(false)
      form.resetFields()
      fetchApp()
    } catch (e) { message.error('创建序列失败：' + getErrorMessage(e)) }
  }

  const handleDeleteSequence = async (seqNumber: string, mode: DeleteMode) => {
    setDeletingSequenceNumbers((current) => new Set(current).add(seqNumber))

    try {
      const outcome = await performDelete('sequence', buildSequenceDeleteUrl(appId, seqNumber), mode)

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
      const items = buildSequenceBatchDeleteItems(appId, selectedSequenceKeys)
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

  const sequenceBatchSummaryItems = buildBatchDeleteSummaryItems(sequenceBatchSummary)
  const failedSequenceBatchResults = getFailedBatchDeleteResults(sequenceBatchSummary)
  const {
    hasSingleDeleteRunning: hasSingleSequenceDeleteRunning,
    canStartBatchDelete,
  } = buildBatchDeleteState({
    selectedKeys: selectedSequenceKeys,
    deletingKeys: deletingSequenceNumbers,
    isBatchDeleteRunning: sequenceBatchDeleteDialog.running,
  })
  const appTitle = formatApplicationDetailsTitle(appData, appId)
  const sequences = getApplicationSequences({ sequences: appSequences })
  const sequenceColumns = buildSequenceColumns({
    isBatchDeleteRunning: sequenceBatchDeleteDialog.running,
    deletingSequenceNumbers,
    onOpenWorkspace,
    onDeleteSequence: openDeleteSequenceDialog,
  })
  const tabItems = [
    {
      key: 'sequences',
      label: '序列',
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
                批量删除
              </Button>
              <Button type="primary" icon={<Plus size={16} className="mr-1" />} onClick={() => setSeqModalVisible(true)}>
                新建序列
              </Button>
            </Space>
          </div>
          <Table<SequenceSummary>
            loading={loading}
            dataSource={sequences}
            rowKey="sequenceNumber"
            size="middle"
            rowSelection={{
              selectedRowKeys: selectedSequenceKeys,
              onChange: (nextSelectedRowKeys) => setSelectedSequenceKeys(normalizeSelectionKeys(nextSelectedRowKeys)),
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
      label: '发布历史',
      children: <PublishHistoryTab appId={appId} />,
    },
  ]

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center gap-4 bg-white p-4 rounded shadow-sm border border-gray-200">
        <Button icon={<ArrowLeft size={16} />} onClick={onBack} disabled={sequenceBatchDeleteDialog.running}>返回申请列表</Button>
        <div className="flex-1">
          <div className="flex justify-between items-start">
            <h2 className="m-0 text-xl font-bold">{appTitle}</h2>
          </div>
          <Space className="mt-2 flex-wrap">
            <Tag color="blue">{getApplicationTemplateLabel(appData)}</Tag>
            <span className="text-gray-500 text-sm border-r pr-2">Created: {formatDate(appData?.createdUtc ?? undefined)}</span>
            {appData?.workingDirectoryPath && (
              <Tooltip title="物理工作目录路径">
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

      <Modal title="新建序列" open={seqModalVisible} onOk={handleCreateSequence} onCancel={() => setSeqModalVisible(false)} destroyOnHidden>
        <Form form={form} layout="vertical">
          <Form.Item name="sequenceNumber" label="序列号" initialValue="0000" rules={[{ required: true }]}>
            <Input placeholder="0000" />
          </Form.Item>
          <Form.Item name="submissionType" label="提交类型" initialValue="Original Application" rules={[{ required: true }]}>
            <Select options={[{ value: 'Original Application', label: 'Original Application' }, { value: 'Supplemental Application', label: 'Supplemental Application' }, { value: 'Amendment', label: 'Amendment' }]} />
          </Form.Item>
          <Form.Item name="submissionSubType" label="提交子类型" initialValue="Presubmission">
            <Input />
          </Form.Item>
          <Form.Item name="description" label="描述" rules={[{ required: true }, { min: 2, max: 512 }]}>
            <Input.TextArea placeholder="例如：初始 eCTD 提交" rows={3} />
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
          {sequenceBatchSummaryItems.map((item) => (
            <div key={item.key}>{item.label}: <Tag color={item.color}>{item.count}</Tag></div>
          ))}
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
