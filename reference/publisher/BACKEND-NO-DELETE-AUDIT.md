# 后端禁删审计与有界回归

2026-11-08，用户明确授权审计全部后端测试文件及其直接生产调用链，并修复执行前置。此记录区分源码检查、实际运行及尚未完成的资格，不把过滤后的集合称作无过滤全套。

## 审计与修复

范围为 `tests/RATools.Tests` 和 Domain、Application、Infrastructure、Api、DatabaseMigrator 五层 C# 源码，排除生成目录 bin/obj/App_Data；PDF 直接依赖另检查 `scripts/pdf_content_worker.py`。每次运行的 `backend-inventory.json` 保存逐文件 SHA-256、测试类、选择/排除原因及直接调用命中。

- 测试侧已知 22 文件清单之外，生产层发现 `DocumentService` 两处 `File.Delete`，`ApplicationDeletionCoordinator` 两处 `File/Directory.Delete`。原分支仅静态确认，未运行物理删除来制造红灯。
- 修复提交 `2921b4d`：文档移入同父 `_recycle/GUID`，共享文件不移动；申请/序列统一交已有工作区回收服务整树移动，不再先删除单个 payload。回收受阻保留原字节；数据库已经提交时不会假装回滚。
- 上传登记失败后回读数据库再次失败的异常覆盖问题，经 `173835-db6316f64a8d4e74831211c57395022e` 的 24 通过/1 失败复现，随后修复。最终 `174139-e287cd295d5049bcadae2e5451ea1d62` 的 26 项通过，三项目构建 0 警告/错误，并通过只读代码与证据 review。
- 系统回收调用保留在定稿 store、报告 store 和两个定稿测试收尾中，均明确 `SendToRecycleBin`。测试拥有的目录移到 `.artifacts/managed-test-fixtures`，并先检查祖先链接；工具 TEMP、编译产物目录继续继承禁止删除 ACL。受控回收和保留目录不会自动释放磁盘。

直接模式扫描覆盖 `File/Directory.Delete`、`DeleteOnClose` 和 `DROP DATABASE`，并登记 Windows 回收调用。人工追踪另外检查 FileInfo/DirectoryInfo 删除形式、外部进程、fixture Dispose、SQLite 内存连接、迁移降级和回收依赖。该检查不是完整 C# 语义分析，也不能证明任意第三方原生库没有文件清理行为。

## 可执行集合与剩余前置

命令：

```powershell
pwsh -NoProfile -File ./scripts/local-check.ps1 -Task msbuild-managed
```

runner 先执行真实 API、DatabaseMigrator、完整 Tests MSBuild，再执行 VSTest 发现。每个已发现用例必须归属库存类；选中的类按实际发现数量计数，执行后的 TRX 名称集合必须与所选发现名称一致，且失败/未执行数必须为零。保存独立的发现、构建和执行进程回执。

当前选择 191 类，其中四个混合类仅执行显式白名单中的六个方法／七个用例，其余 187 类完整选择。另有 41 类整体排除；合计 183 个未运行用例（PostgreSQL 111、原生 PDF 71、Linux 文件系统 1）。精确名单保存在最新运行的 `unexecuted-tests.json`；排除不是通过或框架 skip。managed 入口要求 `RATOOLS_TEST_POSTGRES` 未配置，防止混合类 collection fixture 意外连接数据库。

- PostgreSQL：当前进程未配置 `RATOOLS_TEST_POSTGRES`，未启动测试数据库。迁移降级行为、原生清理控制及独立 migrator 产物定位仍待验证；两类 PDF 数据库测试使用普通 Fact/Theory，不能假定缺环境会自动 skip。
- 原生 PDF：Windows Job/cgroup 是进程树与资源限制，不能作为删除拦截证明。qpdf stdout 输出、Python `-B` 及脚本保留暂存文件减小已知风险，仍未验证 qpdf/PyMuPDF/Tesseract 内部清理。未经该前置不执行这些类。
- 本集合仍使用 SQLite native 内存数据库、已审查的 shell 链接造景及 Windows 回收 API；名称中的 managed 不表示整个进程没有任何 native 调用。

## 首次广泛运行与失败修复

`20261108T175217-26ac8089d7254843b8c2fb44cb778f15` 实际执行 1,630 项：1,592 通过、38 失败、0 未执行。失败原文见其 TRX 和 `failure-groups.json`。

