import { describe, expect, it } from 'vitest'

import { getLifecycleTargetCandidates } from './lifecycleTargetCandidates'
import type { DocumentPlacementRecord, DocumentRecord } from '../../workspaceTree'

const placement = (overrides: Partial<DocumentPlacementRecord>): DocumentPlacementRecord => ({
  id: 'placement',
  applicationId: 'app-1',
  sequenceNumber: '0000',
  documentId: 'doc-1',
  ctdSection: '1.2',
  operation: 'New',
  ...overrides,
})

const documentsById: Record<string, DocumentRecord> = {
  'doc-1': { id: 'doc-1', fileName: 'one.pdf', storagePath: '/one.pdf' },
  'doc-2': { id: 'doc-2', fileName: 'two.pdf', storagePath: '/two.pdf' },
  'doc-3': { id: 'doc-3', fileName: 'three.pdf', storagePath: '/three.pdf' },
}

describe('getLifecycleTargetCandidates', () => {
  it('keeps same-app same-section historical placements with documents', () => {
    const current = placement({ id: 'current', sequenceNumber: '0010', documentId: 'doc-3' })
    const historicalSameSection = placement({ id: 'historical', sequenceNumber: '0009', documentId: 'doc-1' })
    const historicalNumeric = placement({ id: 'numeric', sequenceNumber: '2', documentId: 'doc-2' })

    const candidates = getLifecycleTargetCandidates(
      [
        historicalSameSection,
        historicalNumeric,
        placement({ id: 'future', sequenceNumber: '0011', documentId: 'doc-1' }),
        placement({ id: 'same-sequence', sequenceNumber: '0010', documentId: 'doc-1' }),
        placement({ id: 'other-section', sequenceNumber: '0009', ctdSection: '1.3', documentId: 'doc-1' }),
        placement({ id: 'other-app', applicationId: 'app-2', sequenceNumber: '0009', documentId: 'doc-1' }),
        placement({ id: 'missing-document', sequenceNumber: '0009', documentId: 'missing-doc' }),
      ],
      current,
      documentsById,
    )

    expect(candidates.map((candidate) => candidate.id)).toEqual(['historical', 'numeric'])
  })
})
