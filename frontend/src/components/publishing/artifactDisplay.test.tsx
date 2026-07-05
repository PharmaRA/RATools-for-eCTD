import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import {
  buildArtifactColumns,
  renderArtifactExistsStatus,
} from './artifactDisplay'

describe('artifactDisplay', () => {
  it.each([
    [true, 'green', 'Exists'],
    [false, 'red', 'Missing'],
    [undefined, 'red', 'Missing'],
  ] as const)('renders artifact exists status %s', (exists, color, label) => {
    const element = renderArtifactExistsStatus(exists)

    expect(isValidElement(element)).toBe(true)
    expect((element as ReactElement<{ color: string; children: string }>).props.color).toBe(color)
    expect((element as ReactElement<{ color: string; children: string }>).props.children).toBe(label)
  })

  it('builds artifact table columns', () => {
    const columns = buildArtifactColumns('job-1')

    expect(columns.map(({ title, dataIndex, key }) => ({ title, dataIndex, key }))).toEqual([
      { title: 'Name', dataIndex: 'name', key: 'name' },
      { title: 'Status', dataIndex: 'exists', key: 'exists' },
      { title: 'Size', dataIndex: 'sizeBytes', key: 'size' },
      { title: 'Type', dataIndex: 'contentType', key: 'type' },
      { title: 'Action', dataIndex: undefined, key: 'action' },
    ])

    const nameElement = (columns[0] as { render: (value: string) => unknown }).render('BackboneXml')
    expect(isValidElement(nameElement)).toBe(true)
    expect((nameElement as ReactElement<{ children: string }>).props.children).toBe('BackboneXml')

    expect((columns[2] as { render: (value: number) => unknown }).render(1536)).toBe('1.5 KB')

    const actionElement = (columns[4] as { render: (_: unknown, record: { name: string; exists: boolean }) => unknown })
      .render(null, { name: 'PublishReport', exists: true })
    expect(isValidElement(actionElement)).toBe(true)
    expect((actionElement as ReactElement<{ href: string; target: string; download: boolean }>).props.href)
      .toBe('/api/publish-jobs/job-1/artifacts/PublishReport/download')
    expect((actionElement as ReactElement<{ href: string; target: string; download: boolean }>).props.target).toBe('_blank')
    expect((actionElement as ReactElement<{ href: string; target: string; download: boolean }>).props.download).toBe(true)

    const unavailableElement = (columns[4] as { render: (_: unknown, record: { name: string; exists: boolean }) => unknown })
      .render(null, { name: 'PackageZip', exists: false })
    expect(isValidElement(unavailableElement)).toBe(true)
    expect((unavailableElement as ReactElement<{ className: string; children: string }>).props.className).toBe('text-gray-400')
    expect((unavailableElement as ReactElement<{ className: string; children: string }>).props.children).toBe('Unavailable')
  })
})
