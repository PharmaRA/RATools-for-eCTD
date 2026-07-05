import { isReadinessSort, type ReadinessSort } from './publishHistorySorting'

export type PublishHistoryFilterValues = {
  sequenceNumber?: string
  status?: string
  readinessStatus?: string
  readinessSort?: ReadinessSort | 'default' | null
}

type PublishHistoryRequestFilterValues = {
  sequenceNumber?: string | null
  status?: string | null
  readinessStatus?: string | null
}

export const getPublishHistoryInitialQueryState = (search: string) => {
  const params = new URLSearchParams(search)
  const readinessSort = params.get('publishReadinessSort')
  const validatedReadinessSort = isReadinessSort(readinessSort) ? readinessSort : null

  return {
    formValues: {
      sequenceNumber: params.get('publishSequenceNumber') || undefined,
      status: params.get('publishStatus') || undefined,
      readinessStatus: params.get('publishReadinessStatus') || undefined,
      readinessSort: validatedReadinessSort || undefined,
    },
    readinessSort: validatedReadinessSort,
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

export const buildPublishHistoryRequestUrl = (
  appId: string,
  page: number,
  pageSize: number,
  values: PublishHistoryRequestFilterValues,
) => {
  const params = new URLSearchParams({ page: page.toString(), pageSize: pageSize.toString() })
  if (values.sequenceNumber) params.append('sequenceNumber', values.sequenceNumber)
  if (values.status) params.append('status', values.status)
  if (values.readinessStatus) params.append('readinessStatus', values.readinessStatus)

  return `/api/applications/${appId}/publish-history?${params.toString()}`
}
