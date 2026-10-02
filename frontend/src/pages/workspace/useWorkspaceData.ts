import { useCallback, useMemo, useState, type SetStateAction } from 'react'
import { useQuery } from '@tanstack/react-query'

import { apiFetch as defaultApiFetch } from '../../apiClient'
import {
  loadWorkspaceEctdStructure,
  loadWorkspaceSnapshot,
} from '../../workspaceActions'
import {
  attachDocumentNodes,
  mapSectionTreeData,
  type DocumentPlacementRecord,
  type DocumentRecord,
  type EctdStructureNode,
} from '../../workspaceTree'
import { getErrorMessage } from '../appShared'
import { buildInstanceTree } from './nodeTree'

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

  const snapshotQuery = useQuery({
    queryKey: ['workspace', appId, seqNumber, 'snapshot'],
    queryFn: ({ signal }) => loadWorkspaceSnapshot(appId, seqNumber, apiFetch, signal),
    refetchOnWindowFocus: false,
    refetchOnReconnect: false,
  })
  const ectdStructureQuery = useQuery({
    queryKey: ['workspace', appId, 'ectd-structure'],
    queryFn: ({ signal }) => loadWorkspaceEctdStructure(appId, apiFetch, signal),
  })
  const { refetch: refetchSnapshot } = snapshotQuery
  const { refetch: refetchEctdStructure } = ectdStructureQuery

  const placementSummary = useMemo(() => {
    const list = getWorkspacePlacementsFromResponse<DocumentPlacementRecord>(snapshotQuery.data?.placements)
    return splitWorkspacePlacements(list, appId, seqNumber)
  }, [appId, snapshotQuery.data, seqNumber])
  const placements = placementSummary.sequencePlacements
  const applicationPlacements = placementSummary.applicationPlacements
  const documentsById = useMemo(
    () => buildWorkspaceDocumentsById(snapshotQuery.data?.documents),
    [snapshotQuery.data],
  )
  const ectdRoots = useMemo(
    () => getWorkspaceEctdRootsFromResponse(ectdStructureQuery.data),
    [ectdStructureQuery.data],
  )
  const nodeTree = snapshotQuery.data?.nodeTree
  const defaultExpandedKeys = useMemo(
    () => nodeTree ? nodeTree.nodes.filter(node => !node.parentInstanceId).map(node => `node:${node.nodeInstanceId}`) : buildWorkspaceExpandedKeys(ectdRoots),
    [ectdRoots, nodeTree],
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
    if (nodeTree) return buildInstanceTree(nodeTree, ectdRoots, placements, documentsById)
    return attachDocumentNodes(mapSectionTreeData(ectdRoots), placements, documentsById)
  }, [documentsById, ectdRoots, placements, nodeTree])

  const fetchPlacements = useCallback(async () => {
    await refetchSnapshot()
  }, [refetchSnapshot])

  const fetchDocuments = useCallback(async () => {
    await refetchSnapshot()
  }, [refetchSnapshot])

  const fetchEctdStructure = useCallback(async () => {
    await refetchEctdStructure()
  }, [refetchEctdStructure])

  const refreshWorkspaceData = useCallback(async () => {
    await refetchSnapshot()
  }, [refetchSnapshot])

  return {
    nodeTree: snapshotQuery.data?.nodeTree,
    workspaceRevision: snapshotQuery.isError ? undefined : snapshotQuery.data?.workspaceRevision,
    placements,
    applicationPlacements,
    documentsById,
    treeData,
    treeLoading: ectdStructureQuery.isFetching,
    treeError: ectdStructureQuery.error
      ? getErrorMessage(ectdStructureQuery.error, '加载 eCTD 结构失败')
      : null,
    placementsError: snapshotQuery.error ? getErrorMessage(snapshotQuery.error) : null,
    documentsError: snapshotQuery.error ? getErrorMessage(snapshotQuery.error) : null,
    expandedKeys,
    setExpandedKeys,
    fetchPlacements,
    fetchDocuments,
    fetchEctdStructure,
    refreshWorkspaceData,
  }
}
