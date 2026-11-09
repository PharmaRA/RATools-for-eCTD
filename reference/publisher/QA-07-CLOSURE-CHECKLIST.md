# QA-07 固定关闭清单

状态日期：2026-11-09。依据为[质量计划中的原始五条要求](../../docs/plans/quality-improvement.md#qa-07联合回归与资格验收)。最新进展：FDA/EMA官方原件下载阻塞已解决，29个成功来源已冻结并核实版本与适用日期；生产来源快照绑定、规则映射和独立工具实际报告仍待完成，见[来源核验记录](QA-07-OFFICIAL-SOURCES.md)。本轮没有新增产品验收运行。

**当前结论：原始五项中，2项按已声明范围关闭，2项部分完成，1项未完成。QA-07 总项保持开放。** 后端基线的1824/1824是跨运行去重覆盖；浏览器检查、资源探针和还原步骤单独计量，不加入这个总数。

## 1. 原始五项及关闭状态

| 固定编号 | 原始要求 | 当前状态 | 已接受的证据与边界 | 仍缺什么 |
| --- | --- | --- | --- | --- |
| QA07-01 | 不含物理删除的后端定向 runner 和源码契约检查 | **已关闭，限原始定向范围** | [后端审计](BACKEND-NO-DELETE-AUDIT.md)、[原生验收](QA-07-NATIVE-ACCEPTANCE.md)：跨运行1824/1824，Windows实际三项目MSBuild零警告/错误；源码契约及TestServer运行时OpenAPI对照已记录。最后PG内容2/2及独立review通过。 | 无剩余基线用例。同次无过滤全套是另列的增强验证，不阻止这一原始定向条款关闭。 |
| QA07-02 | 真实PostgreSQL空库/旧库升级、重复迁移、约束、并发和恢复 | **已关闭，限已声明场景** | [验收账本](QA-07-ACCEPTANCE.md)：空库31、旧库30→31、原数据保留、重复0；[环境恢复](QA-07-ENVIRONMENT-RECOVERY.md)：PG107；后续PG/qpdf7和Windows内容2；[浏览器记录](FINALIZATION-ACTUAL-BROWSER.md)：真实API/PG正常停止重启及报告字节复读。结果各自绑定源码与工具，不相加为单次总数。 | 无已声明场景的剩余用例。未覆盖全部历史迁移和机器断电，不据此重新打开已通过场景。 |
| QA07-03 | Chromium端到端装配、定稿、交付与区域表单链路 | **部分完成** | 区域表单/快照、Development装配/定稿、非空PDF原生下载、API/PG重启复读有独立证据；[区域矩阵](REGIONAL-QUALIFICATION-MATRIX.md)保留区域表单和拒绝门禁证据。Development交付拒绝正确不等于Formal成功交付。 | **QA07-03-F：Formal捕获、交付登记及回执的实际业务链路；同时有实现和资格依赖。** |
| QA07-04 | Linux路径、监听、取消、qpdf/Python资源和大小写文件系统门禁 | **部分完成** | Linux平台10、大小写3、qpdf54/PG-qpdf7及[资源与进程树探针](LINUX-PDF-RESOURCE-LIMITS.md)通过。Windows原生内容通过，不能替代Linux Python。 | **QA07-04-P：Linux Python内容宿主及资源资格。** 当前产品主动拒绝该平台，不是安装解释器即可解决，也不能按“不支持”自动豁免原始条款。 |
| QA07-05 | 外部验证器、官方当前资料和区域业务范围对照 | **未完成** | 现有[区域矩阵](REGIONAL-QUALIFICATION-MATRIX.md)为11行、5个selected trial/6个pending；DTD结构对照有证据。2026-11-09已取得FDA准则4.7及EMA3.1.1/8.2等29个[当前来源](QA-07-OFFICIAL-SOURCES.md)，核实官方DTD与仓库字节。完整区域业务资格仍缺。 | **QA07-05-S/R/V：生产来源快照绑定、规则与范围映射、独立固定版本验证器对照。** 原件下载已解决；外部工具当前入口/edition/规则包仍待定位。 |

## 2. 剩余任务的输入、产出和通过条件

| 任务 | 具体缺口及直接依据 | 必须产出的证据 | 通过条件 |
| --- | --- | --- | --- |
| QA07-05-S 官方来源 | **部分完成**：29个原件与离线来源快照已核实；FDA当前4.7与历史catalog4.5需协调，生产`StandardsProfileSnapshot`资源绑定待完成。 | 已保存原件、URL/UTC、版本/日期语义/SHA、历史EMA资产对照；后续把固定来源资源绑定到新生产快照。见[本轮记录](QA-07-OFFICIAL-SOURCES.md)。 | 声明范围所需原文可复核，版本及适用性与快照绑定。HTTP200但正文为拒绝页不得通过。 |
| QA07-05-R 规则与范围 | [区域矩阵](REGIONAL-QUALIFICATION-MATRIX.md)及现有catalog没有完整业务规则资格证据。 | 固定矩阵/规则清单；逐规则官方出处、适用条件、severity、实现或缺口、正反例与结果；记录不适用理由。 | 选定范围内的必需规则无Missing/Unverified；矩阵剩余6行逐行明确实施并验收，或给出有依据且经确认的范围排除。不能把5行trial写成11行正式通过。 |
| QA07-05-V 外部验证器 | DTD工具通过仅证明结构，不能替代区域业务验证器。 | 可合法运行的独立验证器、精确版本/规则包与适用范围；同一包和变体的输入摘要、原始报告、内部/外部逐项差异及处置理由。 | 外部验证真实执行；声明范围内无未解释的必需差异；结果可由版本、输入与报告重现。未知许可证/规则包是否可用属于待核实输入，不能预先声称必须购买或只需用户提供。 |
| QA07-03-F Formal实现与链路 | [ReleaseCapturePlanner](../../src/RATools.Application/Finalization/ReleaseCapturePlanner.cs)在有snapshot的Formal分支无条件追加`RegionalBusinessContextUnconfirmed`；[FinalizationService](../../src/RATools.Application/Finalization/FinalizationService.cs)要求Formal、ready及manifest绑定ZIP。仅补外部文件不会自动打开成功路径。 | 持久化区域上下文与验证结果一致性检查的实现/回归；有资格的快照/样例；真实API+Chromium的Formal捕获→ZIP核对→交付登记→回执登记→重启复读；缺失、过期、上下文或ZIP不匹配的拒绝证据。 | 资格满足后原业务入口成功，数据库与不可变ZIP/回执一致；无资格与Development仍拒绝交付。禁止直接删门禁、手动把Unverified改Verified或伪造实际提交回执。受控验收记录必须标明测试渠道。 |
| QA07-04-P Linux Python | [PdfContentProcess](../../src/RATools.Infrastructure/DocumentProcessing/PdfContentProcess.cs)拒绝非Windows；[PythonPdfContentEngine](../../src/RATools.Infrastructure/DocumentProcessing/PythonPdfContentEngine.cs)返回`CONTENT_HOST_RESOURCE_ENFORCEMENT_NOT_QUALIFIED`。这是未实现/未资格能力，不是测试失败或零成本平台切换。 | Linux内容宿主实现及独立资源/禁删保护；固定Python/PyMuPDF/qpdf及所需模型；真实内容处理与原业务断言，内存/CPU/取消/异常及整棵进程树停止证据。 | Linux真实执行、不跳过、不走拒绝早退；业务输出和资源约束均通过。若只交付Windows内容能力，须另有明确范围修订记录；本次整理未批准或假定该豁免。 |

执行顺序：先核实QA07-05-S的官方来源与QA07-05-V的工具/规则包输入，固定QA07-05-R矩阵；QA07-04-P是独立工程工作，可并行推进；QA07-03-F的上下文实现可先修复并验证拒绝路径，正式成功验收须待S/R/V资格条件满足。每项只以本表通过条件关闭，不以“环境已就绪”代替业务验收。

## 3. 更广工程声明及已解决的前置

- **同次无过滤完整后端套件**：尚未运行。原条款QA07-01明确要求定向runner；该增强验证单独跟踪，不自动追加为原条款的关闭前提。以后若执行，须明确平台条件用例、skip和不支持能力，不能把跨平台分组计数改称同次全套。
- **fresh restore**：Linux空包缓存实际NuGet还原已经通过，见[环境恢复记录](QA-07-ENVIRONMENT-RECOVERY.md#postgresql-platform-and-fresh-restore)的`linux-restore-20261108T211940-ae195e02bf184d74a074027fe67bcb53`；Windows此前尝试失败。不可再笼统写成“fresh restore未运行”，也不外推成新OS/SDK安装资格。
- **资源、浏览器与数据库同一个巨大联合运行**：已有各自绑定的专项证据，没有同次联合证据；原始五条未要求必须合成一个执行。新集成场景须说明新增的风险和断言，不能仅因它尚未运行而撤回现有专项验收。
- **官方当前版本**：本轮已刷新声明范围内FDA/EMA官方页面及直接引用原件，固定取得UTC和摘要。FDA准则4.7发布日期不等于每条规则的生效日；2024/2025/2026三条为2027-01-16生效。离线来源快照不等于生产profile激活，S的资源绑定和R/V继续开放。

## 4. 状态与变更规则

QA-07总项只有在五行均满足关闭条件时才能关闭。当前未关闭工作固定为03-F、04-P和05-S/R/V；后续新问题归入相应编号，新增验收要求必须注明来源与理由，不能用“其他资格”替换具体清单。

已关闭项仅在直接依赖变更、证据失效或发现相关缺陷时重开，并记录原因；文档整理无需重新跑1824项。历史记录中的“剩80/19/2项”等数字保留为当时结果，由本文和各文档首页给出当前状态。

执行状态以[质量计划](../../docs/plans/quality-improvement.md)为入口，本文固定验收判据；[验收账本](QA-07-ACCEPTANCE.md)、[原生验收](QA-07-NATIVE-ACCEPTANCE.md)、[浏览器验收](FINALIZATION-ACTUAL-BROWSER.md)保存已有证据。`docs/`继续仅保留本地，本文受Git版本管理。
