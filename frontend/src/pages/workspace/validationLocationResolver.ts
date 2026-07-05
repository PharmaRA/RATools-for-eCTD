import {
  findWorkspaceTreeNode,
  type DocumentPlacementRecord,
  type WorkspaceTreeNode,
} from '../../workspaceTree'

export type ValidationLocation = {
  placementId?: string | null
  documentId?: string | null
  sectionPath?: string | null
}

type ResolveValidationLocationOptions = {
  location: ValidationLocation
  placements: DocumentPlacementRecord[]
  treeData: WorkspaceTreeNode[]
}

export const hasValidationLocation = (location: ValidationLocation) => Boolean(
  location.placementId?.trim()
  || location.documentId?.trim()
  || location.sectionPath?.trim(),
)

export const resolveValidationLocation = ({
  location,
  placements,
  treeData,
}: ResolveValidationLocationOptions) => {
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
