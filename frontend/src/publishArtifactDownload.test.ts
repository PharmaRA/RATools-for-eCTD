import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiRequestError } from './apiClient'
import { messages } from './i18n/messages'
import { downloadArtifact, getArtifactDownloadErrorMessage } from './publishActions'

describe('authenticated publish artifact download', () => {
  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    vi.unstubAllEnvs()
    document.body.innerHTML = ''
  })

  it('injects the API key, keeps the server filename, and revokes the object URL', async () => {
    vi.stubEnv('VITE_API_KEY', 'download-api-key')
    const blob = new Blob(['package-content'], { type: 'application/zip' })
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      headers: new Headers({
        'content-disposition': 'attachment; filename="submission-0001.zip"',
      }),
      blob: vi.fn().mockResolvedValue(blob),
    })
    const clickedDownloads: string[] = []
    const originalCreateElement = document.createElement.bind(document)

    vi.stubGlobal('fetch', fetchMock)
    vi.stubGlobal('URL', {
      ...URL,
      createObjectURL: vi.fn().mockReturnValue('blob:publish-artifact'),
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

    await expect(downloadArtifact('job-1', 'PackageZip')).resolves.toBe('submission-0001.zip')

    expect(fetchMock).toHaveBeenCalledOnce()
    expect(fetchMock.mock.calls[0][0]).toBe('/api/publish-jobs/job-1/artifacts/PackageZip/download')
    const headers = new Headers((fetchMock.mock.calls[0][1] as RequestInit).headers)
    expect(headers.get('X-RA-Tools-Api-Key')).toBe('download-api-key')
    expect(clickedDownloads).toEqual(['submission-0001.zip'])
    expect(URL.createObjectURL).toHaveBeenCalledWith(blob)
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:publish-artifact')
    expect(document.querySelector('a[download]')).toBeNull()
  })

  it.each([
    [new ApiRequestError(401, 'HTTP Error 401'), messages.artifact.downloadUnauthorized],
    [new ApiRequestError(410, 'Artifact was not found.'), messages.artifact.downloadGone],
    [new TypeError('Failed to fetch'), messages.artifact.downloadNetworkError],
  ])('maps download failures to consistent UI feedback', (error, expectedMessage) => {
    expect(getArtifactDownloadErrorMessage(error)).toBe(expectedMessage)
  })
})
