import { useEffect, useMemo, useState } from 'react'
import { Alert, Button, Col, Form, Modal, Row, Tag, message } from 'antd'
import { ArrowLeft, PlayCircle, Save } from 'lucide-react'

import { splitFileName } from '../ectdFileTypes'
import {
  deletePlacementWithDocument,
  movePlacementToSection,
  PlacementDeletePartialFailureError,
  revisePlacementMetadata,
  uploadDocumentToSection,
} from '../workspaceActions'
import {
  findWorkspaceTreeNode,
  resolveUploadSection,
} from '../workspaceTree'
import { addSectionExpansionKeys, getErrorMessage } from './appShared'
import { PublishProgressCard } from './workspace/PublishProgressCard'
import { getLifecycleTargetCandidates } from './workspace/lifecycleTargetCandidates'
import { PublishModal } from './workspace/PublishModal'
import { useSequencePublishing, type SequencePublishingProviders } from './workspace/useSequencePublishing'
import { useWorkspaceDragDrop } from './workspace/useWorkspaceDragDrop'
import { useWorkspaceData } from './workspace/useWorkspaceData'
import {
  hasValidationLocation,
  resolveValidationLocation,
  type ValidationLocation,
} from './workspace/validationLocationResolver'
import { ValidationSummaryPanel } from './workspace/ValidationSummaryPanel'
import { WorkspaceSelectionDetails } from './workspace/WorkspaceSelectionDetails'
import { WorkspaceTree } from './workspace/WorkspaceTree'

type SequenceWorkspacePageProps = SequencePublishingProviders & {
  appId: string
  seqNumber: string
  onBack: () => void
}

