import { setRuntimeApiKey } from './apiClient'

type RuntimeConfig = {
  apiKey?: string
}

export const initializeRuntimeConfig = async (request: typeof fetch = fetch) => {
  if (import.meta.env.DEV) {
    return
  }

  const response = await request('/runtime-config', {
    cache: 'no-store',
    credentials: 'same-origin',
    headers: { Accept: 'application/json' },
  })
  if (!response.ok) {
    throw new Error(`Runtime configuration request failed with HTTP ${response.status}.`)
  }

  const config = await response.json() as RuntimeConfig
  setRuntimeApiKey(config.apiKey)
}
