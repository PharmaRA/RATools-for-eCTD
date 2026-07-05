import { describe, expect, it } from 'vitest'

import {
  buildPublishHistoryBrowserUrl,
  buildPublishHistoryRequestUrl,
  getPublishHistoryInitialQueryState,
} from './publishHistoryQueryState'

describe('publishHistoryQueryState', () => {
  it('reads publish history filter values from a query string', () => {
    expect(getPublishHistoryInitialQueryState(
      '?publishSequenceNumber=0001&publishStatus=Completed&publishReadinessStatus=Blocked&publishReadinessSort=blocked-first',
    )).toEqual({
      formValues: {
        sequenceNumber: '0001',
        status: 'Completed',
        readinessStatus: 'Blocked',
        readinessSort: 'blocked-first',
      },
      readinessSort: 'blocked-first',
    })
  })

  it('ignores invalid readiness sort values', () => {
    expect(getPublishHistoryInitialQueryState('?publishReadinessSort=latest-first')).toEqual({
      formValues: {
        sequenceNumber: undefined,
        status: undefined,
        readinessStatus: undefined,
        readinessSort: undefined,
      },
      readinessSort: null,
    })
  })

  it('builds a browser URL with publish history filters while preserving unrelated query values', () => {
    expect(buildPublishHistoryBrowserUrl(
      '/applications/app-1',
      '?tab=history&publishStatus=Failed',
      '#activity',
      {
        sequenceNumber: '0002',
        status: 'Completed',
        readinessStatus: 'Ready',
      },
      'ready-first',
    )).toBe('/applications/app-1?tab=history&publishSequenceNumber=0002&publishStatus=Completed&publishReadinessStatus=Ready&publishReadinessSort=ready-first#activity')
  })

  it('removes publish history filters from the browser URL when values are empty', () => {
    expect(buildPublishHistoryBrowserUrl(
      '/applications/app-1',
      '?tab=history&publishSequenceNumber=0002&publishStatus=Failed&publishReadinessSort=blocked-first',
      '',
      {},
      null,
    )).toBe('/applications/app-1?tab=history')
  })

  it('builds a publish history request URL with pagination and filters', () => {
    expect(buildPublishHistoryRequestUrl('app-1', 2, 50, {
      sequenceNumber: '0002',
      status: 'Completed',
      readinessStatus: 'Ready',
    })).toBe('/api/applications/app-1/publish-history?page=2&pageSize=50&sequenceNumber=0002&status=Completed&readinessStatus=Ready')
  })

  it('omits empty publish history request filters', () => {
    expect(buildPublishHistoryRequestUrl('app-1', 1, 20, {
      sequenceNumber: '',
      status: undefined,
      readinessStatus: null,
    })).toBe('/api/applications/app-1/publish-history?page=1&pageSize=20')
  })
})
