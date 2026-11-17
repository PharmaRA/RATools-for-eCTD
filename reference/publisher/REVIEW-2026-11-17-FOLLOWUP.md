# 2026-11-17 审阅后续执行账本

当前最新：FDA1322空目录检查121/121及最终review PASS，30/147已实施、117条完整覆盖待评估，内部74条。1204已提交`9972daa`；用户已明确批准两个已核验旧目录的跨盘回收例外，完整副本保留后移除限定/tmp源副本，最终容量回执待取回。

## 续做 FDA1322

从目标捕获清单检查既无文件也无子目录的目录；嵌套空树仅报告最末端空目录，目标外目录不并入当前规则，Finding.Files为空避免伪造下载地址。来源原件/provenance、选择日期、生效日、旧catalog/profile隔离与文件阶段总预算保持，file引擎v7。

`continue-20261117/fda1322-red`15项3通过/12缺规则预期失败；最终`fda1322-final`**121/121、0失败/0跳过、构建和测试退出0**。实际runner命令、过滤器和TRX均在候选目录，source-manifest SHA256 `eb72ab95b35b14e8533a2efa3d94fa7d75482b0f0db6ccb8cbbd01ec402c42b5`；最终只读review PASS且六交付输入摘要匹配。`python -B scripts/tests/test_package_rule_catalog.py`退出0、74条，canonical SHA256 `2efe92b778322a54f574e82642720a7c23989f494fe04030479fe63133f6fb82`。Windows定向证据不等于完整官方或Formal资格。

最新续做：FDA1034显式operation检查已完成，85/85定向回归及最终只读review PASS，当前28/147已实施、119条完整覆盖待评估，内部72条。远程浏览器原job日志已恢复，失败根因为磁盘空间耗尽，需容量恢复后再继续远程验收。

## 续做 FDA1034

1034已独立提交`f55c0d4`。

检查目标ICH/US leaf的实际无命名空间operation：缺失/空白/默认来源失败，未知显式值未评估，不自动补new；归档原件/provenance和选择日期必须匹配，整体XML未完成/共享finding预算保留未评估，已知失败优先。file引擎同步v5。

证据根`.artifacts/local-check/continue-20261117/`：`fda1034-red`18项中2通过、16因缺规则预期失败；初green82/82。新增区域leaf、低预算和内部DTD三例，首final84/85为测试误认为允许内部DTD，修正为XML-OFFLINE拒绝且1034未评估，没有放宽产品。最终`fda1034-final2`**85/85、0失败/0跳过、构建和测试退出0**；完整命令与源码摘要在summary.json，最终review核对六个交付输入一致。实际命令为`pwsh -NoProfile -File ./scripts/local-check/native-no-delete/run-package-regression.ps1 -Evidence <候选绝对路径> -QualifiedGuard C:/Repositories/RATools-for-eCTD/.artifacts/local-check/service-retention-20261110/qualification-2 -Filter 'FullyQualifiedName~FdaLeafOperationRulesTests|FullyQualifiedName~FdaLeafTitleRulesTests|FullyQualifiedName~FdaFileNameRulesTests|FullyQualifiedName~PackageValidationServiceTests|FullyQualifiedName~FdaIdentityRulesTests'`。

`python -B scripts/tests/test_package_rule_catalog.py`退出0、72条，canonical摘要`6317237fb2a521af3058c95cb997e616a49c2fa3c8caeaf0470d357cae5c4449`。范围为Windows选定回归，不是完整官方/外部/Formal资格。内部DTD用例证明既有拒绝门禁，不宣称默认属性已动态验证。

## 续做 FDA1204

按官方明确字符集检查实际捕获文件名，不作URI解码；可捕获的空格、单引号、波浪号分别有目录/ZIP回归。操作系统非法字符由捕获前置拒绝，拒绝不是FDA finding，也不会产生通过结果。共享归档授权、日期、预算及旧catalog隔离保持，file引擎v6。

