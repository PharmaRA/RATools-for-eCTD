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

当前排除 45 个类：31 个 PostgreSQL 或混合数据库类、13 个原生 PDF 工具类、1 个含 Linux 文件系统用例的混合类。整类排除会同时留下其中可移植用例，具体名称和数量以每次库存与发现清单为准。排除不是通过或框架 skip。

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
