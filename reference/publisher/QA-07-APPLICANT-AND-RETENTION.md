# QA-07 独立公司编号与服务测试回收验收

2026-11-10。对应FDA4.7首批规则后续的两项独立修复。最终选定Windows回归 **350/350通过、0失败、0跳过**，包含11项新增公司编号用例、原9项服务测试、实际HTTP保存/导入/重建、区域元数据、快照和直接writer回归。[固定回执](fda/4.7/applicant-retention-verification.json)绑定13个构建输入、资格源码、DLL、TRX及全部测试名称；不宣称全仓库、当前PostgreSQL或新浏览器资格。

## 独立申请人／公司编号

复用已有区域元数据 `us.applicantIdentifier`，不新增重复存储字段。区域表单的Schema标签为“申请人／公司编号（9位数字）”，说明保留前导零、不能用六位申请号代替，缺失或格式不正确可保存草稿但发布前须补正。已有Schema驱动前端读取该标签和说明；本轮验证实际API/服务链路，没有新增浏览器运行。

预检和保存使用同一校验：缺失显示Required；非9位ASCII数字显示FDA47-2044，诊断定位同字段。值原样保存，不补零、不改写全角数字、不推断编号。当前官方快照的字段来源指向固定FDA4.7 PDF第53页2044；旧快照仍能显示其捕获的DTD来源。

包构建器从已验证、绑定申请/序列/标准快照的区域修订取得独立编号，消除从`ApplicationNumber`回填的行为。没有区域编号时保留空值，生成US XML明确拒绝并提示完善区域元数据；旧归档内容仍可读取。typed writer在输出前要求9位ASCII数字，`001234567`经保存、复读、包构建、XML与独立规则检查保持不变。修改新修订不会改写旧编号。

新测试另发现typed单申请writer仍按M1叶子数量设置`application-containing-files`；FDA M1规格2.6第7页明确单申请必须true。本轮修复typed分支，保留首批对legacy分支的同一修复。自定义区域路径回归现经真实区域服务保存编号，再使用真实builder；继续核对2044/2036通过、区域XML字节、自定义路径和manifest身份。

## 九项服务测试的禁删回收

原业务断言、真实PackageInputReader、失败上传和Dispose均保留。测试根可由runner通过`RATOOLS_TEST_RETENTION_ROOT`一次绑定，必须是当前仓库`.artifacts/local-check/`下的规范绝对路径、祖先无链接、已经创建；非法非空值拒绝。Lazy固定根避免Create与Dispose读取不同环境值。runner把同一个初始空根交给原生守卫，运行结束恢复变量。

守卫新增四种精确移动关系，根记为R，小写N格式GUID记为G：

| 用途 | 来源 | 目标 |
| --- | --- | --- |
| capture回收 | `R/G/validation/captures/input-G` | 同一captures下`_recycle/input-G-新G` |
| 失败上传 | `R/G/validation/inputs/G` | 同一inputs下`_recycle/G-新G` |
| fixture收尾 | `R/G` | `R/_recycle/新G-原G` |
| 缺失文件造景 | `R/G/source/0000或0001/index.xml` | `R/_recycle/新G-index.xml` |

capture只接受普通GUID文件；上传只接受空目录或唯一package.zip。整树移动前逐项检查普通文件/目录、无重解析点、单硬链接和同卷，最多10000节点/32层，失败即拒绝。目的必须不存在；跨fixture、回收目录移出、未知重命名、覆盖和删除均不允许。源句柄身份核对和只读重命名请求快照继续保留。路径检查限定任务独占、受审testhost，不作为抵抗恶意外部并发替换的证明。

新守卫资格`qualification-2`通过原25项ABI预检、14项既有路径检查、18项新增路径检查，以及进程树/异常/故障/超时/既有移动/CLI初始化/四种实际回收模式。四次实际移动各有允许回执，三次覆盖或越界拒绝和两次ABI拒绝分别核对；删除探针未使用有效文件对象。故障模式按预期退出1、1、124，并以根退出、子进程表空、Job活跃数0证明停止，不宣称故障事件日志逐PID完整退出。

真实350项运行中原9项服务测试全部通过，并实测capture回收19次、fixture回收10次、缺失文件造景2次、失败上传回收1次；本次实际测试无拒绝移动。测试进程树共5个创建/保护/退出实例，Job与守卫计数一致、根退出0、整树停止。十三个代码/测试输入与实际构建manifest一致。

复现入口为 `scripts/local-check/native-no-delete/run-package-regression.ps1`，必传新的Evidence、QualifiedGuard和Filter。当前资格目录为`.artifacts/local-check/service-retention-20261110/qualification-2`，实际回归目录为`.artifacts/local-check/applicant-id-20261110/combined-1`。复用BuildFrom时仍须核对交付源码摘要与构建清单；runner不会将旧程序集自动当成当前源码验收。

## 失败保留与边界

公司编号red-1缺using导致编译失败，不计业务红灯；red-2为11项、3通过/8预期失败。首次回归118项、113通过/5失败，分别涉及typed文件归属、夹具revision/期望和同一回收阻塞，均保留。新增路径测试先验证旧策略拒绝合法capture路径；qualification-1在整树长路径检查处停止，前三种移动的字节保留，未物理删除。规范本地路径使用Windows长路径形式后qualification-2通过。

旧九项失败与首批197项验收仍保留其原记录，由本次9/9结果补充解决。公司编号与回收修复不增加已实施官方规则数量，此检查点仍为9/147。完整规则、外部验证器、Formal成功链路与Linux Python继续按[关闭清单](QA-07-CLOSURE-CHECKLIST.md)推进。
