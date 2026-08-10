const normalizeFileName = (value: string, fallback: string) => {
  const withoutControlCharacters = Array.from(value)
    .filter((character) => {
      const characterCode = character.charCodeAt(0)
      return characterCode > 31 && characterCode !== 127
    })
    .join('')
    .trim()
  const fileName = withoutControlCharacters.split(/[/\\]/).pop()
  return fileName || fallback
}

const decodeExtendedFileName = (value: string) => {
  const encodedValue = value.trim().replace(/^"|"$/g, '')
  try {
    return decodeURIComponent(encodedValue)
  } catch {
    return encodedValue
  }
}

export const getDownloadFileName = (contentDisposition: string | null, fallback: string) => {
  if (!contentDisposition) return fallback

  const extendedMatch = contentDisposition.match(/(?:^|;)\s*filename\*\s*=\s*(?:UTF-8'')?([^;]+)/i)
  if (extendedMatch?.[1]) {
    return normalizeFileName(decodeExtendedFileName(extendedMatch[1]), fallback)
  }

  const fileNameMatch = contentDisposition.match(/(?:^|;)\s*filename\s*=\s*(?:"([^"]*)"|([^;]*))/i)
  return normalizeFileName(fileNameMatch?.[1] || fileNameMatch?.[2] || fallback, fallback)
}

export const downloadBlob = (blob: Blob, fileName: string) => {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName

  try {
    document.body.appendChild(link)
    link.click()
  } finally {
    link.remove()
    URL.revokeObjectURL(url)
  }
}
