import { act } from 'react'
import { createRoot } from 'react-dom/client'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiRequestError } from '../../apiClient'
import { messages } from '../../i18n/messages'
import { ArtifactDownloadButton } from './ArtifactDownloadButton'

const flushPromises = async () => {
  await act(async () => {
    await Promise.resolve()
  })
}

describe('ArtifactDownloadButton', () => {
  afterEach(() => {
    document.body.innerHTML = ''
    vi.clearAllMocks()
  })

  it('downloads on click without exposing a navigation href', async () => {
    const container = document.createElement('div')
    document.body.appendChild(container)
    const root = createRoot(container)
    const downloadAction = vi.fn().mockResolvedValue('submission.zip')
    const onDownloadError = vi.fn()

    act(() => {
      root.render(
        <ArtifactDownloadButton
          jobId="job-1"
          artifactName="PackageZip"
          downloadAction={downloadAction}
          onDownloadError={onDownloadError}
        >
          下载包
        </ArtifactDownloadButton>,
      )
    })

    const button = container.querySelector('button') as HTMLButtonElement
    expect(button).toBeTruthy()
    expect(container.querySelector('a')).toBeNull()

    act(() => {
      button.dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })
    await flushPromises()

    expect(downloadAction).toHaveBeenCalledWith('job-1', 'PackageZip')
    expect(onDownloadError).not.toHaveBeenCalled()

    act(() => root.unmount())
    container.remove()
  })

  it('shows the shared authentication error feedback', async () => {
    const container = document.createElement('div')
    document.body.appendChild(container)
    const root = createRoot(container)
    const downloadAction = vi.fn().mockRejectedValue(new ApiRequestError(401, 'HTTP Error 401'))
    const onDownloadError = vi.fn()

    act(() => {
      root.render(
        <ArtifactDownloadButton
          jobId="job-1"
          artifactName="PublishReport"
          downloadAction={downloadAction}
          onDownloadError={onDownloadError}
        >
          下载报告
        </ArtifactDownloadButton>,
      )
    })

    act(() => {
      ;(container.querySelector('button') as HTMLButtonElement)
        .dispatchEvent(new MouseEvent('click', { bubbles: true }))
    })
    await flushPromises()

    expect(onDownloadError).toHaveBeenCalledWith(messages.artifact.downloadUnauthorized)

    act(() => root.unmount())
    container.remove()
  })
})
