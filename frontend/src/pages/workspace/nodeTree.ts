import type { CtdNodeDto, CtdNodeTreeDto, SectionDefinition } from '../../api/generated'
import { attachDocumentNodes, mapSectionTreeData, type DocumentPlacementRecord, type DocumentRecord, type EctdStructureNode, type WorkspaceTreeNode } from '../../workspaceTree'

export const nodeLabel = (node: CtdNodeDto, definitions: SectionDefinition[]) => {
  const schema = definitions.find(item => item.definitionKey === node.definitionKey)
  const identity = schema?.attributes.filter(item => item.identity).map(item => node.attributes[item.name]).filter(Boolean) ?? []
  return [node.title, ...identity].filter(Boolean).join(' / ') || node.ctdSection.toUpperCase()
}

export const nodeContext = (tree: CtdNodeTreeDto, id?: string | null): string => {
  const result: string[] = []
  const visited = new Set<string>()
  for (let node = tree.nodes.find(item => item.nodeInstanceId === id); node && !visited.has(node.nodeInstanceId); node = tree.nodes.find(item => item.nodeInstanceId === node?.parentInstanceId)) {
    visited.add(node.nodeInstanceId)
    const label = nodeLabel(node, tree.definitions)
    if (label !== node.ctdSection.toUpperCase()) result.unshift(label)
  }
  return result.join(' › ')
}

export const buildInstanceTree = (tree: CtdNodeTreeDto, roots: EctdStructureNode[], placements: DocumentPlacementRecord[], documents: Record<string, DocumentRecord>): WorkspaceTreeNode[] => {
  const labels = new Map<string, string>()
  const visit = (items: EctdStructureNode[]) => items.forEach(item => { labels.set(item.sectionPath, item.displayName); visit(item.children ?? []) })
  visit(roots)
  const schema = new Map(tree.definitions.map(item => [item.definitionKey, item]))
  const definitionLabel = (definition: SectionDefinition) => labels.get(definition.sectionPath ?? '') ??
    (definition.kind === 1 ? '扩展分组' : `${definition.sectionPath?.toUpperCase()} ${definition.elementName
      .slice((definition.sectionPath ?? '').length + 1).split('-').map(word => word[0]?.toUpperCase() + word.slice(1)).join(' ')}`)
  const placeholder = (definition: SectionDefinition, parent?: CtdNodeDto): WorkspaceTreeNode => ({
    nodeType: 'section', key: `definition:${parent?.nodeInstanceId ?? 'root'}:${definition.definitionKey}`,
    definitionKey: definition.definitionKey, parentInstanceId: parent?.nodeInstanceId ?? null,
    sectionPath: definition.sectionPath ?? parent?.ctdSection ?? '',
    title: `＋ ${definitionLabel(definition)}`,
    canDrop: false, requiresNodeBinding: true, hasPlacement: false, children: [],
  })
  const document = (placement: DocumentPlacementRecord): WorkspaceTreeNode => ({
    nodeType: 'document', key: `placement:${placement.id}`, sectionPath: placement.ctdSection,
    nodeInstanceId: placement.nodeInstanceId, placementId: placement.id, documentId: placement.documentId,
    title: documents[placement.documentId]?.fileName ?? placement.title ?? placement.documentId, operation: placement.operation, children: [],
  })
  const instance = (node: CtdNodeDto): WorkspaceTreeNode => {
    const definition = schema.get(node.definitionKey)!
    const children: WorkspaceTreeNode[] = []
    for (const slot of definition.children) {
      const childDefinition = schema.get(slot.definitionKey)!
      const members = tree.nodes.filter(child => child.parentInstanceId === node.nodeInstanceId && child.definitionKey === slot.definitionKey)
      children.push(...members.sort(order).map(instance))
      if (members.length === 0 || childDefinition.repeatable) children.push(placeholder(childDefinition, node))
    }
    const entries = placements.filter(leaf => leaf.nodeInstanceId === node.nodeInstanceId).map(leaf => ({ sortOrder: leaf.sortOrder ?? 0, id: leaf.id, value: document(leaf) }))
    const extensions = tree.nodes.filter(child => child.parentInstanceId === node.nodeInstanceId && schema.get(child.definitionKey)?.kind === 1)
      .map(child => ({ sortOrder: child.sortOrder, id: child.nodeInstanceId, value: instance(child) }))
    children.unshift(...[...entries, ...extensions].sort((a, b) => a.sortOrder - b.sortOrder || a.id.localeCompare(b.id)).map(item => item.value))
    if (definition.extensionPolicy === 1) children.push(placeholder(schema.get('node-extension')!, node))
    const context = nodeLabel(node, tree.definitions)
    const label = definition.kind === 1 ? `${node.ctdSection.toUpperCase()} 扩展分组` : definitionLabel(definition)
    return { nodeType: 'section', key: `node:${node.nodeInstanceId}`, nodeInstanceId: node.nodeInstanceId,
      definitionKey: node.definitionKey, parentInstanceId: node.parentInstanceId, sectionPath: node.ctdSection,
      title: `${label}${context !== node.ctdSection.toUpperCase() ? ` — ${context}` : ''}${node.metadataStatus !== 'Complete' ? '（待修复）' : ''}`,
      metadataStatus: node.metadataStatus, requiresNodeBinding: true, canDrop: node.allowsLeaves && node.metadataStatus === 'Complete',
      hasPlacement: entries.length > 0, children }
  }
  const output = attachDocumentNodes(mapSectionTreeData(roots.filter(root => root.sectionPath === 'm1')), placements.filter(leaf => leaf.ctdSection.startsWith('m1')), documents)
  for (const definition of tree.definitions.filter(item => !item.parentDefinitionKey && item.kind === 0 && item.sectionPath !== 'm1').sort((a, b) => (a.sectionPath ?? '').localeCompare(b.sectionPath ?? ''))) {
    const members = tree.nodes.filter(node => node.definitionKey === definition.definitionKey && !node.parentInstanceId)
    output.push(...members.sort(order).map(instance))
    if (members.length === 0) output.push(placeholder(definition))
  }
  const unbound = placements.filter(leaf => !leaf.nodeInstanceId && !leaf.ctdSection.startsWith('m1'))
  if (unbound.length) output.push({ nodeType: 'section', key: 'unbound', sectionPath: '', title: '待映射的历史文件', canDrop: false,
    requiresNodeBinding: true, hasPlacement: true, children: unbound.map(document) })
  return output
}

const order = (a: CtdNodeDto, b: CtdNodeDto) => a.sortOrder - b.sortOrder || a.nodeInstanceId.localeCompare(b.nodeInstanceId)

export const treeAncestorKeys = (roots: WorkspaceTreeNode[], key: string): string[] => {
  for (const node of roots) {
    if (node.key === key) return [key]
    const found = treeAncestorKeys(node.children, key)
    if (found.length) return [node.key, ...found]
  }
  return []
}
