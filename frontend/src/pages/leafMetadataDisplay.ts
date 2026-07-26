import { createElement } from 'react'
import { Tag } from 'antd'

import { formatOptionalText } from './appShared'
import type { DocumentPlacementRecord, DocumentRecord } from '../workspaceTree'

const leafPlacementOperations = ['New', 'Replace', 'Delete', 'Append'] as const

const leafPlacementOperationLabels: Record<string, string> = {
  New: '新建',
  Replace: '替换',
  Delete: '删除',
  Append: '追加',
}

export const buildLeafPlacementOperationOptions = () => leafPlacementOperations.map((operation) => ({
  value: operation,
  label: leafPlacementOperationLabels[operation] ?? operation,
}))

export const buildLeafPlacementDescriptionItems = (
  placement: Pick<DocumentPlacementRecord, 'id' | 'ctdSection' | 'operation'>,
  document: Pick<DocumentRecord, 'storagePath'>,
) => [
  { key: 'placement-id', label: '映射 ID', children: placement.id },
  { key: 'ectd-section', label: 'eCTD 章节', children: createElement(Tag, null, placement.ctdSection) },
  { key: 'operation', label: '操作类型', children: createElement(Tag, { color: 'blue' }, placement.operation) },
  { key: 'storage-path', label: '存储路径', children: renderLeafBreakAllText(document.storagePath) },
]

type LeafPreview = {
  operation: string
  title: string
  href: string
  modifiedFileHref?: string | null
  mediaType?: string | null
  sourceFileName: string
  revisedFileName?: string | null
  storagePath: string
}

export const renderLeafBreakAllText = (value: string) => createElement('span', { className: 'text-xs break-all' }, value)

export const formatLeafPreviewOptionalText = formatOptionalText

export const buildLeafPreviewDescriptionItems = (preview: LeafPreview) => [
  { key: 'operation', label: 'operation', children: preview.operation },
  { key: 'title', label: 'title', children: preview.title },
  { key: 'href', label: 'xlink:href', children: renderLeafBreakAllText(preview.href) },
  ...(preview.modifiedFileHref
    ? [{ key: 'modified-file', label: 'modified-file', children: renderLeafBreakAllText(preview.modifiedFileHref) }]
    : []),
  { key: 'mime-type', label: 'MIME 类型', children: formatLeafPreviewOptionalText(preview.mediaType) },
  { key: 'checksum-type', label: '校验和类型', children: 'md5' },
  { key: 'checksum', label: '校验和', children: renderLeafBreakAllText('发布时计算') },
  { key: 'source-file-name', label: '源文件名', children: preview.sourceFileName },
  { key: 'resulting-file-name', label: '生成文件名', children: formatLeafPreviewOptionalText(preview.revisedFileName) },
  { key: 'storage-path', label: '存储路径', children: renderLeafBreakAllText(preview.storagePath) },
]
