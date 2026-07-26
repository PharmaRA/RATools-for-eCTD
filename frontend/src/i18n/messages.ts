// 用户可见文案常量表：生产代码与测试共同引用同一常量，
// 避免文案调整时测试断言漂移（f000ff1 的 50 个测试失败即此类问题）。
// 仅收纳跨生产/测试共享的文案；组件内部一次性文案无需入表。
export const messages = {
  common: {
    unknownError: '未知错误',
    unavailable: '不可用',
  },
  artifact: {
    exists: '存在',
    missing: '缺失',
    download: '下载',
    columnName: '名称',
    columnStatus: '状态',
    columnSize: '大小',
    columnType: '类型',
    columnAction: '操作',
  },
  packageReview: {
    errorCountLabel: '个错误',
    issueCountLabel: '个问题',
    reportUnavailable: '报告不可用。',
    integrityConsistent: '一致',
    integrityInconsistent: '不一致或不可用',
    artifactsReadySuffix: '已就绪',
    checkPublishSucceeded: '发布成功',
    checkValidationErrors: '校验错误',
    checkLifecycleIssues: '生命周期问题',
    checkIntegrityConsistent: '完整性一致',
    checkRequiredArtifacts: '必需产物齐全',
    passTag: '通过',
    failTag: '未通过',
    columnCheck: '检查项',
    columnStatus: '状态',
    columnDetail: '详情',
    loadErrorGeneric: '无法加载包审阅数据',
    loadErrorNotFound: '未找到报告或产物 (404)',
    loadErrorNotReady: '发布任务尚未就绪 (409)',
    loadErrorGone: '发布数据不可用 (410)',
    loadErrorCorrupted: '发布报告已损坏 (422)',
  },
  prePublish: {
    passed: '发布前检查已通过',
    failed: '发布前检查未通过',
    blockingLabel: '个阻断',
    warningLabel: '个警告',
    readinessPrefix: '[发布就绪度]',
    validationApiProfile: '校验 API',
  },
  importResult: {
    totalLabel: '条问题总数',
    warningLabel: '条警告',
    errorLabel: '条错误',
    lifecycleWarningLabel: '条生命周期目标警告',
  },
  publishHistory: {
    columnReport: '报告',
    columnCreated: '创建时间',
    columnActions: '操作',
    lifecycleAllMatched: '全部匹配',
    lifecycleIssueLabel: '个问题',
  },
  applicationDetails: {
    columnSequence: '序列',
    columnSubmissionType: '递交类型',
    columnDescription: '描述',
    columnActions: '操作',
  },
} as const

export type Messages = typeof messages
