import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import { renderArtifactExistsStatus } from './artifactDisplay'

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
})