export const SequenceWorkspacePage = ({
  appId,
  seqNumber,
  onBack,
  validateSequenceProvider,
  getPublishReadinessProvider,
  getSequencePublishingMetadataProvider,
  updateSequencePublishingMetadataProvider,
  createAndExecutePublishJobProvider,
}: SequenceWorkspacePageProps) => {
  const [loading, setLoading] = useState(false)
  const {
    placements,
    applicationPlacements,
    documentsById,
    treeData,
    treeLoading,
    treeError,
    placementsError,
    documentsError,
    expandedKeys,
    setExpandedKeys,
    refreshWorkspaceData,
  } = useWorkspaceData({ appId, seqNumber })
  const [selectedTreeKey, setSelectedTreeKey] = useState<string | null>(null)
  const [selectedSectionPath, setSelectedSectionPath] = useState<string | null>(null)
  const [deletingPlacementIds, setDeletingPlacementIds] = useState<Set<string>>(new Set())
  const [movingPlacementIds, setMovingPlacementIds] = useState<Set<string>>(new Set())
  const [savingRevisionPlacementId, setSavingRevisionPlacementId] = useState<string | null>(null)

  const [metadataForm] = Form.useForm()
  const revisedPrefix = Form.useWatch('fileNamePrefix', metadataForm)
  const revisedOperation = Form.useWatch('operation', metadataForm)
  const revisedLifecycleTargetPlacementId = Form.useWatch('lifecycleTargetPlacementId', metadataForm)
  const {
    publishing,
    isPublishModalOpen,
    validationSummary,
    validationIssueCountText,
    validationStatusText,
    publishReadiness,
    publishForm,
    publishMetadataForm,
    polledPublishJob,
    isPublishPolling,
    publishPollingError,
    openPublishModal,
    handlePublishModalCancel,
    triggerPublish,
    stopPublishPolling,
  } = useSequencePublishing({
    appId,
    seqNumber,
    validateSequenceProvider,
    getPublishReadinessProvider,
    getSequencePublishingMetadataProvider,
    updateSequencePublishingMetadataProvider,
    createAndExecutePublishJobProvider,
  })

  const selectedNode = useMemo(
    () => (selectedTreeKey ? findWorkspaceTreeNode(treeData, selectedTreeKey) : undefined),
    [selectedTreeKey, treeData],
  )

  const selectedPlacement = useMemo(() => {
    if (!selectedNode || selectedNode.nodeType !== 'document') {
      return undefined
    }

    return placements.find((placement) => placement.id === selectedNode.placementId)
  }, [placements, selectedNode])

  const selectedDocument = useMemo(() => {
    if (!selectedPlacement) {
      return undefined
    }

    return documentsById[selectedPlacement.documentId]
  }, [documentsById, selectedPlacement])

  const selectedSectionChildrenCount = useMemo(() => {
    if (!selectedNode || selectedNode.nodeType !== 'section') {
      return 0
    }

    return selectedNode.children.filter((child) => child.nodeType === 'document').length
  }, [selectedNode])

  const selectedDocumentNameParts = useMemo(() => {
    if (!selectedDocument) {
      return { prefix: '', extension: '' }
    }

    return splitFileName(selectedDocument.fileName)
  }, [selectedDocument])

  const lifecycleTargetCandidates = useMemo(() => {
    if (!selectedPlacement) {
      return []
    }

    return getLifecycleTargetCandidates(applicationPlacements, selectedPlacement, documentsById)
  }, [applicationPlacements, documentsById, selectedPlacement])

  const locateValidationIssue = (location: ValidationLocation) => {
    const resolvedLocation = resolveValidationLocation({ location, placements, treeData })
    if (!resolvedLocation) {
      message.warning('无法在工作区树中定位该校验问题。')
      return
    }

    setSelectedTreeKey(resolvedLocation.key)
    setSelectedSectionPath(resolvedLocation.sectionPath)
    setExpandedKeys((current) => addSectionExpansionKeys(current, resolvedLocation.sectionPath))
  }

  useEffect(() => {
    if (!selectedNode || selectedNode.nodeType !== 'document' || !selectedPlacement || !selectedDocument) {
      metadataForm.resetFields()
      return
    }

    metadataForm.setFieldsValue({
      title: selectedPlacement.title || '',
      operation: selectedPlacement.operation || 'New',
      fileNamePrefix: selectedDocumentNameParts.prefix,
      lifecycleTargetPlacementId: selectedPlacement.lifecycleTargetPlacementId || null,
    })
  }, [metadataForm, selectedDocumentNameParts.prefix, selectedNode, selectedPlacement, selectedDocument])

  useEffect(() => {
    if (!selectedTreeKey) {
      return
    }

    const resolvedSelectedNode = findWorkspaceTreeNode(treeData, selectedTreeKey)
    if (!resolvedSelectedNode) {
      setSelectedTreeKey(null)
      return
    }

    if (selectedSectionPath !== resolvedSelectedNode.sectionPath) {
      setSelectedSectionPath(resolvedSelectedNode.sectionPath)
    }
  }, [selectedSectionPath, selectedTreeKey, treeData])

  const handleMovePlacement = async (placementId: string, fromSection: string, toSection: string) => {
    setMovingPlacementIds((current) => new Set(current).add(placementId))
    setLoading(true)
    try {
      const moved = await movePlacementToSection({ placementId, fromSection, toSection })

      if (!moved) {
        message.info('该文档已映射到此章节。')
        return
      }

      setExpandedKeys((current) => addSectionExpansionKeys(current, toSection))
      setSelectedTreeKey(toSection)
      setSelectedSectionPath(toSection)
      await refreshWorkspaceData()
      message.success('文档已移动到目标章节。')
    } catch (error) {
      message.error(`移动文档失败：${getErrorMessage(error)}`)
    } finally {
      setMovingPlacementIds((current) => {
        const next = new Set(current)
        next.delete(placementId)
        return next
      })
      setLoading(false)
    }
  }

  const handleDeletePlacementWithFile = async (placementId: string, documentId: string) => {
    setDeletingPlacementIds((current) => new Set(current).add(placementId))
    setLoading(true)
    try {
      await deletePlacementWithDocument({ placementId, documentId })
      await refreshWorkspaceData()
      message.success('文档映射与物理文件已删除。')
    } catch (error) {
      if (error instanceof PlacementDeletePartialFailureError) {
        message.error(`映射已删除，但文档/文件删除失败：${error.message}`)
      } else {
        message.error(`删除已映射文档失败：${getErrorMessage(error)}`)
      }
    } finally {
      setDeletingPlacementIds((current) => {
        const next = new Set(current)
        next.delete(placementId)
        return next
      })
      setLoading(false)
    }
  }

  const confirmDeletePlacement = (placementId: string, documentId: string) => {
    Modal.confirm({
      title: '删除已映射文档',
      content: '此操作将移除映射并从工作区删除物理文件。是否继续？',
      okText: '删除',
      okButtonProps: { danger: true },
      cancelText: '取消',
      onOk: async () => {
        await handleDeletePlacementWithFile(placementId, documentId)
      },
    })
  }

  const handleSaveRevision = async () => {
    if (!selectedPlacement || !selectedDocument) {
      return
    }

    const values = await metadataForm.validateFields()
    const normalizedPrefix = String(values.fileNamePrefix || '').trim()
    const operation = String(values.operation || selectedPlacement.operation || 'New')

    setSavingRevisionPlacementId(selectedPlacement.id)
    setLoading(true)
    try {
      await revisePlacementMetadata({
        placementId: selectedPlacement.id,
        title: String(values.title || '').trim() || undefined,
        operation,
        fileNamePrefix: normalizedPrefix,
        lifecycleTargetPlacementId: operation === 'New'
          ? null
          : values.lifecycleTargetPlacementId || null,
      })
      await refreshWorkspaceData()
      message.success('文件元数据修订已保存。')
    } catch (error) {
      message.error(`保存元数据修订失败：${getErrorMessage(error)}`)
    } finally {
      setSavingRevisionPlacementId(null)
      setLoading(false)
    }
  }

  const handleDirectDrop = async (file: File, targetNodeKey: string) => {
    setLoading(true)
    message.loading({ content: `正在处理 ${file.name}...`, key: 'uploading' })

    try {
      const targetSection = resolveUploadSection(targetNodeKey, selectedSectionPath)
      setExpandedKeys((current) => addSectionExpansionKeys(current, targetSection))
      setSelectedTreeKey(targetSection)
      setSelectedSectionPath(targetSection)

      await uploadDocumentToSection({
        applicationId: appId,
        sequenceNumber: seqNumber,
        file,
        ctdSection: targetSection,
      })

      message.success({ content: `${file.name} 已映射到 ${targetSection} 并保存！`, key: 'uploading' })
      await refreshWorkspaceData()
    } catch (err) {
      message.error({ content: `失败：${getErrorMessage(err)}`, key: 'uploading' })
    } finally {
      setLoading(false)
    }
  }

  const dragDrop = useWorkspaceDragDrop({
    placements,
    movePlacement: handleMovePlacement,
    uploadFile: handleDirectDrop,
  })

  return (
    <div className="flex flex-col gap-4">
      <div className="flex justify-between items-center bg-white p-4 rounded shadow-sm border border-gray-200">
        <div className="flex items-center gap-4">
          <Button icon={<ArrowLeft size={16} />} onClick={onBack}>返回详情</Button>
          <div>
            <h2 className="m-0 text-xl font-bold">序列工作区：<Tag color="blue">{seqNumber}</Tag></h2>
            <p className="m-0 text-gray-500 text-sm flex items-center gap-1">
              <Save size={14} /> 文档拖入后将自动保存。
            </p>
          </div>
        </div>
        <Button type="primary" icon={<PlayCircle size={16} className="mr-1" />} loading={publishing} onClick={openPublishModal}>
          发布序列
        </Button>
      </div>

      <PublishModal
        open={isPublishModalOpen}
        publishing={publishing}
        validationSummary={validationSummary}
        publishReadiness={publishReadiness}
        publishForm={publishForm}
        publishMetadataForm={publishMetadataForm}
        onOk={triggerPublish}
        onCancel={handlePublishModalCancel}
      />

      <PublishProgressCard
        job={polledPublishJob}
        isPolling={isPublishPolling}
        error={publishPollingError}
        onDismiss={stopPublishPolling}
      />

      {validationSummary && (
        <ValidationSummaryPanel
          summary={validationSummary}
          statusText={validationStatusText}
          issueCountText={validationIssueCountText}
          hasValidationLocation={hasValidationLocation}
          locateValidationIssue={locateValidationIssue}
        />
      )}

      {placementsError && <Alert type="error" showIcon title="加载工作区映射失败" description={placementsError} />}
      {documentsError && <Alert type="error" showIcon title="加载工作区文档失败" description={documentsError} />}

      <Row gutter={16}>
        <Col span={12}>
          <WorkspaceTree
            treeData={treeData}
            expandedKeys={expandedKeys}
            selectedTreeKey={selectedTreeKey}
            loading={loading}
            treeLoading={treeLoading}
            treeError={treeError}
            setExpandedKeys={setExpandedKeys}
            onSelectNode={(node) => {
              setSelectedTreeKey(node.key)
              setSelectedSectionPath(node.sectionPath)
            }}
            dragDrop={dragDrop}
          />
        </Col>

        <Col span={12}>
          <WorkspaceSelectionDetails
            selectedNode={selectedNode}
            selectedPlacement={selectedPlacement}
            selectedDocument={selectedDocument}
            selectedSectionChildrenCount={selectedSectionChildrenCount}
            metadataForm={metadataForm}
            sequenceNumber={seqNumber}
            documentNameParts={selectedDocumentNameParts}
            revisedPrefix={revisedPrefix}
            revisedOperation={revisedOperation}
            revisedLifecycleTargetPlacementId={revisedLifecycleTargetPlacementId}
            lifecycleTargetCandidates={lifecycleTargetCandidates}
            documentsById={documentsById}
            loading={loading}
            isSaving={savingRevisionPlacementId === selectedPlacement?.id}
            isDeleting={selectedPlacement ? deletingPlacementIds.has(selectedPlacement.id) : false}
            isMoving={selectedPlacement ? movingPlacementIds.has(selectedPlacement.id) : false}
            onSave={handleSaveRevision}
            onDelete={() => {
              if (selectedPlacement) {
                confirmDeletePlacement(selectedPlacement.id, selectedPlacement.documentId)
              }
            }}
            dropFiles={dragDrop.dropFiles}
          />
        </Col>
      </Row>
    </div>
  )
}
