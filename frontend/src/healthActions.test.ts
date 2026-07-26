import { describe, expect, it, vi } from 'vitest'

import { checkHealth } from './healthActions'

describe('healthActions', () => {
  it('returns ok when the endpoint reports ok', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: vi.fn().mockResolvedValue({ status: 'ok' }),
    })

    await expect(checkHealth(fetchMock as unknown as typeof fetch)).resolves.toBe('ok')
    expect(fetchMock).toHaveBeenCalledWith('/health')
  })

  it('returns error when the endpoint reports a non-ok status body', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: vi.fn().mockResolvedValue({ status: 'degraded' }),
    })

    await expect(checkHealth(fetchMock as unknown as typeof fetch)).resolves.toBe('error')
  })

  it('returns error when the response is not ok', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      json: vi.fn(),
    })

    await expect(checkHealth(fetchMock as unknown as typeof fetch)).resolves.toBe('error')
  })

  it('returns error when the request throws', async () => {
    const fetchMock = vi.fn().mockRejectedValue(new Error('network down'))

    await expect(checkHealth(fetchMock as unknown as typeof fetch)).resolves.toBe('error')
  })
})
