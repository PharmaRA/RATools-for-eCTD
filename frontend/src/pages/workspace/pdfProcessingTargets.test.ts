import { expect, it } from 'vitest'
import type { DocumentPlacementRecord, DocumentRecord } from '../../workspaceTree'
import { buildProcessingTargets } from './pdfProcessingTargets'

const placement = (id: string, sequenceNumber: string, applicationId = 'app'): DocumentPlacementRecord => ({
  id, documentId: id, applicationId, sequenceNumber, ctdSection: 'm5.3', operation: 'new',
})
const document = (id: string, storagePath: string, fileName: string): DocumentRecord => ({ id, storagePath, fileName, mediaType: 'application/pdf' })

it('resolves same-sequence and historical delivery paths without leaking physical version folders', () => {
  const placements = [placement('source', '0001'), placement('same', '0001'), placement('prior', '0000')]
  const documents = {
    source: document('source', 'C:/workspace/0001/_processed/job/version/0001/m5/study/source.pdf', 'source.pdf'),
    same: document('same', 'C:/workspace/0001/m5/references/target.pdf', 'target.pdf'),
    prior: document('prior', 'C:/workspace/0000/m5/report.pdf', 'report.pdf'),
  }
  expect(buildProcessingTargets(placements[0], documents.source, placements, documents).map(target => ({
    placementId: target.placementId, fileSpecification: target.fileSpecification,
  }))).toEqual([
    { placementId: 'same', fileSpecification: '../references/target.pdf' },
    { placementId: 'prior', fileSpecification: '../../../0000/m5/report.pdf' },
  ])
})

it('excludes other applications, future sequences, lifecycle deletes and non-PDF content', () => {
  const source = placement('source', '0001')
  const placements = [source, placement('foreign', '0000', 'other'), placement('future', '0002'),
    { ...placement('deleted', '0000'), operation: 'delete' }, placement('text', '0001')]
  const documents = Object.fromEntries(placements.map(item => [item.id,
    document(item.id, `C:/workspace/${item.sequenceNumber}/m5/${item.id}.pdf`, `${item.id}.pdf`)]))
  documents.text = { ...documents.text, mediaType: 'text/plain' }
  expect(buildProcessingTargets(source, documents.source, placements, documents)).toEqual([])
})
