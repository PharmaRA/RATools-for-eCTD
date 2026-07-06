import { useEffect, useMemo, useState } from 'react'
import { Alert, Button, Card, Col, Descriptions, Form, Modal, Row, Tag, message } from 'antd'
import { ArrowLeft, FolderOpen, PlayCircle, Save } from 'lucide-react'

import { createAndExecutePublishJob } from '../publishActions'
import {
  getSequencePublishingMetadata,
  updateSequencePublishingMetadata,
} from '../sequencePublishingMetadataActions'
import {
  getPublishReadiness,
  type PublishReadinessReport,
  validateSequence,
  type ValidationReport,
} from '../validationActions'
import {
  buildPrePublishChecklistSummary,
  buildPrePublishChecklistDisplay,
  getPublishReadinessValidationIssues,
} from '../prePublishChecklist'
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
import { LeafMetadataPanel } from './LeafMetadataPanel'
import { getLifecycleTargetCandidates } from './workspace/lifecycleTargetCandidates'
import { buildSequencePublishingMetadataUpdateRequest } from './workspace/publishingMetadataFormValues'
import { PublishModal, type MetadataFormValues } from './workspace/PublishModal'
import { buildSectionSelectionDescriptionItems } from './workspace/selectionDetailsDisplay'
import { useWorkspaceDragDrop } from './workspace/useWorkspaceDragDrop'
import { useWorkspaceData } from './workspace/useWorkspaceData'
import {
  hasValidationLocation,
  resolveValidationLocation,
  type ValidationLocation,
} from './workspace/validationLocationResolver'
import { ValidationSummaryPanel } from './workspace/ValidationSummaryPanel'
import { WorkspaceTree } from './workspace/WorkspaceTree'

type SequenceWorkspacePageProps = {
  appId: string
  seqNumber: string
  onBack: () => void
  validateSequenceProvider?: typeof validateSequence
  getPublishReadinessProvider?: typeof getPublishReadiness
  getSequencePublishingMetadataProvider?: typeof getSequencePublishingMetadata
  updateSequencePublishingMetadataProvider?: typeof updateSequencePublishingMetadata
  createAndExecutePublishJobProvider?: typeof createAndExecutePublishJob
}

