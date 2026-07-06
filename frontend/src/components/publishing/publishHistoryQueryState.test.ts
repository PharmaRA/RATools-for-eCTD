import { describe, expect, it } from 'vitest'

import {
  buildPublishHistoryBrowserUrl,
  getPublishHistoryInitialQueryState,
  normalizePublishHistoryReadinessSort,
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

  it.each([
    ['blocked-first', 'blocked-first'],
    ['ready-first', 'ready-first'],
    ['default', null],
    ['latest-first', null],
    [null, null],
    [undefined, null],
  ])('normalizes readiness sort value %s', (value, expected) => {
    expect(normalizePublishHistoryReadinessSort(value)).toBe(expected)
  })
})
