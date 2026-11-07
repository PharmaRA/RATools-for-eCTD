import { useState } from 'react'
import { Alert, Button, Card, Form, Input, Select, Space, Table } from 'antd'
import { RotateCcw, Search } from 'lucide-react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'

import {
  AUDIT_LOG_ENTITY_TYPES,
  loadAuditLogs,
  type AuditLogFilterValues,
} from '../auditLogActions'
import { messages } from '../i18n/messages'
import { getErrorMessage } from './appShared'
import { buildAuditLogColumns } from './auditLogsDisplay'

const DEFAULT_PAGE_SIZE = 20

type AuditLogFormValues = {
  entityType?: string
  entityId?: string
  action?: string
  createdFromLocal?: string
  createdToLocal?: string
}

// 表单值 → 请求过滤值：时间范围拆成两个 ISO 边界，与后端 createdFromUtc/createdToUtc 对齐。
const toFilterValues = (values: AuditLogFormValues): AuditLogFilterValues => ({
  entityType: values.entityType,
  entityId: values.entityId?.trim() || undefined,
  action: values.action?.trim() || undefined,
  createdFromUtc: values.createdFromLocal ? new Date(values.createdFromLocal).toISOString() : undefined,
  createdToUtc: values.createdToLocal ? new Date(values.createdToLocal).toISOString() : undefined,
})

export const AuditLogsPage = () => {
  const [form] = Form.useForm<AuditLogFormValues>()
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(DEFAULT_PAGE_SIZE)
  const [filters, setFilters] = useState<AuditLogFilterValues>({})
  const [filterError, setFilterError] = useState<string>()
  const auditLogsQuery = useQuery({
    queryKey: ['audit-logs', { page, pageSize, filters }],
    queryFn: ({ signal }) => loadAuditLogs({ page, pageSize, filters, signal }),
    placeholderData: keepPreviousData,
  })
  const result = auditLogsQuery.data
  const loading = auditLogsQuery.isFetching
  const error = auditLogsQuery.error ? getErrorMessage(auditLogsQuery.error) : null

  const handleSearch = async () => {
    const values = await form.validateFields()
    if ([values.createdFromLocal, values.createdToLocal].some(value => value && !Number.isFinite(new Date(value).getTime()))) {
      setFilterError('请输入有效的日期时间。')
      return
    }
    if (values.createdFromLocal && values.createdToLocal && new Date(values.createdToLocal) < new Date(values.createdFromLocal)) {
      setFilterError('结束时间不能早于开始时间。')
      return
    }
    setFilterError(undefined)
    // 过滤条件变化后回到第 1 页：留在深页会看到空表格，容易被误读成"无数据"。
    setPage(1)
    setFilters(toFilterValues(values))
  }

  const handleReset = () => {
    form.resetFields()
    setPage(1)
    setFilters({})
    setFilterError(undefined)
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-bold m-0">{messages.auditLogs.title}</h2>
      </div>

      <Card size="small">
        <Form form={form} layout="inline" className="gap-y-2" initialValues={{ createdFromLocal: '', createdToLocal: '' }}>
          <Form.Item name="entityType" label={messages.auditLogs.entityTypeLabel}>
            <Select
              allowClear
              placeholder={messages.auditLogs.entityTypePlaceholder}
              style={{ minWidth: 190 }}
              options={AUDIT_LOG_ENTITY_TYPES.map((value) => ({ value, label: value }))}
            />
          </Form.Item>
          <Form.Item name="entityId" label={messages.auditLogs.entityIdLabel}>
            <Input allowClear placeholder={messages.auditLogs.entityIdPlaceholder} style={{ minWidth: 220 }} />
          </Form.Item>
          <Form.Item name="action" label={messages.auditLogs.actionLabel}>
            <Input allowClear placeholder={messages.auditLogs.actionPlaceholder} style={{ minWidth: 150 }} />
          </Form.Item>
          <Form.Item name="createdFromLocal" label="开始时间（本地）">
            <input type="datetime-local" step="1" className="border rounded px-2 py-1" />
          </Form.Item>
          <Form.Item name="createdToLocal" label="结束时间（本地）">
            <input type="datetime-local" step="1" className="border rounded px-2 py-1" />
          </Form.Item>
          <Form.Item>
            <Space>
              <Button type="primary" icon={<Search size={14} />} loading={loading} onClick={() => void handleSearch()}>
                {messages.auditLogs.search}
              </Button>
              <Button icon={<RotateCcw size={14} />} onClick={handleReset}>
                {messages.auditLogs.reset}
              </Button>
            </Space>
          </Form.Item>
        </Form>
      </Card>

      {error && <Alert type="error" showIcon title={messages.auditLogs.loadError} description={error} />}
      {filterError && <Alert type="error" showIcon title={filterError} />}

      <Table
        rowKey="id"
        size="small"
        loading={loading}
        columns={buildAuditLogColumns()}
        dataSource={result?.items ?? []}
        pagination={{
          current: result?.page ?? page,
          pageSize: result?.pageSize ?? pageSize,
          total: result?.totalCount ?? 0,
          showSizeChanger: true,
          // 上限与后端 clamp（200）一致，避免前端能选出会被服务端截断的页大小。
          pageSizeOptions: [20, 50, 100, 200],
          showTotal: (total) => `${total} ${messages.auditLogs.totalLabel}`,
          onChange: (nextPage, nextPageSize) => {
            setPage(nextPage)
            setPageSize(nextPageSize)
          },
        }}
      />
    </div>
  )
}
