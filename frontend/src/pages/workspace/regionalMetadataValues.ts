export type RegionalValuePath = readonly (string | number)[]

export const regionalValueAt = (value: unknown, path: RegionalValuePath): unknown => {
  let current = value
  for (const key of path) {
    if (current === null || typeof current !== 'object') return undefined
    current = (current as Record<string | number, unknown>)[key]
  }
  return current
}

export const withRegionalValue = <T,>(value: T, path: RegionalValuePath, replacement: unknown): T => {
  if (path.length === 0) return replacement as T
  const [key, ...rest] = path
  if (Array.isArray(value)) {
    const result = [...value]
    result[Number(key)] = withRegionalValue(result[Number(key)], rest, replacement)
    return result as T
  }
  const original = value !== null && typeof value === 'object' ? value : {}
  return { ...original, [key]: withRegionalValue((original as Record<string | number, unknown>)[key], rest, replacement) } as T
}

export const regionalFieldPath = (path: RegionalValuePath): string =>
  path.map((key, index) => typeof key === 'number' ? `[${key}]` : `${index ? '.' : ''}${key}`).join('')