`continue-20261117/fda1204-red`40项中27通过、13因缺规则预期失败；green106/106后review发现8个坏ZIP缺合法index导致异常类型断言可能假阳性，已补合法index并断言ZIP-PATHS、UNSAFE_ZIP_ENTRY及实际路径。最终`fda1204-final`**106/106、0失败/0跳过、构建和测试退出0**；同目录summary保存实际过滤器、构建/源码/测试摘要，源码清单摘要`f5027972b20a34327634a0a925e0c5e4aa26b2fe9899d09dd01c13593f64ab7b`。来源/原件/旧catalog/非US测试也覆盖新1204。目录脚本73条通过，canonical摘要`2fa882d773fcf37cab12eb48e7c8d29beeb42f6123af7936497a7268dba33bc3`；最终review PASS，六交付输入匹配最终回执。当前29/147已实施、118条完整覆盖待评估。

## 远程空间清理（已完成）与浏览器验证恢复

**Chromium短路径修复**：原profile位于深层任务目录，Unix socket超长。改为在`/data/home/ratools-browser-`前缀下用mkdtemp独占创建短profile，日志记录完整保留路径，无删除。统一命令最终`20261117T154702-7afa3f9230404239889860397546564d`、源码包SHA256 `ee30723fce8569ec60060d45ca3d628a25d68ca5eba5af11ce288e837054b034`：CLI/远端exit0、category=passed，13个步骤均exit0，真实API/InMemory与Chromium共8项业务检查通过、errors=[]。原生ZIP下载69920字节，SHA256 `4f7e06865d554e1bc1a132049b868395aae638573ee7da32a91ca4c6bd6228fa`与快照清单一致，开发快照/拒绝正式交付/新草稿与旧快照复读均通过；独立ZIP/PDF检查3/3。最终只读review PASS，无阻断项。

DEV-01下一原子项：复用当前真实Linux API/浏览器宿主，补外部ZIP目标/历史根选择、显式profile、真实findings第二页与定位、源文件和完整报告原生下载字节核对、独立停用worker实例的Pending取消；保留所有输入及下载。区域表单新增字典/组合的浏览器保存复读仍须独立覆盖，不能将当前8项记作DEV-01全部完成。

**浏览器区域夹具更新**：使用schema返回的精确snapshot，显式填写九位公司ID、提交ID、已归档官方代码及稳定联系人/电话/邮箱ID，经混合元数据接口保存后逐字段和再次GET核对。首候选`20261117T153425-018eb33431ab40dc8eb8c3c7e8ba89ea`因期望空可选字段在响应保留而失败；依据Domain的WhenWritingNull约定去掉夹具中的空字段。最终`20261117T153856-eaa39d57abbd4721bce2b895b7f4d3cf`、源码包SHA256 `68ca419c8bcf602908de5ba0da6b2eca2d6b2b36e69c682b44a3f3a07c4f9b95`：前置构建/探测exit0，实际缺失字段/保存复读/PDF装配/writer ZIP与独立校验4项通过。整体仍exit1/browser-test-failed，Chromium明确报`Socket path too long`，尚未开始UI步骤；作为下一项修复。区域夹具最终只读review PASS，无阻断项。

**Linux后端响应文件路径修复**：预算修复`b902f55`后的任务`20261117T151436-ce3e98c58a354f12a6cf2d3b0da8feb4`在应用层编译失败，CS2001显示嵌入资源PDF/XLSX的含空格文件名被拆分。`backend-build.py`逐参数使用`subprocess.list2cmdline`写入响应文件。相同统一命令再次执行，证据`20261117T151914-c6184e1bb050439f8604e72ddb893611`、源码包SHA256 `2d40d6e0d4a34ced9ca6f1ab5826a4777c28382715bda647a4f5f9784deb93cd`：前端全部exit0，后端四层显式csc编译exit0；资源/回收/进程探测exit0。该结论不等同MSBuild或完整测试资格。整体仍exit1/browser-test-failed，实际原因是浏览器旧夹具将缺失字段期望为`ApplicantContactName`，现接口返回`us.applicantIdentifier`；作为下一项修复。参数修复及最终Linux编译证据只读review PASS，无阻断项。

**已完成**：用户明确批准此次限定例外后，重新逐文件核对源/目标摘要、文件集合、链接/挂载与活动进程，随后仅移除上述两个/tmp源副本。`recycle-completion.json`记录CLI/远端脚本退出0、complete=true，释放1,132,421,120字节，/tmp可用从32,768增至1,132,453,888字节；65825个文件完整副本仍保留在/data回收目录。未清理其他任务、未销毁/关机实例。后续远程构建统一使用`-RemoteRoot /data/home/ratools-remote`，不在回收目录执行。

