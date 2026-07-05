import { describe, expect, it } from 'vitest'

import { addSectionExpansionKeys } from './appShared'

describe('appShared tree helpers', () => {
  it('adds section ancestors without duplicating existing expanded keys', () => {
    const keys = addSectionExpansionKeys(['m1', 'm3'], 'm3.2.1')

    expect(keys).toEqual(['m1', 'm3', 'm3.2', 'm3.2.1'])
  })
})
