export const readinessSortOptions = ['blocked-first', 'ready-first'] as const

export type ReadinessSort = typeof readinessSortOptions[number]

export const getPublishHistoryEntriesFromResponse = <TEntry>(
  response?: { entries?: TEntry[] | null } | null,
): TEntry[] => response?.entries || []

type PublishHistorySortableEntry = {
  publishReadiness?: {
    status?: string | null
  } | null
}

export const isReadinessSort = (value: string | null): value is ReadinessSort => {
  return !!value && (readinessSortOptions as readonly string[]).includes(value)
}

export const getReadinessSortRank = (readiness?: { status?: string | null } | null) => {
  const status = readiness?.status?.toLowerCase()
  if (status === 'blocked') return 0
  if (!status || status === 'unknown') return 1
  if (status === 'ready') return 2
  return 3
}

export const sortPublishHistoryEntries = <TEntry extends PublishHistorySortableEntry>(
  entries: TEntry[],
  readinessSort: ReadinessSort | null,
) => {
  const sortedEntries = [...entries]
  if (!readinessSort) return sortedEntries

  return sortedEntries.sort((left, right) => {
    const leftRank = getReadinessSortRank(left.publishReadiness)
    const rightRank = getReadinessSortRank(right.publishReadiness)
    if (leftRank === rightRank) return 0

    return readinessSort === 'blocked-first'
      ? leftRank - rightRank
      : rightRank - leftRank
  })
}
