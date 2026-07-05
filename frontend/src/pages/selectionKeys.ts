export const keepKnownSelectionKeys = (currentKeys: string[], validKeys: Set<string>) => {
  const nextKeys = currentKeys.filter((key) => validKeys.has(key))
  return nextKeys.length === currentKeys.length ? currentKeys : nextKeys
}
