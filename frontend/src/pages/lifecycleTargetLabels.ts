import type { DocumentPlacementRecord, DocumentRecord } from '../workspaceTree'

export const buildLifecycleTargetLabel = (
  candidate: DocumentPlacementRecord,
  documentsById: Record<string, DocumentRecord>,
) => {
  const targetDocument = documentsById[candidate.documentId]
  const title = candidate.title || targetDocument?.fileName || candidate.documentId
  return `${candidate.sequenceNumber} | ${candidate.ctdSection} | ${title} | ${candidate.operation}`
}

export const buildLifecycleTargetOptions = (
  candidates: readonly DocumentPlacementRecord[],
  documentsById: Record<string, DocumentRecord>,
) => candidates.map((candidate) => ({
  value: candidate.id,
  label: buildLifecycleTargetLabel(candidate, documentsById),
}))

export const buildLifecycleTargetListText = (
  candidates: readonly DocumentPlacementRecord[],
  documentsById: Record<string, DocumentRecord>,
) => candidates
  .map((candidate) => buildLifecycleTargetLabel(candidate, documentsById))
  .join('; ')
