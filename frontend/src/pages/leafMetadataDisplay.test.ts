import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import { formatOptionalText } from './appShared'
import {
  buildLeafPlacementDescriptionItems,
  buildLeafPlacementOperationOptions,
  buildLeafPreviewDescriptionItems,
  formatLeafPreviewOptionalText,
  renderLeafBreakAllText,
} from './leafMetadataDisplay'

describe('leafMetadataDisplay', () => {
  it('builds leaf placement operation select options', () => {
    expect(buildLeafPlacementOperationOptions()).toEqual([
      { value: 'New', label: '新建' },
      { value: 'Replace', label: '替换' },
      { value: 'Delete', label: '删除' },
      { value: 'Append', label: '追加' },
    ])
  })

  it('builds leaf placement description items', () => {
    const items = buildLeafPlacementDescriptionItems({
      id: 'placement-1',
      ctdSection: 'm3.2.p.1',
      operation: 'Replace',
    }, {
      storagePath: 'm3/32p1/file.pdf',
    })

    expect(items[0]).toEqual({ key: 'placement-id', label: '映射 ID', children: 'placement-1' })
    expect(items.map(({ key, label }) => ({ key, label }))).toEqual([
      { key: 'placement-id', label: '映射 ID' },
      { key: 'ectd-section', label: 'eCTD 章节' },
      { key: 'operation', label: '操作类型' },
      { key: 'storage-path', label: '存储路径' },
    ])

    const sectionTag = items[1].children
    expect(isValidElement(sectionTag)).toBe(true)
    expect((sectionTag as ReactElement<{ children: string }>).props.children).toBe('m3.2.p.1')

    const operationTag = items[2].children
    expect(isValidElement(operationTag)).toBe(true)
    expect((operationTag as ReactElement<{ color: string; children: string }>).props.color).toBe('blue')
    expect((operationTag as ReactElement<{ color: string; children: string }>).props.children).toBe('Replace')

    const storagePath = items[3].children
    expect(isValidElement(storagePath)).toBe(true)
    expect((storagePath as ReactElement<{ className: string; children: string }>).props.className).toBe('text-xs break-all')
    expect((storagePath as ReactElement<{ className: string; children: string }>).props.children).toBe('m3/32p1/file.pdf')
  })

  it('formats optional leaf preview text with a placeholder', () => {
    expect(formatLeafPreviewOptionalText('application/pdf')).toBe('application/pdf')
    expect(formatLeafPreviewOptionalText('')).toBe('-')
    expect(formatLeafPreviewOptionalText(null)).toBe('-')
    expect(formatLeafPreviewOptionalText(undefined)).toBe('-')
  })

  it('shares the optional text formatter for leaf preview text', () => {
    expect(formatLeafPreviewOptionalText).toBe(formatOptionalText)
  })

  it('renders long leaf metadata text with break-all styling', () => {
    const element = renderLeafBreakAllText('m3/us/0001/file.pdf')

    expect(isValidElement(element)).toBe(true)
    expect((element as ReactElement<{ className: string; children?: string }>).props.className).toBe('text-xs break-all')
    expect((element as ReactElement<{ className: string; children?: string }>).props.children).toBe('m3/us/0001/file.pdf')
  })

  it('builds leaf preview description items', () => {
    const items = buildLeafPreviewDescriptionItems({
      operation: 'Replace',
      title: 'Drug Substance',
      href: 'm3/us/0001/file.pdf',
      modifiedFileHref: 'm3/us/0000/file.pdf',
      mediaType: '',
      sourceFileName: 'source.pdf',
      revisedFileName: '',
      storagePath: 'm3/source.pdf',
    })

    expect(items.map(({ key, label }) => ({ key, label }))).toEqual([
      { key: 'operation', label: 'operation' },
      { key: 'title', label: 'title' },
      { key: 'href', label: 'xlink:href' },
      { key: 'modified-file', label: 'modified-file' },
      { key: 'mime-type', label: 'MIME 类型' },
      { key: 'checksum-type', label: '校验和类型' },
      { key: 'checksum', label: '校验和' },
      { key: 'source-file-name', label: '源文件名' },
      { key: 'resulting-file-name', label: '生成文件名' },
      { key: 'storage-path', label: '存储路径' },
    ])
    expect(items[0]).toEqual({ key: 'operation', label: 'operation', children: 'Replace' })
    expect(items[4]).toEqual({ key: 'mime-type', label: 'MIME 类型', children: '-' })
    expect(items[5]).toEqual({ key: 'checksum-type', label: '校验和类型', children: 'md5' })
    expect(items[8]).toEqual({ key: 'resulting-file-name', label: '生成文件名', children: '-' })

    for (const item of [items[2], items[3], items[6], items[9]]) {
      expect(isValidElement(item.children)).toBe(true)
      expect((item.children as ReactElement<{ className: string }>).props.className).toBe('text-xs break-all')
    }
  })
})
