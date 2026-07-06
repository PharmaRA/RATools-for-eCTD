import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import {
  getEvidenceFindingSeverityTagColor,
  renderEvidenceFindingSeverityStatus,
} from './findingSeverityDisplay'

describe('findingSeverityDisplay', () => {
  it.each([
    ['Error', 'red'],
    ['Warning', 'orange'],
    ['Info', 'orange'],
  ] as const)('maps evidence finding severity %s to tag color %s', (severity, color) => {
    expect(getEvidenceFindingSeverityTagColor(severity)).toBe(color)
  })

  it('renders evidence finding severity as a colored tag', () => {
    const element = renderEvidenceFindingSeverityStatus('Warning')

    expect(isValidElement(element)).toBe(true)
    expect((element as ReactElement<{ color: string; children: string }>).props.color).toBe('orange')
    expect((element as ReactElement<{ color: string; children: string }>).props.children).toBe('Warning')
  })
})
