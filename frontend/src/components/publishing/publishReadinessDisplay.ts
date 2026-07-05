export const formatMissingMetadataFields = (fields?: string[] | null) => {
  return fields?.length ? fields.join(', ') : 'None'
}

type PublishReadinessCategoryRow = {
  category: string
}

type PublishReadinessFindingRow = {
  code: string
  fieldName?: string | null
}

export const getPublishReadinessCategoryKey = (row: PublishReadinessCategoryRow) => row.category

export const getPublishReadinessFindingKey = (row: PublishReadinessFindingRow, index?: number) => {
  return `${row.code}-${row.fieldName || 'none'}-${index}`
}
