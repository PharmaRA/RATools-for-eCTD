import { useCallback, useEffect, useMemo, useState } from 'react'

import { apiFetch as defaultApiFetch } from '../../apiClient'
import {
  attachDocumentNodes,
  mapSectionTreeData,
  type DocumentPlacementRecord,
  type DocumentRecord,
  type EctdStructureNode,
} from '../../workspaceTree'
import { type EctdStructureResponse, getErrorMessage } from '../appShared'

type UseWorkspaceDataOptions = {
  appId: string
  seqNumber: string
  apiFetch?: typeof defaultApiFetch
}

export const useWorkspaceData = ({
  appId,
  seqNumber,
  apiFetch = defaultApiFetch,
}: UseWorkspaceDataOptions) => {
  const [placements, setPlacements] = useState<DocumentPlacementRecord[]>([])
  const [applicationPlacements, setApplicationPlacements] = useState<DocumentPlacementRecord[]>([])
  const [documentsById, setDocumentsById] = useState<Record<string, DocumentRecord>>({})
  const [treeLoading, setTreeLoading] = useState(false)
  const [treeError, setTreeError] = useState<string | null>(null)
  const [placementsError, setPlacementsError] = useState<string | null>(null)
  const [documentsError, setDocumentsError] = useState<string | null>(null)
  const [ectdRoots, setEctdRoots] = useState<EctdStructureNode[]>([])
  const [expandedKeys, setExpandedKeys] = useState<string[]>([])

  const treeData = useMemo(() => {
    return attachDocumentNodes(mapSectionTreeData(ectdRoots), placements, documentsById)
  }, [documentsById, ectdRoots, placements])

  const fetchPlacements = useCallback(async () => {
    setPlacementsError(null)
    try {
      const res = await apiFetch('/api/document-placements')
      const list = Array.isArray(res) ? res : (res.items || [])
      const applicationMapped = list.filter((placement: DocumentPlacementRecord) => placement.applicationId === appId)
      const mapped = list.filter((placement: DocumentPlacementRecord) => placement.applicationId === appId && placement.sequenceNumber === seqNumber)
      setApplicationPlacements(applicationMapped)
      setPlacements(mapped)
    } catch (error) {
      const message = getErrorMessage(error)
      setPlacementsError(message)
    }
  }, [apiFetch, appId, seqNumber])

  const fetchDocuments = useCallback(async () => {
    setDocumentsError(null)
    try {
      const docs = await apiFetch('/api/documents') as DocumentRecord[]
      const mapped = Object.fromEntries((docs || []).map((doc) => [doc.id, doc]))
      setDocumentsById(mapped)
    } catch (error) {
      const message = getErrorMessage(error)
      setDocumentsError(message)
    }
  }, [apiFetch])

  const fetchEctdStructure = useCallback(async () => {
    setTreeLoading(true)
    setTreeError(null)
    try {
      const response = await apiFetch(`/api/applications/${appId}/ectd-structure`) as EctdStructureResponse
      const roots = response.roots || []
      setEctdRoots(roots)
      setExpandedKeys(roots.map((node) => node.sectionPath))
    } catch (error) {
      setTreeError(getErrorMessage(error, 'Failed to load eCTD structure'))
      setEctdRoots([])
      setExpandedKeys([])
    } finally {
      setTreeLoading(false)
    }
  }, [apiFetch, appId])

  useEffect(() => {
    void Promise.resolve().then(async () => {
      await fetchPlacements()
      await fetchDocuments()
    })
  }, [fetchDocuments, fetchPlacements])

  useEffect(() => {
    void Promise.resolve().then(fetchEctdStructure)
  }, [fetchEctdStructure])

  const refreshWorkspaceData = useCallback(async () => {
    await Promise.all([fetchPlacements(), fetchDocuments()])
  }, [fetchDocuments, fetchPlacements])

  return {
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
    fetchPlacements,
    fetchDocuments,
    fetchEctdStructure,
    refreshWorkspaceData,
  }
}
