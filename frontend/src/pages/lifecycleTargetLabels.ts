import type { DocumentPlacementRecord, DocumentRecord } from '../workspaceTree'

export const buildLifecycleTargetLabel = (
  candidate: DocumentPlacementRecord,
  documentsById: Record<string, DocumentRecord>,
  contexts?: Record<string, string>,
) => {
  const targetDocument = documentsById[candidate.documentId]
  const title = candidate.title || targetDocument?.fileName || candidate.documentId
  const context = candidate.nodeInstanceId ? contexts?.[candidate.nodeInstanceId] : undefined
  return `${candidate.sequenceNumber} | ${candidate.ctdSection}${context ? ` | ${context}` : ''} | ${title} | ${candidate.operation}`
}

export const buildLifecycleTargetOptions = (
  candidates: readonly DocumentPlacementRecord[],
  documentsById: Record<string, DocumentRecord>,
  contexts?: Record<string, string>,
) => candidates.map((candidate) => ({
  value: candidate.id,
  label: buildLifecycleTargetLabel(candidate, documentsById, contexts),
}))

export const buildLifecycleTargetListText = (
  candidates: readonly DocumentPlacementRecord[],
  documentsById: Record<string, DocumentRecord>,
) => candidates
  .map((candidate) => buildLifecycleTargetLabel(candidate, documentsById))
  .join('; ')
