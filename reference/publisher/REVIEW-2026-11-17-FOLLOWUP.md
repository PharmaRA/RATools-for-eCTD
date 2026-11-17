# 2026-11-17 审阅后续执行账本

基线 `68826ec`。用户授权先保存审阅/计划，再按顺序实现，每项验证和只读review通过后独立commit。审查是核心链路限定静态阅读，不代表全仓审计或测试通过。

本地文档：[审阅报告](../../docs/reviews/2026-11-17-code-review.md)、[执行计划](../../docs/plans/review-followup-2026-11-17.md)、[文档入口](../../docs/README.md)。本文件受版本管理，以下表格保留可携带任务定义和结果；docs仍按原策略仅本地保存。

| 顺序 | 任务 | 问题/交付 | 状态 | 验证/review/提交 |
| --- | --- | --- | --- | --- |
| 1 | CR-01 | PublishJobService忽略完整性错误并登记成功；让报告、终态、下载一致 | 已提交 `6c18fb3` | 36/36；最终只读review PASS |
| 2 | CR-02 | PublishOutputVerifier仅比较ZIP名称；补成员内容与双向集合检查 | 已提交 `f13054f` | 27/27，关联45/45含前27；最终review PASS |
| 3 | CR-03 | 多文件拖入复用旧placements导致排序号重复 | 验证及只读review PASS | 5/5、tsc/lint退出0；本项独立提交 |
| 4 | CR-04 | 文档元数据保存冲突后的刷新覆盖未保存输入 | 待复现 | — |
| 5 | CR-05 | 捕获回收异常覆盖主异常并中断后续回收 | 待复现 | — |
| 6 | CR-06 | 条件性：同摘要定稿目录成员损坏后仍被复用 | 待复现 | — |
| 7 | DEV-01 | 独立验包和新增字典/组合的真实业务链路 | 待执行 | — |
| 8 | DEV-02 / 05-R | 官方规则映射与逐批实现，区域pending范围 | 待逐批执行 | 23/147已实施为基线，124条完整覆盖待评估 |
| 9 | DEV-03 / 03-F | Formal持久上下文、入队与捕获双层绑定 | 待实现 | 正式资格依赖05-R/V |
| 10 | DEV-04 / 05-V | 固定版本外部验证器真实对照 | 依赖待就绪 | 既有记录无可用外部验证器；不得以准备文档替代执行 |
| 11 | DEV-05 / 04-P | Linux Python内容与资源宿主 | 待实现 | 现有非Windows拒绝不算执行验收 |
| 12 | DEV-06 / 03-F | Formal捕获、ZIP、测试渠道交付、回执、重启复读 | 依赖待就绪 | 依赖DEV-02/03/04合格范围 |

## 边界与工作区

- QA07-01/02和05-S维持已声明关闭范围；03-F、04-P、05-R/V保持开放，依据[原关闭清单](QA-07-CLOSURE-CHECKLIST.md)。
- 历史657/657是选定Windows回归，不是本轮运行，也不是完整资格。新验证按候选分别记录，不累加重叠用例。
- 既有`scripts/remote-check.ps1`修改及未跟踪备份保留，不纳入任务提交。
- 文档入口修改前备份：`backups/review-followup-20261117-085843/`；各修复备份按CR编号归档。

## CR-01 发布完整性失败终态

实现：生成后先验证实际产物，完整性错误构建Failed候选并保留原始findings；报告写入并复读后才以旧lease/token落终态，再写历史摘要。Failed保留OutputPath定位报告、PackagePath为空，报告API可读失败报告而包下载继续拒绝。Warning保持成功。只对实际生成后的检查累加错误数，发布前阻断不增加伪造产物错误。

验证记录（Windows本机、真实文件、InMemory持久化，不代表PG/浏览器/全套）：

