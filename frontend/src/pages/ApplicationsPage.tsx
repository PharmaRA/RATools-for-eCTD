import { useCallback, useEffect, useState } from 'react'
import { Alert, Button, Form, Modal, Radio, Space, Table, Tag, message } from 'antd'
import { HardDrive, Plus, Trash2 } from 'lucide-react'

import { ApiRequestError } from '../apiClient'
import { createApplication, loadApplications } from '../applicationActions'
import { buildApplicationBatchDeleteItems, buildApplicationDeleteUrl, getFailedBatchDeleteResults, performBatchDelete, performDelete, type BatchDeleteSummary, type DeleteMode } from '../deleteActions'
import { buildEctdTemplateSelectOptions, getDefaultEctdTemplateKey, importApplicationWithTemplate, loadEctdTemplates, type EctdTemplateOption } from '../ectdTemplateActions'
import { mapImportErrorToMessage, type ImportApplicationResult } from '../importActions'
import { type Application, getErrorMessage } from './appShared'
import { ApplicationFormModals } from './applications/ApplicationFormModals'
import { ApplicationImportResultModal } from './applications/ApplicationImportResultModal'
import { buildApplicationColumns } from './applicationsDisplay'
import { buildBatchDeleteSummaryItems } from './batchDeleteDisplay'
import { buildBatchDeleteState } from './batchDeleteState'
import { buildSelectionKeySet, keepKnownSelectionKeys, normalizeSelectionKeys } from './selectionKeys'

