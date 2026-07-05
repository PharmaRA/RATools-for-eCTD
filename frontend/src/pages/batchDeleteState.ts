type BatchDeleteStateInput = {
  selectedKeys: readonly string[]
  deletingKeys: ReadonlySet<string>
  isBatchDeleteRunning: boolean
}

export const buildBatchDeleteState = ({
  selectedKeys,
  deletingKeys,
  isBatchDeleteRunning,
}: BatchDeleteStateInput) => {
  const hasSingleDeleteRunning = deletingKeys.size > 0

  return {
    hasSingleDeleteRunning,
    canStartBatchDelete: selectedKeys.length > 0 && !isBatchDeleteRunning && !hasSingleDeleteRunning,
  }
}
