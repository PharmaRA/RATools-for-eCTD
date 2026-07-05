export const buildPublishedHrefPreview = (
  storagePath: string | undefined,
  sequenceNumber: string,
  fallbackFileName: string | undefined,
) => {
  const fileName = fallbackFileName || '-'
  if (!storagePath) {
    return fileName
  }

  const segments = storagePath.split(/[\\/]+/).filter(Boolean)
  const sequenceIndex = segments
    .map((segment) => segment.toLowerCase())
    .lastIndexOf(sequenceNumber.toLowerCase())

  if (sequenceIndex >= 0 && sequenceIndex < segments.length - 1) {
    return [...segments.slice(sequenceIndex + 1, -1), fileName].join('/')
  }

  return fileName
}
