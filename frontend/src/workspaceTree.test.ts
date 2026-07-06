import { describe, expect, it } from 'vitest'

import {
  attachDocumentNodes,
  buildWorkspaceTreeNodeClassName,
  buildPlacementsBySection,
  findWorkspaceTreeNode,
  getWorkspaceTreeNodeDropCapabilities,
  getWorkspaceTreeNodeTitleParts,
  mapSectionTreeData,
  resolveUploadSection,
} from './workspaceTree'

describe('workspaceTree', () => {
  it('keeps the top-level module title without duplicating the module number', () => {
    const result = mapSectionTreeData([
      {
        elementName: 'm1',
        sectionPath: 'm1',
        displayName: 'Module 1',
        sourceProfile: 'US',
        children: [],
      },
    ])

    expect(result[0]).toMatchObject({
      sectionPath: 'm1',
      title: 'Module 1',
    })
  })

  it('sorts sections by eCTD section path when building the section tree', () => {
    const result = mapSectionTreeData([
      {
        elementName: 'm1-10',
        sectionPath: 'm1.10',
        displayName: 'Ten',
        sourceProfile: 'US',
        children: [],
      },
      {
        elementName: 'm1-2-a',
        sectionPath: 'm1.2.a',
        displayName: 'A section',
        sourceProfile: 'US',
        children: [],
      },
      {
        elementName: 'm1-2',
        sectionPath: 'm1.2',
        displayName: 'Two',
        sourceProfile: 'US',
        children: [],
      },
      {
        elementName: 'm1-2-1',
        sectionPath: 'm1.2.1',
        displayName: 'One',
        sourceProfile: 'US',
        children: [],
      },
    ])

    expect(result.map((node) => node.sectionPath)).toEqual([
      'm1.2',
      'm1.2.1',
      'm1.2.a',
      'm1.10',
    ])
  })

  it('treats only m<number> segments as module numbers when sorting sections', () => {
    const result = mapSectionTreeData([
      {
        elementName: 'ma',
        sectionPath: 'ma',
        displayName: 'Letter section',
        sourceProfile: 'US',
        children: [],
      },
      {
        elementName: 'm2',
        sectionPath: 'm2',
        displayName: 'Module 2',
        sourceProfile: 'US',
        children: [],
      },
    ])

    expect(result.map((node) => node.sectionPath)).toEqual(['m2', 'ma'])
  })

  it('normalizes backend whitespace when building fallback section labels', () => {
    const result = mapSectionTreeData([
      {
        elementName: 'm1-2-3',
        sectionPath: 'm1.2.3',
        displayName: '  Labeling  ',
        sourceProfile: 'US',
        children: [],
      },
    ])

    expect(result[0]).toMatchObject({
      sectionPath: 'm1.2.3',
      title: '1.2.3 Labeling',
    })
  })

  it('splits section tree titles into display prefix and label parts', () => {
    expect(getWorkspaceTreeNodeTitleParts({
      nodeType: 'section',
      key: 'm1.2.3',
      sectionPath: 'm1.2.3',
      title: '1.2.3 Labeling',
      canDrop: true,
      hasPlacement: false,
      children: [],
    })).toEqual({
      text: '1.2.3 Labeling',
      prefix: '1.2.3',
      label: 'Labeling',
    })

    expect(getWorkspaceTreeNodeTitleParts({
      nodeType: 'section',
      key: 'm1',
      sectionPath: 'm1',
      title: 'Module 1',
      canDrop: false,
      hasPlacement: false,
      children: [],
    })).toEqual({
      text: 'Module 1',
      prefix: null,
      label: 'Module 1',
    })

    expect(getWorkspaceTreeNodeTitleParts({
      nodeType: 'document',
      key: 'placement:1',
      sectionPath: 'm1.2.3',
      placementId: 'placement-1',
      documentId: 'doc-1',
      title: '1.2.3-labeling.pdf',
      operation: 'New',
      children: [],
    })).toEqual({
      text: '1.2.3-labeling.pdf',
      prefix: null,
      label: '1.2.3-labeling.pdf',
    })
  })

  it('resolves workspace tree node drop capabilities', () => {
    const leafSection = {
      nodeType: 'section' as const,
      key: 'm1.2.3',
      sectionPath: 'm1.2.3',
      title: '1.2.3 Labeling',
      canDrop: true,
      hasPlacement: false,
      children: [],
    }
    const branchSection = {
      ...leafSection,
      key: 'm1.2',
      sectionPath: 'm1.2',
      title: '1.2 Administrative information',
      canDrop: false,
    }
    const documentNode = {
      nodeType: 'document' as const,
      key: 'placement:1',
      sectionPath: 'm1.2.3',
      placementId: 'placement-1',
      documentId: 'doc-1',
      title: 'labeling.pdf',
      operation: 'New',
      children: [] as [],
    }

    expect(getWorkspaceTreeNodeDropCapabilities(leafSection, null)).toEqual({
      isSection: true,
      acceptsPlacementDrop: true,
      acceptsFileDrop: true,
      canDrop: true,
    })
    expect(getWorkspaceTreeNodeDropCapabilities(branchSection, null)).toEqual({
      isSection: true,
      acceptsPlacementDrop: true,
      acceptsFileDrop: false,
      canDrop: false,
    })
    expect(getWorkspaceTreeNodeDropCapabilities(branchSection, 'placement-1')).toMatchObject({
      canDrop: true,
    })
    expect(getWorkspaceTreeNodeDropCapabilities(documentNode, 'placement-1')).toEqual({
      isSection: false,
      acceptsPlacementDrop: false,
      acceptsFileDrop: false,
      canDrop: false,
    })
  })

  it('builds workspace tree node class names from display state', () => {
    expect(buildWorkspaceTreeNodeClassName({
      nodeType: 'section',
      canDrop: true,
      isHovered: true,
      isSelected: true,
      isDragging: false,
    })).toBe('ectd-tree-node ectd-tree-node--section ectd-tree-node--droppable ectd-tree-node--hover ectd-tree-node--selected')

    expect(buildWorkspaceTreeNodeClassName({
      nodeType: 'document',
      canDrop: false,
      isHovered: false,
      isSelected: false,
      isDragging: true,
    })).toBe('ectd-tree-node ectd-tree-node--document ectd-tree-node--dragging')
  })

  it('groups document placements by eCTD section', () => {
    const firstPlacement = {
      id: 'placement-1',
      applicationId: 'app-1',
      sequenceNumber: '0001',
      documentId: 'doc-1',
      ctdSection: 'm1.2.3',
      operation: 'New',
    }
    const secondPlacement = {
      id: 'placement-2',
      applicationId: 'app-1',
      sequenceNumber: '0001',
      documentId: 'doc-2',
      ctdSection: 'm1.2.3',
      operation: 'Replace',
    }
    const thirdPlacement = {
      id: 'placement-3',
      applicationId: 'app-1',
      sequenceNumber: '0001',
      documentId: 'doc-3',
      ctdSection: 'm2.3',
      operation: 'New',
    }

    expect(buildPlacementsBySection([
      firstPlacement,
      thirdPlacement,
      secondPlacement,
    ])).toEqual({
      'm1.2.3': [firstPlacement, secondPlacement],
      'm2.3': [thirdPlacement],
    })
  })

  it('adds mapped files as direct child nodes under the matching section', () => {
    const sectionTree = mapSectionTreeData([
      {
        elementName: 'm1-2-3',
        sectionPath: 'm1.2.3',
        displayName: 'Labeling',
        sourceProfile: 'US',
        children: [],
      },
    ])

    const result = attachDocumentNodes(
      sectionTree,
      [
        {
          id: 'placement-1',
          applicationId: 'app-1',
          sequenceNumber: '0001',
          documentId: 'doc-1',
          ctdSection: 'm1.2.3',
          operation: 'New',
        },
      ],
      {
        'doc-1': {
          id: 'doc-1',
          fileName: 'labeling.pdf',
          storagePath: '/tmp/labeling.pdf',
        },
      },
    )

    expect(result[0].children).toHaveLength(1)
    expect(result[0].children[0]).toMatchObject({
      nodeType: 'document',
      sectionPath: 'm1.2.3',
      title: 'labeling.pdf',
    })
  })

  it('keeps droppable leaf sections droppable after document children are added', () => {
    const sectionTree = mapSectionTreeData([
      {
        elementName: 'm1-2-3',
        sectionPath: 'm1.2.3',
        displayName: 'Labeling',
        sourceProfile: 'US',
        children: [],
      },
    ])

    const result = attachDocumentNodes(
      sectionTree,
      [
        {
          id: 'placement-1',
          applicationId: 'app-1',
          sequenceNumber: '0001',
          documentId: 'doc-1',
          ctdSection: 'm1.2.3',
          operation: 'New',
        },
      ],
      {
        'doc-1': {
          id: 'doc-1',
          fileName: 'labeling.pdf',
          storagePath: '/tmp/labeling.pdf',
        },
      },
    )

    expect(result[0]).toMatchObject({
      nodeType: 'section',
      sectionPath: 'm1.2.3',
      canDrop: true,
    })
  })

  it('does not create synthetic branches for unmatched placements', () => {
    const sectionTree = mapSectionTreeData([
      {
        elementName: 'm1',
        sectionPath: 'm1',
        displayName: 'Module 1',
        sourceProfile: 'US',
        children: [
          {
            elementName: 'm1-2',
            sectionPath: 'm1.2',
            displayName: 'Administrative information',
            sourceProfile: 'US',
            children: [],
          },
        ],
      },
    ])

    const result = attachDocumentNodes(
      sectionTree,
      [
        {
          id: 'placement-1',
          applicationId: 'app-1',
          sequenceNumber: '0001',
          documentId: 'doc-1',
          ctdSection: 'm1.9.9',
          operation: 'New',
        },
      ],
      {
        'doc-1': {
          id: 'doc-1',
          fileName: 'unmatched.pdf',
          storagePath: '/tmp/unmatched.pdf',
        },
      },
    )

    expect(result).toEqual(sectionTree)
  })

  it('sorts multiple document children under a section by file name', () => {
    const sectionTree = mapSectionTreeData([
      {
        elementName: 'm1-2-3',
        sectionPath: 'm1.2.3',
        displayName: 'Labeling',
        sourceProfile: 'US',
        children: [],
      },
    ])

    const result = attachDocumentNodes(
      sectionTree,
      [
        {
          id: 'placement-2',
          applicationId: 'app-1',
          sequenceNumber: '0001',
          documentId: 'doc-2',
          ctdSection: 'm1.2.3',
          operation: 'New',
        },
        {
          id: 'placement-1',
          applicationId: 'app-1',
          sequenceNumber: '0001',
          documentId: 'doc-1',
          ctdSection: 'm1.2.3',
          operation: 'New',
        },
      ],
      {
        'doc-1': {
          id: 'doc-1',
          fileName: 'a-labeling.pdf',
          storagePath: '/tmp/a-labeling.pdf',
        },
        'doc-2': {
          id: 'doc-2',
          fileName: 'z-labeling.pdf',
          storagePath: '/tmp/z-labeling.pdf',
        },
      },
    )

    expect(result[0].children.map((child) => child.title)).toEqual([
      'a-labeling.pdf',
      'z-labeling.pdf',
    ])
  })

  it('finds a selected document node without losing its parent section path', () => {
    const sectionTree = mapSectionTreeData([
      {
        elementName: 'm1',
        sectionPath: 'm1',
        displayName: 'Module 1',
        sourceProfile: 'US',
        children: [
          {
            elementName: 'm1-2-3',
            sectionPath: 'm1.2.3',
            displayName: 'Labeling',
            sourceProfile: 'US',
            children: [],
          },
        ],
      },
    ])

    const result = attachDocumentNodes(
      sectionTree,
      [
        {
          id: 'placement-1',
          applicationId: 'app-1',
          sequenceNumber: '0001',
          documentId: 'doc-1',
          ctdSection: 'm1.2.3',
          operation: 'New',
        },
      ],
      {
        'doc-1': {
          id: 'doc-1',
          fileName: 'labeling.pdf',
          storagePath: '/tmp/labeling.pdf',
        },
      },
    )

    expect(findWorkspaceTreeNode(result, 'placement:placement-1')).toMatchObject({
      nodeType: 'document',
      key: 'placement:placement-1',
      sectionPath: 'm1.2.3',
      title: 'labeling.pdf',
    })
  })

  it('returns undefined for missing workspace tree nodes', () => {
    const sectionTree = mapSectionTreeData([
      {
        elementName: 'm1',
        sectionPath: 'm1',
        displayName: 'Module 1',
        sourceProfile: 'US',
        children: [],
      },
    ])

    expect(findWorkspaceTreeNode(sectionTree, 'missing-node')).toBeUndefined()
  })

  it('prefers the dropped section path when resolving the upload section', () => {
    expect(resolveUploadSection(' m1.1 ', null)).toBe('m1.1')
  })

  it('falls back to the selected section path when the drop target is blank', () => {
    expect(resolveUploadSection('   ', ' m5.3.5.1 ')).toBe('m5.3.5.1')
  })

  it('throws when no valid upload section is available', () => {
    expect(() => resolveUploadSection('', ' ')).toThrow('No valid eCTD section selected for upload')
  })
})