容量恢复后实际命令`pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task test -Target linux -Scope backend-browser -RemoteRoot /data/home/ratools-remote -WaitMinutes 1`，证据`20261117T145043-4bdfac4e2df848099e6e21418347b40e`。任务exit1、category=build-failed，结果已完整下载；依赖、启动/取消辅助检查、TS及Vite均exit0，budget.log明确工作区chunk18.1KiB超过18KiB，尚未到后端/浏览器。

针对实际预算失败，将已有NodePropertiesPanel/InheritNodeStructure按页面既有lazy/Suspense方式加载，参数和禁用条件保留，预算不提高。本机`workspace-budget-tests-20261117`30/30，TypeScript/限定ESLint均exit0；`workspace-budget-build-20261117`实际新产物32项预算通过，工作区gzip14311B；frontend-inputs.json绑定实际构建源码。最终只读review PASS，无阻断项；结论仅覆盖此次预算修复，远程真实浏览器流程仍需继续验证。

以下为授权及复制过程的历史记录；当前完成状态以上述回执为准。

用户已要求清理远程Linux空间。只读盘点证据`.artifacts/remote-check/cleanup-20261117/`：/tmp总3.89GiB仅余32KiB，RATools任务约3.74GiB；/data剩15.1GiB。9个已知/tmp任务无活动进程引用。选定`20261107T141035-d23dcb21495e4449a8e8f07d0c83fb9d`与`20261107T140749-8ca3361ca680457398efb91f2e9893a1`两目录，均无符号/硬链接或特殊文件，共约1.05GiB。正在禁删守卫下复制到`/data/home/ratools-recycle/20261117-tmp-space-first-two/`并逐文件核验；原路径尚未移除。因A2硬闸，已单独询问是否允许跨盘回收时核验后移除这两个源副本；未答复不得执行该步骤。

复制已完成：远程CLI与复制脚本退出0，无超时/OOM/截断；copyVerified=true、sourceRemoved=false。两任务分别32912/32913个文件，共65825个，逐文件内容SHA256及源身份/大小/修改时间检查通过。完整清单保存在目标copy-manifest.jsonl，SHA256 `69fd66a5e12b641dbd70891c7363859b42a98539c55580ab714012f34547cdda`，本地copy-verification.json保留回执。当前仅完成安全副本，未释放/tmp容量；等待明确授权后再重新核验源/目标/占用并执行限定移除。

基线 `68826ec`。用户授权先保存审阅/计划，再按顺序实现，每项验证和只读review通过后独立commit。审查是核心链路限定静态阅读，不代表全仓审计或测试通过。

本地文档：[审阅报告](../../docs/reviews/2026-11-17-code-review.md)、[执行计划](../../docs/plans/review-followup-2026-11-17.md)、[文档入口](../../docs/README.md)。本文件受版本管理，以下表格保留可携带任务定义和结果；docs仍按原策略仅本地保存。

| 顺序 | 任务 | 问题/交付 | 状态 | 验证/review/提交 |
| --- | --- | --- | --- | --- |
| 1 | CR-01 | PublishJobService忽略完整性错误并登记成功；让报告、终态、下载一致 | 已提交 `6c18fb3` | 36/36；最终只读review PASS |
| 2 | CR-02 | PublishOutputVerifier仅比较ZIP名称；补成员内容与双向集合检查 | 已提交 `f13054f` | 27/27，关联45/45含前27；最终review PASS |
| 3 | CR-03 | 多文件拖入复用旧placements导致排序号重复 | 已提交 `891c25d` | 5/5、tsc/lint退出0；只读review PASS |
| 4 | CR-04 | 文档元数据保存冲突后的刷新覆盖未保存输入 | 已提交 `2ceb2c6` | 30/30、tsc/lint退出0；最终review PASS |
| 5 | CR-05 | 捕获回收异常覆盖主异常并中断后续回收 | 已提交 `05d3beb` | 15/15；最终review PASS |
| 6 | CR-06 | 条件性：同摘要定稿目录成员损坏后仍被复用 | 已提交 `cc48aaa` | 9/9；最终review PASS |
| 7 | DEV-01 | 独立验包和新增字典/组合的真实业务链路 | 前置已提交 `727e079`；远程浏览器阻断 | 发布88/88及补充5/5；远程job退出1、结果下载传输退出1，未取得测试报告 |
| 8 | DEV-02 / 05-R | 官方规则映射与逐批实现，区域pending范围 | 文件名 `41ef75f`、叶标题 `b639e76` 已提交 | 文件名79/79，叶标题64/64；27/147已实施、120条待评估；全项仍开放 |
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

