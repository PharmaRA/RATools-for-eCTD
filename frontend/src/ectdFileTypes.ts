export const ECTD_ALLOWED_EXTENSIONS = [
  '.pdf',
  '.xml',
  '.xpt',
  '.sas7bdat',
  '.txt',
  '.rtf',
  '.doc',
  '.docx',
  '.xls',
  '.xlsx',
  '.csv',
  '.jpg',
  '.jpeg',
  '.png',
  '.tif',
  '.tiff',
] as const

const ECTD_ALLOWED_EXTENSION_SET = new Set<string>(ECTD_ALLOWED_EXTENSIONS)

export const isAllowedEctdFileName = (fileName: string | null | undefined) => {
  if (!fileName) {
    return false
  }

  const normalized = fileName.trim()
  if (!normalized) {
    return false
  }

  if (normalized.includes('/') || normalized.includes('\\')) {
    return false
  }

  const extensionIndex = normalized.lastIndexOf('.')
  if (extensionIndex <= 0 || extensionIndex === normalized.length - 1) {
    return false
  }

  const extension = normalized.slice(extensionIndex).toLowerCase()
  return ECTD_ALLOWED_EXTENSION_SET.has(extension)
}

export const ectdAllowedExtensionsHint = ECTD_ALLOWED_EXTENSIONS.join(', ')

export const splitFileName = (fileName: string | null | undefined) => {
  const normalized = (fileName || '').trim()
  const extensionIndex = normalized.lastIndexOf('.')

  if (extensionIndex <= 0 || extensionIndex === normalized.length - 1) {
    return { prefix: normalized, extension: '' }
  }

  return {
    prefix: normalized.slice(0, extensionIndex),
    extension: normalized.slice(extensionIndex),
  }
}
