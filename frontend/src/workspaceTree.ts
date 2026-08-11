import type {
  DocumentPlacementRecord,
  DocumentRecord,
  EctdStructureNode,
} from './api/contracts'

export type { DocumentPlacementRecord, DocumentRecord, EctdStructureNode } from './api/contracts'

export type WorkspaceTreeNode =
  | {
      nodeType: 'section'
      key: string
      sectionPath: string
      title: string
      canDrop: boolean
      hasPlacement: boolean
      children: WorkspaceTreeNode[]
    }
  | {
      nodeType: 'document'
      key: string
      sectionPath: string
      placementId: string
      documentId: string
      title: string
      operation: string
      children: []
    }

export type WorkspaceTreeNodeTitleParts = {
  text: string
  prefix: string | null
  label: string
}

export type WorkspaceTreeNodeDropCapabilities = {
  isSection: boolean
  acceptsPlacementDrop: boolean
  acceptsFileDrop: boolean
  canDrop: boolean
}

type WorkspaceTreeNodeClassNameInput = {
  nodeType: WorkspaceTreeNode['nodeType']
  canDrop: boolean
  isHovered: boolean
  isSelected: boolean
  isDragging: boolean
}

type SegmentType = 'module' | 'number' | 'text'

const compareSectionPaths = (left: string, right: string) => {
  const normalize = (value: string) => value.split('.').map((segment) => {
    if (/^m\d+$/i.test(segment)) {
      return { type: 'module' as SegmentType, value: parseInt(segment.slice(1), 10) }
    }

    if (/^\d+$/.test(segment)) {
      return { type: 'number' as SegmentType, value: parseInt(segment, 10) }
    }

    return { type: 'text' as SegmentType, value: segment.toLowerCase() }
  })

  const a = normalize(left)
  const b = normalize(right)
  const length = Math.max(a.length, b.length)

  for (let index = 0; index < length; index += 1) {
    const aValue = a[index]
    const bValue = b[index]

    if (!aValue) return -1
    if (!bValue) return 1

    if (aValue.type !== bValue.type) {
      const order: Record<SegmentType, number> = { module: 0, number: 1, text: 2 }
      return order[aValue.type] - order[bValue.type]
    }

    if (aValue.value < bValue.value) return -1
    if (aValue.value > bValue.value) return 1
  }

  return 0
}

const formatSectionNumber = (sectionPath: string) => {
  const parts = sectionPath.split('.')
  if (parts.length === 0) return sectionPath

  const [module, ...rest] = parts
  const formattedModule = module.startsWith('m') ? module.slice(1) : module
  const formattedRest = rest.map((part) => (/^[a-z]$/i.test(part) ? part.toUpperCase() : part))
  return [formattedModule, ...formattedRest].join('.')
}

const buildTreeLabel = (node: EctdStructureNode) => {
  const prefix = formatSectionNumber(node.sectionPath)
  const normalizedDisplayName = node.displayName.trim()
  const normalizedLowerCaseTitle = normalizedDisplayName.toLowerCase()

  if (!prefix) {
    return normalizedDisplayName
  }

  if (
    normalizedLowerCaseTitle === `module ${prefix.toLowerCase()}`
    || normalizedLowerCaseTitle.startsWith(`module ${prefix.toLowerCase()} `)
  ) {
    return normalizedDisplayName
  }

  if (normalizedDisplayName.startsWith(`${prefix} `)) {
    return normalizedDisplayName
  }

  return `${prefix} ${normalizedDisplayName}`.trim()
}

const createDocumentNode = (
  placement: DocumentPlacementRecord,
  documentsById: Record<string, DocumentRecord>,
): WorkspaceTreeNode => {
  const fileName = documentsById[placement.documentId]?.fileName || placement.title || placement.documentId

  return {
    nodeType: 'document',
    key: `placement:${placement.id}`,
    sectionPath: placement.ctdSection,
    placementId: placement.id,
    documentId: placement.documentId,
    title: fileName,
    operation: placement.operation,
    children: [],
  }
}

