import { afterEach, describe, expect, it, vi } from 'vitest'

import { downloadJson } from './packageReviewDownload'

const readBlobText = async (blob: Blob) => new Promise<string>((resolve, reject) => {
  const reader = new FileReader()
  reader.onload = () => resolve(String(reader.result))
  reader.onerror = () => reject(reader.error)
  reader.readAsText(blob)
})

describe('packageReviewDownload', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    document.body.innerHTML = ''
  })

  it('downloads a pretty-printed JSON blob and revokes the object URL', async () => {
    const createdBlobs: Blob[] = []
    const clickedDownloads: string[] = []
    const originalCreateElement = document.createElement.bind(document)

    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: vi.fn((blob: Blob | MediaSource) => {
        createdBlobs.push(blob as Blob)
        return 'blob:package-review'
      }),
      revokeObjectURL: vi.fn(),
    })
    vi.spyOn(document, 'createElement').mockImplementation(((tagName: string, options?: ElementCreationOptions) => {
      const element = originalCreateElement(tagName, options)
      if (tagName.toLowerCase() === 'a') {
        vi.spyOn(element, 'click').mockImplementation(() => {
          clickedDownloads.push((element as HTMLAnchorElement).download)
        })
      }
      return element
    }) as typeof document.createElement)

    downloadJson('review.json', { status: 'Ready', count: 2 })

    expect(createdBlobs).toHaveLength(1)
    expect(createdBlobs[0].type).toBe('application/json')
    await expect(readBlobText(createdBlobs[0])).resolves.toBe(JSON.stringify({ status: 'Ready', count: 2 }, null, 2))
    expect(clickedDownloads).toEqual(['review.json'])
    expect(URL.createObjectURL).toHaveBeenCalledWith(createdBlobs[0])
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:package-review')
    expect(document.querySelector('a[download="review.json"]')).toBeNull()
  })
})
