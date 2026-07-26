import { Alert, Button, Descriptions, Form, Input, Select, Space } from 'antd'
import type { FormInstance } from 'antd'
import { Trash2 } from 'lucide-react'

import type { DocumentPlacementRecord, DocumentRecord } from '../workspaceTree'
import {
  buildLeafPlacementDescriptionItems,
  buildLeafPlacementOperationOptions,
  buildLeafPreviewDescriptionItems,
} from './leafMetadataDisplay'
import { buildLifecycleTargetListText, buildLifecycleTargetOptions } from './lifecycleTargetLabels'
import { buildPublishedHrefPreview } from './publishedHrefPreview'

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
        <h3 className="text-base font-semibold m-0">叶节点元数据</h3>
        <p className="text-xs text-gray-500 m-0">Edit the metadata that will be emitted on this document's backbone leaf.</p>
      </div>

      <Form form={form} layout="vertical" requiredMark={false}>
        <Form.Item name="title" label="叶节点标题">
          <Input maxLength={255} placeholder="可选标题" />
        </Form.Item>
        <Form.Item name="operation" label="操作类型" rules={[{ required: true, message: '操作类型为必填项。' }]}>
          <Select
            options={buildLeafPlacementOperationOptions()}
          />
        </Form.Item>
        {isLifecycleOperation && (
          <Alert
            type="warning"
            showIcon
            className="mb-3"
            title="生命周期操作"
            description={lifecycleTargetCandidates.length === 0
              ? 'No historical leaf targets are available in this CTD section. Validation will report an error until a valid target exists.'
              : 'Select the historical leaf that this lifecycle operation modifies. Validation will report an error if no valid target is selected.'}
          />
        )}
        {isLifecycleOperation && (
          <>
            <Form.Item name="lifecycleTargetPlacementId" label="生命周期目标">
              <Select
                allowClear
                placeholder="选择历史叶节点目标"
                options={buildLifecycleTargetOptions(lifecycleTargetCandidates, documentsById)}
              />
            </Form.Item>
            {lifecycleTargetCandidates.length > 0 && (
              <div className="text-xs text-gray-500 -mt-3 mb-3">
                可选目标： {buildLifecycleTargetListText(lifecycleTargetCandidates, documentsById)}
              </div>
            )}
          </>
        )}
        <Form.Item
          name="fileNamePrefix"
          label="文件名前缀"
          rules={[
            { required: true, message: '文件名前缀为必填项。' },
            {
              validator: (_, value) => (
                String(value || '').trim().length > 0
                  ? Promise.resolve()
                  : Promise.reject(new Error('文件名前缀不能为空。'))
              ),
            },
          ]}
        >
          <Input maxLength={255} placeholder="example-file-name" />
        </Form.Item>
        <Form.Item label="扩展名">
          <Input value={documentNameParts.extension || '（无扩展名）'} readOnly />
        </Form.Item>
        <Form.Item label="生成文件名">
          <Input
            value={revisedFileName}
            readOnly
          />
        </Form.Item>
      </Form>

      <Descriptions
        title="叶节点预览"
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
          保存叶节点元数据
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

      <p className="text-xs text-gray-500">删除会移除映射并删除物理文件。编辑叶节点元数据可修改映射的操作类型、骨架标题与文件名前缀；扩展名保持不变。</p>
    </div>
  )
}
