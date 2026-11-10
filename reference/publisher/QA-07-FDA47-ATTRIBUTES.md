# QA07-05-R：M1 官方字典、必填属性与提交组合

2026-11-10，本批实现 FDA 4.7 的 **1496、2012、2013、2022、2034**。当前官方清单 **18/147 条已实施，129 条完整覆盖待评估**；内部目录 61 条。最终 Windows 选定回归 **479/479 通过、0 失败、0 跳过**，包含前批全部 378 项和本批 101 项。前 378 项测试名称多重集合完全保留，旧 56 条目录定义未改写。

可用行为：对选定标准快照下的实际 M1 XML 检查官方码值、必填属性及申请/提交组合，报告包含原值、节点、属性、行号与输入文件地址。例如 `application-type="nda"` 产生 1496/2013 诊断；NDA 的正式码值是 `fdaat1`。对完整性有效的可读快照，缺失来源或不支持的来源返回 NotEvaluated，不借用当前仓库文件。若描述符仍声明已丢失或已修改的原件，快照完整性检查会先拒绝它。本批完成独立验证规则；现有输入中的别名不会自动转换，也未新增字典下拉控件。

## 五条规则

| FDA 编号 | 检查内容 | 生效日 | 来源页 |
| --- | --- | --- | --- |
| 1496 | 主申请允许 active 的 fdaat1–6；交叉引用另允许 7、9、10；FDA 专用 fdaat8 拒绝 | 2013-04-30 | 23 |
| 2012 | 已出现元素上全部 16 组 DTD REQUIRED 属性缺失或空白 | 2015-06-15 | 44 |
| 2013 | 十份官方字典的准确 active 码值，以及 11 组 DTD 枚举/FIXED 声明 | 2015-06-15 | 45 |
| 2022 | 提交子类型与已识别申请/提交组合的适配 | 2015-06-15 | 46 |
| 2034 | 提交类型与申请类型的适配 | 2015-06-15 | 49 |

五条均为官方 High，映射报告 Error，保留 requiredForReadiness 和禁止人工关闭。完整规则、外部对照、区域资格及 Formal 门禁继续有效。

十字典覆盖 application、submission、submission subtype、applicant contact、telephone、supplement effective date、form、promotional audience、promotional document、promotional material。`fdapmt17/20/34` 在选定字典中 inactive，`fdapmt47` active。补充剂属性归属 `submission-id`。

2012 只检查已出现元素，不要求可选元素出现。`leaf` 必填 ID/operation；`xref` 必填 xlink:title/xlink:href；leaf 的 href 和 title 的 ID 可选。FIXED 默认属性缺席不报缺值；自由文本 ID、material-id 不当作字典枚举。条件性 supplement-effective-date（2041）与 form 位置（2045）仍未实施。

组合来自 FDA M1 规格 2.6 表 2、表 4：11 行覆盖 11 种提交类型、6 种主申请类型、7 种子类型。各 application 独立取值，不跨申请借字段；同一申请内重复父节点或值节点不猜测为唯一值。2034 不依赖子类型完整；2022 已知的 submission/subtype 冲突不会被缺失 application-type 掩盖。明确冲突保留 Fail，缺失或歧义另记 NotEvaluated。

## 来源与快照边界

[派生策略](fda/4.7/m1-attribute-policy.json) 标为 ProjectDerivedEvidence，算法 `fda-m1-attributes-v1`，自身 SHA-256 为 `b4ed79e15ac4fe37e32496495c34fa209001adbb16bc784f16d1a25f0d513ed9`。它绑定十份 XML 的摘要/root/version、官方 M1 PDF 和 DTD：

- [M1 规格 2.6](official-sources/2026-11-09/fda-m1-specification.pdf)：`1adcbc40ad55adc63b3ae0c6ab6428721ccf6c8f3f64a4a1be58b1ee276b87f2`。
- [US DTD 3.3](../dtd/us-regional-v3-3.dtd)：`d45139785dd6aa7eb3a60a23854ec2ea7d5d17c7c87b9ca12ab268baafc1dbec`，与官方 ZIP 对照一致。
- [准则 4.7](official-sources/2026-11-09/fda-criteria.pdf)：`2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c`。

