import { describe, expect, it } from 'vitest'

import type { DocumentPlacementRecord, WorkspaceTreeNode } from '../../workspaceTree'
import { hasValidationLocation, resolveValidationLocation } from './validationLocationResolver'

const sectionNode = (
  sectionPath: string,
  children: WorkspaceTreeNode[] = [],
): WorkspaceTreeNode => ({
  nodeType: 'section',
  key: sectionPath,
  sectionPath,
  title: sectionPath,
  canDrop: children.length === 0,
  hasPlacement: children.some((child) => child.nodeType === 'document'),
  children,
})

const documentNode = (placementId: string, documentId: string, sectionPath: string): WorkspaceTreeNode => ({
  nodeType: 'document',
  key: `placement:${placementId}`,
  sectionPath,
  placementId,
  documentId,
  title: documentId,
  operation: 'New',
  children: [],
})

const placement = (id: string, documentId: string, ctdSection: string): DocumentPlacementRecord => ({
  id,
  applicationId: 'app-1',
  sequenceNumber: '0001',
  documentId,
  ctdSection,
  operation: 'New',
})

const placements = [
  placement('p-1', 'doc-1', 'm1.1'),
  placement('p-2', 'doc-1', 'm1.2'),
  placement('p-3', 'doc-2', 'm1.3'),
]

const treeData = [
  sectionNode('m1', [
    sectionNode('m1.1', [documentNode('p-1', 'doc-1', 'm1.1')]),
    sectionNode('m1.2', [documentNode('p-2', 'doc-1', 'm1.2')]),
    sectionNode('m1.3', [documentNode('p-3', 'doc-2', 'm1.3')]),
  ]),
]

describe('validationLocationResolver', () => {
  it('detects whether a validation location has any usable target', () => {
    expect(hasValidationLocation({ placementId: '  ', documentId: null, sectionPath: undefined })).toBe(false)
    expect(hasValidationLocation({ placementId: ' p-1 ' })).toBe(true)
    expect(hasValidationLocation({ documentId: 'doc-1' })).toBe(true)
    expect(hasValidationLocation({ sectionPath: 'm1.1' })).toBe(true)
  })

  it('resolves an explicit placement id before other location fields', () => {
    expect(resolveValidationLocation({
      location: { placementId: ' p-2 ', documentId: 'doc-1', sectionPath: 'm1.1' },
      placements,
      treeData,
    })).toEqual({ key: 'placement:p-2', sectionPath: 'm1.2' })
  })

  it('resolves duplicate document placements by section when possible', () => {
    expect(resolveValidationLocation({
      location: { documentId: 'doc-1', sectionPath: 'm1.2' },
      placements,
      treeData,
    })).toEqual({ key: 'placement:p-2', sectionPath: 'm1.2' })
  })

  it('falls back from document id to section path when no document placement exists', () => {
    expect(resolveValidationLocation({
      location: { documentId: 'missing-doc', sectionPath: 'm1.3' },
      placements,
      treeData,
    })).toEqual({ key: 'm1.3', sectionPath: 'm1.3' })
  })

  it('returns null when no location can be resolved in the tree', () => {
    expect(resolveValidationLocation({
      location: { placementId: 'missing-placement', documentId: 'missing-doc', sectionPath: 'm9' },
      placements,
      treeData,
    })).toBeNull()
  })
})
