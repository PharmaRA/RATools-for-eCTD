import { apiFetch, buildJsonRequestInit } from './apiClient'
import type {
  DirectoryBrowseResult,
  DirectoryResolutionResult,
} from './api/contracts'

export type { DirectoryBrowseEntry, DirectoryBrowseResult, DirectoryResolutionResult } from './api/contracts'

export const buildFilesystemDirectoriesUrl = () => '/api/filesystem/directories'

export const buildDirectoryListingUrl = (path?: string) => (
  path ? `${buildFilesystemDirectoriesUrl()}?path=${encodeURIComponent(path)}` : buildFilesystemDirectoriesUrl()
)

export const buildResolveDirectoryUrl = () => '/api/filesystem/resolve-directory'

export const listDirectories = async (
  path?: string,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<DirectoryBrowseResult> => {
  return executeRequest(buildDirectoryListingUrl(path))
}

export const resolveDirectory = async (
  path: string,
  executeRequest: typeof apiFetch = apiFetch,
): Promise<DirectoryResolutionResult> => {
  return executeRequest(buildResolveDirectoryUrl(), buildJsonRequestInit('POST', { path }))
}

export const filesystemActions = {
  listDirectories,
  resolveDirectory,
}
