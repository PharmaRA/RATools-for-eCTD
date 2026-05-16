import { apiFetch } from './apiClient'

export type DirectoryBrowseEntry = {
  name: string
  fullPath: string
  canBrowse: boolean
  hasChildren: boolean
}

export type DirectoryBrowseResult = {
  currentPath: string | null
  parentPath: string | null
  directories: DirectoryBrowseEntry[]
}

export type DirectoryResolutionResult = {
  fullPath: string
  exists: boolean
  isDirectory: boolean
  isAccessible: boolean
}

export const listDirectories = async (path?: string, executeRequest: typeof apiFetch = apiFetch) => {
  const url = path ? `/api/filesystem/directories?path=${encodeURIComponent(path)}` : '/api/filesystem/directories'

  return executeRequest(url)
}

export const resolveDirectory = async (path: string, executeRequest: typeof apiFetch = apiFetch) => {
  return executeRequest('/api/filesystem/resolve-directory', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path }),
  })
}

export const filesystemActions = {
  listDirectories,
  resolveDirectory,
}
