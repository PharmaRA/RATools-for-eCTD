import { describe, expect, it, vi } from 'vitest'

import { createQueryClient } from './queryClient'

describe('queryClient', () => {
  it('reuses fresh query data within the configured stale window', async () => {
    const queryClient = createQueryClient()
    const queryFn = vi.fn().mockResolvedValue({ items: ['cached'] })

    const first = await queryClient.fetchQuery({ queryKey: ['audit-logs', { page: 1 }], queryFn })
    const second = await queryClient.fetchQuery({ queryKey: ['audit-logs', { page: 1 }], queryFn })

    expect(first).toEqual({ items: ['cached'] })
    expect(second).toBe(first)
    expect(queryFn).toHaveBeenCalledTimes(1)
  })
})
