import { describe, expect, it, vi } from 'vitest'

import {
  buildEctdTemplatesUrl,
  buildEctdTemplateSelectOptions,
  getDefaultEctdTemplateKey,
  importApplicationWithTemplate,
  loadEctdTemplates,
  type EctdTemplateOption,
} from './ectdTemplateActions'

describe('ectdTemplateActions', () => {
  it('builds eCTD template endpoint URL', () => {
    expect(buildEctdTemplatesUrl()).toBe('/api/ectd-templates')
  })

  it('loads available eCTD templates from the API', async () => {
    const request = vi.fn().mockResolvedValue([
      {
        key: 'us-fda-ectd-3-2-2',
        displayName: 'US FDA eCTD 3.2.2',
        region: 'US',
      },
    ] satisfies EctdTemplateOption[])

    const result = await loadEctdTemplates(request)

    expect(request).toHaveBeenCalledWith('/api/ectd-templates')
    expect(result).toEqual([
      {
        key: 'us-fda-ectd-3-2-2',
        displayName: 'US FDA eCTD 3.2.2',
        region: 'US',
      },
    ])
  })

  it('returns the first template key as the default selection', () => {
    expect(getDefaultEctdTemplateKey([
      { key: 'us-fda-ectd-3-2-2', displayName: 'US FDA eCTD 3.2.2', region: 'US' },
      { key: 'eu-ema-ectd-4-0', displayName: 'EU EMA eCTD 4.0', region: 'EU' },
    ])).toBe('us-fda-ectd-3-2-2')
  })

  it('returns undefined when there are no templates to preselect', () => {
    expect(getDefaultEctdTemplateKey([])).toBeUndefined()
  })

  it('builds select options from eCTD templates', () => {
    expect(buildEctdTemplateSelectOptions([
      { key: 'us-fda-ectd-3-2-2', displayName: 'US FDA eCTD 3.2.2', region: 'US' },
      { key: 'eu-ema-ectd-4-0', displayName: 'EU EMA eCTD 4.0', region: 'EU' },
    ])).toEqual([
      { value: 'us-fda-ectd-3-2-2', label: 'US FDA eCTD 3.2.2' },
      { value: 'eu-ema-ectd-4-0', label: 'EU EMA eCTD 4.0' },
    ])
  })

  it('submits import requests with ectdTemplateKey', async () => {
    const request = vi.fn().mockResolvedValue({ applicationId: 'app-1' })

    await importApplicationWithTemplate({
      workingDirectoryPath: 'D:\\work\\IND-IMPORT',
      ectdTemplateKey: 'us-fda-ectd-3-2-2',
      sponsorName: 'Demo Sponsor',
    }, request)

    expect(request).toHaveBeenCalledWith('/api/applications/import', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        workingDirectoryPath: 'D:\\work\\IND-IMPORT',
        ectdTemplateKey: 'us-fda-ectd-3-2-2',
        sponsorName: 'Demo Sponsor',
      }),
    })
  })
})
