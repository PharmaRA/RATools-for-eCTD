import { afterEach, describe, expect, it, vi } from 'vitest'

import { buildDirectoryListingUrl, listDirectories, resolveDirectory } from './filesystemActions'

describe('filesystemActions', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('builds directory listing URLs from optional paths', () => {
    expect(buildDirectoryListingUrl()).toBe('/api/filesystem/directories')
    expect(buildDirectoryListingUrl('')).toBe('/api/filesystem/directories')
    expect(buildDirectoryListingUrl('C:/working/root')).toBe('/api/filesystem/directories?path=C%3A%2Fworking%2Froot')
  })

  it('lists directories without a path query parameter', async () => {
    const executeRequest = vi.fn().mockResolvedValue({ directories: [] })

    await listDirectories(undefined, executeRequest)

    expect(executeRequest).toHaveBeenCalledWith('/api/filesystem/directories')
  })

  it('lists directories with a path query parameter', async () => {
    const executeRequest = vi.fn().mockResolvedValue({ directories: [] })

    await listDirectories('C:/working/root', executeRequest)

    expect(executeRequest).toHaveBeenCalledWith('/api/filesystem/directories?path=C%3A%2Fworking%2Froot')
  })

  it('posts the requested path when resolving a directory', async () => {
    const executeRequest = vi.fn().mockResolvedValue({ fullPath: 'C:/working/root', exists: true })

    await resolveDirectory('C:/working/root', executeRequest)

    expect(executeRequest).toHaveBeenCalledWith('/api/filesystem/resolve-directory', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path: 'C:/working/root' }),
    })
  })
})
