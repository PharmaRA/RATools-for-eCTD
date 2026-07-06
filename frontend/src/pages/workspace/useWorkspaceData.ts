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

export const buildWorkspaceDataUrls = (appId: string) => {
  const encodedAppId = encodeURIComponent(appId)

  return {
    placements: `/api/document-placements?applicationId=${encodedAppId}`,
    documents: `/api/documents?applicationId=${encodedAppId}`,
    ectdStructure: `/api/applications/${encodedAppId}/ectd-structure`,
  }
}

export const splitWorkspacePlacements = (
  placements: DocumentPlacementRecord[],
  appId: string,
  seqNumber: string,
) => {
  const applicationPlacements: DocumentPlacementRecord[] = []
  const sequencePlacements: DocumentPlacementRecord[] = []

  for (const placement of placements) {
    if (placement.applicationId !== appId) {
      continue
    }

    applicationPlacements.push(placement)

    if (placement.sequenceNumber === seqNumber) {
      sequencePlacements.push(placement)
    }
  }

  return {
    applicationPlacements,
    sequencePlacements,
  }
}

export const getWorkspacePlacementsFromResponse = <T,>(
  response?: T[] | { items?: T[] | null } | null,
): T[] => Array.isArray(response) ? response : response?.items || []

export const buildWorkspaceDocumentsById = (
  docs?: DocumentRecord[] | null,
): Record<string, DocumentRecord> => Object.fromEntries((docs || []).map((doc) => [doc.id, doc]))

export const getWorkspaceEctdRootsFromResponse = (
  response?: { roots?: EctdStructureNode[] | null } | null,
): EctdStructureNode[] => response?.roots || []

export const buildWorkspaceExpandedKeys = (
  roots: readonly EctdStructureNode[],
): string[] => roots.map((node) => node.sectionPath)

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

  const workspaceDataUrls = useMemo(() => buildWorkspaceDataUrls(appId), [appId])

  const treeData = useMemo(() => {
    return attachDocumentNodes(mapSectionTreeData(ectdRoots), placements, documentsById)
  }, [documentsById, ectdRoots, placements])

  const fetchPlacements = useCallback(async () => {
    setPlacementsError(null)
    try {
      const res = await apiFetch(workspaceDataUrls.placements)
      const list = getWorkspacePlacementsFromResponse<DocumentPlacementRecord>(
        res as DocumentPlacementRecord[] | { items?: DocumentPlacementRecord[] | null },
      )
      const placementSummary = splitWorkspacePlacements(list, appId, seqNumber)
      setApplicationPlacements(placementSummary.applicationPlacements)
      setPlacements(placementSummary.sequencePlacements)
    } catch (error) {
      const message = getErrorMessage(error)
      setPlacementsError(message)
    }
  }, [apiFetch, appId, seqNumber, workspaceDataUrls])

  const fetchDocuments = useCallback(async () => {
    setDocumentsError(null)
    try {
      const docs = await apiFetch(workspaceDataUrls.documents) as DocumentRecord[]
      const mapped = buildWorkspaceDocumentsById(docs)
      setDocumentsById(mapped)
    } catch (error) {
      const message = getErrorMessage(error)
      setDocumentsError(message)
    }
  }, [apiFetch, workspaceDataUrls])

  const fetchEctdStructure = useCallback(async () => {
    setTreeLoading(true)
    setTreeError(null)
    try {
      const response = await apiFetch(workspaceDataUrls.ectdStructure) as EctdStructureResponse
      const roots = getWorkspaceEctdRootsFromResponse(response)
      setEctdRoots(roots)
      setExpandedKeys(buildWorkspaceExpandedKeys(roots))
    } catch (error) {
      setTreeError(getErrorMessage(error, 'Failed to load eCTD structure'))
      setEctdRoots([])
      setExpandedKeys([])
    } finally {
      setTreeLoading(false)
    }
  }, [apiFetch, workspaceDataUrls])

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