## CR-04 保存冲突的编辑缓冲

保存开始按application/sequence/placement保护当前编辑缓冲，失败后刷新服务器基线但保留输入并显示明确重载入口；成功保存/切换选中项正常回填。最终review发现等待保存时仍可改输入，已将当前提交表单及映射/排序输入在保存期间禁用，避免成功响应覆盖等待期间新编辑。

- 首轮`cr04-red-20261117`29/30失败于不适当的等待文字，未算业务复现；`cr04-red2-20261117`29/30，真实页面本地标题被409后的服务器标题替换，取得有效红灯。
- 最终命令`node ./scripts/local-check/review-workspace.mjs C:/Repositories/RATools-for-eCTD/.artifacts/local-check/cr04-final-20261117`：**30/30、0失败/0跳过、退出0**；含25个实际页面jsdom用例及5个hook用例。冲突保留、显式重载、再次保存、延迟期间禁用及完成后恢复均断言。
- 同CR-03的TypeScript命令及ESLint检查SequenceWorkspacePage.tsx、其validation测试和LeafMetadataPanel.tsx，退出0。证据含源码hash；HTTP为模拟，非真实API/浏览器资格。
- 最终只读review PASS，交付文件摘要与回执匹配；备份`backups/review-followup-20261117-085843/CR-04/`。

## CR-05 主异常保留与逐项回收

Reader失败回收的异常写入主异常Data[CaptureRetentionFailure]，保留取消/超时/非法输入分类。Service逐个回收所有捕获；有主异常时保留实例并附聚合回收诊断，无主异常则在finally之后抛聚合异常。InputChanged转换保留InnerException，诊断链不丢失。

- 证据根`.artifacts/local-check/review-20261117/`。`cr05-red`新增6项全部预期失败：取消/非法ZIP被IOException覆盖；多捕获只回收首个；宿主关闭被登记Failed。
- `cr05-green`因CA2219编译失败、未运行测试；已把正常完成时的聚合异常抛出移至finally之后。
- 最终命令`pwsh -NoProfile -File ./scripts/local-check/native-no-delete/run-package-regression.ps1 -Evidence C:/Repositories/RATools-for-eCTD/.artifacts/local-check/review-20261117/cr05-green2 -QualifiedGuard C:/Repositories/RATools-for-eCTD/.artifacts/local-check/service-retention-20261110/qualification-2 -Filter 'FullyQualifiedName~PackageValidationServiceTests|FullyQualifiedName~CaptureFailurePreservesPrimaryErrorWhenRecycleIsBlocked'`。
- **15/15、0失败/0跳过、退出0**，三项目构建退出0，源码清单摘要`9315093b0d04d9c14cad7e0ccf79472463406fd39ac66df1922c4bb6a16eeb3e`。Reader两个用例实际制造_recycle文件障碍；Service wrapper在真实Dispose后注入失败，验证异常优先级/逐项回收/关闭恢复契约，不声称真实移动失败全链路。
- 最终静态及证据review PASS，五个交付输入匹配最终构建。范围为Windows定向回归，未执行全部Reader链接/边界用例或PG/Linux资格。

## CR-06 已有定稿目录复用核验

正常重复目录与并发赢家复用均在回收新staging前，对已有manifest全部成员重验路径、链接、长度、SHA256、MD5；含delivery ZIP。失败不覆盖已有目录，CommitAsync本身保留新staging；上层补偿仍可按既有流程回收它，不承诺永留原路径。

- 红灯构建`.artifacts/local-check/20261117T103245-b1b00f416a8d48ec9907e0f4649b63b4/`，`cr06-red`三项均在“不抛异常”断言失败，退出1；样本为缺成员(移动保留)、等长改写及ZIP改写。
- 首次绿灯7/9，产品已拒绝损坏，但测试误将InvalidDataException视为IOException子类；改为分别断言实际类型，产品无再改动。
- 最终构建命令`pwsh -NoProfile -File ./scripts/local-check.ps1 -Task msbuild`；`.artifacts/local-check/20261117T103735-182c3e478d1641eeb52637f6e50e7398/`三项目退出0。`cr06-final/summary.json`保存实际VSTest命令，ReleaseCaptureTests全类**9/9、0失败/0跳过、退出0**，进程回执与TRX保留。
- 源码清单摘要`2ad722f16b3b79bab0868acdcf8ab3379576882fb827d7f8466c05f63c3e015c`；两交付输入匹配。静态及最终证据review PASS。范围为Windows实际文件/回收，非敌对并发替换保证或跨平台资格。

