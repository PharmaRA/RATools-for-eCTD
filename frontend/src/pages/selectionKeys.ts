type SelectionKey = string | number | bigint

export const normalizeSelectionKeys = (
  keys: readonly SelectionKey[],
): string[] => keys.map((key) => String(key))

export const buildSelectionKeySet = <T>(
  items: readonly T[],
  getKey: (item: T) => SelectionKey,
): Set<string> => new Set(normalizeSelectionKeys(items.map(getKey)))

export const keepKnownSelectionKeys = (currentKeys: string[], validKeys: Set<string>) => {
  const nextKeys = currentKeys.filter((key) => validKeys.has(key))
  return nextKeys.length === currentKeys.length ? currentKeys : nextKeys
}
