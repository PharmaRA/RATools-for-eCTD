import { describe, expect, it } from 'vitest'
import type { FinalizedSequence, PackageRunSummary, ReleaseManifest, StandardsSnapshotView, ValidationReport } from './api/generated'
import { buildDeliveryBody, buildFinalizationBody } from './finalizationActions'

const binding = {
  inputManifestDigest: 'a'.repeat(64), historyBaselineDigest: 'b'.repeat(64),
  profileSnapshotId: 'eu-profile', rulesDigest: 'c'.repeat(64), engineVersion: 'test-engine',
  validationMode: 'Development' as const, resourceLimitsDigest: 'd'.repeat(64),
  standardsSnapshotId: 'standards-1', selectionDate: '2026-11-07',
}
const run = { id: 'run-1', applicationId: 'app-1', sequenceNumber: '0000', status: 'Completed',
  reportAvailable: true, binding } as PackageRunSummary
const report = { runStatus: 'Completed', executionCompleted: true, binding: { ...binding },
  isReadyForFinalization: false, blockingReasons: ['ProfileNotQualified'] } as ValidationReport
const standards = { id: 'standards-1', ruleProfileAlias: 'eu-profile', ichEctdVersion: '3.2.2',
  regionalModule1Version: '3.1.1' } as StandardsSnapshotView

describe('finalization request evidence', () => {
  const prepare = (r = run, p = report, s = standards, revision: number | undefined = 7) =>
    buildFinalizationBody('app-1', '0000', revision, r, p, s, 'fixed-attempt-key')

  it('uses the selected completed run and exact archived standards without typed digests', () => {
    expect(prepare()).toEqual({ expectedRevision: 7, packageValidationRunId: 'run-1',
      standardsProfileSnapshotId: 'eu-profile', ichEctdVersion: '3.2.2', regionalModule1Version: '3.1.1',
      historyBaselineDigest: binding.historyBaselineDigest, idempotencyKey: 'fixed-attempt-key' })
  })
  it.each([{ applicationId: 'other' }, { sequenceNumber: '0001' }])('rejects another workspace %j', change => {
    expect(() => prepare({ ...run, ...change })).toThrow('申请或序列')
  })
  it.each(['Running', 'Failed', 'InputChanged'] as const)('rejects %s runs', status => {
    expect(() => prepare({ ...run, status })).toThrow('完成')
  })
  it('requires an available completed report', () => {
    expect(() => prepare({ ...run, reportAvailable: false })).toThrow('报告')
    expect(() => prepare(run, { ...report, executionCompleted: false })).toThrow('报告')
  })
  it.each(['inputManifestDigest', 'historyBaselineDigest', 'resourceLimitsDigest', 'standardsSnapshotId'] as const)(
    'rejects changed report %s', field => {
      expect(() => prepare(run, { ...report, binding: { ...binding, [field]: 'changed' } })).toThrow('绑定')
    })
  it('rejects a different standards snapshot or alias', () => {
    expect(() => prepare(run, report, { ...standards, id: 'other' })).toThrow('标准')
    expect(() => prepare(run, report, { ...standards, ruleProfileAlias: 'other' })).toThrow('标准')
  })
  it('requires revalidation of legacy runs without an exact standards snapshot', () => {
    const legacy = { ...binding, standardsSnapshotId: null }
    expect(() => prepare({ ...run, binding: legacy }, { ...report, binding: legacy })).toThrow('标准')
  })
  it('preserves formal readiness blocking reasons', () => {
    const formal = { ...binding, validationMode: 'Formal' as const }
    expect(() => prepare({ ...run, binding: formal }, { ...report, binding: formal })).toThrow('ProfileNotQualified')
  })
  it('accepts a ready Formal report without altering its evidence', () => {
    const formal = { ...binding, validationMode: 'Formal' as const }
    expect(prepare({ ...run, binding: formal }, { ...report, binding: formal, isReadyForFinalization: true }))
      .toMatchObject({ packageValidationRunId: run.id, expectedRevision: 7 })
  })
  it.each([undefined, -1, NaN, 1.5, Number.MAX_SAFE_INTEGER + 1])('refuses missing or unsafe revision %s', revision => {
    expect(() => buildFinalizationBody('app-1', '0000', revision, run, report, standards, 'key')).toThrow('修订')
  })
})

describe('delivery request evidence', () => {
  const snapshot = { id: 'snapshot-1', applicationId: 'app-1', sequenceNumber: '0000',
    sourceWorkspaceRevision: 7, manifestDigest: 'e'.repeat(64), validationMode: 'Formal',
    isReadyForFinalization: true } as FinalizedSequence
  const manifest = { digest: snapshot.manifestDigest, applicationId: 'app-1', sequenceNumber: '0000',
    sourceWorkspaceRevision: 7, files: [{ relativePath: 'delivery-package.zip', kind: 3, length: 17,
      sha256: 'f'.repeat(64), md5: '0'.repeat(32) }] } as ReleaseManifest
  const fields = { channel: 'portal', externalReference: 'receipt-123', note: null, recordedBy: 'operator statement' }
  it('binds the manifest ZIP bytes instead of the manifest digest', () => {
    expect(buildDeliveryBody(snapshot, manifest, fields)).toEqual({ ...fields, packageSha256: 'f'.repeat(64) })
  })
  it.each([{ validationMode: 'Development' as const }, { isReadyForFinalization: false }])('blocks non-formal/non-ready snapshots', change => {
    expect(() => buildDeliveryBody({ ...snapshot, ...change }, manifest, fields)).toThrow('正式')
  })
  it.each([{ digest: 'wrong' }, { applicationId: 'other' }, { sequenceNumber: '0001' }, { sourceWorkspaceRevision: 8 }])(
    'rejects a manifest from a different snapshot %j', change => {
      expect(() => buildDeliveryBody(snapshot, { ...manifest, ...change }, fields)).toThrow('快照')
    })
  it('rejects absent, duplicated or malformed delivery ZIP evidence', () => {
    expect(() => buildDeliveryBody(snapshot, { ...manifest, files: [] }, fields)).toThrow('ZIP')
    expect(() => buildDeliveryBody(snapshot, { ...manifest, files: [...manifest.files, ...manifest.files] }, fields)).toThrow('ZIP')
    expect(() => buildDeliveryBody(snapshot, { ...manifest, files: [{ ...manifest.files[0], sha256: 'wrong' }] }, fields)).toThrow('ZIP')
  })
})
