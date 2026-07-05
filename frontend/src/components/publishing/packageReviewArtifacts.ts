import type { PackageReviewArtifact } from './packageReviewExport'

const isArtifact = (value: unknown): value is PackageReviewArtifact => {
  return !!value && typeof value === 'object' && typeof (value as PackageReviewArtifact).name === 'string'
}

const toArtifactArray = (value: unknown) => Array.isArray(value) ? value.filter(isArtifact) : []

export const getArtifactsFromResponse = (value: unknown) => {
  if (Array.isArray(value)) return toArtifactArray(value)
  if (!value || typeof value !== 'object') return []
  return toArtifactArray((value as { artifacts?: unknown }).artifacts)
}
