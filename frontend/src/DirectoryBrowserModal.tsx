import { useEffect, useRef, useState } from 'react'
import { Alert, Button, Modal, Spin, Space } from 'antd'

import { filesystemActions, type DirectoryBrowseEntry, type DirectoryBrowseResult } from './filesystemActions'

type FilesystemProvider = Pick<typeof filesystemActions, 'listDirectories'>

export type DirectoryBrowserModalProps = {
  open: boolean
  initialPath: string
  onCancel: () => void
  onSelect: (path: string) => void
  provider?: FilesystemProvider
}

const defaultProvider = filesystemActions

export const DirectoryBrowserModal = ({
  open,
  initialPath,
  onCancel,
  onSelect,
  provider = defaultProvider,
}: DirectoryBrowserModalProps) => {
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<DirectoryBrowseResult | null>(null)
  const [currentPath, setCurrentPath] = useState(initialPath)
  const requestSequence = useRef(0)
  const mounted = useRef(false)

  useEffect(() => {
    mounted.current = true

    return () => {
      mounted.current = false
    }
  }, [])

  useEffect(() => {
    if (!open) {
      return
    }

    const requestId = ++requestSequence.current

    const loadPath = async (path?: string) => {
      setLoading(true)
      setError(null)

      try {
        const nextResult = await provider.listDirectories(path)
        if (!mounted.current || requestSequence.current !== requestId) {
          return
        }

        setResult(nextResult)
        setCurrentPath(nextResult.currentPath ?? path ?? initialPath)
      } catch (caught) {
        if (!mounted.current || requestSequence.current !== requestId) {
          return
        }

        setError(caught instanceof Error ? caught.message : 'Failed to load directories')
        setResult(null)
      } finally {
        if (mounted.current && requestSequence.current === requestId) {
          setLoading(false)
        }
      }
    }

    void loadPath(initialPath)
  }, [initialPath, open, provider])

  const goToPath = async (path?: string | null) => {
    if (!path) {
      return
    }

    const requestId = ++requestSequence.current
    setCurrentPath(path)
    setLoading(true)
    setError(null)

    try {
      const nextResult = await provider.listDirectories(path)
      if (!mounted.current || requestSequence.current !== requestId) {
        return
      }

      setResult(nextResult)
      setCurrentPath(nextResult.currentPath ?? path)
    } catch (caught) {
      if (!mounted.current || requestSequence.current !== requestId) {
        return
      }

      setError(caught instanceof Error ? caught.message : 'Failed to load directories')
      setResult(null)
    } finally {
      if (mounted.current && requestSequence.current === requestId) {
        setLoading(false)
      }
    }
  }

  const handleChildClick = (entry: DirectoryBrowseEntry) => {
    if (!entry.canBrowse) {
      return
    }

    void goToPath(entry.fullPath)
  }

  const handleParentClick = () => {
    void goToPath(result?.parentPath)
  }

  return (
    <Modal
      open={open}
      title="选择目录"
      onCancel={onCancel}
      footer={(
        <Space>
          <Button onClick={onCancel}>取消</Button>
          <Button type="primary" onClick={() => onSelect(currentPath)} disabled={loading || !!error || !currentPath}>
            选择此目录
          </Button>
        </Space>
      )}
      destroyOnHidden
    >
      <div className="flex flex-col gap-4">
        {loading && <Spin description="正在加载目录…" />}

        {error && <Alert type="error" showIcon title="加载目录失败" description={error} />}

        {!error && result && (
          <div className="flex flex-col gap-3">
            <div>
              <div className="text-xs uppercase text-gray-500">当前路径</div>
              <div className="font-medium break-all">{result.currentPath ?? currentPath}</div>
            </div>

            <div>
              <div className="text-xs uppercase text-gray-500">上一级</div>
              {result.parentPath ? (
                <Button type="link" className="px-0" onClick={handleParentClick}>
                  {result.parentPath}
                </Button>
              ) : (
                <div className="text-gray-400">没有上一级目录</div>
              )}
            </div>

            <div>
              <div className="text-xs uppercase text-gray-500 mb-2">子目录</div>
              {result.directories.length > 0 ? (
                <div className="flex flex-col gap-2">
                  {result.directories.map((entry) => (
                    <Button
                      key={entry.fullPath}
                      type="link"
                      className="px-0 justify-start"
                      onClick={() => handleChildClick(entry)}
                    >
                      {entry.name}
                    </Button>
                  ))}
                </div>
              ) : (
                <div className="text-gray-400">没有子目录</div>
              )}
            </div>
          </div>
        )}
      </div>
    </Modal>
  )
}
