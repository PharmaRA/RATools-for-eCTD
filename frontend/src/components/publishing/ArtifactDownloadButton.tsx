import { useState, type ReactNode } from 'react'
import { Button, message, type ButtonProps } from 'antd'

import { downloadArtifact, getArtifactDownloadErrorMessage } from '../../publishActions'

type ArtifactDownloadButtonProps = Omit<
  ButtonProps,
  'href' | 'target' | 'download' | 'onClick' | 'loading'
> & {
  jobId: string | null
  artifactName: string
  children: ReactNode
  downloadAction?: typeof downloadArtifact
  onDownloadError?: (errorMessage: string) => void
}

export const ArtifactDownloadButton = ({
  jobId,
  artifactName,
  children,
  disabled,
  downloadAction = downloadArtifact,
  onDownloadError = (errorMessage) => { message.error(errorMessage) },
  ...buttonProps
}: ArtifactDownloadButtonProps) => {
  const [downloading, setDownloading] = useState(false)

  const handleDownload = async () => {
    if (!jobId || downloading) return

    setDownloading(true)
    try {
      await downloadAction(jobId, artifactName)
    } catch (error) {
      onDownloadError(getArtifactDownloadErrorMessage(error))
    } finally {
      setDownloading(false)
    }
  }

  return (
    <Button
      {...buttonProps}
      disabled={disabled || !jobId || downloading}
      loading={downloading}
      onClick={() => { void handleDownload() }}
    >
      {children}
    </Button>
  )
}
