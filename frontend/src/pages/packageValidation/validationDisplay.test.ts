import { describe, expect, it } from 'vitest'
import type { ValidationFinding } from '../../api/generated'
import type { ValidationEvidence } from '../../packageValidationActions'
import { findingSources } from './validationDisplay'

export const evidence: ValidationEvidence = {
  inputId: 'target', comparisonInputId: 'zip', history: [{ inputId: 'history', profileSnapshotId: 'profile' }],
  ruleCatalog: { rules: [] }, historyBaseline: { entries: [{ sequenceNumber: '0000', sourceId: 'history', trustStatus: 'Unverified', contentDigest: 'old' }] },
}
export const finding: ValidationFinding = {
  ruleId: 'XML-DTD', checkStatus: 'Fail', severity: 'Error', code: 'XML_INVALID', message: 'Invalid node', expected: null, actual: null, recommendedAction: null,
  location: { sequenceNumber: '0001', logicalPath: '0001/index.xml', backboneRelativePath: 'index.xml', leafId: 'leaf', nodePath: '/ectd', fieldPath: null, line: 1, column: 1, files: null },
}

describe('independent package finding addresses', () => {
  it('uses an explicitly selected history, not a business document or same-name target', () => {
    expect(findingSources({ ...finding, location: { ...finding.location, sequenceNumber: '0000', logicalPath: '0000/index.xml' } }, evidence, '0001'))
      .toEqual([{ inputId: 'history', path: '0000/index.xml', isSurrounding: false }])
  })
  it('keeps two byte-mismatch sources distinct', () => {
    const value = { ...finding, ruleId: 'ZIP-PARITY', location: { ...finding.location, files: [
      { sourceId: 'target', logicalPath: '0001/a.pdf', isSurrounding: false },
      { sourceId: 'zip', logicalPath: '0001/a.pdf', isSurrounding: false },
    ] } }
    expect(findingSources(value, evidence, '0001').map(item => item.inputId)).toEqual(['target', 'zip'])
  })
  it('locates a ZIP-only file in the comparison input', () => {
    const value = { ...finding, ruleId: 'ZIP-PARITY', location: { ...finding.location, files: [
      { sourceId: 'zip', logicalPath: '0001/zip-only.txt', isSurrounding: false },
    ] } }
    expect(findingSources(value, evidence, '0001')).toEqual([{ inputId: 'zip', path: '0001/zip-only.txt', isSurrounding: false }])
  })
  it('preserves the surrounding address space even when its spelling matches a selected file', () => {
    const value = { ...finding, code: 'SURROUNDING_INPUT_FILE', location: { ...finding.location, files: [
      { sourceId: 'target', logicalPath: '0001/index.xml', isSurrounding: true },
    ] } }
    expect(findingSources(value, evidence, '0001')).toEqual([{ inputId: 'target', path: '0001/index.xml', isSurrounding: true }])
  })
  it('does not guess provenance missing from legacy surrounding or parity reports', () => {
    expect(findingSources({ ...finding, code: 'SURROUNDING_INPUT_FILE' }, evidence, '0001')).toEqual([])
    expect(findingSources({ ...finding, ruleId: 'ZIP-PARITY' }, evidence, '0001')).toEqual([])
  })
  it('rejects unselected sources and respects explicit non-file locations', () => {
    const foreign = { ...finding, location: { ...finding.location, files: [{ sourceId: 'other', logicalPath: '0001/index.xml', isSurrounding: false }] } }
    expect(findingSources(foreign, evidence, '0001')).toEqual([])
    expect(findingSources({ ...finding, location: { ...finding.location, files: [] } }, evidence, '0001')).toEqual([])
  })
  it('rejects disagreement between sequence identity and logical path', () => {
    expect(findingSources({ ...finding, location: { ...finding.location, logicalPath: '0000/index.xml' } }, evidence, '0001')).toEqual([])
  })
})
