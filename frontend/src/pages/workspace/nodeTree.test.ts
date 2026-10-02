import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'
import type { CtdNodeDto, CtdNodeTreeDto, SectionDefinition } from '../../api/generated'
import { findWorkspaceTreeNode, getWorkspaceTreeNodeDropCapabilities, type DocumentPlacementRecord } from '../../workspaceTree'
import { buildInstanceTree, nodeContext, treeAncestorKeys } from './nodeTree'

const raw = JSON.parse(readFileSync(resolve('../reference/publisher/ich-3.2.2-nodes.json'), 'utf8')) as {
  definitionVersion: string
  definitions: Array<Omit<SectionDefinition, 'kind' | 'extensionPolicy'> & { kind: string, extensionPolicy: string }>
}
const definitions: SectionDefinition[] = raw.definitions.map(definition => ({ ...definition,
  kind: definition.kind === 'Extension' ? 1 : 0, extensionPolicy: definition.extensionPolicy === 'Allowed' ? 1 : 0 }))
const node = (id: string, definitionKey: string, parentInstanceId: string | null, attributes: Record<string, string> = {}): CtdNodeDto => {
  const definition = definitions.find(item => item.definitionKey === definitionKey)!
  return { nodeInstanceId: id, parentInstanceId, definitionKey, attributes, ctdSection: definition.sectionPath!,
    title: null, sortOrder: 0, storageSegment: null, metadataStatus: 'Complete', identityStatus: 'Resolved', allowsLeaves: definition.allowsLeaves, issues: [] }
}
const tree: CtdNodeTreeDto = {
  applicationId: 'app', sequenceNumber: '0000', workspaceRevision: 6, definitionVersion: raw.definitionVersion, definitions, diagnostics: [],
  nodes: [node('m3', 'm3-quality', null), node('body', 'm3-2-body-of-data', 'm3'),
    node('alpha', 'm3-2-s-drug-substance', 'body', { substance: 'Drug A', manufacturer: 'Alpha' }),
    node('beta', 'm3-2-s-drug-substance', 'body', { substance: 'Drug A', manufacturer: 'Beta' }),
    node('alpha-control', 'm3-2-s-4-control-of-drug-substance', 'alpha'), node('beta-control', 'm3-2-s-4-control-of-drug-substance', 'beta'),
    node('alpha-spec', 'm3-2-s-4-1-specification', 'alpha-control'), node('beta-spec', 'm3-2-s-4-1-specification', 'beta-control')],
}
const placement = (id: string, nodeInstanceId?: string): DocumentPlacementRecord => ({
  id, nodeInstanceId, documentId: id, applicationId: 'app', sequenceNumber: '0000', ctdSection: 'm3.2.s.4.1', operation: 'New', title: 'specification.pdf',
})

describe('node-aware workspace tree', () => {
  it('keeps same-section manufacturer files in distinct branches and unbound files visible', () => {
    const output = buildInstanceTree(tree, [], [placement('alpha-file', 'alpha-spec'), placement('beta-file', 'beta-spec'), placement('legacy')], {})
    expect(findWorkspaceTreeNode(output, 'node:alpha-spec')?.children.map(item => item.key)).toContain('placement:alpha-file')
    expect(findWorkspaceTreeNode(output, 'node:alpha-spec')?.children.map(item => item.key)).not.toContain('placement:beta-file')
    expect(findWorkspaceTreeNode(output, 'unbound')?.children[0].key).toBe('placement:legacy')
    expect(nodeContext(tree, 'alpha-spec')).toContain('Alpha')
    expect(nodeContext(tree, 'alpha-spec')).not.toContain('Beta')
    expect(treeAncestorKeys(output, 'placement:beta-file')).toEqual(['node:m3', 'node:body', 'node:beta', 'node:beta-control', 'node:beta-spec', 'placement:beta-file'])
    expect(output.slice(0, 4).map(item => item.sectionPath)).toEqual(['m2', 'm3', 'm4', 'm5'])
  })

  it('uses schema content permissions and binds new-section placeholders to a concrete parent', () => {
    const output = buildInstanceTree(tree, [], [], {})
    const root = findWorkspaceTreeNode(output, 'node:m3')!
    const leaf = findWorkspaceTreeNode(output, 'node:alpha-spec')!
    const missing = findWorkspaceTreeNode(output, 'definition:beta-control:m3-2-s-4-2-analytical-procedures')!
    // The pinned ICH DTD permits leaves before the module's defined children.
    expect(getWorkspaceTreeNodeDropCapabilities(root, 'file').acceptsPlacementDrop).toBe(true)
    expect(getWorkspaceTreeNodeDropCapabilities(leaf, 'file').acceptsPlacementDrop).toBe(true)
    expect(getWorkspaceTreeNodeDropCapabilities(missing, 'file').canDrop).toBe(false)
    expect(missing).toMatchObject({ parentInstanceId: 'beta-control', definitionKey: 'm3-2-s-4-2-analytical-procedures' })
    expect(missing.title).toContain('Analytical Procedures')
  })
})
