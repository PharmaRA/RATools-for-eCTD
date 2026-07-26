export type HealthStatus = 'ok' | 'error'

export type HealthResponse = {
  status?: string
}

export const buildHealthUrl = () => '/health'

/**
 * Probes the backend liveness endpoint. `/health` is anonymous, so this does
 * not need the API key; it stays a thin fetch wrapper that is easy to stub in
 * tests and never throws (any failure maps to 'error').
 */
export const checkHealth = async (
  fetchImpl: typeof fetch = fetch,
): Promise<HealthStatus> => {
  try {
    const response = await fetchImpl(buildHealthUrl())
    if (!response.ok) {
      return 'error'
    }

    const data = (await response.json()) as HealthResponse
    return data.status === 'ok' ? 'ok' : 'error'
  } catch {
    return 'error'
  }
}