export const SequenceWorkspacePage = ({
  appId,
  seqNumber,
  onBack,
  validateSequenceProvider = validateSequence,
  getPublishReadinessProvider = getPublishReadiness,
  getSequencePublishingMetadataProvider = getSequencePublishingMetadata,
  updateSequencePublishingMetadataProvider = updateSequencePublishingMetadata,
  createAndExecutePublishJobProvider = createAndExecutePublishJob,
}: SequenceWorkspacePageProps) => {
  const [loading, setLoading] = useState(false)
  const [publishing, setPublishing] = useState(false)
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
  const [isPublishModalOpen, setIsPublishModalOpen] = useState(false)
  const [validationResult, setValidationResult] = useState<ValidationReport | null>(null)

  const [metadataForm] = Form.useForm()
  const [publishForm] = Form.useForm()
  const [publishMetadataForm] = Form.useForm<MetadataFormValues>()
  const revisedPrefix = Form.useWatch('fileNamePrefix', metadataForm)
  const revisedOperation = Form.useWatch('operation', metadataForm)
  const revisedLifecycleTargetPlacementId = Form.useWatch('lifecycleTargetPlacementId', metadataForm)
  const [publishReadiness, setPublishReadiness] = useState<PublishReadinessReport | null>(null)

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
      message.warning('Could not locate this validation issue in the workspace tree.')
      return
    }

    setSelectedTreeKey(resolvedLocation.key)
    setSelectedSectionPath(resolvedLocation.sectionPath)
    setExpandedKeys((current) => addSectionExpansionKeys(current, resolvedLocation.sectionPath))
  }

  const validationSummary = useMemo(() => {
    if (!validationResult) {
      return null
    }

    return buildPrePublishChecklistSummary(validationResult)
  }, [validationResult])

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
        message.info('Document is already mapped to this section.')
        return
      }

      setExpandedKeys((current) => addSectionExpansionKeys(current, toSection))
      setSelectedTreeKey(toSection)
      setSelectedSectionPath(toSection)
      await refreshWorkspaceData()
      message.success('Document moved to target section.')
    } catch (error) {
      message.error(`Failed to move document: ${getErrorMessage(error)}`)
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
      message.success('Document mapping and physical file deleted.')
    } catch (error) {
      if (error instanceof PlacementDeletePartialFailureError) {
        message.error(`Mapping deleted, but document/file delete failed: ${error.message}`)
      } else {
        message.error(`Failed to delete mapped document: ${getErrorMessage(error)}`)
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
      title: 'Delete mapped document',
      content: 'This will remove mapping and delete the physical file from workspace. Continue?',
      okText: 'Delete',
      okButtonProps: { danger: true },
      cancelText: 'Cancel',
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
      message.success('File metadata revision saved.')
    } catch (error) {
      message.error(`Failed to save metadata revision: ${getErrorMessage(error)}`)
    } finally {
      setSavingRevisionPlacementId(null)
      setLoading(false)
    }
  }

  const handleDirectDrop = async (file: File, targetNodeKey: string) => {
    setLoading(true)
    message.loading({ content: `Processing ${file.name}...`, key: 'uploading' })

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

      message.success({ content: `${file.name} mapped to ${targetSection} and saved!`, key: 'uploading' })
      await refreshWorkspaceData()
    } catch (err) {
      message.error({ content: `Failed: ${getErrorMessage(err)}`, key: 'uploading' })
    } finally {
      setLoading(false)
    }
  }

  const dragDrop = useWorkspaceDragDrop({
    placements,
    movePlacement: handleMovePlacement,
    uploadFile: handleDirectDrop,
  })

  const openPublishModal = async () => {
    setPublishing(true)
    setValidationResult(null)
    setPublishReadiness(null)
    setIsPublishModalOpen(false)
    publishForm.resetFields()
    publishMetadataForm.resetFields()
    try {
      const sequenceNumber = String(seqNumber).trim()
      const validationResult = await validateSequenceProvider({
        applicationId: appId,
        sequenceNumber,
      })

      const checklistSummary = buildPrePublishChecklistSummary(validationResult)
      setValidationResult(validationResult)
      if (!checklistSummary.canProceed) {
        return
      }

      const [metadata, readiness] = await Promise.all([
        getSequencePublishingMetadataProvider({
          applicationId: appId,
          sequenceNumber,
        }),
        getPublishReadinessProvider({
          applicationId: appId,
          sequenceNumber,
        }),
      ])

      publishMetadataForm.setFieldsValue({
        applicationType: metadata.applicationType || '',
        submissionType: metadata.submissionType,
        submissionSubtype: metadata.submissionSubtype || '',
        sequenceDescription: metadata.sequenceDescription,
        applicantName: metadata.applicantName,
        formType: metadata.formType || '',
        applicantContactName: metadata.applicantContactName || '',
        applicantContactType: metadata.applicantContactType || '',
        telephone: metadata.telephone || '',
        telephoneNumberType: metadata.telephoneNumberType || '',
        email: metadata.email || '',
      })
      setPublishReadiness(readiness)

      if (!readiness.isReady && readiness.missingMetadataFields.length === 0) {
        setValidationResult({
          ...validationResult,
          isValid: false,
          issues: [
            ...validationResult.issues,
            ...getPublishReadinessValidationIssues(readiness),
          ],
        })
        setPublishReadiness(null)
        return
      }

      publishForm.setFieldsValue({
        outputDirectoryPath: '',
      })
      setIsPublishModalOpen(true)
    } catch (err) {
      const errorMessage = getErrorMessage(err)
      setValidationResult({
        applicationId: appId,
        sequenceNumber: String(seqNumber).trim(),
        validationProfile: 'Validation API',
        isValid: false,
        issues: [{ severity: 'Error', code: 'API_ERROR', message: errorMessage }],
        sectionMatches: [],
        lifecycleMatches: [],
      })
    } finally {
      setPublishing(false)
    }
  }

  const handlePublishModalCancel = () => {
    setIsPublishModalOpen(false)
    publishForm.resetFields()
    publishMetadataForm.resetFields()
    setPublishReadiness(null)
  }

  const triggerPublish = async () => {
    setPublishing(true)
    try {
      const sequenceNumber = String(seqNumber).trim()
      const publishValues = await publishForm.validateFields()
      let readinessToUse = publishReadiness

      if (publishReadiness && !publishReadiness.isReady && publishReadiness.missingMetadataFields.length > 0) {
        const metadataValues = await publishMetadataForm.validateFields()
        await updateSequencePublishingMetadataProvider(buildSequencePublishingMetadataUpdateRequest(
          appId,
          sequenceNumber,
          metadataValues,
        ))
        readinessToUse = await getPublishReadinessProvider({
          applicationId: appId,
          sequenceNumber,
        })
        setPublishReadiness(readinessToUse)

        if (!readinessToUse.isReady) {
          message.error('Publish readiness is still blocked. Resolve the remaining findings before publishing.')
          return
        }
      }

      await createAndExecutePublishJobProvider({
        applicationId: appId,
        sequenceNumber,
        outputDirectoryPath: String(publishValues.outputDirectoryPath || '').trim(),
      })

      message.success('Publish job initiated successfully! Check History tab for results.')
      setIsPublishModalOpen(false)
      publishForm.resetFields()
      publishMetadataForm.resetFields()
      setPublishReadiness(null)
      onBack()
    } catch (err) {
      message.error('Publish failed: ' + getErrorMessage(err))
    } finally {
      setPublishing(false)
    }
  }

  const validationDisplay = validationSummary ? buildPrePublishChecklistDisplay(validationSummary) : null
  const validationIssueCountText = validationDisplay?.issueCountText || ''
  const validationStatusText = validationDisplay?.statusText || ''
  return (
    <div className="flex flex-col gap-4">
      <div className="flex justify-between items-center bg-white p-4 rounded shadow-sm border border-gray-200">
        <div className="flex items-center gap-4">
          <Button icon={<ArrowLeft size={16} />} onClick={onBack}>Back to Details</Button>
          <div>
            <h2 className="m-0 text-xl font-bold">Sequence Workspace: <Tag color="blue">{seqNumber}</Tag></h2>
            <p className="m-0 text-gray-500 text-sm flex items-center gap-1">
              <Save size={14} /> Changes are saved automatically upon document drop.
            </p>
          </div>
        </div>
        <Button type="primary" icon={<PlayCircle size={16} className="mr-1" />} loading={publishing} onClick={openPublishModal}>
          Publish Sequence
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

      {validationSummary && (
        <ValidationSummaryPanel
          summary={validationSummary}
          statusText={validationStatusText}
          issueCountText={validationIssueCountText}
          hasValidationLocation={hasValidationLocation}
          locateValidationIssue={locateValidationIssue}
        />
      )}

      {placementsError && <Alert type="error" showIcon title="Failed to load workspace placements" description={placementsError} />}
      {documentsError && <Alert type="error" showIcon title="Failed to load workspace documents" description={documentsError} />}

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
          <Card title="Selection Details" size="small" className="shadow-sm border-gray-200 h-[600px] overflow-y-auto">
            {!selectedNode && (
              <div className="text-center text-gray-400 mt-20">
                <FolderOpen size={48} className="mx-auto mb-4 opacity-50" />
                <p>Select a section or mapped file from the left tree.</p>
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
                  title="Leaf Metadata Guide"
                  description={(
                    <div className="flex flex-col gap-1 text-sm">
                      <div>Mapped Leaves: <b>{selectedSectionChildrenCount}</b></div>
                      <div>Drop files on leaf sections, then select a mapped leaf to edit its title, operation, and file naming metadata.</div>
                      {!selectedNode.canDrop && <div>This section has child sections, so files should be mapped to a leaf child section.</div>}
                    </div>
                  )}
                />

                <p className="text-xs text-gray-500">Tip: Drop files on leaf sections. Drag file nodes between sections to move them.</p>
              </div>
            )}

            {selectedNode?.nodeType === 'document' && selectedPlacement && selectedDocument && (
              <LeafMetadataPanel
                form={metadataForm}
                placement={selectedPlacement}
                document={selectedDocument}
                sequenceNumber={seqNumber}
                documentNameParts={selectedDocumentNameParts}
                revisedPrefix={revisedPrefix}
                revisedOperation={revisedOperation}
                revisedLifecycleTargetPlacementId={revisedLifecycleTargetPlacementId}
                lifecycleTargetCandidates={lifecycleTargetCandidates}
                documentsById={documentsById}
                loading={loading}
                isSaving={savingRevisionPlacementId === selectedPlacement.id}
                isDeleting={deletingPlacementIds.has(selectedPlacement.id)}
                isMoving={movingPlacementIds.has(selectedPlacement.id)}
                onSave={handleSaveRevision}
                onDelete={() => confirmDeletePlacement(selectedPlacement.id, selectedPlacement.documentId)}
              />
            )}
          </Card>
        </Col>
      </Row>
    </div>
  )
}
