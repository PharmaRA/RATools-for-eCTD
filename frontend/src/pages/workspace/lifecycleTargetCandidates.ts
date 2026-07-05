import type { DocumentPlacementRecord, DocumentRecord } from '../../workspaceTree'

const compareSequenceNumbers = (left: string, right: string) => {
  const leftNumber = Number(left)
  const rightNumber = Number(right)

  if (Number.isFinite(leftNumber) && Number.isFinite(rightNumber)) {
    return leftNumber - rightNumber
  }

  return left.localeCompare(right)
}

export const getLifecycleTargetCandidates = (
  placements: DocumentPlacementRecord[],
  selectedPlacement: DocumentPlacementRecord,
  documentsById: Record<string, DocumentRecord>,
) => {
  const candidates: DocumentPlacementRecord[] = []

  for (const placement of placements) {
    if (placement.applicationId !== selectedPlacement.applicationId) {
      continue
    }

    if (placement.ctdSection !== selectedPlacement.ctdSection) {
      continue
    }

    if (compareSequenceNumbers(placement.sequenceNumber, selectedPlacement.sequenceNumber) >= 0) {
      continue
    }

    if (!documentsById[placement.documentId]) {
      continue
    }

    candidates.push(placement)
  }

  return candidates
}