| 数量 | 原因 | 修复与边界 |
| --- | --- | --- |
| 27 | PostgreSQL 正则等约束直接交 SQLite，建库报 `near "~"` | 测试专用 `SqliteTestModelCustomizer` 翻译现有三个正则和 `octet_length`，保留约束；生产模型/迁移不变。新增实际写库摘要正反例。 |
| 7 | 投影安全测试通过反射调用旧构造签名 | 补当前构造函数的可选摘要参数，不修改投影安全断言。 |
| 1 | `PDF_VERSION_UNQUALIFIED` 已发出但问题目录没有元信息 | 补分类与处理建议，既有全码覆盖测试验收。 |
| 1 | DTD 路径假定构建产物固定层级 | 使用已有仓库根定位 helper，支持隔离输出。 |
| 1 | EU writer 测试缺少显式区域元数据 | 增加显式合成 EU envelope，保留官方 DTD 验证。 |
| 1 | 节点 API 测试把文本当 PDF，引用影响保护拒绝移动 | 使用仓库既有真实 PDF，保留移动成功、节点身份、revision 和陈旧请求冲突断言。 |

第二轮 `20261108T185147-efafa64e6cb3493a8dc743baafd96bb3` 实际执行 1,634 项（增加四项 SQLite 摘要约束测试）：1,627 通过、7 失败、0 未执行。七项全部为投影夹具进入下一层 `PackageLifecycleInspection` 反射构造时遗漏两个可选参数；已按实际签名补入 `null, null`，安全断言未修改。其余首次失败组在此轮均通过。

最终运行 `20261108T185912-905e676b7691466389931831aff2e8bb`：发现 **1,824** 项，实际执行 **187 类／1,634 项，全部通过、0 失败、0 未执行**；另 45 类／190 项明确排除。TRX 名称集合与所选发现名称完全一致。三项目构建均 0 警告/0 错误；构建三阶段、发现及测试五份进程回执均 exit0、assignedBeforeResume=true、processTreeStopped=true、forcedTreeStop=false。

- 源码清单 SHA-256：`a992b75c486da7d864086f8ab49e0b1cfa4eaa0cd16924fdfbaab30af2946489`。
- TRX SHA-256：`a143f017a148ec7cb30ae9a64bb86a0179e0701e38a83be9ad61be5c027e9aa5`。
- 库存含 789 份 C# 源码，列明形式直接删除命中 0；原始发现、选择、排除与回执均在同一证据目录。
- 最终代码与运行证据只读 review PASS；25 份交付代码/脚本哈希匹配，逐类数量和完整名称多重集合独立核对一致。

全后端无过滤执行、fresh restore、当前 Linux/PostgreSQL/原生内容工具联合、原生浏览器下载和外部 Formal 资格继续开放。

本轮独立修复提交：2921b4d（文档/工作区回收）；0c6a01b（PDF版本资格问题码目录）；791a1a3（SQLite测试约束适配）；648f1c6（投影/DTD/EU/节点API夹具）。最终运行证据绑定提交前源码清单，各提交未再修改已验证的实现。


## 2026-11-08 剩余可移植用例补验

最终运行 `.artifacts/local-check/20261108T193701-9f4c35398c074abca37213a198661999/`：发现 **1,824** 项，执行 **191 类／1,641 项，全部通过、0 失败、0 未执行**。789 份 C# 源码列明的直接删除模式命中 0。三个 MSBuild 均 0 警告/0 错误；Api、DatabaseMigrator、Tests、discovery、selected-tests 五份回执均 exit0、assignedBeforeResume/processTreeStopped=true、forcedTreeStop=false。最终代码与运行证据只读 review PASS；两个 runner 脚本当前哈希匹配 source manifest。

新增七项包括内存流哈希缓存 2、FDA 显式标识 HTTP 保存→XML→再导入 1、EU 双 envelope/独立 PI context 往返 2、区域准入与引擎身份 2。HTTP 场景实际使用 loopback 与 InMemory；区域准入验证保留 Formal 拒绝，不是正式成功交付。精确方法过滤及 `selected-test-names.json` 与 TRX 完整名称多重集合、逐类数量均一致。既有 `OpenApiContractTests.RuntimeDocument_MatchesCommittedSnapshot` 也通过：当前 API 在 TestServer pipeline 生成的 OpenAPI 与已存快照深度相等，属于运行时合同验证，不是实际网络端口验证。

- source manifest SHA-256：`ae446aaa78f60adbb9bffda29a07b4ec0c8a786350e0c043ab7a1dc05d939548`。
- TRX SHA-256：`e9043aa87f9464153930aeb434815c63a3d812c67504ca2f3195af6958cfb846`。
- inventory SHA-256：`b1e8ed38cd3f24aaf1f960732797f9e7ab633e324a12e790be0bba0088da76a3`。

