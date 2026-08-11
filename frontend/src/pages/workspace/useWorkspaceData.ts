import { useCallback, useMemo, useState, type SetStateAction } from 'react'
import { useQuery } from '@tanstack/react-query'

import { apiFetch as defaultApiFetch } from '../../apiClient'
import {
  loadWorkspaceDocuments,
  loadWorkspaceEctdStructure,
  loadWorkspacePlacements,
} from '../../workspaceActions'
import {
  attachDocumentNodes,
  mapSectionTreeData,
  type DocumentPlacementRecord,
  type DocumentRecord,
  type EctdStructureNode,
} from '../../workspaceTree'
import { getErrorMessage } from '../appShared'

type UseWorkspaceDataOptions = {
  appId: string
  seqNumber: string
  apiFetch?: typeof defaultApiFetch
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
  const [expandedKeysOverride, setExpandedKeysOverride] = useState<{
    appId: string
    keys: string[]
  } | null>(null)

  const placementsQuery = useQuery({
    queryKey: ['workspace', appId, 'placements'],
    queryFn: ({ signal }) => loadWorkspacePlacements(appId, apiFetch, signal),
  })
  const documentsQuery = useQuery({
    queryKey: ['workspace', appId, 'documents'],
    queryFn: ({ signal }) => loadWorkspaceDocuments(appId, apiFetch, signal),
  })
  const ectdStructureQuery = useQuery({
    queryKey: ['workspace', appId, 'ectd-structure'],
    queryFn: ({ signal }) => loadWorkspaceEctdStructure(appId, apiFetch, signal),
  })
  const { refetch: refetchPlacements } = placementsQuery
  const { refetch: refetchDocuments } = documentsQuery
  const { refetch: refetchEctdStructure } = ectdStructureQuery

  const placementSummary = useMemo(() => {
    const list = getWorkspacePlacementsFromResponse<DocumentPlacementRecord>(placementsQuery.data)
    return splitWorkspacePlacements(list, appId, seqNumber)
  }, [appId, placementsQuery.data, seqNumber])
  const placements = placementSummary.sequencePlacements
  const applicationPlacements = placementSummary.applicationPlacements
  const documentsById = useMemo(
    () => buildWorkspaceDocumentsById(documentsQuery.data),
    [documentsQuery.data],
  )
  const ectdRoots = useMemo(
    () => getWorkspaceEctdRootsFromResponse(ectdStructureQuery.data),
    [ectdStructureQuery.data],
  )
  const defaultExpandedKeys = useMemo(
    () => buildWorkspaceExpandedKeys(ectdRoots),
    [ectdRoots],
  )
  const expandedKeys = expandedKeysOverride?.appId === appId
    ? expandedKeysOverride.keys
    : defaultExpandedKeys
  const setExpandedKeys = useCallback((value: SetStateAction<string[]>) => {
    setExpandedKeysOverride((current) => {
      const currentKeys = current?.appId === appId ? current.keys : defaultExpandedKeys
      const keys = typeof value === 'function' ? value(currentKeys) : value
      return { appId, keys }
    })
  }, [appId, defaultExpandedKeys])

  const treeData = useMemo(() => {
    return attachDocumentNodes(mapSectionTreeData(ectdRoots), placements, documentsById)
  }, [documentsById, ectdRoots, placements])

  const fetchPlacements = useCallback(async () => {
    await refetchPlacements()
  }, [refetchPlacements])

  const fetchDocuments = useCallback(async () => {
    await refetchDocuments()
  }, [refetchDocuments])

  const fetchEctdStructure = useCallback(async () => {
    await refetchEctdStructure()
  }, [refetchEctdStructure])

  const refreshWorkspaceData = useCallback(async () => {
    await Promise.all([fetchPlacements(), fetchDocuments()])
  }, [fetchDocuments, fetchPlacements])

  return {
    placements,
    applicationPlacements,
    documentsById,
    treeData,
    treeLoading: ectdStructureQuery.isFetching,
    treeError: ectdStructureQuery.error
      ? getErrorMessage(ectdStructureQuery.error, '加载 eCTD 结构失败')
      : null,
    placementsError: placementsQuery.error ? getErrorMessage(placementsQuery.error) : null,
    documentsError: documentsQuery.error ? getErrorMessage(documentsQuery.error) : null,
    expandedKeys,
    setExpandedKeys,
    fetchPlacements,
    fetchDocuments,
    fetchEctdStructure,
    refreshWorkspaceData,
  }
}
