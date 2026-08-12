import { afterEach, describe, expect, it, vi } from 'vitest'

import { apiFetch, setRuntimeApiKey } from './apiClient'
import { initializeRuntimeConfig } from './runtimeConfig'

describe('runtimeConfig', () => {
  afterEach(() => {
    setRuntimeApiKey(undefined)
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
  })

  it('loads the same-origin runtime key before API requests', async () => {
    vi.stubEnv('DEV', false)
    const configRequest = vi.fn().mockResolvedValue(new Response(
      JSON.stringify({ apiKey: 'runtime-api-key' }),
      { status: 200, headers: { 'Content-Type': 'application/json' } },
    ))

    await initializeRuntimeConfig(configRequest)

    expect(configRequest).toHaveBeenCalledWith('/runtime-config', {
      cache: 'no-store',
      credentials: 'same-origin',
      headers: { Accept: 'application/json' },
    })

    const apiRequest = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }))
    vi.stubGlobal('fetch', apiRequest)
    await apiFetch('/api/applications')

    const headers = new Headers(apiRequest.mock.calls[0][1]?.headers)
    expect(headers.get('X-RA-Tools-Api-Key')).toBe('runtime-api-key')
  })

  it('fails startup when production runtime configuration cannot be loaded', async () => {
    vi.stubEnv('DEV', false)

    await expect(initializeRuntimeConfig(vi.fn().mockResolvedValue(
      new Response(null, { status: 503 }),
    ))).rejects.toThrow('HTTP 503')
  })
})
