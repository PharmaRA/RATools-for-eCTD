import { describe, expect, it } from 'vitest'

import { buildLifecycleTargetLabel, buildLifecycleTargetListText, buildLifecycleTargetOptions } from './lifecycleTargetLabels'
import type { DocumentPlacementRecord, DocumentRecord } from '../workspaceTree'

const placement = (overrides: Partial<DocumentPlacementRecord>): DocumentPlacementRecord => ({
  id: 'placement-1',
  applicationId: 'app-1',
  sequenceNumber: '0001',
  documentId: 'doc-1',
  ctdSection: '1.2',
  operation: 'Replace',
  ...overrides,
})

describe('buildLifecycleTargetLabel', () => {
  it('formats lifecycle target labels with the best available title', () => {
    const documentsById: Record<string, DocumentRecord> = {
      'doc-1': { id: 'doc-1', fileName: 'protocol.pdf', storagePath: '/0001/protocol.pdf' },
    }

    expect(buildLifecycleTargetLabel(placement({ title: 'Protocol' }), documentsById)).toBe('0001 | 1.2 | Protocol | Replace')
    expect(buildLifecycleTargetLabel(placement({ title: '', operation: 'Append' }), documentsById)).toBe('0001 | 1.2 | protocol.pdf | Append')
    expect(buildLifecycleTargetLabel(placement({ title: '', documentId: 'missing-doc', operation: 'Delete' }), documentsById)).toBe('0001 | 1.2 | missing-doc | Delete')
  })

  it('builds lifecycle target select options', () => {
    const documentsById: Record<string, DocumentRecord> = {
      'doc-1': { id: 'doc-1', fileName: 'protocol.pdf', storagePath: '/0001/protocol.pdf' },
    }

    expect(buildLifecycleTargetOptions([
      placement({ id: 'target-1', title: 'Protocol' }),
      placement({ id: 'target-2', title: '', operation: 'Append' }),
    ], documentsById)).toEqual([
      { value: 'target-1', label: '0001 | 1.2 | Protocol | Replace' },
      { value: 'target-2', label: '0001 | 1.2 | protocol.pdf | Append' },
    ])
  })

  it('builds lifecycle target list text', () => {
    const documentsById: Record<string, DocumentRecord> = {
      'doc-1': { id: 'doc-1', fileName: 'protocol.pdf', storagePath: '/0001/protocol.pdf' },
    }

    expect(buildLifecycleTargetListText([
      placement({ title: 'Protocol' }),
      placement({ title: '', operation: 'Append' }),
    ], documentsById)).toBe('0001 | 1.2 | Protocol | Replace; 0001 | 1.2 | protocol.pdf | Append')
  })
})
