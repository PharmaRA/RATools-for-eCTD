export type LifecycleMatchLike = {
  resultCode: string
}

export type LifecycleSummary = {
  matchedCount: number
  replaceTargetNotFoundCount: number
  deleteTargetNotFoundCount: number
  appendTargetNotFoundCount: number
  ambiguousCount: number
  currentSequenceCount: number
  issueCount: number
}

export const summarizeLifecycleMatches = (matches: LifecycleMatchLike[]): LifecycleSummary => {
  const summary: LifecycleSummary = {
    matchedCount: 0,
    replaceTargetNotFoundCount: 0,
    deleteTargetNotFoundCount: 0,
    appendTargetNotFoundCount: 0,
    ambiguousCount: 0,
    currentSequenceCount: 0,
    issueCount: 0,
  }

  for (const match of matches) {
    switch (match.resultCode) {
      case 'MATCHED':
        summary.matchedCount += 1
        break
      case 'REPLACE_TARGET_NOT_FOUND':
        summary.replaceTargetNotFoundCount += 1
        summary.issueCount += 1
        break
      case 'DELETE_TARGET_NOT_FOUND':
        summary.deleteTargetNotFoundCount += 1
        summary.issueCount += 1
        break
      case 'APPEND_TARGET_NOT_FOUND':
        summary.appendTargetNotFoundCount += 1
        summary.issueCount += 1
        break
      case 'LIFECYCLE_TARGET_AMBIGUOUS':
        summary.ambiguousCount += 1
        summary.issueCount += 1
        break
      case 'LIFECYCLE_TARGET_IN_CURRENT_SEQUENCE':
        summary.currentSequenceCount += 1
        summary.issueCount += 1
        break
      default:
        summary.issueCount += 1
        break
    }
  }

  return summary
}
