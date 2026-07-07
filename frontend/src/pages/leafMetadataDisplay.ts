import { createElement } from 'react'
import { Tag } from 'antd'

import { formatOptionalText } from './appShared'
import type { DocumentPlacementRecord, DocumentRecord } from '../workspaceTree'

const leafPlacementOperations = ['New', 'Replace', 'Delete', 'Append'] as const

export const buildLeafPlacementOperationOptions = () => leafPlacementOperations.map((operation) => ({
  value: operation,
  label: operation,
}))

export const buildLeafPlacementDescriptionItems = (
  placement: Pick<DocumentPlacementRecord, 'id' | 'ctdSection' | 'operation'>,
  document: Pick<DocumentRecord, 'storagePath'>,
) => [
  { key: 'placement-id', label: 'Placement ID', children: placement.id },
  { key: 'ectd-section', label: 'eCTD Section', children: createElement(Tag, null, placement.ctdSection) },
  { key: 'operation', label: 'Operation', children: createElement(Tag, { color: 'blue' }, placement.operation) },
  { key: 'storage-path', label: 'Storage Path', children: renderLeafBreakAllText(document.storagePath) },
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
  { key: 'mime-type', label: 'Mime Type', children: formatLeafPreviewOptionalText(preview.mediaType) },
  { key: 'checksum-type', label: 'Checksum Type', children: 'md5' },
  { key: 'checksum', label: 'Checksum', children: renderLeafBreakAllText('Computed at publish') },
  { key: 'source-file-name', label: 'Source File Name', children: preview.sourceFileName },
  { key: 'resulting-file-name', label: 'Resulting File Name', children: formatLeafPreviewOptionalText(preview.revisedFileName) },
  { key: 'storage-path', label: 'Storage Path', children: renderLeafBreakAllText(preview.storagePath) },
]
