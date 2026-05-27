import { useEffect, useMemo, useState } from 'react'
import { Alert, Button, Card, Col, Descriptions, Form, Input, Modal, Row, Space, Spin, Tag, Tree, message } from 'antd'
import { ArrowLeft, CheckCircle, FileText, FolderOpen, PlayCircle, Save, Trash2 } from 'lucide-react'

import { apiFetch } from '../apiClient'
import { PathPicker } from '../PathPicker'
import { createAndExecutePublishJob } from '../publishActions'
import { validateSequence, type ValidationReport } from '../validationActions'
import { ectdAllowedExtensionsHint, isAllowedEctdFileName, splitFileName } from '../ectdFileTypes'
import {
  deletePlacementWithDocument,
  movePlacementToSection,
  PlacementDeletePartialFailureError,
  revisePlacementMetadata,
  serializePlacementDragPayload,
  tryParsePlacementDragPayload,
  WORKSPACE_PLACEMENT_DRAG_MIME,
} from '../workspaceActions'
import {
  attachDocumentNodes,
  findWorkspaceTreeNode,
  mapSectionTreeData,
  resolveUploadSection,
  type DocumentPlacementRecord,
  type DocumentRecord,
  type EctdStructureNode,
  type WorkspaceTreeNode,
} from '../workspaceTree'
import { type EctdStructureResponse, getSectionAncestorKeys } from './appShared'

type SequenceWorkspacePageProps = {
  appId: string
  seqNumber: string
  onBack: () => void
  validateSequenceProvider?: typeof validateSequence
  createAndExecutePublishJobProvider?: typeof createAndExecutePublishJob
}

