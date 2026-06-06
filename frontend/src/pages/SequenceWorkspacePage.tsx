import { useEffect, useMemo, useState } from 'react'
import { Alert, Button, Card, Col, Descriptions, Form, Modal, Row, Spin, Tag, Tree, message } from 'antd'
import { ArrowLeft, CheckCircle, FileText, FolderOpen, PlayCircle, Save } from 'lucide-react'

import { apiFetch } from '../apiClient'
import { PathPicker } from '../PathPicker'
import { createAndExecutePublishJob } from '../publishActions'
import { validateSequence, type ValidationIssue, type ValidationReport } from '../validationActions'
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
import { LeafMetadataPanel } from './LeafMetadataPanel'

const compareSequenceNumbers = (left: string, right: string) => {
  const leftNumber = Number(left)
  const rightNumber = Number(right)

  if (Number.isFinite(leftNumber) && Number.isFinite(rightNumber)) {
    return leftNumber - rightNumber
  }

  return left.localeCompare(right)
}

type SequenceWorkspacePageProps = {
  appId: string
  seqNumber: string
  onBack: () => void
  validateSequenceProvider?: typeof validateSequence
  createAndExecutePublishJobProvider?: typeof createAndExecutePublishJob
}

type ValidationLocation = {
  placementId?: string | null
  documentId?: string | null
  sectionPath?: string | null
}

type PrePublishChecklistRow = {
  key: string
  label: string
  status: 'pass' | 'fail' | 'info'
  detail: string
  blocking: boolean
}

const stringEqualsIgnoreCase = (left: string | null | undefined, right: string) => {
  return String(left || '').trim().toLowerCase() === right.toLowerCase()
}

const isErrorIssue = (issue: ValidationIssue) => stringEqualsIgnoreCase(issue.severity, 'Error')

const getChecklistTagColor = (row: PrePublishChecklistRow) => {
  if (row.status === 'pass') return 'green'
  if (row.blocking) return 'red'
  return 'blue'
}

const getChecklistTagLabel = (row: PrePublishChecklistRow) => {
  if (row.status === 'pass') return 'Pass'
  if (row.status === 'fail') return 'Fail'
  return 'Awareness'
}

const buildPrePublishChecklistSummary = (validationResult: ValidationReport) => {
  const issues = validationResult.issues || []
  const sectionMatches = validationResult.sectionMatches || []
  const lifecycleMatches = validationResult.lifecycleMatches || []
  const blockingIssues = issues.filter(isErrorIssue)
  const warningIssues = issues.filter((issue) => !isErrorIssue(issue))
  const hasApiError = issues.some((issue) => issue.code === 'API_ERROR')
  const invalidSectionCount = sectionMatches.filter((match) => !match.isValid).length
  const nonStandardSectionCount = sectionMatches.filter((match) => match.isValid && !match.isStandard).length
  const lifecycleIssueCount = lifecycleMatches.filter((match) => match.resultCode !== 'MATCHED').length
  const sectionRows = sectionMatches.filter((match) => !match.isValid || !match.isStandard)
  const canProceed = !hasApiError && blockingIssues.length === 0
  const hasBlockingLifecycleIssue = blockingIssues.some((issue) => lifecycleMatches.some((match) => issue.code === match.resultCode)
    || issue.code.startsWith('LIFECYCLE_')
    || issue.code.endsWith('_TARGET_NOT_FOUND'))
  const hasBlockingSectionIssue = blockingIssues.some((issue) => issue.code === 'INVALID_SECTION_PATH')
  const checklistRows: PrePublishChecklistRow[] = [
    {
      key: 'api-reachable',
      label: 'Validation API reachable',
      status: hasApiError ? 'fail' : 'pass',
      detail: hasApiError ? 'Validation service did not return a usable report.' : 'Validation API returned a report.',
      blocking: true,
    },
    {
      key: 'blocking-errors',
      label: 'No blocking validation errors',
      status: blockingIssues.length === 0 ? 'pass' : 'fail',
      detail: `${blockingIssues.length} blocking error(s)`,
      blocking: true,
    },
    {
      key: 'lifecycle-targets',
      label: 'Lifecycle targets resolved',
      status: lifecycleIssueCount === 0 ? 'pass' : hasBlockingLifecycleIssue ? 'fail' : 'info',
      detail: lifecycleMatches.length === 0
        ? 'No lifecycle operations were checked.'
        : `${lifecycleIssueCount} lifecycle issue(s)`,
      blocking: lifecycleIssueCount > 0 && hasBlockingLifecycleIssue,
    },
    {
      key: 'section-paths',
      label: 'Section paths acceptable',
      status: invalidSectionCount > 0 && hasBlockingSectionIssue
        ? 'fail'
        : invalidSectionCount > 0 || nonStandardSectionCount > 0
          ? 'info'
          : 'pass',
      detail: `${invalidSectionCount} invalid | ${nonStandardSectionCount} non-standard`,
      blocking: invalidSectionCount > 0 && hasBlockingSectionIssue,
    },
    {
      key: 'warnings-reviewed',
      label: 'Warnings reviewed',
      status: warningIssues.length === 0 ? 'pass' : 'info',
      detail: `${warningIssues.length} warning(s) for reviewer awareness`,
      blocking: false,
    },
  ]

  return {
    severity: canProceed ? 'success' as const : 'error' as const,
    profile: validationResult.validationProfile,
    issueCount: issues.length,
    blockingIssueCount: blockingIssues.length,
    warningCount: warningIssues.length,
    hasApiError,
    canProceed,
    blockingIssues,
    warningIssues,
    lifecycleMatches,
    lifecycleIssueCount,
    sectionMatches,
    invalidSectionCount,
    nonStandardSectionCount,
    sectionRows,
    checklistRows,
  }
}

