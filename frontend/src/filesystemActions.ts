import { apiFetch, buildJsonRequestInit } from './apiClient'

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

export const buildDirectoryListingUrl = (path?: string) => (
  path ? `/api/filesystem/directories?path=${encodeURIComponent(path)}` : '/api/filesystem/directories'
)

export const listDirectories = async (path?: string, executeRequest: typeof apiFetch = apiFetch) => {
  return executeRequest(buildDirectoryListingUrl(path))
}

export const resolveDirectory = async (path: string, executeRequest: typeof apiFetch = apiFetch) => {
  return executeRequest('/api/filesystem/resolve-directory', buildJsonRequestInit('POST', { path }))
}

export const filesystemActions = {
  listDirectories,
  resolveDirectory,
}
