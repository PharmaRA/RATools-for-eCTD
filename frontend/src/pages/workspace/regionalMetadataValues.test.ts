import { describe, expect, it } from 'vitest'
import { regionalFieldPath, regionalValueAt, withRegionalValue } from './regionalMetadataValues'

describe('regional draft value edits', () => {
  it('changes one country without erasing retained procedure fields or another envelope', () => {
    const draft = { schemaVersion: 1, regionKey: 'EU', eu: { envelopes: [
      { id: 'first', country: 'de', procedure: 'national', submissionMode: 'grouping', inns: [{ id: 'inn-a', value: 'A' }] },
      { id: 'second', country: 'fr', procedure: 'centralised', applicant: 'Other applicant' },
    ] } }
    const edited = withRegionalValue(draft, ['eu', 'envelopes', 0, 'country'], 'fr')
    expect(edited).toEqual({ schemaVersion: 1, regionKey: 'EU', eu: { envelopes: [
      { id: 'first', country: 'fr', procedure: 'national', submissionMode: 'grouping', inns: [{ id: 'inn-a', value: 'A' }] },
      { id: 'second', country: 'fr', procedure: 'centralised', applicant: 'Other applicant' },
    ] } })
    expect(draft.eu.envelopes[0].country).toBe('de')
  })

  it('keeps equal-valued entries distinct through a reorder and subsequent edit', () => {
    const draft = { us: { contacts: [{ id: 'a', name: 'Same' }, { id: 'b', name: 'Same' }] } }
    const contacts = regionalValueAt(draft, ['us', 'contacts']) as typeof draft.us.contacts
    const reordered = withRegionalValue(draft, ['us', 'contacts'], [contacts[1], contacts[0]])
    const edited = withRegionalValue(reordered, ['us', 'contacts', 0, 'name'], 'Changed B')
    expect(regionalValueAt(edited, ['us', 'contacts'])).toEqual([{ id: 'b', name: 'Changed B' }, { id: 'a', name: 'Same' }])
    expect(draft.us.contacts).toEqual([{ id: 'a', name: 'Same' }, { id: 'b', name: 'Same' }])
  })

  it('distinguishes explicit clearing from an absent field and preserves sibling context', () => {
    const draft = { documentContexts: [{ placementId: 'a', country: 'de', formType: 'old' }, { placementId: 'b', country: 'fr' }] }
    const cleared = withRegionalValue(draft, ['documentContexts', 0, 'formType'], null)
    expect(regionalValueAt(cleared, ['documentContexts', 0, 'formType'])).toBeNull()
    expect(regionalValueAt(cleared, ['documentContexts', 1, 'formType'])).toBeUndefined()
    expect(regionalValueAt(cleared, ['documentContexts', 1, 'country'])).toBe('fr')
  })

  it('uses diagnostic-compatible paths for nested collection inputs', () => {
    expect(regionalFieldPath(['eu', 'envelopes', 1, 'inventedNames', 2, 'value'])).toBe('eu.envelopes[1].inventedNames[2].value')
    expect(regionalFieldPath(['nodeContexts', 0, 'language'])).toBe('nodeContexts[0].language')
  })
})
