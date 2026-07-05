import { describe, expect, it } from 'vitest'

import { buildSectionSelectionDescriptionItems } from './selectionDetailsDisplay'

describe('selectionDetailsDisplay', () => {
  it('builds section selection description items', () => {
    expect(buildSectionSelectionDescriptionItems({
      sectionPath: 'm3.2.p.1',
      title: 'Drug Substance',
      canDrop: false,
    }, 3)).toEqual([
      { key: 'section', label: 'Section', children: 'm3.2.p.1' },
      { key: 'display', label: 'Display', children: 'Drug Substance' },
      { key: 'leaf-node', label: 'Leaf Node', children: 'No' },
      { key: 'mapped-files', label: 'Mapped Files', children: 3 },
    ])
  })
})
