import { describe, expect, it } from 'vitest'

describe('test console warning gate', () => {
  it('fails on an unexpected console warning', () => {
    expect(() => console.warn('new warning')).toThrowError(
      'Unexpected console.warn: new warning',
    )
  })

  it('allows a registered console warning', () => {
    expect(() => console.warn('React Router Future Flag Warning: registered')).not.toThrow()
  })
})
