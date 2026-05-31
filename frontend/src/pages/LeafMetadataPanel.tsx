import { Alert, Button, Descriptions, Form, Input, Select, Space, Tag } from 'antd'
import type { FormInstance } from 'antd'
import { Trash2 } from 'lucide-react'

import type { DocumentPlacementRecord, DocumentRecord } from '../workspaceTree'

const placementOperations = ['New', 'Replace', 'Delete', 'Append']

const buildPublishedHrefPreview = (storagePath: string | undefined, sequenceNumber: string, fallbackFileName: string | undefined) => {
  const fileName = fallbackFileName || '-'
  if (!storagePath) {
    return fileName
  }

  const segments = storagePath.split(/[\\/]+/).filter(Boolean)
  const sequenceIndex = segments.map((segment) => segment.toLowerCase()).lastIndexOf(sequenceNumber.toLowerCase())
  if (sequenceIndex >= 0 && sequenceIndex < segments.length - 1) {
    return [...segments.slice(sequenceIndex + 1, -1), fileName].join('/')
  }

  return fileName || segments.at(-1) || '-'
}

type DocumentNameParts = {
  prefix: string
  extension: string
}

type LeafMetadataPanelProps = {
  form: FormInstance
  placement: DocumentPlacementRecord
  document: DocumentRecord
  sequenceNumber: string
  documentNameParts: DocumentNameParts
  revisedPrefix: unknown
  revisedOperation: unknown
  loading: boolean
  isSaving: boolean
  isDeleting: boolean
  isMoving: boolean
  onSave: () => void
  onDelete: () => void
}

export const LeafMetadataPanel = ({
  form,
  placement,
  document,
  sequenceNumber,
  documentNameParts,
  revisedPrefix,
  revisedOperation,
  loading,
  isSaving,
  isDeleting,
  isMoving,
  onSave,
  onDelete,
}: LeafMetadataPanelProps) => {
  const revisedFileName = `${String(revisedPrefix || '').trim()}${documentNameParts.extension}`
  const leafHrefPreview = buildPublishedHrefPreview(document.storagePath, sequenceNumber, revisedFileName || document.fileName)
  const leafTitlePreview = String(form.getFieldValue('title') || '').trim() || placement.title || document.fileName || '-'
  const leafOperationPreview = String(revisedOperation || placement.operation || 'New')

  return (
    <div className="flex flex-col gap-4">
      <Descriptions size="small" bordered column={1} className="selection-details-descriptions">
        <Descriptions.Item label="Placement ID">{placement.id}</Descriptions.Item>
        <Descriptions.Item label="eCTD Section"><Tag>{placement.ctdSection}</Tag></Descriptions.Item>
        <Descriptions.Item label="Operation"><Tag color="blue">{placement.operation}</Tag></Descriptions.Item>
        <Descriptions.Item label="Storage Path"><span className="text-xs break-all">{document.storagePath}</span></Descriptions.Item>
      </Descriptions>

      <div>
        <h3 className="text-base font-semibold m-0">Leaf Metadata</h3>
        <p className="text-xs text-gray-500 m-0">Edit the metadata that will be emitted on this document's backbone leaf.</p>
      </div>

      <Form form={form} layout="vertical" requiredMark={false}>
        <Form.Item name="title" label="Leaf Title">
          <Input maxLength={255} placeholder="Optional title" />
        </Form.Item>
        <Form.Item name="operation" label="Operation" rules={[{ required: true, message: 'Operation is required.' }]}>
          <Select
            options={placementOperations.map((operation) => ({ value: operation, label: operation }))}
          />
        </Form.Item>
        {['Replace', 'Delete', 'Append'].includes(leafOperationPreview) && (
          <Alert
            type="warning"
            showIcon
            className="mb-3"
            title="Lifecycle operation"
            description="Replace, Delete, and Append require a matching historical lifecycle target. Validation will report an error until a valid target exists."
          />
        )}
        <Form.Item
          name="fileNamePrefix"
          label="File Prefix"
          rules={[
            { required: true, message: 'File prefix is required.' },
            {
              validator: (_, value) => (
                String(value || '').trim().length > 0
                  ? Promise.resolve()
                  : Promise.reject(new Error('File prefix cannot be empty.'))
              ),
            },
          ]}
        >
          <Input maxLength={255} placeholder="example-file-name" />
        </Form.Item>
        <Form.Item label="Extension">
          <Input value={documentNameParts.extension || '(no extension)'} readOnly />
        </Form.Item>
        <Form.Item label="Resulting File Name">
          <Input
            value={revisedFileName}
            readOnly
          />
        </Form.Item>
      </Form>

      <Descriptions title="Leaf Preview" size="small" bordered column={1} className="selection-details-descriptions">
        <Descriptions.Item label="operation">{leafOperationPreview}</Descriptions.Item>
        <Descriptions.Item label="title">{leafTitlePreview}</Descriptions.Item>
        <Descriptions.Item label="xlink:href"><span className="text-xs break-all">{leafHrefPreview}</span></Descriptions.Item>
        <Descriptions.Item label="Mime Type">{document.mediaType || '-'}</Descriptions.Item>
        <Descriptions.Item label="Checksum Type">md5</Descriptions.Item>
        <Descriptions.Item label="Checksum"><span className="text-xs break-all">Computed at publish</span></Descriptions.Item>
        <Descriptions.Item label="Source File Name">{document.fileName}</Descriptions.Item>
        <Descriptions.Item label="Resulting File Name">{revisedFileName || '-'}</Descriptions.Item>
        <Descriptions.Item label="Storage Path"><span className="text-xs break-all">{document.storagePath}</span></Descriptions.Item>
      </Descriptions>

      <Space>
        <Button
          type="primary"
          loading={isSaving}
          disabled={loading || isDeleting || isMoving}
          onClick={onSave}
        >
          Save Leaf Metadata
        </Button>
        <Button
          danger
          icon={<Trash2 size={14} />}
          loading={isDeleting}
          disabled={loading || isDeleting || isMoving}
          onClick={onDelete}
        >
          Delete
        </Button>
      </Space>

      <p className="text-xs text-gray-500">Delete removes mapping and physical file. Editing leaf metadata can change the placement operation, backbone title, and file prefix; extension remains unchanged.</p>
    </div>
  )
}