## DEV-01 前置状态

续做已恢复原job日志：`recovered-job-log.json`，jobs logs读取退出0，原job仍exited1。明确错误为`OSError: [Errno 28] No space left on device`，manager写回执也失败；再次Resume取回dependencies.log、manager-launch.json、summary.json、worker-error.log均为零字节。故本次为远程容量阻断，不能判产品测试失败；停止远程重跑且不自动清理远程历史。继续独立DEV-02，下一原子任务为FDA1034：检查显式operation缺失，区分DTD默认属性与源属性，保留选择日期、归档授权和解析覆盖边界。

三项旧发布集成失败在恢复真实依赖后逐层定位：导入夹具已有regional记录但builder未接入同一store/snapshot；合成XML公司编号只有6位，显式有效副本需9位及官方字典值，并同步index引用和index-md5；独立构造typed revision须非零且匹配workspace revision。随后真实发布返回4.7规则集缺失，已从Application DI确认生产RegionalEctdRuleSetProvider也仅注册4.5，属于实际产品路由缺口，不是单纯夹具问题。修复保持项目规则来源标记，不增加官方147条已实施计数，也不开放Formal。

原本机input-browser记录已具备6项真实Chromium UI/Blob证据，但原生下载管理器完成未覆盖；本机ACL拒绝下载晋升。因此DEV-01不能仅重跑旧入口就宣称全链路关闭。后续使用统一Linux脚手架前需审查当前入口和直接运行依赖；用户既有remote-check.ps1修改不纳入本批提交。

前置最终验证：`pwsh -NoProfile -File ./scripts/local-check.ps1 -Task msbuild-publishing`，证据`.artifacts/local-check/20261117T110648-95c94f4cd1f44c65a2db19bad8e396ba/`，Api/Migrator/Tests三构建退出0、零警告/错误；发布组**88/88、0失败/0跳过、退出0**，原三项基线失败关闭。同构建`dev01-prerequisite/`补充provider4项及节点四序列发布1项，**5/5、退出0**；完整实际命令/进程/TRX保留。4.7 provider直接红灯在`20261117T105753-16b4db4be6874c71b110d7c6e8e47954/provider-red/`为3通过/1预期失败。

最终源码清单摘要`7c7452648199cf95cfe58e4854793bec2b7be1f4ed6ec181bd8a3b8b22c5730b`。静态及最终证据review PASS，七交付输入匹配，原共享XML和业务断言未削弱。早期85/88的失败分别保留在104525、104910、105347、105753候选，说明了显式编号、修订、引用摘要、归档快照和4.7路由的实际依赖，不算最终通过数。

前置提交`727e079`。随后实际运行`pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task test -Target linux -Scope backend-browser -RemoteRoot /tmp/ratools-remote -WaitMinutes 1`，证据`.artifacts/remote-check/20261117T111212-5a8a69799e884dce869243329b339d3a/`。job-result.json记录job47c8f820 exited、exitCode1、oomKilled=false；结果下载又报`remote transport/CLI exit 1; no local fallback`，本机进程退出1。已保留run.json/source-manifest/source.zip/job-result，未取得测试报告、通过计数或错误日志切片，不能确定远端失败根因。依调度约束停止本次远程步骤，不切换平台代替验收；继续可独立进行的DEV-02。

## DEV-02 文件名规则首批（验证中）

本批只新增FDA1221文件名最多64字符及1298缺扩展名，针对目标序列实际捕获清单；来源/选择日期/旧catalog不自动升级。12项缺规则红灯已确认。初实现44项通过；补充17项规则测试并加入旧文件检查回归后49/77，28项因旧夹具captures布局不符合原生回收许可而失败，不算业务失败。review发现1298官方Medium误映射Info，已改Warning并补断言；另外补归档原件缺失和共享finding预算的复现，尚待绿灯与最终review，不宣称规则已验收。

