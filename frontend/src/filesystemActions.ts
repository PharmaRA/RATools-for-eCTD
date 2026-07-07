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

export const buildFilesystemDirectoriesUrl = () => '/api/filesystem/directories'

export const buildDirectoryListingUrl = (path?: string) => (
  path ? `${buildFilesystemDirectoriesUrl()}?path=${encodeURIComponent(path)}` : buildFilesystemDirectoriesUrl()
)

export const buildResolveDirectoryUrl = () => '/api/filesystem/resolve-directory'

export const listDirectories = async (path?: string, executeRequest: typeof apiFetch = apiFetch) => {
  return executeRequest(buildDirectoryListingUrl(path))
}

export const resolveDirectory = async (path: string, executeRequest: typeof apiFetch = apiFetch) => {
  return executeRequest(buildResolveDirectoryUrl(), buildJsonRequestInit('POST', { path }))
}

export const filesystemActions = {
  listDirectories,
  resolveDirectory,
}
