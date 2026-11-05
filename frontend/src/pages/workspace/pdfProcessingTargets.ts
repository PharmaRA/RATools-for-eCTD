import type { DocumentPlacementRecord, DocumentRecord } from '../../workspaceTree'
import type { RemotePdfTarget } from './PdfLinksEditor'
import { buildPublishedHrefPreview, findPublishedSequenceSegmentIndex } from '../publishedHrefPreview'

export function buildProcessingTargets(source: DocumentPlacementRecord, document: DocumentRecord,
  placements: readonly DocumentPlacementRecord[], documents: Record<string, DocumentRecord>): RemotePdfTarget[] {
  const sourceHref = buildPublishedHrefPreview(document.storagePath, source.sequenceNumber, document.fileName)
  const directory = [source.sequenceNumber, ...sourceHref.split('/').slice(0, -1)]
  return placements.flatMap(placement => {
    const target = documents[placement.documentId]
    if (!target || placement.id === source.id || placement.applicationId !== source.applicationId
      || placement.sequenceNumber > source.sequenceNumber || placement.operation.toLowerCase() === 'delete'
      || (target.mediaType ? target.mediaType !== 'application/pdf' : !target.fileName.toLowerCase().endsWith('.pdf'))
      || findPublishedSequenceSegmentIndex(target.storagePath.split(/[\\/]+/).filter(Boolean), placement.sequenceNumber) < 0) return []
    const href = buildPublishedHrefPreview(target.storagePath, placement.sequenceNumber, target.fileName)
    const path = [placement.sequenceNumber, ...href.split('/')]
    let common = 0
    while (common < directory.length && common < path.length && directory[common] === path[common]) common++
    return [{ id: placement.id, placementId: placement.id, versionId: null, namedDestinations: null,
      fileSpecification: [...directory.slice(common).map(() => '..'), ...path.slice(common)].join('/'),
      label: `${placement.sequenceNumber} · ${placement.title || target.fileName} · ${href}` }]
  })
}
