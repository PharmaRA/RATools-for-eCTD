# QA07-05-S 生产标准快照来源绑定

2026-11-09。在[官方来源取得](QA-07-OFFICIAL-SOURCES.md)的固定原件上，生产 `StandardsSnapshotService.CaptureAsync` 已增加官方来源捕获。新FDA快照以准则4.7为目标，EU保留M1 3.1.1／准则8.2／指南6.0.1；历史快照仍通过自己的归档profile、规则和资源读取。

## 生产行为

[来源绑定清单](official-sources/2026-11-09-production/bindings.json)固定31份原件：此前29份，加上重新取得的ICH 3.2.2规格PDF和EMA Harmonised Guidance 6.0.1。FDA选择22份原件，EU选择10份，其中ICH由两区域共同引用。清单SHA-256为 `627ec5b1fd05231a7d2524b8b48194f582c0d881e78b07dc24190e5c40f52f03`；此前29源快照摘要保持 `67cfb2e3d35631945cd8ba66c0deed6044ce26bc9bd514411b4953caad24ba84`。

匹配完整的template、region、ICH、regional M1、guide、criteria组合，并核对profile引用全部属于该来源集后，捕获才会创建Verified来源证据。每份原件作为非空 `OfficialSource` 资源保存，provenance绑定相同SHA、版本及2026-11-09取得／核实日期。`Sources`证据指向同为`Sources`类别的绑定清单；原来源清单另存为`SourceInventory`，保留URL、取得UTC与逐规则适用日期。

原件随应用内嵌，按固定摘要和长度核对后进入快照；部署后的捕获不依赖源码目录中的PDF文件。资源继续受单项8MiB、总量32MiB及64项上限约束。读取和HTTP下载使用快照归档字节，保留正确media type、ETag与Range行为。官方参考PDF属于证据资源，没有加入 `profile.Assets` 的DTD/XSL交付资产列表。

新增的归档一致性检查比较同一快照内部profile与descriptor的Region、Guide、Criteria；不会拿当前provider拒绝旧快照。版本或引用不匹配的新profile保持来源Unverified；早于来源取得日的新捕获被拒绝。原文缺失或替换不能通过既有快照完整性验证。

## 与规则资格的边界

`Sources=Verified`只表示官方原件、版本和适用性证据已绑定。生命周期仍为AcquiredNotActive，范围仍为TrialOnly／Finalize=false；ControlledVocabulary、MetadataMapping、Writer、Rules及ExternalComparison继续未资格。没有通过设置Active绕过这些条件。

[FDA 4.5→4.7差异清单](official-sources/2026-11-09-production/fda-criteria-delta.json)记录原文定位、severity、生效日、当前实现缺口及所需正反例：

| 官方规则 | 版本变化 | 适用日期 | 当前实现边界 |
| --- | --- | --- | --- |
| 7 | 4.6修改Problem／Corrective Action | 2018-07-27，High | 适用可填写表单存在性仍缺；`US-FORM-ATTRIBUTES`只检查属性，不替代该规则 |
| 2024 | 4.7修改Effective Date | 2027-01-16，High | 同一活动的跨序列submission type一致性尚未取得官方规则实现资格 |
| 2025 | 4.7修改Effective Date | 2027-01-16，High | 新活动的sequence／submission-id对应及subtype适用性尚缺 |
| 2026 | 4.7修改Effective Date | 2027-01-16，Medium | 延续活动指向父活动起始序列的规则尚缺 |

本次来源绑定验收时（提交a176396），43条catalog规则的`authorityRuleId`均为空。FDA来源直接关联的是DTD结构检查和仍为NotImplemented的`US-CRITERIA-INVENTORY`门禁；该次验收没有改写catalog。后续按用户确认直接实施FDA4.7全文，当前147条来源清单及首批九条原子规则见[FDA4.7实施记录](QA-07-FDA47-RULES.md)，历史声明保留原时点范围。

补充EMA指南封面的2025-03-01指version6.0，6.0.1只明确June 2025发布；没有把前者当成6.0.1生效日。FDA准则4.7的文件修订日也不替代各条规则Effective Date。

## 验证与保留

