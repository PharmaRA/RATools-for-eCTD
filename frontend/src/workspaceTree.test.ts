import { describe, expect, it } from 'vitest'

import { attachDocumentNodes, findWorkspaceTreeNode, mapSectionTreeData } from './workspaceTree'

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
})
