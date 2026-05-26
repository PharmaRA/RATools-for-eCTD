import { useEffect, useState } from 'react'
import { Alert, Button, Card, Col, Form, Input, Modal, Radio, Row, Select, Space, Statistic, Table, Tag, Tooltip, message } from 'antd'
import { Activity, HardDrive, Plus, Trash2 } from 'lucide-react'

import { ApiRequestError, apiFetch } from '../apiClient'
import { performBatchDelete, performDelete, type BatchDeleteSummary, type DeleteMode } from '../deleteActions'
import { createApplication, getDefaultEctdTemplateKey, importApplicationWithTemplate, loadEctdTemplates, type EctdTemplateOption } from '../ectdTemplateActions'
import { mapImportErrorToMessage, type ImportApplicationResult } from '../importActions'
import { PathPicker } from '../PathPicker'
import { type Application, formatDate } from './appShared'

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

  const fetchApps = async () => {
    setLoading(true)
    try {
      const data = await apiFetch('/api/applications')
      setApps(data)
    } catch (err: any) {
      message.error('Failed to load apps: ' + err.message)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { fetchApps() }, [])

  useEffect(() => {
    const fetchTemplates = async () => {
      setTemplatesLoading(true)
      try {
        const templates = await loadEctdTemplates()
        setEctdTemplates(templates)
      } catch (err: any) {
        message.error('Failed to load eCTD templates: ' + err.message)
      } finally {
        setTemplatesLoading(false)
      }
    }

    void fetchTemplates()
  }, [])

  useEffect(() => {
    const validAppIds = new Set(apps.map((app) => app.id))
    setSelectedAppKeys((current) => {
      const next = current.filter((key) => validAppIds.has(key))
      return next.length === current.length ? current : next
    })
  }, [apps])

  const defaultTemplateKey = getDefaultEctdTemplateKey(ectdTemplates)
  const ectdTemplateOptions = ectdTemplates.map((template) => ({
    value: template.key,
    label: template.displayName,
  }))

  const handleCreateApp = async () => {
    try {
      const values = await form.validateFields()
      await createApplication({
        applicationNumber: values.applicationNumber,
        ectdTemplateKey: values.ectdTemplateKey,
        sponsorName: values.sponsorName,
        workingDirectoryParentPath: values.workingDirectoryParentPath,
      })
      message.success('Application created with Workspace!')
      setAppModalVisible(false)
      form.resetFields()
      fetchApps()
    } catch (e: any) { message.error('Failed to create application: ' + e.message) }
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
      message.success(`Application ${result.applicationNumber} imported.`)
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
      const outcome = await performDelete('application', `/api/applications/${id}`, mode)

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
      const items = selectedAppKeys.map((appId) => ({
        key: appId,
        label: appId,
        url: `/api/applications/${appId}`,
      }))
      const summary = await performBatchDelete('application', mode, items)

      setAppBatchSummary(summary)
      setAppBatchSummaryOpen(true)
      setAppBatchDeleteDialog({ open: false, mode: 'databaseOnly', running: false })
    } catch (error: any) {
      message.error('批量删除失败: ' + (error?.message || '未知错误'))
      setAppBatchDeleteDialog((current) => ({ ...current, running: false }))
    }
  }

  const closeAppBatchSummary = async () => {
    setAppBatchSummaryOpen(false)
    setAppBatchSummary(null)
    setSelectedAppKeys([])
    await fetchApps()
  }

  const failedAppBatchResults = (appBatchSummary?.results || []).filter((result) => result.outcome.kind === 'error')
  const hasSingleAppDeleteRunning = deletingAppIds.size > 0
  const canStartAppBatchDelete = selectedAppKeys.length > 0 && !appBatchDeleteDialog.running && !hasSingleAppDeleteRunning

  const columns = [
    { title: 'App Number', dataIndex: 'applicationNumber', render: (t: string) => <b>{t}</b> },
    { title: 'Region', dataIndex: 'region', render: (t: string) => <Tag>{t}</Tag> },
    { title: 'Sponsor', dataIndex: 'sponsorName' },
    { title: 'Created', dataIndex: 'createdUtc', render: formatDate },
    { title: 'Sequences', key: 'sequences', render: (_: any, r: Application) => r.sequences?.length || 0 },
    {
      title: 'Action', key: 'action', render: (_: any, r: Application) => (
        <Space>
          <Button
            type="primary"
            size="small"
            disabled={appBatchDeleteDialog.running}
            onClick={() => onSelectApp(r.id)}
          >
            Manage App
          </Button>
          <Button
            danger
            size="small"
            icon={<Trash2 size={14} />}
            title="Delete App"
            loading={deletingAppIds.has(r.id)}
            disabled={deletingAppIds.has(r.id) || appBatchDeleteDialog.running}
            onClick={() => openDeleteAppDialog(r.id)}
          />
        </Space>
      ),
    },
  ]

  return (
    <div className="bg-white p-6 rounded shadow-sm border border-gray-200">
      <div className="flex justify-between items-center mb-4">
        <h2 className="text-xl font-bold m-0 text-gray-800">Applications</h2>
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
            Batch Delete
          </Button>
          <Button
            type="primary"
            icon={<Plus size={16} className="mr-1" />}
            onClick={() => {
              form.setFieldsValue({ ectdTemplateKey: defaultTemplateKey })
              setAppModalVisible(true)
            }}
          >
            New Application
          </Button>
          <Button
            icon={<HardDrive size={16} className="mr-1" />}
            onClick={() => {
              importForm.setFieldsValue({ ectdTemplateKey: defaultTemplateKey })
              setImportModalVisible(true)
            }}
          >
            Import Application
          </Button>
          <Button onClick={fetchApps} loading={loading}>Refresh</Button>
        </Space>
      </div>
      <Table
        loading={loading}
        columns={columns}
        dataSource={apps}
        rowKey="id"
        rowSelection={{
          selectedRowKeys: selectedAppKeys,
          onChange: (nextSelectedRowKeys) => setSelectedAppKeys(nextSelectedRowKeys.map((key) => String(key))),
          getCheckboxProps: (record: any) => ({
            disabled: appBatchDeleteDialog.running || deletingAppIds.has(String(record.id)),
          }),
        }}
        pagination={{
          pageSize: 15,
          onChange: () => setSelectedAppKeys([]),
        }}
      />

      <Modal title="Create New Application" open={appModalVisible} onOk={handleCreateApp} onCancel={() => setAppModalVisible(false)} destroyOnClose width={600}>
        <Form form={form} layout="vertical" initialValues={{ ectdTemplateKey: defaultTemplateKey }}>
          <Row gutter={16}>
            <Col span={12}>
              <Form.Item name="applicationNumber" label="Application Number" rules={[{ required: true }]}>
                <Input placeholder="e.g. NDA123456" />
              </Form.Item>
            </Col>
            <Col span={12}>
              <Form.Item
                name="ectdTemplateKey"
                label="eCTD Template"
                initialValue={defaultTemplateKey}
                rules={[{ required: true, message: 'Please select an eCTD template.' }]}
              >
                <Select loading={templatesLoading} options={ectdTemplateOptions} placeholder="Select an eCTD template" />
              </Form.Item>
            </Col>
          </Row>
          <Form.Item name="sponsorName" label="Sponsor Name" rules={[{ required: true }]}>
            <Input placeholder="e.g. Acme Pharma Ltd." />
          </Form.Item>
          <Form.Item
            name="workingDirectoryParentPath"
            label={(
              <span className="flex items-center gap-1">
                Workspace Parent Directory
                <Tooltip title="The physical folder path on the server where this application's folder will be assembled.">
                  <Activity size={14} className="text-gray-400 cursor-help" />
                </Tooltip>
              </span>
            )}
            rules={[{ required: true, message: 'Please specify the working directory parent path.' }]}
          >
            <PathPicker placeholder="e.g. C:/eCTD/workspaces" />
          </Form.Item>
        </Form>
      </Modal>

      <Modal
        title="Import Application"
        open={importModalVisible}
        onOk={() => { void handleImportApplication() }}
        onCancel={() => setImportModalVisible(false)}
        okText="Import"
        cancelText="Cancel"
        confirmLoading={importingApplication}
        destroyOnClose
        width={680}
      >
        <Form form={importForm} layout="vertical" initialValues={{ ectdTemplateKey: defaultTemplateKey }}>
          <Form.Item
            name="workingDirectoryPath"
            label="Working Directory Path"
            rules={[{ required: true, message: 'Please input working directory path.' }]}
          >
            <PathPicker placeholder="e.g. C:/eCTD/workspaces/NDA123456" />
          </Form.Item>
          <Row gutter={16}>
            <Col span={8}>
              <Form.Item name="ectdTemplateKey" label="eCTD Template" rules={[{ required: true, message: 'Please select an eCTD template.' }]}>
                <Select loading={templatesLoading} options={ectdTemplateOptions} placeholder="Select an eCTD template" />
              </Form.Item>
            </Col>
            <Col span={16}>
              <Form.Item name="sponsorName" label="Sponsor Name" rules={[{ required: true, message: 'Please input sponsor name.' }]}>
                <Input placeholder="e.g. Demo Sponsor" />
              </Form.Item>
            </Col>
          </Row>
          <Alert
            type="info"
            showIcon
            message="The import reads sequences from the application workspace directory and parses each sequence index.xml."
          />
        </Form>
      </Modal>

      <Modal
        title="Import Result"
        open={importResultVisible}
        okText="Close"
        cancelButtonProps={{ style: { display: 'none' } }}
        onOk={() => {
          setImportResultVisible(false)
          setImportResult(null)
        }}
        onCancel={() => {
          setImportResultVisible(false)
          setImportResult(null)
        }}
        width={860}
      >
        {importResult && (
          <div className="flex flex-col gap-4">
            <Row gutter={12}>
              <Col span={8}><Card size="small"><Statistic title="Imported Sequences" value={importResult.importedSequenceCount} /></Card></Col>
              <Col span={8}><Card size="small"><Statistic title="Imported Documents" value={importResult.importedDocumentCount} /></Card></Col>
              <Col span={8}><Card size="small"><Statistic title="Imported Placements" value={importResult.importedPlacementCount} /></Card></Col>
            </Row>
            <Row gutter={12}>
              <Col span={12}><Card size="small"><Statistic title="Skipped Sequences" value={importResult.skippedSequenceCount} /></Card></Col>
              <Col span={12}><Card size="small"><Statistic title="Failed Sequences" value={importResult.failedSequenceCount} /></Card></Col>
            </Row>

            {(importResult.issues || []).length === 0 ? (
              <Alert type="success" showIcon message="Import finished without warnings or errors." />
            ) : (
              <Table
                size="small"
                pagination={{ pageSize: 8 }}
                rowKey={(_, index) => `issue-${index}`}
                dataSource={importResult.issues}
                columns={[
                  {
                    title: 'Severity',
                    dataIndex: 'severity',
                    key: 'severity',
                    width: 110,
                    render: (value: string) => <Tag color={String(value).toLowerCase() === 'error' ? 'red' : 'gold'}>{value}</Tag>,
                  },
                  { title: 'Code', dataIndex: 'code', key: 'code', width: 220 },
                  { title: 'Sequence', dataIndex: 'sequenceNumber', key: 'sequenceNumber', width: 130, render: (value?: string | null) => value || '-' },
                  { title: 'Message', dataIndex: 'message', key: 'message' },
                ]}
              />
            )}
          </div>
        )}
      </Modal>

      <Modal
        title="删除 Application"
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
              message="purgeWorkspace 是破坏性操作，无法撤销。"
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
              message="purgeWorkspace 是破坏性操作，无法撤销。"
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
          <div>成功: <Tag color="green">{appBatchSummary?.successCount ?? 0}</Tag></div>
          <div>失败: <Tag color="red">{appBatchSummary?.failureCount ?? 0}</Tag></div>
          {failedAppBatchResults.length > 0 && (
            <div className="flex flex-col gap-2">
              {failedAppBatchResults.map((result) => (
                <Alert
                  key={result.key}
                  type="error"
                  showIcon
                  message={`${result.label}: ${result.outcome.message}`}
                />
              ))}
            </div>
          )}
        </div>
      </Modal>
    </div>
  )
}
