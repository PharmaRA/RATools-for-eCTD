import { useCallback, useState, type DragEvent, type KeyboardEvent } from 'react'
import { message } from 'antd'

import { ectdAllowedExtensionsHint, isAllowedEctdFileName } from '../../ectdFileTypes'
import {
  serializePlacementDragPayload,
  tryParsePlacementDragPayload,
  WORKSPACE_PLACEMENT_DRAG_MIME,
  type PlacementDragPayload,
} from '../../workspaceActions'
import type { DocumentPlacementRecord, WorkspaceTreeNode } from '../../workspaceTree'

type MessageApi = {
  error: (content: string) => void
  warning: (content: string) => void
}

type UseWorkspaceDragDropOptions = {
  placements: DocumentPlacementRecord[]
  movePlacement: (placementId: string, fromSection: string, toSection: string) => Promise<void>
  uploadFile: (file: File, targetNodeKey: string) => Promise<void>
  messageApi?: MessageApi
}

type HandleNodeDropOptions = {
  event: DragEvent<HTMLElement>
  nodeData: WorkspaceTreeNode
  acceptsPlacementDrop: boolean
  acceptsFileDrop: boolean
}

const getPlacementPayloadFromDataTransfer = (dataTransfer: DataTransfer) => {
  const preferred = tryParsePlacementDragPayload(dataTransfer.getData(WORKSPACE_PLACEMENT_DRAG_MIME))
  if (preferred) {
    return preferred
  }

  return tryParsePlacementDragPayload(dataTransfer.getData('text/plain'))
}

export const partitionDroppedFiles = (files: File[] | FileList) => {
  const validFiles: File[] = []
  const invalidFiles: File[] = []

  for (const file of Array.from(files)) {
    if (isAllowedEctdFileName(file.name)) {
      validFiles.push(file)
    } else {
      invalidFiles.push(file)
    }
  }

  return { validFiles, invalidFiles }
}

export const useWorkspaceDragDrop = ({
  placements,
  movePlacement,
  uploadFile,
  messageApi = message,
}: UseWorkspaceDragDropOptions) => {
  const [dragOverNode, setDragOverNode] = useState<string | null>(null)
  const [draggingPlacementId, setDraggingPlacementId] = useState<string | null>(null)
  const [keyboardPlacementPayload, setKeyboardPlacementPayload] = useState<PlacementDragPayload | null>(null)

  const getFallbackDraggingPayload = useCallback((): PlacementDragPayload | null => {
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
  }, [draggingPlacementId, placements])

  const dropFiles = useCallback(async (files: File[] | FileList, targetNodeKey: string) => {
    const { validFiles, invalidFiles } = partitionDroppedFiles(files)

    if (invalidFiles.length > 0) {
      messageApi.error(`Unsupported file extension. Allowed: ${ectdAllowedExtensionsHint}. Skipped: ${invalidFiles.map((file) => file.name).join(', ')}`)
    }

    for (const file of validFiles) {
      await uploadFile(file, targetNodeKey)
    }
  }, [messageApi, uploadFile])

  const handleDragStart = useCallback((nodeData: WorkspaceTreeNode, dataTransfer: DataTransfer) => {
    if (nodeData.nodeType !== 'document') {
      return
    }

    setDraggingPlacementId(nodeData.placementId)
    dataTransfer.effectAllowed = 'move'
    const payload = serializePlacementDragPayload({
      placementId: nodeData.placementId,
      documentId: nodeData.documentId,
      sectionPath: nodeData.sectionPath,
    })
    dataTransfer.setData(WORKSPACE_PLACEMENT_DRAG_MIME, payload)
    dataTransfer.setData('text/plain', payload)
  }, [])

  const handleDragEnd = useCallback(() => {
    setDraggingPlacementId(null)
  }, [])

  const handleDragOver = useCallback((
    event: DragEvent<HTMLElement>,
    nodeKey: string,
    acceptsPlacementDrop: boolean,
    acceptsFileDrop: boolean,
  ) => {
    event.preventDefault()
    event.stopPropagation()

    const internalPayload = getPlacementPayloadFromDataTransfer(event.dataTransfer)
    const internalDragActive = draggingPlacementId !== null || internalPayload !== null
    const allowDrop = internalDragActive ? acceptsPlacementDrop : acceptsFileDrop

    event.dataTransfer.dropEffect = allowDrop
      ? (internalDragActive ? 'move' : 'copy')
      : 'none'

    if (allowDrop) {
      setDragOverNode(nodeKey)
    } else if (dragOverNode === nodeKey) {
      setDragOverNode(null)
    }
  }, [dragOverNode, draggingPlacementId])

  const handleDragLeave = useCallback((event: DragEvent<HTMLElement>, nodeKey: string) => {
    event.preventDefault()
    event.stopPropagation()
    if (dragOverNode === nodeKey) setDragOverNode(null)
  }, [dragOverNode])

  const handleNodeDrop = useCallback(async ({
    event,
    nodeData,
    acceptsPlacementDrop,
    acceptsFileDrop,
  }: HandleNodeDropOptions) => {
    event.preventDefault()
    event.stopPropagation()
    setDragOverNode(null)

    const internalPayload = getPlacementPayloadFromDataTransfer(event.dataTransfer) ?? getFallbackDraggingPayload()

    if (internalPayload) {
      if (!acceptsPlacementDrop) {
        messageApi.warning('Move documents onto a section node.')
        return
      }

      await movePlacement(internalPayload.placementId, internalPayload.sectionPath, nodeData.sectionPath)
      setDraggingPlacementId(null)
      return
    }

    const files = event.dataTransfer.files
    if (!files || files.length === 0) {
      return
    }

    if (!acceptsFileDrop) {
      messageApi.warning(nodeData.nodeType === 'document'
        ? 'Drop files on a section, not a document.'
        : 'Only leaf sections accept dropped files.')
      return
    }

    await dropFiles(files, nodeData.sectionPath)
  }, [dropFiles, getFallbackDraggingPayload, messageApi, movePlacement])

  const handleNodeKeyDown = useCallback(async (
    event: KeyboardEvent<HTMLElement>,
    nodeData: WorkspaceTreeNode,
    acceptsPlacementDrop: boolean,
  ) => {
    if (event.key !== 'Enter' && event.key !== ' ') {
      return
    }

    event.preventDefault()
    event.stopPropagation()

    if (nodeData.nodeType === 'document') {
      const payload = {
        placementId: nodeData.placementId,
        documentId: nodeData.documentId,
        sectionPath: nodeData.sectionPath,
      }
      setKeyboardPlacementPayload(payload)
      setDraggingPlacementId(nodeData.placementId)
      return
    }

    const payload = keyboardPlacementPayload ?? getFallbackDraggingPayload()
    if (!payload) {
      return
    }

    if (!acceptsPlacementDrop) {
      messageApi.warning('Move documents onto a section node.')
      return
    }

    await movePlacement(payload.placementId, payload.sectionPath, nodeData.sectionPath)
    setKeyboardPlacementPayload(null)
    setDraggingPlacementId(null)
  }, [getFallbackDraggingPayload, keyboardPlacementPayload, messageApi, movePlacement])

  return {
    dragOverNode,
    draggingPlacementId,
    setDragOverNode,
    setDraggingPlacementId,
    getPlacementPayloadFromDataTransfer,
    dropFiles,
    handleDragStart,
    handleDragEnd,
    handleDragOver,
    handleDragLeave,
    handleNodeDrop,
    handleNodeKeyDown,
  }
}

export type UseWorkspaceDragDropResult = ReturnType<typeof useWorkspaceDragDrop>