首次 `20261108T192259-d18b81af024d47ba82987c0f81611715` 在 API 构建发生 NETSDK1064，测试未启动。既有 assets 引用的缓存有 98 个缺失/不完整包；从官方 NuGet 下载并保留归档，用 SDK NuGet 库验证内容 SHA-512 与原 assets 相等、签名包完整性通过后，以独占新建补齐缺失文件。已有内容冲突即停止；过程没有覆盖或物理删除。一次解包因 URI 编码的 `portable-net45+win8` 路径校验停止，按 NuGet 文件名解码后续接，两个编码名副本经链接/硬链接及字节核对移入证据目录 `_recycle`。恢复证据：`.artifacts/local-check/dependency-recovery-20261108T192916-c76ccd29531d420aa85fb7d989be6b0c/`。review 独立核对 98 个包集合及归档 SHA-256 一致；没有重放写入或重新执行签名校验。本项是既有资产约束下的缓存恢复，不是 fresh restore，也不宣称完成证书信任链资格。

同轮 `environment-probe.json`：Instabox CLI 存在且进程变量已配置，远程 `true` 的继承网络与 7890 代理探测均 exit4，无超时；原始输出丢弃，不能据此判断失败原因。PostgreSQL 进程变量未配置；docker/podman 命令不可用；wsl 可找到但列表命令 exit1，未获得可用 Linux 环境。183 个排除用例、无过滤全套、fresh restore、当前 Linux/PG/native 联合、原生浏览器下载和官方/外部 Formal 门禁仍开放。FDA 原文取证重试见 [OFFICIAL-SOURCE-REFRESH.md](OFFICIAL-SOURCE-REFRESH.md)。


2026-11-08 环境恢复与PG补验：旧insta01名称失效，既有机器ID远程true已成功，当前名linux；/data仍满，利用已检查的/dev/shm独立目录与前台执行继续，未扩容/重启/删除。Linux大小写源码方法3/3（新增1，另2与Windows重叠），前台linux-platform10/10，PG16.15环境初始化/重启通过。真正空缓存NuGet还原211940-ae195e02全部通过，六assets仅新缓存/官方源无fallback，四进程组无剩余，246清理对象保留。当前Linux实际API/Migrator/Tests三个MSBuild均0warning/error，独立迁移31/重复0，最终PG213745-c79498e2为28类107/107、0失败0跳过，名称多重集合一致，PG与各阶段进程全停，最终代码/证据只读review PASS。首轮116/99/17保留；七个旧降级断言改为最新迁移明确无条件拒绝并验证完整迁移历史/原数据不变，HTTP fixture改已有任务根，生产保护未改。旧SQL55000条件guard未覆盖。PG与Windows重叠5case（4methods），跨运行组合覆盖1744，剩80原生相关（71native+9PG/native）；不是同次无过滤全套。当前Python内容宿主仍仅支持Windows。完整入口、失败、摘要及边界见[环境恢复记录](QA-07-ENVIRONMENT-RECOVERY.md)。浏览器当前验收另记，Formal/官方/外部资格继续开放。


2026-11-09 QA07当前原生补验：官方qpdf12.4.1资产API/receipt/ZIP摘要一致，链接条目审查后以普通文件保存。最终linux-qpdf-final-20261108T234925-aa7169f80e2346d59e3f1529b9250433为54/54+PG/qpdf7/7，四build零warning/error，迁移31/0，393组及根pop0、manager0、PG内外残留空；代码/证据最终只读review PASS。四可移植并发方法先红50pass4fail，测试工具路径改读取环境变量且保留Windows默认后54全过，断言未弱化。组合覆盖1805/1824、剩19；明确映射PG迁移测试EmptySchemaCanBeRolledBack到CurrentSchemaRefusesDowngrade，保留参数化名称多重集合。61为净新增，非单次无过滤全套。剩余五类内容16项及三个Windows条件并发项，需Windows原生禁删执行保护。
当前linux-resources前台20261109T000339-152b9f5adc114109be2c2d540197c4c2另通过3资源+3进程树+1manager故障探针；memory oom_kill4/groupkill1、CPU201974us、5组pop0、manager0/480s、foreground无timeout/trunc/OOM，当前788清单hash核对及最终review PASS。directcsc构建，七探针不加入1824或替代Windows三项。完整摘要与剩余清单见 reference/publisher/QA-07-NATIVE-ACCEPTANCE.md。QA07仍开放无过滤全套、Windows内容保护、官方规则映射、固定版本外部验证器及Formal交付/回执。FDA远程两次实际重试仍404且非PDF，未取得原文；不改变区域资格。
