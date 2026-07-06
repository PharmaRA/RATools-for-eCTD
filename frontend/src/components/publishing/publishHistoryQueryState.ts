import { isReadinessSort, type ReadinessSort } from './publishHistorySorting'

export type PublishHistoryFilterValues = {
  sequenceNumber?: string
  status?: string
  readinessStatus?: string
  readinessSort?: ReadinessSort | 'default' | null
}

export const normalizePublishHistoryReadinessSort = (value?: string | null): ReadinessSort | null => {
  const sortableValue = value ?? null
  return isReadinessSort(sortableValue) ? sortableValue : null
}

export const getPublishHistoryInitialQueryState = (search: string) => {
  const params = new URLSearchParams(search)
  const readinessSort = normalizePublishHistoryReadinessSort(params.get('publishReadinessSort'))

  return {
    formValues: {
      sequenceNumber: params.get('publishSequenceNumber') || undefined,
      status: params.get('publishStatus') || undefined,
      readinessStatus: params.get('publishReadinessStatus') || undefined,
      readinessSort: readinessSort || undefined,
    },
    readinessSort,
  }
}

export const buildPublishHistoryBrowserUrl = (
  pathname: string,
  search: string,
  hash: string,
  values: PublishHistoryFilterValues,
  nextReadinessSort: ReadinessSort | null,
) => {
  const params = new URLSearchParams(search)
  params.delete('publishSequenceNumber')
  params.delete('publishStatus')
  params.delete('publishReadinessStatus')
  params.delete('publishReadinessSort')

  if (values.sequenceNumber) params.set('publishSequenceNumber', values.sequenceNumber)
  if (values.status) params.set('publishStatus', values.status)
  if (values.readinessStatus) params.set('publishReadinessStatus', values.readinessStatus)
  if (nextReadinessSort) params.set('publishReadinessSort', nextReadinessSort)

  const nextSearch = params.toString()
  return `${pathname}${nextSearch ? `?${nextSearch}` : ''}${hash}`
}
