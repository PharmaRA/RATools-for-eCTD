import { isValidElement, type ReactElement } from 'react'
import { describe, expect, it } from 'vitest'

import { formatReportCount, formatReportList, renderReportSeverityStatus, renderZipEntryPresentStatus } from './reportDisplay'

describe('reportDisplay', () => {
  it('formats report list values as a comma-separated list', () => {
    expect(formatReportList(['Lifecycle', 'Validation'])).toBe('Lifecycle, Validation')
  })

  it('uses a dash when report list values are missing', () => {
    expect(formatReportList([])).toBe('-')
    expect(formatReportList(undefined)).toBe('-')
  })

  it('uses a dash only when a report count is missing', () => {
    expect(formatReportCount(3)).toBe(3)
    expect(formatReportCount(0)).toBe(0)
    expect(formatReportCount(null)).toBe('-')
    expect(formatReportCount(undefined)).toBe('-')
  })

  it.each([
    ['Error', 'red'],
    ['Warning', 'orange'],
  ] as const)('renders %s report severity status', (severity, color) => {
    const element = renderReportSeverityStatus(severity)

    expect(isValidElement(element)).toBe(true)
    expect((element as ReactElement<{ color: string; children: string }>).props.color).toBe(color)
    expect((element as ReactElement<{ color: string; children: string }>).props.children).toBe(severity)
  })

  it.each([
    [true, 'green', 'Present'],
    [false, 'red', 'Missing from zip'],
  ] as const)('renders zip entry present status %s', (present, color, label) => {
    const element = renderZipEntryPresentStatus(present)

    expect(isValidElement(element)).toBe(true)
    expect((element as ReactElement<{ color: string; children: string }>).props.color).toBe(color)
    expect((element as ReactElement<{ color: string; children: string }>).props.children).toBe(label)
  })

  it('uses a dash when zip entry present status is missing', () => {
    expect(renderZipEntryPresentStatus(null)).toBe('-')
    expect(renderZipEntryPresentStatus(undefined)).toBe('-')
  })
})
