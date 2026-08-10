import { downloadBlob } from '../../browserDownload'

export const downloadJson = (filename: string, value: unknown) => {
  const blob = new Blob([JSON.stringify(value, null, 2)], { type: 'application/json' })
  downloadBlob(blob, filename)
}
