import { describe, expect, it } from 'vitest'

import { buildSectionSelectionDescriptionItems } from './selectionDetailsDisplay'

describe('selectionDetailsDisplay', () => {
  it('builds section selection description items', () => {
    expect(buildSectionSelectionDescriptionItems({
      sectionPath: 'm3.2.p.1',
      title: 'Drug Substance',
      canDrop: false,
    }, 3)).toEqual([
      { key: 'section', label: '章节', children: 'm3.2.p.1' },
      { key: 'display', label: '显示名称', children: 'Drug Substance' },
      { key: 'leaf-node', label: '叶节点', children: '否' },
      { key: 'mapped-files', label: '已映射文件', children: 3 },
    ])
  })
})
