type SectionSelection = {
  sectionPath: string
  title: string
  canDrop?: boolean | null
}

export const buildSectionSelectionDescriptionItems = (
  section: SectionSelection,
  mappedFileCount: number,
) => [
  { key: 'section', label: 'Section', children: section.sectionPath },
  { key: 'display', label: 'Display', children: section.title },
  { key: 'leaf-node', label: 'Leaf Node', children: section.canDrop ? 'Yes' : 'No' },
  { key: 'mapped-files', label: 'Mapped Files', children: mappedFileCount },
]