- 初始原生守卫候选`review-20261117/cr01-red`及`cr01-red-valid`各1/10通过，原因是守卫未许可报告pending晋升，回收站调用被拒绝；均为环境失败，不记业务红灯，保留原件。
- 有效红灯命令：`pwsh -NoProfile -File ./scripts/local-check.ps1 -Task msbuild-publishing`。证据`.artifacts/local-check/20261117T092517-67d03d5e5df54a2ca5ada78f408e212d/`，构建三项退出0，VSTest退出1，70/79、0跳过。六项新增错误产物用例在Succeeded断言失败；三个旧集成失败单列如下。
- 最终构建同命令：`.artifacts/local-check/20261117T093310-342630f533a24b6fa8d1e3a7ccbecbe9/`，Api/Migrator/Tests退出0且零警告/错误。原发布组76/79、VSTest退出1，仍仅三项基线失败。四个产品/测试改动文件逐一匹配该构建source-manifest。
- 同构建最终定向运行：子目录`cr01-final/`保留完整实际VSTest命令、TRX、process.json与summary.json；服务10项、验证器18项、报告兼容/路径/取消8项，共**36/36、0失败/0跳过、退出0**。源码清单SHA256 `9035c8dda595819c5d07b209e3002910229added59e4a97a053f8c53aed45d19`。
- review首轮发现提前阻断会多计3个错误，已限定verifiedIntegrity并补断言；最终只读review PASS，四个交付输入摘要与最终TRX/进程回执核对通过。runner范围说明从71改79仅修正文案，不改变本次测试程序集。

基线失败：`MultiInstanceRoundTripTests`两项缺`us.applicantIdentifier`；`PublishJobServiceRealEctdIntegrationTests`一项在既有成功断言失败，完整日志保留，尚未确认其根因。这些在CR-01产品改动前已失败，不计本项通过范围；进入DEV-01前须处理或说明实际适用范围，不能声称原发布组全绿。

## CR-02 ZIP成员与交付字节一致性

实际ZIP成员采用精确相对路径核对交付文件，拒绝重复、大小写/文件目录冲突、额外成员和非规范路径；长度及有限缓冲逐字节比较含末尾EOF检查，循环响应取消。正常父目录条目可省略，已有空目录保持原Warning语义。

- 红灯：`.artifacts/local-check/review-20261117/cr02-red/`，原18项通过、新9项在IsConsistent断言预期失败，VSTest退出1、脚本退出0，summary.passed=false。
- 实际命令：`pwsh -NoProfile -File ./scripts/local-check/native-no-delete/run-package-regression.ps1 -Evidence <上述候选绝对路径> -QualifiedGuard C:/Repositories/RATools-for-eCTD/.artifacts/local-check/service-retention-20261110/qualification-2 -Filter 'FullyQualifiedName~PublishOutputVerifierTests'`。
- 首个green因CA1859编译失败，未执行测试；具体参数类型已修正。最终`cr02-green2/`构建三项退出0、零警告/错误，原生守卫测试**27/27、0失败/0跳过、退出0**。
- 同构建`cr02-green2/integration/`实际VSTest完整命令/进程回执/TRX记录**45/45、0失败/0跳过、退出0**，包括27项验证器、10项服务、8项报告兼容/路径/取消。计数重叠不相加，完整发布组未重跑。
- 最终source-manifest SHA256 `7f2517c9ac327d61dd7897e80ba2216c9680626281995947442e0d414dfb52b1`；六个关联交付输入核对一致。静态及最终证据review PASS。范围为Windows选定文件/服务测试，不声明PG、浏览器、Linux或Formal资格。

## CR-03 多文件批次排序

有效文件逐个携带本批position，页面使用同一闭包的已有最大排序+1+position。异步刷新不更换运行中的批次回调，中间已处理失败留下空位，后续成功文件顺序保持。

- `node ./scripts/local-check/review-workspace.mjs C:/Repositories/RATools-for-eCTD/.artifacts/local-check/cr03-red-20261117`：3通过/2预期失败、退出1；旧闭包实际得到[8,8]，期望[8,10]。
- 同命令使用`cr03-green-20261117`：**5/5、0失败/0跳过、退出0**；限定真实hook和jsdom，hashes/tests.json/summary.json保留。测试中的8+position模拟页面排序，页面真实公式由静态review确认，未宣称实际页面/API执行。
- frontend目录执行`node --import ../scripts/remote-check/no-delete.mjs node_modules/typescript/bin/tsc --project tsconfig.app.json --noEmit --incremental false`及同守卫下ESLint检查三个改动文件，均退出0。
- 最终只读review PASS，限单批次顺序；真实浏览器仍为DEV-01。备份为`backups/review-followup-20261117-085843/CR-03/`。
