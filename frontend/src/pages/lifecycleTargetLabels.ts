import type { DocumentPlacementRecord, DocumentRecord } from '../workspaceTree'

export const buildLifecycleTargetLabel = (
  candidate: DocumentPlacementRecord,
  documentsById: Record<string, DocumentRecord>,
) => {
  const targetDocument = documentsById[candidate.documentId]
  const title = candidate.title || targetDocument?.fileName || candidate.documentId
  return `${candidate.sequenceNumber} | ${candidate.ctdSection} | ${title} | ${candidate.operation}`
}
