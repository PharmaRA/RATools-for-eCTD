import { useRef } from 'react'
import { Alert, Button, Card, Descriptions, type FormInstance } from 'antd'
import { FolderOpen } from 'lucide-react'

import type {
  DocumentPlacementRecord,
  DocumentRecord,
  WorkspaceTreeNode,
} from '../../workspaceTree'
import { LeafMetadataPanel } from '../LeafMetadataPanel'
import { buildSectionSelectionDescriptionItems } from './selectionDetailsDisplay'
import type { UseWorkspaceDragDropResult } from './useWorkspaceDragDrop'

type DocumentNameParts = {
  prefix: string
  extension: string
}

type WorkspaceSelectionDetailsProps = {
  selectedNode?: WorkspaceTreeNode
  selectedPlacement?: DocumentPlacementRecord
  selectedDocument?: DocumentRecord
  selectedSectionChildrenCount: number
  metadataForm: FormInstance
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
  dropFiles: UseWorkspaceDragDropResult['dropFiles']
}

export const WorkspaceSelectionDetails = ({
  selectedNode,
  selectedPlacement,
  selectedDocument,
  selectedSectionChildrenCount,
  metadataForm,
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
  dropFiles,
}: WorkspaceSelectionDetailsProps) => {
  const fileInputRef = useRef<HTMLInputElement | null>(null)

  return (
    <Card title="选中项详情" size="small" className="shadow-sm border-gray-200 h-[600px] overflow-y-auto">
      {!selectedNode && (
        <div className="text-center text-gray-400 mt-20">
          <FolderOpen size={48} className="mx-auto mb-4 opacity-50" />
          <p>请从左侧树中选择章节或已映射文件。</p>
        </div>
      )}

      {selectedNode?.nodeType === 'section' && (
        <div className="flex flex-col gap-4">
          <Descriptions
            size="small"
            bordered
            column={1}
            className="selection-details-descriptions"
            items={buildSectionSelectionDescriptionItems(selectedNode, selectedSectionChildrenCount)}
          />

          <Alert
            type="info"
            showIcon
            title="叶节点元数据指南"
            description={(
              <div className="flex flex-col gap-1 text-sm">
                <div>已映射叶节点：<b>{selectedSectionChildrenCount}</b></div>
                <div>将文件拖放到叶级章节，然后选择已映射的叶节点以编辑其标题、操作类型与文件命名元数据。</div>
                {!selectedNode.canDrop && <div>该章节包含子章节，文件应映射到叶级子章节。</div>}
              </div>
            )}
          />

          {selectedNode.canDrop && (
            <div>
              <input
                ref={fileInputRef}
                type="file"
                multiple
                className="hidden"
                data-testid="section-file-input"
                aria-label={`上传文件到 ${selectedNode.sectionPath || selectedNode.key}`}
                onChange={(event) => {
                  const files = event.target.files
                  if (files && files.length > 0) {
                    void dropFiles(files, selectedNode.key)
                  }
                  event.target.value = ''
                }}
              />
              <Button
                icon={<FolderOpen size={14} className="mr-1" />}
                onClick={() => fileInputRef.current?.click()}
              >
                选择文件上传到此章节
              </Button>
            </div>
          )}

          <p className="text-xs text-gray-500">提示：将文件拖放到叶级章节；在章节之间拖动文件节点可移动它们。</p>
        </div>
      )}

      {selectedNode?.nodeType === 'document' && selectedPlacement && selectedDocument && (
        <LeafMetadataPanel
          form={metadataForm}
          placement={selectedPlacement}
          document={selectedDocument}
          sequenceNumber={sequenceNumber}
          documentNameParts={documentNameParts}
          revisedPrefix={revisedPrefix}
          revisedOperation={revisedOperation}
          revisedLifecycleTargetPlacementId={revisedLifecycleTargetPlacementId}
          lifecycleTargetCandidates={lifecycleTargetCandidates}
          documentsById={documentsById}
          loading={loading}
          isSaving={isSaving}
          isDeleting={isDeleting}
          isMoving={isMoving}
          onSave={onSave}
          onDelete={onDelete}
        />
      )}
    </Card>
  )
}