export const ApplicationsPage = ({ onSelectApp }: { onSelectApp: (id: string) => void }) => {
  const [loading, setLoading] = useState(false)
  const [deletingAppIds, setDeletingAppIds] = useState<Set<string>>(new Set())
  const [apps, setApps] = useState<Application[]>([])
  const [appModalVisible, setAppModalVisible] = useState(false)
  const [importModalVisible, setImportModalVisible] = useState(false)
  const [importingApplication, setImportingApplication] = useState(false)
  const [importResult, setImportResult] = useState<ImportApplicationResult | null>(null)
  const [importResultVisible, setImportResultVisible] = useState(false)
  const [appDeleteDialog, setAppDeleteDialog] = useState<{ open: boolean; appId: string | null; mode: DeleteMode }>({
    open: false,
    appId: null,
    mode: 'databaseOnly',
  })
  const [selectedAppKeys, setSelectedAppKeys] = useState<string[]>([])
  const [appBatchDeleteDialog, setAppBatchDeleteDialog] = useState<{ open: boolean; mode: DeleteMode; running: boolean }>({
    open: false,
    mode: 'databaseOnly',
    running: false,
  })
  const [appBatchSummary, setAppBatchSummary] = useState<BatchDeleteSummary | null>(null)
  const [appBatchSummaryOpen, setAppBatchSummaryOpen] = useState(false)
  const [form] = Form.useForm()
  const [importForm] = Form.useForm()
  const [ectdTemplates, setEctdTemplates] = useState<EctdTemplateOption[]>([])
  const [templatesLoading, setTemplatesLoading] = useState(false)

  const fetchApps = useCallback(async () => {
    setLoading(true)
    try {
      const data = await loadApplications()
      setApps(data)
    } catch (err) {
      message.error('加载申请列表失败：' + getErrorMessage(err))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void Promise.resolve().then(fetchApps)
  }, [fetchApps])

  useEffect(() => {
    const fetchTemplates = async () => {
      setTemplatesLoading(true)
      try {
        const templates = await loadEctdTemplates()
        setEctdTemplates(templates)
      } catch (err) {
        message.error('加载 eCTD 模板失败：' + getErrorMessage(err))
      } finally {
        setTemplatesLoading(false)
      }
    }

    void fetchTemplates()
  }, [])

  useEffect(() => {
    const validAppIds = buildSelectionKeySet(apps, (app) => app.id)
    setSelectedAppKeys((current) => keepKnownSelectionKeys(current, validAppIds))
  }, [apps])

  const defaultTemplateKey = getDefaultEctdTemplateKey(ectdTemplates)
  const ectdTemplateOptions = buildEctdTemplateSelectOptions(ectdTemplates)

  const handleCreateApp = async () => {
    try {
      const values = await form.validateFields()
      await createApplication({
        applicationNumber: values.applicationNumber,
        ectdTemplateKey: values.ectdTemplateKey,
        sponsorName: values.sponsorName,
        workingDirectoryParentPath: values.workingDirectoryParentPath,
      })
      message.success('申请与工作区已创建！')
      setAppModalVisible(false)
      form.resetFields()
      fetchApps()
    } catch (e) { message.error('创建申请失败：' + getErrorMessage(e)) }
  }

  const handleImportApplication = async () => {
    try {
      const values = await importForm.validateFields()
      setImportingApplication(true)

      const result = await importApplicationWithTemplate({
        workingDirectoryPath: values.workingDirectoryPath,
        ectdTemplateKey: values.ectdTemplateKey,
        sponsorName: values.sponsorName,
      })

      setImportResult(result)
      setImportResultVisible(true)
      setImportModalVisible(false)
      importForm.resetFields()
      await fetchApps()
      message.success(`申请 ${result.applicationNumber} 已导入。`)
    } catch (error) {
      if (error instanceof ApiRequestError || error instanceof Error) {
        message.error(mapImportErrorToMessage(error))
      }
    } finally {
      setImportingApplication(false)
    }
  }

  const handleDeleteApp = async (id: string, mode: DeleteMode) => {
    setDeletingAppIds((current) => new Set(current).add(id))

    try {
      const outcome = await performDelete('application', buildApplicationDeleteUrl(id), mode)

      if (outcome.kind === 'success') {
        message.success(outcome.message)
      } else {
        message.error(outcome.message)
      }

      if (outcome.shouldRefresh) {
        await fetchApps()
      }
    } finally {
      setDeletingAppIds((current) => {
        const next = new Set(current)
        next.delete(id)
        return next
      })
    }
  }

  const openDeleteAppDialog = (id: string) => {
    setAppDeleteDialog({
      open: true,
      appId: id,
      mode: 'databaseOnly',
    })
  }

  const confirmDeleteApp = async () => {
    const appId = appDeleteDialog.appId
    if (!appId) {
      return
    }

    const mode = appDeleteDialog.mode
    setAppDeleteDialog((current) => ({ ...current, open: false }))
    await handleDeleteApp(appId, mode)
  }

  const confirmBatchDeleteApps = async () => {
    if (selectedAppKeys.length === 0 || deletingAppIds.size > 0) {
      if (deletingAppIds.size > 0) {
        message.warning('存在进行中的单条删除，请稍后再试批量删除。')
      }
      return
    }

    setAppBatchDeleteDialog((current) => ({ ...current, running: true }))

    try {
      const mode = appBatchDeleteDialog.mode
      const items = buildApplicationBatchDeleteItems(selectedAppKeys)
      const summary = await performBatchDelete('application', mode, items)

      setAppBatchSummary(summary)
      setAppBatchSummaryOpen(true)
      setAppBatchDeleteDialog({ open: false, mode: 'databaseOnly', running: false })
    } catch (error) {
      message.error('批量删除失败: ' + getErrorMessage(error, '未知错误'))
      setAppBatchDeleteDialog((current) => ({ ...current, running: false }))
    }
  }

  const closeAppBatchSummary = async () => {
    setAppBatchSummaryOpen(false)
    setAppBatchSummary(null)
    setSelectedAppKeys([])
    await fetchApps()
  }

  const appBatchSummaryItems = buildBatchDeleteSummaryItems(appBatchSummary)
  const failedAppBatchResults = getFailedBatchDeleteResults(appBatchSummary)
  const {
    hasSingleDeleteRunning: hasSingleAppDeleteRunning,
    canStartBatchDelete: canStartAppBatchDelete,
  } = buildBatchDeleteState({
    selectedKeys: selectedAppKeys,
    deletingKeys: deletingAppIds,
    isBatchDeleteRunning: appBatchDeleteDialog.running,
  })
  const columns = buildApplicationColumns({
    isBatchDeleteRunning: appBatchDeleteDialog.running,
    deletingAppIds,
    onSelectApp,
    onDeleteApp: openDeleteAppDialog,
  })

  return (
    <div className="bg-white p-6 rounded shadow-sm border border-gray-200">
      <div className="flex justify-between items-center mb-4">
        <h2 className="text-xl font-bold m-0 text-gray-800">申请列表</h2>
        <Space>
          <Button
            danger
            icon={<Trash2 size={14} className="mr-1" />}
            disabled={!canStartAppBatchDelete}
            loading={appBatchDeleteDialog.running}
            onClick={() => {
              if (hasSingleAppDeleteRunning) {
                return
              }
              setAppBatchDeleteDialog({ open: true, mode: 'databaseOnly', running: false })
            }}
          >
            批量删除
          </Button>
          <Button
            type="primary"
            icon={<Plus size={16} className="mr-1" />}
            onClick={() => {
              form.setFieldsValue({ ectdTemplateKey: defaultTemplateKey })
              setAppModalVisible(true)
            }}
          >
            新建申请
          </Button>
          <Button
            icon={<HardDrive size={16} className="mr-1" />}
            onClick={() => {
              importForm.setFieldsValue({ ectdTemplateKey: defaultTemplateKey })
              setImportModalVisible(true)
            }}
          >
            导入申请
          </Button>
          <Button onClick={fetchApps} loading={loading}>刷新</Button>
        </Space>
      </div>
      <Table<Application>
        loading={loading}
        columns={columns}
        dataSource={apps}
        rowKey="id"
        rowSelection={{
          selectedRowKeys: selectedAppKeys,
          onChange: (nextSelectedRowKeys) => setSelectedAppKeys(normalizeSelectionKeys(nextSelectedRowKeys)),
          getCheckboxProps: (record) => ({
            disabled: appBatchDeleteDialog.running || deletingAppIds.has(String(record.id)),
          }),
        }}
        pagination={{
          pageSize: 15,
          onChange: () => setSelectedAppKeys([]),
        }}
      />

      <ApplicationFormModals
        createOpen={appModalVisible}
        importOpen={importModalVisible}
        createForm={form}
        importForm={importForm}
        defaultTemplateKey={defaultTemplateKey}
        templateOptions={ectdTemplateOptions}
        templatesLoading={templatesLoading}
        importingApplication={importingApplication}
        onCreate={() => { void handleCreateApp() }}
        onCreateCancel={() => setAppModalVisible(false)}
        onImport={() => { void handleImportApplication() }}
        onImportCancel={() => setImportModalVisible(false)}
      />

      <ApplicationImportResultModal
        open={importResultVisible}
        result={importResult}
        onClose={() => {
          setImportResultVisible(false)
          setImportResult(null)
        }}
      />

      <Modal
        title="删除申请"
        open={appDeleteDialog.open}
        okText="确认删除"
        cancelText="取消"
        onOk={confirmDeleteApp}
        onCancel={() => setAppDeleteDialog({ open: false, appId: null, mode: 'databaseOnly' })}
        confirmLoading={appDeleteDialog.appId !== null && deletingAppIds.has(appDeleteDialog.appId)}
      >
        <div className="flex flex-col gap-3">
          <div>
            即将删除 Application: <Tag>{appDeleteDialog.appId ?? '-'}</Tag>
          </div>
          <Radio.Group
            value={appDeleteDialog.mode}
            onChange={(event) => setAppDeleteDialog((current) => ({
              ...current,
              mode: event.target.value as DeleteMode,
            }))}
          >
            <Space direction="vertical">
              <Radio value="databaseOnly">只删数据库记录</Radio>
              <Radio value="purgeWorkspace">删除数据库记录并递归删除对应工作目录/发布产物</Radio>
            </Space>
          </Radio.Group>
          {appDeleteDialog.mode === 'purgeWorkspace' && (
            <Alert
              type="warning"
              showIcon
              title="purgeWorkspace 是破坏性操作，无法撤销。"
            />
          )}
        </div>
      </Modal>

      <Modal
        title="批量删除 Application"
        open={appBatchDeleteDialog.open}
        okText="确认批量删除"
        cancelText="取消"
        onOk={confirmBatchDeleteApps}
        onCancel={() => setAppBatchDeleteDialog({ open: false, mode: 'databaseOnly', running: false })}
        confirmLoading={appBatchDeleteDialog.running}
        okButtonProps={{ disabled: !canStartAppBatchDelete }}
        cancelButtonProps={{ disabled: appBatchDeleteDialog.running }}
      >
        <div className="flex flex-col gap-3">
          <div>
            已选择 <Tag>{selectedAppKeys.length}</Tag> 个 Application。
          </div>
          <Radio.Group
            value={appBatchDeleteDialog.mode}
            onChange={(event) => setAppBatchDeleteDialog((current) => ({
              ...current,
              mode: event.target.value as DeleteMode,
            }))}
          >
            <Space direction="vertical">
              <Radio value="databaseOnly">只删数据库记录</Radio>
              <Radio value="purgeWorkspace">删除数据库记录并递归删除对应工作目录/发布产物</Radio>
            </Space>
          </Radio.Group>
          {appBatchDeleteDialog.mode === 'purgeWorkspace' && (
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
        open={appBatchSummaryOpen}
        okText="关闭"
        cancelButtonProps={{ style: { display: 'none' } }}
        onOk={() => { void closeAppBatchSummary() }}
        onCancel={() => { void closeAppBatchSummary() }}
      >
        <div className="flex flex-col gap-3">
          {appBatchSummaryItems.map((item) => (
            <div key={item.key}>{item.label}: <Tag color={item.color}>{item.count}</Tag></div>
          ))}
          {failedAppBatchResults.length > 0 && (
            <div className="flex flex-col gap-2">
              {failedAppBatchResults.map((result) => (
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
