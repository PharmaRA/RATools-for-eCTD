type SectionSelection = {
  sectionPath: string
  title: string
  canDrop?: boolean | null
}

export const buildSectionSelectionDescriptionItems = (
  section: SectionSelection,
  mappedFileCount: number,
) => [
  { key: 'section', label: '章节', children: section.sectionPath },
  { key: 'display', label: '显示名称', children: section.title },
  { key: 'leaf-node', label: '叶节点', children: section.canDrop ? '是' : '否' },
  { key: 'mapped-files', label: '已映射文件', children: mappedFileCount },
]
