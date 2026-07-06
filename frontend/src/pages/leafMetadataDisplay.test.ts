import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import {
  buildLeafPlacementDescriptionItems,
  buildLeafPlacementOperationOptions,
  buildLeafPreviewDescriptionItems,
  formatLeafPreviewOptionalText,
} from './leafMetadataDisplay'

describe('leafMetadataDisplay', () => {
  it('builds leaf placement operation select options', () => {
    expect(buildLeafPlacementOperationOptions()).toEqual([
      { value: 'New', label: 'New' },
      { value: 'Replace', label: 'Replace' },
      { value: 'Delete', label: 'Delete' },
      { value: 'Append', label: 'Append' },
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

    expect(items[0]).toEqual({ key: 'placement-id', label: 'Placement ID', children: 'placement-1' })
    expect(items.map(({ key, label }) => ({ key, label }))).toEqual([
      { key: 'placement-id', label: 'Placement ID' },
      { key: 'ectd-section', label: 'eCTD Section' },
      { key: 'operation', label: 'Operation' },
      { key: 'storage-path', label: 'Storage Path' },
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
      { key: 'mime-type', label: 'Mime Type' },
      { key: 'checksum-type', label: 'Checksum Type' },
      { key: 'checksum', label: 'Checksum' },
      { key: 'source-file-name', label: 'Source File Name' },
      { key: 'resulting-file-name', label: 'Resulting File Name' },
      { key: 'storage-path', label: 'Storage Path' },
    ])
    expect(items[0]).toEqual({ key: 'operation', label: 'operation', children: 'Replace' })
    expect(items[4]).toEqual({ key: 'mime-type', label: 'Mime Type', children: '-' })
    expect(items[5]).toEqual({ key: 'checksum-type', label: 'Checksum Type', children: 'md5' })
    expect(items[8]).toEqual({ key: 'resulting-file-name', label: 'Resulting File Name', children: '-' })

    for (const item of [items[2], items[3], items[6], items[9]]) {
      expect(isValidElement(item.children)).toBe(true)
      expect((item.children as ReactElement<{ className: string }>).props.className).toBe('text-xs break-all')
    }
  })
})