export const SequenceWorkspacePage = ({
  appId,
  seqNumber,
  onBack,
  validateSequenceProvider = validateSequence,
  createAndExecutePublishJobProvider = createAndExecutePublishJob,
}: SequenceWorkspacePageProps) => {
  const [placements, setPlacements] = useState<DocumentPlacementRecord[]>([])
  const [documentsById, setDocumentsById] = useState<Record<string, DocumentRecord>>({})
  const [loading, setLoading] = useState(false)
  const [publishing, setPublishing] = useState(false)
  const [dragOverNode, setDragOverNode] = useState<string | null>(null)
  const [draggingPlacementId, setDraggingPlacementId] = useState<string | null>(null)
  const [treeLoading, setTreeLoading] = useState(false)
  const [treeError, setTreeError] = useState<string | null>(null)
  const [ectdRoots, setEctdRoots] = useState<EctdStructureNode[]>([])
  const [expandedKeys, setExpandedKeys] = useState<string[]>([])
  const [selectedTreeKey, setSelectedTreeKey] = useState<string | null>(null)
  const [selectedSectionPath, setSelectedSectionPath] = useState<string | null>(null)
  const [deletingPlacementIds, setDeletingPlacementIds] = useState<Set<string>>(new Set())
  const [movingPlacementIds, setMovingPlacementIds] = useState<Set<string>>(new Set())
  const [savingRevisionPlacementId, setSavingRevisionPlacementId] = useState<string | null>(null)
  const [isPublishModalOpen, setIsPublishModalOpen] = useState(false)
  const [validationResult, setValidationResult] = useState<ValidationReport | null>(null)

  const treeData = useMemo(() => {
    return attachDocumentNodes(mapSectionTreeData(ectdRoots), placements, documentsById)
  }, [documentsById, ectdRoots, placements])

  const [metadataForm] = Form.useForm()
  const [publishForm] = Form.useForm()
  const revisedPrefix = Form.useWatch('fileNamePrefix', metadataForm)

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

  const validationSummary = useMemo(() => {
    if (!validationResult) {
      return null
    }

    const issueCount = validationResult.issues.length
    const hasApiError = validationResult.issues.some((issue) => issue.code === 'API_ERROR')

    if (validationResult.isValid) {
      return {
        severity: 'success' as const,
        profile: validationResult.validationProfile,
        issueCount,
        hasApiError,
        detailItems: [{ code: 'OK', message: 'No validation issues found.' }],
      }
    }

    return {
      severity: 'error' as const,
      profile: validationResult.validationProfile,
      issueCount,
      hasApiError,
      detailItems: validationResult.issues.map((issue) => ({
        code: issue.code,
        message: issue.message,
      })),
    }
  }, [validationResult])

  useEffect(() => {
    if (!selectedNode || selectedNode.nodeType !== 'document' || !selectedPlacement || !selectedDocument) {
      metadataForm.resetFields()
      return
    }

    metadataForm.setFieldsValue({
      title: selectedPlacement.title || '',
      fileNamePrefix: selectedDocumentNameParts.prefix,
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

  const fetchPlacements = async () => {
    try {
      const res = await apiFetch('/api/document-placements')
      const list = Array.isArray(res) ? res : (res.items || [])
      const mapped = list.filter((p: DocumentPlacementRecord) => p.applicationId === appId && p.sequenceNumber === seqNumber)
      setPlacements(mapped)
    } catch (e) {
      console.warn('Could not fetch existing placements', e)
    }
  }

  const fetchDocuments = async () => {
    try {
      const docs = await apiFetch('/api/documents') as DocumentRecord[]
      const mapped = Object.fromEntries((docs || []).map((doc) => [doc.id, doc]))
      setDocumentsById(mapped)
    } catch (e) {
      console.warn('Could not fetch documents', e)
    }
  }

  const fetchEctdStructure = async () => {
    setTreeLoading(true)
    setTreeError(null)
    try {
      const response = await apiFetch(`/api/applications/${appId}/ectd-structure`) as EctdStructureResponse
      const roots = response.roots || []
      setEctdRoots(roots)
      setExpandedKeys(roots.map((node) => node.sectionPath))
    } catch (e: any) {
      setTreeError(e.message || 'Failed to load eCTD structure')
      setEctdRoots([])
      setExpandedKeys([])
    } finally {
      setTreeLoading(false)
    }
  }

  useEffect(() => {
    fetchPlacements()
    fetchDocuments()
  }, [appId, seqNumber])

  useEffect(() => { fetchEctdStructure() }, [appId])

  const refreshWorkspaceData = async () => {
    await Promise.all([fetchPlacements(), fetchDocuments()])
  }

  const getPlacementPayloadFromDataTransfer = (dataTransfer: DataTransfer) => {
    const preferred = tryParsePlacementDragPayload(dataTransfer.getData(WORKSPACE_PLACEMENT_DRAG_MIME))
    if (preferred) {
      return preferred
    }

    return tryParsePlacementDragPayload(dataTransfer.getData('text/plain'))
  }

  const handleMovePlacement = async (placementId: string, fromSection: string, toSection: string) => {
    setMovingPlacementIds((current) => new Set(current).add(placementId))
    setLoading(true)
    try {
      const moved = await movePlacementToSection({ placementId, fromSection, toSection })

      if (!moved) {
        message.info('Document is already mapped to this section.')
        return
      }

      setExpandedKeys((current) => Array.from(new Set([...current, ...getSectionAncestorKeys(toSection)])))
      setSelectedTreeKey(toSection)
      setSelectedSectionPath(toSection)
      await refreshWorkspaceData()
      message.success('Document moved to target section.')
    } catch (error: any) {
      message.error(`Failed to move document: ${error?.message || 'Unknown error'}`)
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
    } catch (error: any) {
      if (error instanceof PlacementDeletePartialFailureError) {
        message.error(`Mapping deleted, but document/file delete failed: ${error.message}`)
      } else {
        message.error(`Failed to delete mapped document: ${error?.message || 'Unknown error'}`)
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

    setSavingRevisionPlacementId(selectedPlacement.id)
    setLoading(true)
    try {
      await revisePlacementMetadata({
        placementId: selectedPlacement.id,
        title: String(values.title || '').trim() || undefined,
        fileNamePrefix: normalizedPrefix,
      })
      await refreshWorkspaceData()
      message.success('File metadata revision saved.')
    } catch (error: any) {
      message.error(`Failed to save metadata revision: ${error?.message || 'Unknown error'}`)
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
      setExpandedKeys((current) => Array.from(new Set([...current, ...getSectionAncestorKeys(targetSection)])))
      setSelectedTreeKey(targetSection)
      setSelectedSectionPath(targetSection)

      const formData = new FormData()
      formData.append('file', file)
      formData.append('CtdSection', targetSection)
      const docRes = await apiFetch(`/api/applications/${appId}/sequences/${seqNumber}/documents/upload`, { method: 'POST', body: formData })

      await apiFetch('/api/document-placements', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          applicationId: appId,
          sequenceNumber: seqNumber,
          documentId: docRes.id,
          ctdSection: targetSection,
          operation: 'New',
        }),
      })

      message.success({ content: `${file.name} mapped to ${targetSection} and saved!`, key: 'uploading' })
      await refreshWorkspaceData()
    } catch (err: any) {
      message.error({ content: `Failed: ${err.message}`, key: 'uploading' })
    } finally {
      setLoading(false)
    }
  }

  const openPublishModal = async () => {
    setPublishing(true)
    setValidationResult(null)
    setIsPublishModalOpen(false)
    publishForm.resetFields()
    try {
      const validationResult = await validateSequenceProvider({
        applicationId: appId,
        sequenceNumber: String(seqNumber).trim(),
      })

      setValidationResult(validationResult)
      if (!validationResult.isValid) {
        return
      }

      publishForm.setFieldsValue({
        outputDirectoryPath: '',
      })
      setIsPublishModalOpen(true)
    } catch (err: any) {
      const errorMessage = err?.message || 'Unknown error'
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
  }

  const triggerPublish = async () => {
    const values = await publishForm.validateFields()
    setPublishing(true)
    try {
      const sequenceNumber = String(seqNumber).trim()

      await createAndExecutePublishJobProvider({
        applicationId: appId,
        sequenceNumber,
        outputDirectoryPath: String(values.outputDirectoryPath || '').trim(),
      })

      message.success('Publish job initiated successfully! Check History tab for results.')
      setIsPublishModalOpen(false)
      publishForm.resetFields()
      onBack()
    } catch (err: any) {
      message.error('Publish failed: ' + err.message)
    } finally {
      setPublishing(false)
    }
  }

  const validationIssueCountText = validationSummary
    ? `${validationSummary.issueCount} ${validationSummary.issueCount === 1 ? 'issue' : 'issues'}`
    : ''
  const validationStatusText = validationSummary
    ? validationSummary.severity === 'success'
      ? 'Validation passed'
      : validationSummary.hasApiError
        ? 'Validation API error'
        : 'Validation failed'
    : ''

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

      <Modal
        title="Publish Sequence"
        open={isPublishModalOpen}
        onCancel={handlePublishModalCancel}
        onOk={triggerPublish}
        confirmLoading={publishing}
        destroyOnClose
      >
        <Form form={publishForm} layout="vertical" requiredMark={false}>
          <Form.Item
            name="outputDirectoryPath"
            label="Export Directory"
            rules={[{ required: true, message: 'Export directory is required.' }]}
          >
            <PathPicker placeholder="e.g. C:/eCTD/exports" />
          </Form.Item>
        </Form>
      </Modal>

      {validationSummary && (
        <div data-testid="validation-summary" data-severity={validationSummary.severity}>
          <Alert
            type={validationSummary.severity}
            showIcon
            message={<span data-testid="validation-summary-title">{validationStatusText}</span>}
            description={(
              <div className="flex flex-col gap-1">
                <div className="flex flex-wrap gap-2">
                  <span data-testid="validation-summary-profile">{validationSummary.profile}</span>
                  <span data-testid="validation-summary-issue-count">{validationIssueCountText}</span>
                  <span data-testid="validation-summary-has-api-error">{validationSummary.hasApiError ? 'Yes' : 'No'}</span>
                  <span data-testid="validation-summary-status-label">{validationStatusText}</span>
                </div>
                <div data-testid="validation-summary-details" className="flex flex-col gap-1">
                  {validationSummary.detailItems.map((item) => (
                    <div key={`${item.code}-${item.message}`}>
                      {item.code !== 'OK' && <Tag color="red">{item.code}</Tag>}
                      {item.message}
                    </div>
                  ))}
                </div>
              </div>
            )}
          />
        </div>
      )}

      <Row gutter={16}>
        <Col span={12}>
          <Card title="eCTD Structure (Drag & Drop files here)" size="small" className="shadow-sm border-gray-200 h-[600px] overflow-y-auto">
            {treeError && <Alert type="error" showIcon className="mb-3" message="Failed to load eCTD structure" description={treeError} />}
            <p className="mb-2 text-xs text-gray-500">Tip: Drag a mapped file node to a section node to move it. Allowed extensions: {ectdAllowedExtensionsHint}</p>
            <Spin spinning={loading || treeLoading}>
              <Tree
                className="ectd-tree"
                treeData={treeData}
                expandedKeys={expandedKeys}
                selectedKeys={selectedTreeKey ? [selectedTreeKey] : []}
                blockNode
                height={520}
                virtual
                motion={null}
                onExpand={(keys) => setExpandedKeys(keys.map((key) => String(key)))}
                onSelect={(keys) => {
                  const selectedKey = keys.length > 0 ? String(keys[0]) : null
                  if (!selectedKey) {
                    setSelectedTreeKey(null)
                    return
                  }

                  const resolvedSelectedNode = findWorkspaceTreeNode(treeData, selectedKey)
                  if (!resolvedSelectedNode) {
                    return
                  }

                  setSelectedTreeKey(resolvedSelectedNode.key)
                  setSelectedSectionPath(resolvedSelectedNode.sectionPath)
                  setExpandedKeys((current) => Array.from(new Set([...current, ...getSectionAncestorKeys(resolvedSelectedNode.sectionPath)])))
                }}
                titleRender={(nodeData: WorkspaceTreeNode) => {
                  const isSelected = selectedTreeKey === nodeData.key
                  const isHovered = dragOverNode === nodeData.key
                  const isSection = nodeData.nodeType === 'section'
                  const acceptsPlacementDrop = isSection
                  const acceptsFileDrop = isSection && nodeData.canDrop
                  const canDrop = acceptsFileDrop || (isSection && draggingPlacementId !== null)
                  const isBusy = loading || treeLoading
                  const titleText = String(nodeData.title ?? '')
                  const titleMatch = isSection ? /^([0-9]+(?:\.[0-9A-Z]+)*)\s+(.+)$/.exec(titleText) : null
                  const titlePrefix = titleMatch ? titleMatch[1] : null
                  const titleLabel = titleMatch ? titleMatch[2] : titleText

                  return (
                    <div
                      draggable={!isSection && !isBusy}
                      onDragStart={(e) => {
                        if (isSection || nodeData.nodeType !== 'document') {
                          return
                        }

                        setDraggingPlacementId(nodeData.placementId)
                        e.dataTransfer.effectAllowed = 'move'
                        const payload = serializePlacementDragPayload({
                          placementId: nodeData.placementId,
                          documentId: nodeData.documentId,
                          sectionPath: nodeData.sectionPath,
                        })
                        e.dataTransfer.setData(
                          WORKSPACE_PLACEMENT_DRAG_MIME,
                          payload,
                        )
                        e.dataTransfer.setData('text/plain', payload)
                      }}
                      onDragEnd={() => setDraggingPlacementId(null)}
                      onDragOver={(e) => {
                        e.preventDefault()
                        e.stopPropagation()

                        const internalPayload = getPlacementPayloadFromDataTransfer(e.dataTransfer)
                        const internalDragActive = draggingPlacementId !== null || internalPayload !== null
                        const allowDrop = internalDragActive ? acceptsPlacementDrop : acceptsFileDrop

                        e.dataTransfer.dropEffect = allowDrop
                          ? (internalDragActive ? 'move' : 'copy')
                          : 'none'

                        if (allowDrop) {
                          setDragOverNode(nodeData.key)
                        } else if (dragOverNode === nodeData.key) {
                          setDragOverNode(null)
                        }
                      }}
                      onDragLeave={(e) => {
                        e.preventDefault()
                        e.stopPropagation()
                        if (dragOverNode === nodeData.key) setDragOverNode(null)
                      }}
                      onDrop={async (e) => {
                        e.preventDefault(); e.stopPropagation()
                        setDragOverNode(null)

                        const internalPayload = getPlacementPayloadFromDataTransfer(e.dataTransfer)
                          ?? (() => {
                            if (!draggingPlacementId) {
                              return null
                            }

                            const placement = placements.find((item) => item.id === draggingPlacementId)
                            if (!placement) {
                              return null
                            }

                            return {
                              placementId: placement.id,
                              documentId: placement.documentId,
                              sectionPath: placement.ctdSection,
                            }
                          })()

                        if (internalPayload) {
                          if (!acceptsPlacementDrop) {
                            message.warning('Move documents onto a section node.')
                            return
                          }

                          await handleMovePlacement(internalPayload.placementId, internalPayload.sectionPath, nodeData.sectionPath)
                          setDraggingPlacementId(null)
                          return
                        }

                        const files = e.dataTransfer.files
                        if (!files || files.length === 0) {
                          return
                        }

                        if (!acceptsFileDrop) {
                          message.warning(nodeData.nodeType === 'document'
                            ? 'Drop files on a section, not a document.'
                            : 'Only leaf sections accept dropped files.')
                          return
                        }

                        const file = files[0]
                        if (!isAllowedEctdFileName(file.name)) {
                          message.error(`Unsupported file extension. Allowed: ${ectdAllowedExtensionsHint}`)
                          return
                        }
                        await handleDirectDrop(file, nodeData.sectionPath)
                      }}
                      className={`ectd-tree-node ${isSection ? 'ectd-tree-node--section' : 'ectd-tree-node--document'} ${canDrop ? 'ectd-tree-node--droppable' : ''} ${isHovered ? 'ectd-tree-node--hover' : ''} ${isSelected ? 'ectd-tree-node--selected' : ''} ${nodeData.nodeType === 'document' && draggingPlacementId === nodeData.placementId ? 'ectd-tree-node--dragging' : ''}`}
                    >
                      <div className="ectd-tree-node__main">
                        <span className="ectd-tree-node__icon">
                          {isSection ? <FolderOpen size={16} /> : <FileText size={16} />}
                        </span>
                        <div className="ectd-tree-node__text">
                          <div className="ectd-tree-node__labelRow">
                            {titlePrefix && <span className="ectd-tree-node__prefix">{titlePrefix}</span>}
                            <span className="ectd-tree-node__label">{titleLabel}</span>
                            {!isSection && <Tag className="ectd-tree-node__tag" color="blue">{nodeData.operation}</Tag>}
                          </div>
                        </div>
                      </div>
                      {isSection && nodeData.hasPlacement && <CheckCircle size={14} className="ectd-tree-node__status" />}
                    </div>
                  )
                }}
              />
            </Spin>
          </Card>
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
                <Descriptions size="small" bordered column={1} className="selection-details-descriptions">
                  <Descriptions.Item label="Section">{selectedNode.sectionPath}</Descriptions.Item>
                  <Descriptions.Item label="Display">{selectedNode.title}</Descriptions.Item>
                  <Descriptions.Item label="Leaf Node">{selectedNode.canDrop ? 'Yes' : 'No'}</Descriptions.Item>
                  <Descriptions.Item label="Mapped Files">{selectedSectionChildrenCount}</Descriptions.Item>
                </Descriptions>

                <Alert
                  type="info"
                  showIcon
                  message="Leaf Element Data Entry (Reserved)"
                  description="This section is reserved for future leaf element data entry fields."
                />

                <p className="text-xs text-gray-500">Tip: Drop files on leaf sections. Drag file nodes between sections to move them.</p>
              </div>
            )}

            {selectedNode?.nodeType === 'document' && selectedPlacement && selectedDocument && (
              <div className="flex flex-col gap-4">
                <Descriptions size="small" bordered column={1} className="selection-details-descriptions">
                  <Descriptions.Item label="Placement ID">{selectedPlacement.id}</Descriptions.Item>
                  <Descriptions.Item label="eCTD Section"><Tag>{selectedPlacement.ctdSection}</Tag></Descriptions.Item>
                  <Descriptions.Item label="Operation"><Tag color="blue">{selectedPlacement.operation}</Tag></Descriptions.Item>
                  <Descriptions.Item label="Storage Path"><span className="text-xs break-all">{selectedDocument.storagePath}</span></Descriptions.Item>
                </Descriptions>

                <Form form={metadataForm} layout="vertical" requiredMark={false}>
                  <Form.Item name="title" label="Backbone Title (index.xml title)">
                    <Input maxLength={255} placeholder="Optional title" />
                  </Form.Item>
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
                    <Input value={selectedDocumentNameParts.extension || '(no extension)'} readOnly />
                  </Form.Item>
                  <Form.Item label="Resulting File Name">
                    <Input
                      value={`${String(revisedPrefix || '').trim()}${selectedDocumentNameParts.extension}`}
                      readOnly
                    />
                  </Form.Item>
                </Form>

                <Space>
                  <Button
                    type="primary"
                    loading={savingRevisionPlacementId === selectedPlacement.id}
                    disabled={loading || deletingPlacementIds.has(selectedPlacement.id) || movingPlacementIds.has(selectedPlacement.id)}
                    onClick={handleSaveRevision}
                  >
                    Save Revision
                  </Button>
                  <Button
                    danger
                    icon={<Trash2 size={14} />}
                    loading={deletingPlacementIds.has(selectedPlacement.id)}
                    disabled={loading || deletingPlacementIds.has(selectedPlacement.id) || movingPlacementIds.has(selectedPlacement.id)}
                    onClick={() => confirmDeletePlacement(selectedPlacement.id, selectedPlacement.documentId)}
                  >
                    Delete
                  </Button>
                </Space>

                <p className="text-xs text-gray-500">Delete removes mapping and physical file. Editing revision only changes the file prefix; extension remains unchanged.</p>
              </div>
            )}
          </Card>
        </Col>
      </Row>
    </div>
  )
}