修改前定向验证`red-3`为3通过／13失败／0跳过，覆盖官方资源缺失、Sources未核实、背日期未拒绝及三种归档版本错配未拒绝。实现后`green-1`新增专项16/16；增加未知引用反例并调整直接相关旧断言后，`green-2`标准定向回归85/85、0失败／0跳过，含真实HTTP原文下载、Range及ETag。三项目Windows MSBuild使用缓存依赖、warnings-as-errors构建通过。

首次既有回归`standards-regression-1`为81通过／3失败／1跳过：一项旧断言仍要求来源未核实，两项资产解析夹具的收尾移动被禁删保护拒绝。修正旧断言后其余Formal拒绝仍保留；解析夹具改为直接保留其任务目录，原业务断言未变。TRX旧汇总字段未反映skip，临时runner已改为统计实际NotExecuted并要求passed等于total。后续数据库用例单独运行，不用条件跳过补充通过数。

本轮证据保存在 `.artifacts/local-check/official-binding-20261109/`；重要文件修改前备份在 `backups/qa07-official-binding-20261109-195425/`。补充两个PDF共190页在已资格原生禁删保护下提取，失败EMA拒绝页保留且排除。构建、运行、进程树和来源摘要均保留各自回执，不执行物理删除。

**QA07-05-S已完成来源绑定及声明的数据库验收。** [最终验证回执](official-sources/2026-11-09-production/verification.json)固定11个代码/测试输入摘要、31份原件、构建与执行证据；[数据库捕获元数据](official-sources/2026-11-09-production/postgres-captures.json)保留FDA及EU实际快照ID、来源资源与出处。Windows标准定向回归为85/85；Linux数据库本机的五个原始用例为5/5，分属独立运行，不合称同次90项或全仓库通过。

Linux v5运行相同Windows构建的托管程序集（测试DLL SHA-256 `840a16d77f297d98bac68a09e6157ad918a8b884526d742bc6a1a171645ea079`），不是Linux重新构建。五个独立VSTest host分别验证双连接并发归档与首次捕获复读、不可变数据库保护、数据库身份错配拒绝、迁移与运行时模型一致、设置Active及范围Verified仍不能绕过缺失Formal证据。原双连接用例内部的并发断言保持原样；每份TRX只有一个Passed，合并名称多重集与原五项完全一致，没有skip。

PG 16.15实际迁移31／重复0，服务身份与本次拥有数据库一致。全部迁移、发现和测试阶段exit0，无阶段超时或残留进程；首次ready-0退出2是启动探测，后续ready-1成功。PG正常退出0，server/native残留为空。独立systemd manager退出0，MainPID=0、ControlGroup为空、Result=success；RemainAfterExit使ActiveState仍显示active，不代表有活进程。Windows本地传输守卫4guard=4Job、整树停止，传输无timeout/truncation/OOM。16份Linux证据的manifest SHA-256为 `e30f564fdd1b0179da5e284e48ec02198cfa840c82dd9a1bb9459e7e4e3e7a33`。

US回执含测试专用DisplayName；EU回执故意设置Active及verified scope以验证Formal仍被拒绝，不能当成生产激活。来源Verified没有改变Rules、ExternalComparison和正式业务范围的资格门禁。新来源状态由上述生产回执记录，历史29源清单的Pending字段保持当时原文。

环境失败保留在同一证据根：`pg-sources-1`阶段超时；`pg-sources-2`迁移用例通过但双连接用例Npgsql读流超时；Linux首次与v3的systemd结果为oom-kill，均无完整五项通过回执。这些失败没有被覆盖或重新标成通过，也未据此确定Windows读流超时只有网络这一种根因。历史[环境阻塞回执](../../.artifacts/local-check/official-binding-20261109/qa07-environment-blocker.json)由本次成功回执补充解决记录。

本次在远端根文件系统新增任务拥有的2GiB swap，并保留移动经所有权核对的陈旧postmaster.pid后完成v5。swap位于 `/var/tmp/ratools-qa07-swap-4cd3f24a749a40949d248e683553c302/task.swap`，0600、普通文件、单硬链接；收尾查询仍启用，使用496296KiB（约485MiB），没有修改fstab或停用swap。**环境未恢复到操作前状态**，swap与数据库/证据保留。配置的256MiB仅限制单个.NET进程托管GC堆；OOM调查中记录过268705792字节服务峰值，不宣称两次失败都有这个峰值，也没有整棵进程树RSS资格结论。禁删守卫、失败材料和受控回收材料均保留，无物理删除。
