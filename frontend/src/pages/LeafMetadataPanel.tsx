import { Alert, Button, Descriptions, Form, Input, Select, Space } from 'antd'
import type { FormInstance } from 'antd'
import { Trash2 } from 'lucide-react'

import type { DocumentPlacementRecord, DocumentRecord } from '../workspaceTree'
import {
  buildLeafPlacementDescriptionItems,
  buildLeafPreviewDescriptionItems,
} from './leafMetadataDisplay'
import { buildLifecycleTargetLabel } from './lifecycleTargetLabels'
import { buildPublishedHrefPreview } from './publishedHrefPreview'

const placementOperations = ['New', 'Replace', 'Delete', 'Append']

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
  revisedLifecycleTargetPlacementId: unknown
  lifecycleTargetCandidates: DocumentPlacementRecord[]
  documentsById: Record<string, DocumentRecord>
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
  revisedLifecycleTargetPlacementId,
  lifecycleTargetCandidates,
  documentsById,
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
  const selectedLifecycleTargetId = String(revisedLifecycleTargetPlacementId || '') || null
  const selectedLifecycleTarget = lifecycleTargetCandidates.find((candidate) => candidate.id === selectedLifecycleTargetId)
  const selectedLifecycleTargetDocument = selectedLifecycleTarget ? documentsById[selectedLifecycleTarget.documentId] : undefined
  const selectedLifecycleTargetHref = selectedLifecycleTarget && selectedLifecycleTargetDocument
    ? buildPublishedHrefPreview(selectedLifecycleTargetDocument.storagePath, selectedLifecycleTarget.sequenceNumber, selectedLifecycleTargetDocument.fileName)
    : 'Not selected'
  const isLifecycleOperation = ['Replace', 'Delete', 'Append'].includes(leafOperationPreview)

  return (
    <div className="flex flex-col gap-4">
      <Descriptions
        size="small"
        bordered
        column={1}
        className="selection-details-descriptions"
        items={buildLeafPlacementDescriptionItems(placement, document)}
      />

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
        {isLifecycleOperation && (
          <Alert
            type="warning"
            showIcon
            className="mb-3"
            title="Lifecycle operation"
            description={lifecycleTargetCandidates.length === 0
              ? 'No historical leaf targets are available in this CTD section. Validation will report an error until a valid target exists.'
              : 'Select the historical leaf that this lifecycle operation modifies. Validation will report an error if no valid target is selected.'}
          />
        )}
        {isLifecycleOperation && (
          <>
            <Form.Item name="lifecycleTargetPlacementId" label="Lifecycle Target">
              <Select
                allowClear
                placeholder="Select historical leaf target"
                options={lifecycleTargetCandidates.map((candidate) => ({
                  value: candidate.id,
                  label: buildLifecycleTargetLabel(candidate, documentsById),
                }))}
              />
            </Form.Item>
            {lifecycleTargetCandidates.length > 0 && (
              <div className="text-xs text-gray-500 -mt-3 mb-3">
                Available Targets: {lifecycleTargetCandidates
                  .map((candidate) => buildLifecycleTargetLabel(candidate, documentsById))
                  .join('; ')}
              </div>
            )}
          </>
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

      <Descriptions
        title="Leaf Preview"
        size="small"
        bordered
        column={1}
        className="selection-details-descriptions"
        items={buildLeafPreviewDescriptionItems({
          operation: leafOperationPreview,
          title: leafTitlePreview,
          href: leafHrefPreview,
          modifiedFileHref: isLifecycleOperation ? selectedLifecycleTargetHref : null,
          mediaType: document.mediaType,
          sourceFileName: document.fileName,
          revisedFileName,
          storagePath: document.storagePath,
        })}
      />

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
