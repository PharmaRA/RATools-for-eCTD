import { Tag, Tooltip, type TableColumnsType } from 'antd'

import { type AuditLogEntry } from '../auditLogActions'
import { messages } from '../i18n/messages'
import { formatDate } from './appShared'

// 实体类型着色：与发布相关的三类审计写入方各用一色，便于在长列表里扫读。
const ENTITY_TYPE_COLORS: Record<string, string> = {
  PublishJob: 'blue',
  SequenceValidation: 'orange',
  PublishJobArtifact: 'green',
}

export const getAuditLogEntityTypeColor = (entityType: string) =>
  ENTITY_TYPE_COLORS[entityType] ?? 'default'

/**
 * details 是自由文本摘要，长度不可控（发布审计里会带上完整的规则命中列表）。
 * 表格里截断显示、悬停看全文，避免单行把表格撑破。
 */
export const truncateAuditDetails = (details: string | null | undefined, maxLength = 80) => {
  if (!details) return ''
  return details.length <= maxLength ? details : `${details.slice(0, maxLength)}…`
}

export const buildAuditLogColumns = (): TableColumnsType<AuditLogEntry> => [
  {
    title: messages.auditLogs.columnCreated,
    dataIndex: 'createdUtc',
    width: 180,
    render: formatDate,
  },
  {
    title: messages.auditLogs.columnEntityType,
    dataIndex: 'entityType',
    width: 170,
    render: (value: string) => <Tag color={getAuditLogEntityTypeColor(value)}>{value}</Tag>,
  },
  {
    title: messages.auditLogs.columnEntityId,
    dataIndex: 'entityId',
    render: (value: string) => <span className="font-mono text-xs">{value}</span>,
  },
  {
    title: messages.auditLogs.columnAction,
    dataIndex: 'action',
    width: 140,
  },
  {
    title: messages.auditLogs.columnActor,
    dataIndex: 'actor',
    width: 120,
  },
  {
    title: messages.auditLogs.columnDetails,
    dataIndex: 'details',
    render: (value: string | null | undefined) => {
      const truncated = truncateAuditDetails(value)
      if (!truncated) return <span className="text-gray-400">—</span>

      return truncated === value
        ? <span>{truncated}</span>
        : <Tooltip title={value}><span>{truncated}</span></Tooltip>
    },
  },
]