export const SequenceWorkspacePage = ({
  appId,
  seqNumber,
  onBack,
  validateSequenceProvider = validateSequence,
  createAndExecutePublishJobProvider = createAndExecutePublishJob,
}: SequenceWorkspacePageProps) => {
  const [placements, setPlacements] = useState<DocumentPlacementRecord[]>([])
  const [applicationPlacements, setApplicationPlacements] = useState<DocumentPlacementRecord[]>([])
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
  const revisedOperation = Form.useWatch('operation', metadataForm)
  const revisedLifecycleTargetPlacementId = Form.useWatch('lifecycleTargetPlacementId', metadataForm)

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

    return applicationPlacements
      .filter((placement) => placement.applicationId === selectedPlacement.applicationId)
      .filter((placement) => placement.ctdSection === selectedPlacement.ctdSection)
      .filter((placement) => compareSequenceNumbers(placement.sequenceNumber, selectedPlacement.sequenceNumber) < 0)
      .filter((placement) => Boolean(documentsById[placement.documentId]))
  }, [applicationPlacements, documentsById, selectedPlacement])

  const hasValidationLocation = (location: ValidationLocation) => Boolean(
    location.placementId?.trim()
    || location.documentId?.trim()
    || location.sectionPath?.trim(),
  )

  const resolveValidationLocation = (location: ValidationLocation) => {
    const placementId = location.placementId?.trim()
    if (placementId) {
      const key = `placement:${placementId}`
      const node = findWorkspaceTreeNode(treeData, key)
      if (node) {
        return { key: node.key, sectionPath: node.sectionPath }
      }
    }

    const documentId = location.documentId?.trim()
    const sectionPath = location.sectionPath?.trim()
    if (documentId) {
      const placement = sectionPath
        ? placements.find((item) => item.documentId === documentId && item.ctdSection === sectionPath)
        : undefined
      const fallbackPlacement = placement || placements.find((item) => item.documentId === documentId)
      if (fallbackPlacement) {
        const key = `placement:${fallbackPlacement.id}`
        const node = findWorkspaceTreeNode(treeData, key)
        if (node) {
          return { key: node.key, sectionPath: node.sectionPath }
        }
      }
    }

    if (sectionPath) {
      const node = findWorkspaceTreeNode(treeData, sectionPath)
      if (node) {
        return { key: node.key, sectionPath: node.sectionPath }
      }
    }

    return null
  }

  const locateValidationIssue = (location: ValidationLocation) => {
    const resolvedLocation = resolveValidationLocation(location)
    if (!resolvedLocation) {
      message.warning('Could not locate this validation issue in the workspace tree.')
      return
    }

    setSelectedTreeKey(resolvedLocation.key)
    setSelectedSectionPath(resolvedLocation.sectionPath)
    setExpandedKeys((current) => Array.from(new Set([
      ...current,
      ...getSectionAncestorKeys(resolvedLocation.sectionPath),
      resolvedLocation.sectionPath,
    ])))
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

  const fetchPlacements = async () => {
    try {
      const res = await apiFetch('/api/document-placements')
      const list = Array.isArray(res) ? res : (res.items || [])
      const applicationMapped = list.filter((p: DocumentPlacementRecord) => p.applicationId === appId)
      const mapped = list.filter((p: DocumentPlacementRecord) => p.applicationId === appId && p.sequenceNumber === seqNumber)
      setApplicationPlacements(applicationMapped)
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

      const checklistSummary = buildPrePublishChecklistSummary(validationResult)
      setValidationResult(validationResult)
      if (!checklistSummary.canProceed) {
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
    ? `${validationSummary.blockingIssueCount} blocking | ${validationSummary.warningCount} ${validationSummary.warningCount === 1 ? 'warning' : 'warnings'}`
    : ''
  const validationStatusText = validationSummary
    ? validationSummary.canProceed
      ? 'Pre-publish checks passed'
      : 'Pre-publish checks failed'
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
        destroyOnHidden
      >
        <Form form={publishForm} layout="vertical" requiredMark={false}>
          {validationSummary?.canProceed && (
            <Alert
              type="success"
              showIcon
              className="mb-3"
              title="Pre-publish checks passed"
              description={`Pre-publish checks passed. ${validationSummary.warningCount} warning(s) remain for reviewer awareness.`}
            />
          )}
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
            title={<span data-testid="validation-summary-title">{validationStatusText}</span>}
            description={(
              <div className="flex flex-col gap-1">
                <div className="flex flex-wrap gap-2">
                  <span data-testid="validation-summary-profile">{validationSummary.profile}</span>
                  <span data-testid="validation-summary-issue-count">{validationIssueCountText}</span>
                  <span data-testid="validation-summary-has-api-error">{validationSummary.hasApiError ? 'Yes' : 'No'}</span>
                  <span data-testid="validation-summary-status-label">{validationStatusText}</span>
                </div>
                <div data-testid="validation-summary-details" className="flex flex-col gap-3">
                  <div data-testid="validation-summary-checklist" className="rounded border border-gray-200 bg-white/70 p-3">
                    <div className="mb-2 font-semibold">Pre-publish Checklist</div>
                    <div className="flex flex-col gap-2">
                      {validationSummary.checklistRows.map((row) => (
                        <div key={row.key} data-testid={`validation-summary-checklist-${row.key}`}>
                          <Tag color={getChecklistTagColor(row)}>{getChecklistTagLabel(row)}</Tag>
                          <span>{row.label}</span>
                          <span> | {row.detail}</span>
                          {!row.blocking && <Tag color="blue" className="ml-2">Non-blocking</Tag>}
                        </div>
                      ))}
                    </div>
                  </div>

                  <div data-testid="validation-summary-issues" className="rounded border border-gray-200 bg-white/70 p-3">
                    <div className="mb-2 font-semibold">Blocking Issues</div>
                    {validationSummary.blockingIssues.length === 0 ? (
                      <div>No blocking validation errors found.</div>
                    ) : (
                      <div className="flex flex-col gap-2">
                        {validationSummary.blockingIssues.map((issue: ValidationIssue) => (
                          <div key={`blocking-${issue.severity}-${issue.code}-${issue.message}`}>
                            <Tag color="red">{issue.severity}</Tag>
                            <Tag color="red">{issue.code}</Tag>
                            {issue.message}
                            {hasValidationLocation(issue) && (
                              <Button size="small" className="ml-2" onClick={() => locateValidationIssue(issue)}>Locate</Button>
                            )}
                          </div>
                        ))}
                      </div>
                    )}
                  </div>

                  <div data-testid="validation-summary-warnings" className="rounded border border-gray-200 bg-white/70 p-3">
                    <div className="mb-2 font-semibold">Warnings</div>
                    {validationSummary.warningIssues.length === 0 ? (
                      <div>No validation warnings found.</div>
                    ) : (
                      <div className="flex flex-col gap-2">
                        {validationSummary.warningIssues.map((issue: ValidationIssue) => (
                          <div key={`warning-${issue.severity}-${issue.code}-${issue.message}`}>
                            <Tag color="gold">{issue.severity}</Tag>
                            <Tag color="gold">{issue.code}</Tag>
                            {issue.message}
                            {hasValidationLocation(issue) && (
                              <Button size="small" className="ml-2" onClick={() => locateValidationIssue(issue)}>Locate</Button>
                            )}
                          </div>
                        ))}
                      </div>
                    )}
                  </div>

                  <div data-testid="validation-summary-lifecycle" className="rounded border border-gray-200 bg-white/70 p-3">
                    <div className="mb-2 font-semibold">Lifecycle Targets</div>
                    {validationSummary.lifecycleMatches.length === 0 ? (
                      <div>No lifecycle operations were checked.</div>
                    ) : (
                      <div className="flex flex-col gap-2">
                        {validationSummary.lifecycleMatches.map((match) => (
                          <div key={`${match.operation}-${match.sequenceNumber}-${match.ctdSection}-${match.documentId}`}>
                            <Tag color={match.resultCode === 'MATCHED' ? 'green' : 'red'}>{match.resultCode}</Tag>
                            <span>{match.operation} in {match.ctdSection}</span>
                            <span> | sequence {match.sequenceNumber}</span>
                            <span> | strategy {match.matchStrategy}</span>
                            <span> | {match.historicalMatchCount} historical match{match.historicalMatchCount === 1 ? '' : 'es'}</span>
                            {match.historicalSequenceNumbers.length > 0 && <span> | historical sequences {match.historicalSequenceNumbers.join(', ')}</span>}
                            <span> | final state {match.historicalFinalState}</span>
                            {hasValidationLocation({ documentId: match.documentId, sectionPath: match.ctdSection }) && (
                              <Button size="small" className="ml-2" onClick={() => locateValidationIssue({ documentId: match.documentId, sectionPath: match.ctdSection })}>Locate</Button>
                            )}
                          </div>
                        ))}
                      </div>
                    )}
                  </div>

                  <div data-testid="validation-summary-sections" className="rounded border border-gray-200 bg-white/70 p-3">
                    <div className="mb-2 font-semibold">Section Matches</div>
                    {validationSummary.sectionMatches.length === 0 ? (
                      <div>No section matches were checked.</div>
                    ) : (
                      <div className="flex flex-col gap-2">
                        <div>
                          {validationSummary.sectionMatches.length} checked | {validationSummary.invalidSectionCount} invalid | {validationSummary.nonStandardSectionCount} non-standard
                        </div>
                        {validationSummary.sectionRows.length === 0 ? (
                          <div>All checked sections are valid standard matches.</div>
                        ) : (
                          validationSummary.sectionRows.map((match) => (
                            <div key={`${match.sectionPath}-${match.reason || 'ok'}`}>
                              <Tag color={match.isValid ? 'gold' : 'red'}>{match.isValid ? 'Non-standard' : 'Invalid'}</Tag>
                              <span>{match.sectionPath}</span>
                              {match.matchedPrefix && <span> | matched {match.matchedPrefix}</span>}
                              {match.reason && <span> | {match.reason}</span>}
                              {hasValidationLocation({ sectionPath: match.sectionPath }) && (
                                <Button size="small" className="ml-2" onClick={() => locateValidationIssue({ sectionPath: match.sectionPath })}>Locate</Button>
                              )}
                            </div>
                          ))
                        )}
                      </div>
                    )}
                  </div>
                </div>
              </div>
            )}
          />
        </div>
      )}

      <Row gutter={16}>
        <Col span={12}>
          <Card title="eCTD Structure (Drag & Drop files here)" size="small" className="shadow-sm border-gray-200 h-[600px] overflow-y-auto">
            {treeError && <Alert type="error" showIcon className="mb-3" title="Failed to load eCTD structure" description={treeError} />}
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