review修复结果：1298为Warning、1221为Info；源必须同时有归档Verified provenance和匹配的OfficialSource原件。完整性有效但缺原件的快照、MaxXmlFindings=2用例均先红后修复；两条官方规则与原文件检查共享总预算，保留终态诊断位置。引擎提升为package-file-inspector-v3，旧排队请求不会混用。旧文件检查夹具只将captures移到已有资格的validation/captures，原断言保持。

最终证据`.artifacts/local-check/review-20261117/dev02-filenames-final2/`三构建退出0、零警告/错误；原生守卫76/79，剩3项旧缺文件模拟的RetainFile被拒绝，该候选不算通过。同一程序集在`retained-final/`用既有BrowserJob、输出ACL及已审阅移动回收夹具执行，**79/79、0失败/0跳过、退出0**；实际命令/进程回执/TRX/摘要完整保留。源码清单SHA256 `3262221acec4ae5e7d942dd16e75cca4c94233ff234c4fdf6feba2333b2f861c`，八交付输入核对匹配。

`python -B scripts/tests/test_package_rule_catalog.py`退出0，69条目录通过，canonical SHA256 `bc6be41e132d4a95aa5c58ef60887fc412119384ba6a1fb6c6d59637cf1c3ff4`。静态及最终证据复审PASS，八交付输入摘要匹配。当前25/147，122条完整覆盖待评估；不代表全部FDA、外部或Formal资格。

## 当前断点与未完成项

### DEV-02 叶标题批次

首批文件名规则提交后继续1276首尾空格、1289缺失/空白标题：读取目标XML原始title元素，重复/复杂结构或未解析保持未评估，严重级别分别Info/Warning，共享文件阶段finding预算。初始测试重载歧义导致编译失败，修正后的`dev02-titles-red2`11项因无规则全部红灯；初实现`dev02-titles-green`62/62通过。

review发现MaxBackbones限制可能未把区域XML加入Backbones，只看已读文档会误Pass；`dev02-titles-review-red`取得1通过/1预期失败。现要求XML-OFFLINE和XML-LIMITS整体完成，已知标题错误仍Fail优先。新engine为package-file-inspector-v4，不沿用上一候选绿灯作为最终验收。

最终命令`pwsh -NoProfile -File ./scripts/local-check/native-no-delete/run-package-regression.ps1 -Evidence C:/Repositories/RATools-for-eCTD/.artifacts/local-check/review-20261117/dev02-titles-final -QualifiedGuard C:/Repositories/RATools-for-eCTD/.artifacts/local-check/service-retention-20261110/qualification-2 -Filter 'FullyQualifiedName~FdaLeafTitleRulesTests|FullyQualifiedName~FdaFileNameRulesTests|FullyQualifiedName~PackageValidationServiceTests|FullyQualifiedName~FdaIdentityRulesTests'`：三构建退出0、零警告/错误，**64/64、0失败/0跳过、测试退出0**。来源清单摘要`891890d151990b09921f974472afda143c44805bbed3a2f26e02da99c34cc99b`；`python -B scripts/tests/test_package_rule_catalog.py`退出0，71规则、canonical摘要`be6093307242097cf5756f39981e663260885497b9e2b4af90b9eea384e9bb18`。静态及最终证据review PASS，六交付输入匹配最终回执。

九项实现提交均完成相应验证和只读review：CR-01 `6c18fb3`、CR-02 `f13054f`、CR-03 `891c25d`、CR-04 `2ceb2c6`、CR-05 `05d3beb`、CR-06 `cc48aaa`、DEV-01前置 `727e079`、DEV-02文件名 `41ef75f`、DEV-02叶标题 `b639e76`。计划基线提交`d88c4bb`。上述测试数互有重叠，不能累加为完整套件。

DEV-01下一步是恢复同一Linux任务的错误证据并处理实际根因；当前没有日志，不能认定编译、单测或浏览器哪个阶段失败，也不能切本机冒充原生下载完成。DEV-02文件名与叶标题共四条已实现，剩余120条仍需逐项评估/实施；尚未开始下一规则批次。DEV-03/04/05/06未开始，外部验证器当前无可用入口的既有依赖保持记录。

本次工作区仅保留任务前已有`scripts/remote-check.ps1`修改与其未跟踪备份；不读取备份、不将其提交。无本次仍运行的本地构建/测试；远程任务已记录exited1，未发销毁/关机指令。
