import { useEffect, useRef, useState } from 'react'
import { Alert, Button, Input, Space } from 'antd'

import { filesystemActions, type DirectoryResolutionResult } from './filesystemActions'
import { DirectoryBrowserModal } from './DirectoryBrowserModal'

type FilesystemProvider = typeof filesystemActions

export type PathPickerProps = {
  value?: string
  onChange?: (path: string) => void
  placeholder?: string
  provider?: FilesystemProvider
}

const defaultProvider = filesystemActions

const mapDirectoryResolutionError = (caught: unknown) => {
  if (caught instanceof TypeError && caught.message.includes('Failed to fetch')) {
    return 'Cannot reach the API server. If you are running locally, make sure the backend is running at http://localhost:5000.'
  }

  return caught instanceof Error ? caught.message : 'Failed to resolve directory'
}

export const PathPicker = ({ value, onChange, placeholder, provider = defaultProvider }: PathPickerProps) => {
  const resolvedValue = value ?? ''
  const resolvedOnChange = onChange ?? (() => {})
  const [draftValue, setDraftValue] = useState(resolvedValue)
  const [browseOpen, setBrowseOpen] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const requestSequence = useRef(0)
  const mounted = useRef(false)

  useEffect(() => {
    mounted.current = true

    return () => {
      mounted.current = false
    }
  }, [])

  useEffect(() => {
    setDraftValue(resolvedValue)
  }, [resolvedValue])

  const normalizeTypedPath = async (path: string) => {
    if (!path.trim()) {
      setDraftValue(resolvedValue)
      setError(null)
      return
    }

    const requestId = ++requestSequence.current

    try {
      const result = await provider.resolveDirectory(path)
      if (!mounted.current || requestSequence.current !== requestId) {
        return
      }

      const normalizedPath = (result as DirectoryResolutionResult).fullPath
      setDraftValue(normalizedPath)
      setError(null)
      resolvedOnChange(normalizedPath)
    } catch (caught) {
      if (!mounted.current || requestSequence.current !== requestId) {
        return
      }

      setError(mapDirectoryResolutionError(caught))
      setDraftValue(resolvedValue)
    }
  }

  const handleSelect = (selectedPath: string) => {
    setBrowseOpen(false)
    setError(null)
    requestSequence.current += 1
    setDraftValue(selectedPath)
    resolvedOnChange(selectedPath)
  }

  const handleDraftChange = (path: string) => {
    setDraftValue(path)
    setError(null)
    resolvedOnChange(path)
  }

  return (
    <Space.Compact style={{ width: '100%' }}>
      <Input
        value={draftValue}
        placeholder={placeholder}
        onChange={(event) => handleDraftChange(event.target.value)}
        onBlur={(event) => {
          void normalizeTypedPath(event.target.value)
        }}
      />
      <Button onClick={() => setBrowseOpen(true)}>Browse</Button>

      {error && <Alert type="error" showIcon message="Directory path could not be resolved" description={error} />}

      <DirectoryBrowserModal
        open={browseOpen}
        initialPath={draftValue || resolvedValue}
        onCancel={() => setBrowseOpen(false)}
        onSelect={handleSelect}
        provider={provider}
      />
    </Space.Compact>
  )
}