新快照捕获独立策略资源；检查只读经过完整性验证的快照字节。运行时同时验证目录派生来源的 ID、ProjectPolicy 类型、版本、Verified 状态和摘要。在快照自身完整性有效的前提下，缺资源、未知摘要或目录来源错配使本批五条 NotEvaluated，旧规则按自身依据执行；没有这五条的旧目录不注入新规则。区域引擎升级为 `package-regional-inspector-v5`。

CDATA 字段精确 Ordinal 比较，不转换展示名、大小写或尾空格。DTD 枚举使用 XML 解析器已规范化的 token 值；xlink 属性要求真实命名空间 URI。M1 PDF 第 8 页示例存在 `fdaat1 ` 尾空格，与字典不一致，已留痕且没有据此全局 Trim。字典 AsOf 跨多个年份，未提供完整历史状态迁移；本批仅声称按选定快照重验，不还原历年 FDA 收件时状态。

## 验证与修复记录

[固定验收回执](fda/4.7/attributes-verification.json) 保存来源、目录、构建、测试与守卫摘要。证据根 `.artifacts/local-check/fda47-attributes-20261110/`；重要改动备份在 `backups/qa07-fda47-attributes-20261110-104710/`。

- `red-1`：48 项全部因缺少新规则结果失败，构建成功。
- `green-2`：94 项中 81 通过、13 失败。复现补充剂属性错误归属导致漏检；其余为测试未先满足快照 provenance 完整性约束，以及定位断言未处理 DTD 前置发现的空 NodePath。修正后仍断言外层完整性拒绝，再用可读旧快照验证降级。
- `review-red`：478 项中 469 通过、9 失败。6 项证明派生目录来源撤销/错配仍错误 Pass；3 项证明有效集成夹具使用展示名被新规则拒绝。实现补足目录来源绑定，夹具输入改官方码值；原无失败和真实归档逐字节一致性断言保留。
- `dispatch-red`：旧项目规则仅贴相同 authority 编号会误入 FDA 派发，1 项复现 NullReferenceException；改为同时要求受支持的内部规则身份。
- `green-3`：最终 **479/479**。101 个新 xUnit 用例含十字典正反例、16 组必填关系、143 次表 2 组合枚举、缺值/歧义/多申请、来源及目录错配、生效日、旧目录和 Formal 拒绝。来源/设计审查及两项 P2 修复后的只读代码复审通过。

`green-1` 因同一 PowerShell 重复加载守卫类型未开始构建，错误见本次终端记录，未产生构建或测试回执；后续使用独立进程，未改守卫。最终三个实际 MSBuild 项目成功，11 个交付构建输入与 manifest 摘要一致。独立 Python 检查通过：61 条目录 canonical SHA-256 为 `b0bc927f62fc6f34edb1063534eb0650bd5a71ce9677b7ef2db67ca8975cfdf6`；从固定 DTD 独立提取核对全部 16/11 声明、十字典属性归属与来源字节。

构建、测试、Python 检查沿用已资格原生禁删守卫。测试进程 5/5、构建进程 26/26 在恢复前受保护，根退出均 0，进程树已停止；实际项目/测试之外，两个拒删探针按预期非零退出，残留 conhost 由守卫收尾终止，forcedTreeStop 如实记录。32 次夹具回收决定均允许且只读确认身份，覆盖四种既定关系；未执行物理删除，构建缓存、探针和失败日志保留为证据。

本批不新增真实 PostgreSQL、浏览器、外部业务验证器或 Formal 资格。QA07-05-R/V、03-F、04-P 继续按[固定关闭清单](QA-07-CLOSURE-CHECKLIST.md)处理，QA-07 总项未关闭。