export const mapSectionTreeData = (nodes: EctdStructureNode[]): WorkspaceTreeNode[] => {
  return [...nodes]
    .sort((a, b) => compareSectionPaths(a.sectionPath, b.sectionPath))
    .map((node) => ({
      nodeType: 'section',
      key: node.sectionPath,
      sectionPath: node.sectionPath,
      title: buildTreeLabel(node),
      canDrop: (node.children || []).length === 0,
      hasPlacement: false,
      children: mapSectionTreeData(node.children || []),
    }))
}

export const buildPlacementsBySection = (
  placements: DocumentPlacementRecord[],
): Record<string, DocumentPlacementRecord[]> => placements.reduce<Record<string, DocumentPlacementRecord[]>>((accumulator, placement) => {
  if (!accumulator[placement.ctdSection]) {
    accumulator[placement.ctdSection] = []
  }

  accumulator[placement.ctdSection].push(placement)
  return accumulator
}, {})

export const attachDocumentNodes = (
  nodes: WorkspaceTreeNode[],
  placements: DocumentPlacementRecord[],
  documentsById: Record<string, DocumentRecord>,
): WorkspaceTreeNode[] => {
  const placementsBySection = buildPlacementsBySection(placements)

  const attach = (treeNodes: WorkspaceTreeNode[]): WorkspaceTreeNode[] => {
    return treeNodes.map((node) => {
      if (node.nodeType === 'document') {
        return node
      }

      const sectionChildren = attach(node.children.filter((child) => child.nodeType === 'section'))
      const documentChildren = [...(placementsBySection[node.sectionPath] || [])]
        .map((placement) => createDocumentNode(placement, documentsById))
        .sort((left, right) => left.title.localeCompare(right.title))

      return {
        ...node,
        hasPlacement: documentChildren.length > 0,
        children: [...sectionChildren, ...documentChildren],
      }
    })
  }

  return attach(nodes)
}

export const findWorkspaceTreeNode = (
  nodes: WorkspaceTreeNode[],
  key: string,
): WorkspaceTreeNode | undefined => {
  for (const node of nodes) {
    if (node.key === key) {
      return node
    }

    const match = findWorkspaceTreeNode(node.children, key)
    if (match) {
      return match
    }
  }

  return undefined
}

export const getWorkspaceTreeNodeTitleParts = (
  node: WorkspaceTreeNode,
): WorkspaceTreeNodeTitleParts => {
  const text = String(node.title ?? '')
  const titleMatch = node.nodeType === 'section' ? /^([0-9]+(?:\.[0-9A-Z]+)*)\s+(.+)$/.exec(text) : null

  return {
    text,
    prefix: titleMatch ? titleMatch[1] : null,
    label: titleMatch ? titleMatch[2] : text,
  }
}

export const getWorkspaceTreeNodeDropCapabilities = (
  node: WorkspaceTreeNode,
  draggingPlacementId: string | null,
): WorkspaceTreeNodeDropCapabilities => {
  const isSection = node.nodeType === 'section'
  const acceptsFileDrop = isSection && node.canDrop

  return {
    isSection,
    acceptsPlacementDrop: isSection,
    acceptsFileDrop,
    canDrop: acceptsFileDrop || (isSection && draggingPlacementId !== null),
  }
}

export const buildWorkspaceTreeNodeClassName = ({
  nodeType,
  canDrop,
  isHovered,
  isSelected,
  isDragging,
}: WorkspaceTreeNodeClassNameInput) => [
  'ectd-tree-node',
  `ectd-tree-node--${nodeType}`,
  canDrop ? 'ectd-tree-node--droppable' : null,
  isHovered ? 'ectd-tree-node--hover' : null,
  isSelected ? 'ectd-tree-node--selected' : null,
  isDragging ? 'ectd-tree-node--dragging' : null,
].filter(Boolean).join(' ')

export const resolveUploadSection = (
  targetSectionPath: string | null | undefined,
  selectedSectionPath: string | null | undefined,
) => {
  const normalizedTarget = targetSectionPath?.trim() || ''
  if (normalizedTarget.length >= 2) {
    return normalizedTarget
  }

  const normalizedSelected = selectedSectionPath?.trim() || ''
  if (normalizedSelected.length >= 2) {
    return normalizedSelected
  }

  throw new Error('No valid eCTD section selected for upload. Refresh the page and try again.')
}
