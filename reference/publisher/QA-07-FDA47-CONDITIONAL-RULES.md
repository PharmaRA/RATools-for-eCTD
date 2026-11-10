# QA07-05-R：FDA 2041／2045与补充生效类型输入

本批于2026-11-10开始，2026-11-11完成本地选定验收。基线 `9e33c58`；新增2041、2045，当前官方清单 **20/147条已实施，127条完整覆盖待评估**，内部目录63条。最终 **543/543通过、0失败、0跳过**；原479项测试名称多重集合完整保留，新增46项规则、17项输入及1项运行时OpenAPI用例。前端19/19，类型／lint／契约／目录检查通过，最终代码和证据只读复审通过。见[固定验收回执](fda/4.7/conditional-verification.json)。

## 规则范围

| 编号 | 本批行为 | 原件依据 |
| --- | --- | --- |
| 2041 | efficacy、CMC、labeling、REMS补充类型，且子类型为application时，submission-id须有非空补充生效类型。未知或歧义条件返回NotEvaluated；多申请不互借字段。只检查缺值，码值合法性仍由2013负责。 | FDA准则4.7 PDF第52页；M1规格2.6 PDF第9、11、12页，印刷页7、9、10 |
| 2045 | 行政区submission-information下的form只允许fdaft1（1571）、fdaft2（356h）；缺失类型不能建立位置资格。本条不声称覆盖所有M1位置或专利表单映射。 | FDA准则4.7 PDF第53页；M1规格2.6 PDF第20页表10，印刷页18 |

两条官方严重程度均为Medium，映射Warning；requiredForReadiness及禁止人工关闭保持有效，不能将Warning理解为允许正式就绪。生效日均为2015-06-15，按绑定选择日期执行。

M1正文III.B.2.a只列三种补充类型，但表2／3／4明确包含REMS。本批依据明确表格行纳入fdast11并记录差异；不根据submission-id等于sequence-number猜测首次提交，也不推断未选择的历史。

## 不可变来源与兼容性

[条件策略](fda/4.7/m1-conditional-policy.json)是独立的ProjectDerivedEvidence，算法fda-m1-conditional-v1，原始SHA-256为 `a3af8c7479c18843992ecdfe6b02a31c4ab437655f443fdd5099039ac2b40673`。它绑定M1规格、US DTD及四份官方字典；运行时同时核对归档目录的策略ID、类型、版本、资格状态和摘要。原属性策略fda-m1-attributes-v1及其字节保持不变。

缺少或不支持的归档资源不能由当前仓库替代；可读旧目录不会自动新增这两条规则。区域引擎为package-regional-inspector-v6。新字段 `us.supplementEffectiveDateType` 可空，null不写入JSON，旧记录不因新增空字段改变序列化身份。

## 输入与XML链路

新增补充申请生效类型字段，使用所选快照的官方码值；提交类型、子类型和表单字段也获得对应归档字典选项。两个required-all条件同时满足时显示必填。切换条件保留原值供修正，不自动填值或转换旧别名。

保存／预检定位缺值、字典非法值，以及切换后的已知不适用／适用性未知；草稿原值保留，writer在发布前拒绝这些输入。合法值写入submission-id/@supplement-effective-date-type，并经独立验包、XML导入回到同一typed字段。完整行政表单编辑与所有补充类型／生效类型组合资格未在本批宣称完成。

## 验证过程

证据根 `.artifacts/local-check/fda47-conditional-20261110/`；重要文件备份 `backups/qa07-fda47-conditional-20261110-234156/`。

- red-1：511项中479通过、32预期失败；缺少新规则结果，实际三个MSBuild成功。
- green-1：525项中524通过、1失败；失败为引擎版本断言仍期待v5，已更新为v6。
- inputs-red-1：测试项目被新增测试的CA1861数组写法阻断，未运行测试；不能计为业务红灯。
- inputs-red-2：13项中2通过、11预期失败，覆盖字段反序列化、字段诊断和Schema缺失。
- ui-red-1：18通过、1预期失败；required-all修复后ui-green-1为19/19，0失败／0跳过。
- green-2：539/539，包含原479项、46项规则用例、13项输入链路用例及1项运行时OpenAPI一致性测试；只读review随后发现输入切换场景缺口，本候选不是最终验收。
- 类型检查、定向lint、生成契约比较和63条目录／来源校验均已通过，回执为static-*-2。
- review-red-1：17项中13通过、4预期失败。复现原始申请、补充amendment以及未知提交类型／子类型仍保留生效类型的缺口。修复后green-3为543/543，草稿原值保留，已知不适用和无法确认适用性分别诊断，writer拒绝输出；原历史修订保持不变。最终只读复审无新增阻断发现。

原件首次提取因GBK输出失败；同目录重试被守卫拒绝，换用新证据目录和UTF-8后提取成功。API生成器首次按CJS入口解析失败，改用其声明的ESM入口后成功。静态检查包装器首次工作目录与夹具根不匹配，被守卫在启动前拒绝；项目根下的第二次执行通过。这些执行错误保留，未冒充业务红灯或成功结果。

## 仍开放的资格

本批为Windows选定回归、React/jsdom组件与TestServer合同验证，未新增真实PostgreSQL、浏览器、Linux、外部业务验证器或Formal成功资格。用户已确认目前无可用独立验证器；[外部对照准备](QA-07-EXTERNAL-COMPARISON-PREPARATION.md)记录工具前置与本批对照设计，05-V未关闭。

QA07-05-R/V、03-F、04-P继续按[固定关闭清单](QA-07-CLOSURE-CHECKLIST.md)执行。
