# 2026-11-17 审阅后续执行账本

1426本批两轮四个build/bin、build/obj已核对终态、路径及链接后送入Windows回收站，可恢复；日志、TRX、源码清单与证据副本保留。

1408本批两轮四个build/bin、build/obj已检查终态、路径及链接后送入Windows回收站，可恢复；原始证据及备份保留。

1374本批三轮六个build/bin、build/obj已在终态、路径及链接检查后送入Windows回收站，可恢复；日志、TRX、源码清单及证据副本保留。

2026-11-19新增授权：继续按顺序补齐所有剩余107条，基线HEAD `6d6ab4c`，顺序与完成条件见[固定执行队列](FDA47-REMAINING-107.md)。此前“后续未指定新规则不在交付内”仅描述当时三项交付范围，本次已扩展。FDA3/4已完成，当前继续FDA5及其直接前置能力。

当前 FDA 4.7 共 147 条，46 条已实施、11 条部分实施、90 条待评估。仍有 101 条待完整覆盖。内部目录有 101 条。剩余107条执行队列基线提交为 `14c08fa`。

**FDA1442：index显式DTD版本，Implemented。** 1426已提交`c869848`。FDA第20页，Medium，2008-03-10生效。仅唯一目标index的已识别ICH根：缺无命名空间属性或IsDefault=true判Warning/Fail；显式3.2为Pass，无辅助ExplicitValue不等于遗漏。无根、错误/空版本被reader门禁拦截、歧义inspection/文档/根保持NE；不由诊断字符串猜遗漏。完整根属性已保留后，尾部损坏或regional未读不抹掉该根结论；历史隔离，共享finding预算，取消传播。file/delivery分量v38。新增规则内部NE/已知Fail快照；Finish只补启用且缺席的1442，覆盖前序规则已中断而1442尚未开始的窗口，不替换已有结果。

`fda1442-red`23项1过22败，其中2项为公共夹具leaf缺必需checksum使DTD前提失败，未计业务红；在本项私有夹具补齐leaf属性后`red2`23项1过22败，全部为缺规则结果。candidate为283/283；随后定向补测前序1408 deadline，`review-red`2/2失败准确复现1442缺位。限定Finish修复后`fda1442-final`三项目构建退出0，**283/283通过、0失败/跳过、测试退出0**，仅十一类指定规则/属性/中断/目录回归，非全套。源码清单SHA`2a864f7c234b6894cd01f956e200fd458914bebbe14b73ee997e385eee23b267`，8/8交付输入匹配；TRX SHA`9772b8898f01bb44b2a57aed361fcd006b6fecea018f22d6ad928f83d2d5b646`原件/备份一致。目录101条独立校验通过，canonical`a477a890f337c2568d080417f0d3ffb26703bb13c80af576266d41f682ca8306`。子代理限定实现复审及终态核验PASS。证据副本`backups/fda1442-20261120/evidence/`；五轮十个bin/obj已检查后送回收站，可恢复。下一项1445按实际区域选择集合独立处理。

**FDA1426：delete checksum固定范围收口，Implemented。** 1408已提交`618a542`。按序核对1425后保留index-md5遗漏官方编号归责依赖；未将通用INDEX-MD5错误视作1425映射完成。1426现有ICH/US leaf遍历覆盖明确delete场景，固定STF DTD无内部checksum/operation，撤回不存在的STF欠账。检测逻辑不变：非空含空白Fail，缺/空值不触发，未知操作NE；独立DTD缺属性诊断继续存在。报告Coverage先红后绿：`fda1426-completion-red`5项2过3败，失败均为应Pass而被Partial屏蔽。

`fda1426-completion-final`三项目构建退出0，**218/218通过、0失败/跳过、测试退出0**，仅指定八类checksum/目录回归。源码清单SHA`10225af447448ea00731770337b898a37013622cda63eef6574a546818b74afe`，测试/catalog/直接依赖3/3匹配；inventory静态核对，sha文件独立验证。目录100条canonical`2a19e335a74f591bbd64947fd0eef56c10121a8d3918ed4162c58e58f43e17ec`。TRX SHA`b79b86347d61be9e41c1ee090d1f7139341854e3f73105231e43eacd1a726010`原件/备份一致；限定只读审查通过，证据副本`backups/fda1426-completion-20261120/evidence/`。Implemented不等于DTD或整包readiness通过。下一项1442，先区分显式根属性、DTD默认补值与未保存根事实。

**FDA1408：固定支持范围收口，Implemented。** 1374已提交`7c9edc4`。ICH/US leaf的算法声明（含引用STF文件的leaf）已有完整遍历，固定STF DTD内无checksum-type，撤回旧泛化欠账；检测逻辑不变，OrdinalIgnoreCase且不trim，缺声明/未读骨架保留NE。新增报告Coverage四例先红后绿：`fda1408-completion-red`4项2过2败，失败为md5/MD5的Pass被Partial屏蔽。`fda1408-completion-final`三项目构建退出0，**214/214通过、0失败/跳过、测试退出0**，仅指定八类checksum/目录回归。源码清单SHA`d9245a9d1758835975c22bf83ec1a26cb4da25e159c04f7ec0c660376b850ed9`，测试/catalog/规则依赖3/3匹配；inventory静态核对，sha文件独立校验。目录100条canonical`9d88e6757c1c4e342fb09c2e35749354da86458491c686ce9c0e2372418133c9`。TRX SHA`1bc70d6dd9a5808a73325f38d2028e99783b1b22cc1cbc70ba54e7a5b3e64023`原件/备份一致；限定只读审查通过，证据副本`backups/fda1408-completion-20261120/evidence/`。只升级1408，不代表全profile或Formal readiness。

**FDA1374：算法名称一致性修复及范围收口，Implemented。** 原1408接受MD5/Md5而1374及通用FILE-MD5仅接受小写，已先复现再统一为OrdinalIgnoreCase，不trim、不接受其他算法。固定ICH/US DTD在leaf声明checksum/type/operation；STF doc-content无这些属性，STF文件自身由index叶摘要比较，不能继续保留无依据的“STF内部checksum未覆盖”欠账。显式历史、实际字节、index-md5、缺证据/非法声明/预算中断语义不变。file/delivery分量v37。只升级1374；1408/1426按序另项收口，1425的index-md5遗漏归责仍待核对，通用INDEX-MD5已有缺文件诊断不自动证明FDA编号映射。

`fda1374-algorithm-red`11项5过6败；candidate为151/151。补报告Coverage的match Pass/mismatch Fail/missing NE三例并更新目录后，`fda1374-algorithm-final`三项目构建退出0，**211/211通过、0失败/跳过、测试退出0**；仅八个指定checksum/目录测试类，不是全套。源码清单SHA`eda396abd5c0c29049908c639c381bffb3f318e2ef0226f6bf17f5f8c9c36b8b`，五代码/测试加catalog共6/6输入匹配。sha256文件及inventory不在构建manifest中，分别由独立摘要校验与静态状态核对覆盖，不计8/8。TRX SHA`6b96e180ecf555c377ee10501f97fd1f8962b29605bf4eadd35f6b6f670620c8`原件/备份一致。目录100条独立校验通过，canonical`e920e7d43d4ab0d38ec6114e917034b8f8c16162bb312c1a0a189571ee151ba7`。子代理范围审查、修复审查及最终增量复审均通过。证据副本`backups/fda1374-completion-20261120/evidence/`。Implemented不是全profile readiness或监管资格。

**FDA1368：STF多余分类增量，继续Partial。** 完整且DTD有效的目标index按实际叶引用建立STF允许分类并集，复用精确章节祖先检查并支持node-extension；不按物理目录判断章节。相关query/fragment等不确定引用使对应STF未评估，无法解析而不能定位的引用使整体允许集合未知，不能据不完整归属误报多余。已知ich分类不在允许集合时定位Info/Fail；duration/us仅重复剂量及长期致癌性示例范围可识别，其他duration、realm/未知分类保持未评估。历史引用不扩大目标集合，原heading失败及另一确定STF失败保留，共享剩余额度。file/delivery分量v36；区域、leaf、extension及其他STF属性范围仍Partial。

`fda1368-stf-red`27项10过17败；candidate为104/104。补路径级uncertain与全局unresolved、实际有效历史不扩大target、heading失败保留、余额1/0后candidate2为111/111；子代理限定增量复审PASS。更新目录后`fda1368-stf-final`三项目构建退出0，**269/269通过、0失败/跳过、测试退出0**，仅指定1368/STF/1357/1362/1344/utility/目录类，非全套。源码清单SHA`7afd667ad34697d1dedf2b41c764a70411b5b909a43cc9be41f844773a88e7f7`，7/7输入匹配；TRX SHA`c24b3252b03421124e97d30d8f048bcefac44f586afcd6596f83ccdcdb27d404`原件/备份一致。目录100条独立校验通过，canonical`bc6663741aa7520a64b71067f5d3598091cae73e4a129180157cc40d99e1935a`。证据副本`backups/fda1368-stf-20261120/evidence/`；四轮8个bin/obj已检查后送回收站，可恢复。

下一项1374：已确认1408接受MD5算法名称大小写变化，但1374与通用FILE-MD5仍只接受小写，需先复现再修复一致性。STF DTD的doc-content没有checksum/operation属性，引用的STF文件摘要由index叶承担；不能凭“STF未检查checksum”保留不存在的开发要求。对完整覆盖范围仍须核对真实声明位置。

1362本批三轮6个build/bin、build/obj经进程、绝对路径及链接检查后送入Windows回收站，可恢复；日志、TRX、源码清单和证据副本保留。

**FDA1362：STF已知category重复，Partial。** FDA第17页，Medium，2008-03-10生效。独立检查每份目标STF的唯一study-identifier下直接category：species/route-of-admin/type-of-control限ich，duration限us。从第二次及后续出现定位Warning/Fail，文本值相同或不同均不改变重复身份；每文档重置集合，不串历史。未知name/realm、截断、缺来源/日期保持未评估；file-tag/property不作为study category。共享剩余finding预算，已知重复优先于其他未完成证据。file/delivery分量v35；其他业务属性及区域变体未资格，规则从NotAssessed进入Partial，不减少待完整覆盖数。

`fda1362-red`构建退出0，23项全部因缺规则结果失败；candidate为96/96。只读审查要求补三次重复及本规则余额2/1/0、历史STF DTD Pass与实际重复节点前提，均已补齐并复审通过。`fda1362-final`三项目构建退出0，**216/216通过、0失败/跳过、测试退出0**，仅指定1362/1357/1344/STF/目录/utility类，非全套。源码清单SHA`3d6e53dd9bdcb0ae2e322b61ebaa2bfd0fe8f4e202fc4843a326fa2add4089c0`，6/6输入匹配；TRX SHA`fac9b5153e88ba11dc563f0ea6fc982e22eb53bdc7b99047a7714ed848b17bb6`原件/备份一致。目录100条，canonical`5243f7ca3f55f8d7018ae3f1a26d0b0c8adbad2c85ec2cdec4a7fd329e6db4f0`。证据副本`backups/fda1362-20261120/evidence/`。下一条1368的STF多余category按实际章节归属另项处理。

**FDA1357：STF必需分类增量，继续Partial。** 根据已归档STF规范2.6.1第5–7页，实际目标index叶引用和完整ICH章节祖先链决定分类：单次/重复剂量毒性需species及route-of-admin，长期致癌性需species，受控临床需type-of-control。仅完整且DTD有效的index/STF可支持缺失判断；不根据文件目录推断章节。支持node-extension中引用，同一STF被多章节引用取要求并集、重复引用不重复发现。缺或空白分类报Warning/Fail；重复分类或未知realm、未知归属保持未评估；重复剂量duration“if applicable”无证据不能强制判缺。默认命名空间有效，目标/历史隔离，原heading失败和一个STF的已知失败不被其他截断证据抹除，共享剩余finding预算。file/delivery分量v34，其他STF属性及区域/duration资格仍Partial。

`fda1357-stf-red`24项10过14败。首candidate为71/84，其中13项因新夹具缺leaf checksum/checksum-type而index DTD Fail；修正夹具、增加正常index DTD Pass断言，没有放宽生产闸，candidate2为84/84。补多引用/真实历史/默认namespace/重复realm/混合失败/一项及零项预算后candidate3为92/92；子代理指出extension被直接引用掩盖，最终将single两条引用均置于extension，保留controlled直接引用。最终`fda1357-stf-final`三项目构建退出0，**190/190通过、0失败/跳过、测试退出0**，仅指定1357/STF/1344/目录/utility类，非全套。源码清单SHA`dde657024895a1e9eed114f15049a68c05b47204a73631b826ad439acbefa3ed`，6/6输入匹配；TRX SHA`32aa91438145b874bc9e33e9a76b5a25b9e71f87652a7e9c882ff74e906f0e04`原件/备份一致。目录99条独立校验通过，canonical`698be74bf2dace4aea796a7f82a8eead75d330df3b97df3235da1f78929f7ec1`。子代理限定增量审查PASS；五轮证据副本`backups/fda1357-stf-20261120/evidence/`，10个bin/obj已检查后送回收站，可恢复。下一条1362独立处理STF业务属性重复。

**FDA1344：STF显式属性空格增量，继续Partial。** 目标STF元素纳入已有原始属性遍历，不改变原ICH/US骨架语义；DTD默认属性排除，缺ExplicitValue不能用规范化Value替代。字符引用空格和字面制表符归一化为空格时检查首尾U+0020，字符引用制表符、内部空格和空字符串不误报。STF发现/读取不完整及缺原始命名空间证据保持未评估；历史STF虽实际读取但不进入目标结果。定位保留目标SourceId、STF路径、节点/属性行列，LeafId为null。共享finding预算，已知骨架Fail不被STF未完成抹除。file/delivery分量v33；其他XML范围仍未资格，1344状态保持Partial。

`fda1344-stf-red`构建退出0，19项10过9败；candidate为114/114。更新目录证据后`fda1344-stf-final`三项目构建退出0，**155/155通过、0失败/跳过、测试退出0**，仅指定1344/STF/utility/目录/1368类，非全套。源码清单SHA`24c61ff9c64177b05f3541d9735169d299d801a478b7ae1904b13c4fc01aeffc`，5/5输入匹配；TRX SHA`5d9a379f4d363946e52f251553f6cafa8b4d84160309470878a4243d43360a15`原件/备份一致。目录99条独立校验通过，canonical`8f95ad5825219dcd301aa504908d0665600ddf3d958b7e055b7829df40fc4261`。子代理限定增量静态审查PASS；证据副本`backups/fda1344-stf-20261120/evidence/`。三轮6个bin/obj已检查后送回收站，可恢复。

按序核对1238/1255/1314后，原Partial缺口仍在：1238的400MB精确字节口径/数据集例外；1255的区域/细分章节/用途条件和历史格式版本；1314的未知utility用途。未强行修改既有已证实规则。下一项1357须按STF实际章节归属判断species/route-of-admin/type-of-control及duration适用性；1362另行处理category业务属性重复，不能把允许重复的file-tag混为一谈。

**FDA1119/1130：STF条件性utility增量，继续Partial。** 在原ICH主DTD检查上聚合实际目标STF的DTD2.2、声明的本地样式和受支持样式所证明的valid-values依赖。1119定位缺失文件；1130比较有版本身份的DTD2.2/样式2.3发布MD5，标准2.3文件损坏Fail、精确2.3字节的别名可识别、未知旧版/别名保持NotEvaluated。valid-values仅认可精确6.0字节，其他版本/不明字节未评估，不冒称新版本损坏；日期、完整发现、来源及共享finding预算分别保留，独立已知失败不被其他缺证据覆盖。文件/交付分量v32。

归档ICH STF官方内容API、valid-values6 ZIP及三份checksum ZIP：DTD MD5`0972c10a4dadf3df5d2f41b2026a4a5c`、样式MD5`92bfeb4cb358a4af786317a0da2309c5`与原件一致；网页明确发布的6.0 MD5`797b4110bddb6d3cb4ad3869ffccb53c`与6.0原件一致，但其链接checksum ZIP内容为`301aaeb6b0fac8873a668fb4cbade517`。后者只归档冲突证据，不用于授权；采用精确版本网页证据及FDA标准页6.0支持日期2025-02-18。五项新权威来源冻结至快照并校验原件SHA；获取时间本地11月20日对应UTC11月19日，Capture与provenance统一UTC，不混同规则/版本生效日期。样式2.3 checksum门控不早于2017-06-01。

本机禁删回归`fda1119-1130-stf-red`15项2过13败；candidate为30/38，八项因UTC获取日期门控晚一天导致未评估。纠正实际UTC日期后review-red为15/17，剩两项准确复现未知样式误报2.3损坏；source-red为0/1，复现缺依赖来源仍报values缺失。修复后candidate2为104/104；更新目录Partial说明及摘要后final三项目构建退出0，**172/172通过、0失败/跳过、测试退出0**，仅指定utility/STF/1368/目录/快照/路径测试类，排除Postgres，不是全套。源码清单SHA`70af23a1a77b739e026e52f2c6f3f595d26936c36c6201dd4731958cdd79472f`，10/10输入匹配；TRX SHA`24406db44df30668b3e76cc8056545f5d1bf2840ddc414a084e2ff9e834d2e58`原件/备份一致。目录99条独立校验通过，canonical`826397cefcc901453ea3476f448ec5c490b54e08eb66d013e41f18356c5bb8a0`。子代理限定增量复审PASS；六轮证据副本`backups/fda1119-1130-20261120/evidence/`。

本批六次构建的12个bin/obj经进程、路径及链接检查后送入Windows回收站，可恢复；回执/TRX/源码清单与官方来源继续保留。

1119/1130仍缺编制时实际使用的ICH样式版本、历史/其他支持文件变体及未解析条件的完整资格；不能把本批局部Pass升为全规则Implemented。下一项1154需要跨申请历史，而现有HistoryBaselineManifest/PackageInputSet仅绑定单ApplicationId；此依赖保留，再按固定队列核对1238/1255/1314及1344的剩余范围。

**STF共同前置：独立发现与固定DTD解析（不计规则完成）。** 为1119/1130条件性支持文件及后续STF规则补入`PackageXmlInspection.StudyTaggingFiles`，不混入ICH/US/EU Backbones或生命周期节点。仅US profile采用；受限扫描目标XML根及STF DOCTYPE，非stf命名的真实study仍识别；缺引用、STF命名错误根、声明与根冲突、损坏、内部DTD、未知/远程DTD和错误版本均不证明完整。Probe保留DOCTYPE证据、resolver=null，正式读取只用同一快照的固定DTD；省略显式xmlns及dtd-version的DTD默认属性仍可读取，原始显式值保持独立。旧快照无STF资产返回STF_ASSET_UNAVAILABLE，不借Current；声明资产但字节缺失由原快照完整性闸拒绝。Probe和正式reader共享节点/发现预算与取消；STF/骨架共享文档数限额，读取中取消传播。新XML引擎`package-xml-inspector-stf-v3`，交付v5。

归档STF规范2.6.1、DTD2.2和样式2.3，来源清单见`official-sources/2026-11-20-stf/source-snapshot.json`。DTD解包字节SHA`e6d47695501e263534a89a2b7bf7018c92e12a11c3dd82fc1d0c462bfa52c81f`；与样式一起进入快照资产。新manifest SHA`09f527b1a36fecda6a24bc58239ad7906f4d113346190ce2c9bc30425590f001`，仅增加两项，原ICH schema未变。1368原闸固定旧manifest导致19项6过13败，已复现后仅允许两个已核验摘要，保留历史兼容。规则目录仍99条，独立canonical校验`9fbfc91ab2c9b1f5e5d2301c1551a3593c75bd1f4346783f8c666be59b4a5917`通过。

`stf-foundation-red`11项1过10败；首candidate为95/98，三失败为既有CustomEncodedRegionalAddressUsesExactRelativeDtdResolution两例和MissingReferencedRegionalXmlFailsWhileUnselectedRegionalScopeRemainsUnevaluated一例的夹具移动权限，未改保护、未计通过。review-red两项均失败，复现DOCTYPE遗漏/默认xmlns漏读；修复后candidate2为151/151。补正式reader接续Probe预算的打开计数断言后，`stf-foundation-final`三项目构建退出0，**152/152通过、0失败/跳过、测试退出0**，筛选保留上述两个方法及Postgres排除，非全套。源码清单SHA`e30e44953794344a51a7fcbe1bdbea9f25febecf1390428017bae4938bcd8639`，11/11输入匹配；TRX SHA`09c726d30a0062b4f37cd3e59f0836fa6114f88dba1793ba45e10febfbb04fc0`原件/备份一致。子代理限定增量静态审查PASS。六轮回执副本`backups/stf-foundation-20261120/evidence/`；五次构建10个bin/obj检查后送回收站，可恢复。

**前序依赖与当前执行点。** FDA5三项前置已提交03cffe5/0959517/05a7131，模板/附件识别和历史/登录生成表单资格仍未完成。FDA6缺强制格式适用/豁免证据；FDA7缺适当表单与申请/中心映射及可填写性资格；1038 replace例外仍需审核证据；1085已核230阈值，但路径起算口径未由本轮固定来源明确；1102尚需原始URI/path分量与百分号解码的规范绑定。以上保持原状态，未因评估或前置工作减少剩余数。当前继续1119/1130：M1规格2.6物理第47页17项明确US区域支持文件改网站引用，不能一律要求放util；STF规格第4页明确条件性DTD/style目录。已归档STF样式读取valid-values.xml，官方入口为`https://www.ich.org/page/study-tagging-file-specification-and-related-files`，尚需取得有效值原件及独立发布校验和/适用性证据。STF解析前置不证明这些规则完整覆盖。

2026-11-20 XFA批次七轮共14个build/bin、build/obj已在进程、绝对路径及链接检查后送入Windows回收站，可恢复；仅解除这些目标的禁删ACE以允许回收。原始回执/日志/TRX、证据备份和官方样本保留。

**FDA5前置：有界XFA packet/data读取（不计规则完成）。** 通过FDA forms页面脚本定位公开目录`https://www.fda.gov/datatables-json/fda-forms.json`，归档2252/2253/3674/3938/3988五份官方PDF，均为AcroForm Fields为空的XFA动态表单，不能以普通widget缺失判断无表单。七份归档源长度/SHA全部校验；五份XFA template解码摘要、官网版次、PDF修改时间和观察字段见来源清单，彼此不混同。3397实际返回用户费登录页，3792/3794目录指向登录生成系统，未认证访问；2567未见匹配目录行但不能据此认定不存在/不适用。

新增不可变`PackageXfaFacts`，读取packet数组或单一XDP流；保留解码SHA/字节数及含命名空间/同名序号的原始数据节点路径。缺datasets与存在空data分开；必须恰好一个template、一个datasets和其中一个data，重复/畸形保持不完整。支持无过滤器及受限Flate，累计展开字节、压缩比、XML节点/深度并共享PDF对象预算和取消；DTD/外部实体禁止，不执行脚本。XFA元数据解析失败独立计数，不能因原因去重而发布不可信packet；envelope验证首尾配对、根、唯一边界且拒绝注释/PI隐藏。ZLib同时验证头、精确消费位置和Adler-32，输入耗尽后仍被请求读取即拒绝，避免未设置最终块但校验和正确的截断流被误接受。PDF inspector版本`pdfpig-0.1.15/package-navigation-forms-xfa-v3`。**Xfa.ReadComplete仅表示物理数据读取，Form.Complete仍false；不证明模板识别、脚本计算、可填写性或FDA5匹配。**

`fda5-xfa-red`构建退出0，18项8通过10失败。candidate为94/97，但构建期间新增测试导致源码清单不一致，仅作诊断、不作验收。固定源码`review-red`4项1通过3失败，复现悬空Filter/DecodeParms与损坏envelope；修复后candidate2为102/103，唯一截断ZLib尾仍被接受。加入尾校验/精确消费后candidate3为105/105；增量审查提出固定marker注释隐藏、重复悬空元数据、BFINAL=false但正确Adler，`review2-red`4项1通过3失败，均实证后修复。最终`fda5-xfa-final`三项目构建退出0，**170/170通过、0失败/跳过、测试退出0**（135相关基线+35XFA）；源码清单SHA`b5e21e9a9f8005a0035c45f9f3baf895db48da653e06ff49c26ca213ff8e950d`，6/6交付输入匹配；TRX SHA`8f257d1c4b206fce65ab83514cff24408c710eb75509f8f5347f7a3b4ea84941`，原件与备份一致。子代理限定增量只读复审PASS。七轮证据副本在`backups/fda5-xfa-20261120/evidence/`，不作全仓库或监管资格宣称。

**FDA5前置：目标XML表单候选组及申请归属（不计规则完成）。** 固定FDA M1规格2.6物理第7/10/20页明确356h/1571位于对应申请的admin表单组、其他表单在M1，且一个form可有附件叶。因此新增`PackageFileInspection.Forms`保留候选组而不逐叶判为主表单：ApplicationAdmin精确绑定祖先application，SharedM1保留全部申请，不误选containing-files=true。保留原始编号文本、类型/containing-files属性事实、重复编号、空组、delete/无href和node-extension附件叶；通过SourceLogicalPath+NodePath与已有引用事实对应。只消费目标digest/profile唯一匹配的已解析XML，不重开文件；历史不串入，根/业务命名空间和祖先链必须匹配。DTD/读取不完整、缺/多regional、错位置、展开超过MaxXmlNodes均不能报清单完整；超限只发布此前完整组，取消传播。文件/交付绑定v31，目录99条及FDA5状态未改。

`fda5-binding-red`因新测试CA1861编译失败，不计业务红；修正后`fda5-binding-red2`构建退出0，7项3通过4失败。candidate为51/51；子代理要求隔离命名空间变量、目标唯一性/多文档、精确展开预算及delete/无href测试，已补齐。`fda5-binding-final`三项目构建退出0，**135/135通过、0失败/跳过、测试退出0**，覆盖两个binding类、1038/路径规则类及三个PDF类；不是全套、业务FDA5匹配或监管资格。源码清单SHA`931d9011adb61bdb7810445b3230de3c8e2a6f3b3080e49a066e7bd5d34593b6`，7/7交付输入匹配；TRX SHA`ffa9bfd16f5f2989ea0570b40180ce7055d75739005d9c15957cf251c8249fb8`，原件与备份一致。限定增量只读审查PASS。四轮证据在`backups/fda5-binding-20261120/evidence/`；8个bin/obj已检查进程/路径/链接后送入Windows回收站，可恢复，证据继续保留。

下一步FDA5仍需实际模板识别和字段映射、主表单与附件区分、历史模板和其他适用表单的官方来源/语义资格。官网forms目录有一次45秒零字节超时；356h/1571归档可用，不把目录暂时失败视作来源不存在，也不以这两种表单覆盖全部FDA5。

**FDA5前置：有界AcroForm字段事实（不计规则完成）。** 新增只读字段事实，区分当前V和默认DV、缺失/空值、父子继承、PDF null、损坏/悬空引用、无表单、XFA存在但未解释。字段/间接引用和导航共用对象及深度预算、取消检查；不执行JavaScript、不合成外观、不用默认值替代填写值。缓存间接对象保证父子和循环身份一致；表单解析异常仅留下表单不完整原因，不伪造整份PDF不可读。父属性在子覆盖前验证；widget仅接受与父有效值同型等值的冗余，冲突/重复名称保持不完整。事实集合复制并只读封装；PDF inspector版本为`pdfpig-0.1.15/package-navigation-forms-v2`。

归档官方356h/1571空白源及SHA，见[来源清单](official-sources/2026-11-20-forms/source-snapshot.json)。两个真实样本均通过完整字段树读取断言；356h的`db_appl_type`为只读合成值，不能单独作为一致选择证据；1571实际IND编号字段为`db_ind_id`，不能混用其他申请列表。固定表单字典共有11项，这两种来源不证明全部适用类型、历史模板、分组归属或号码规范化，**FDA5仍NotAssessed，FDA7未实施，统计不变**。

本机禁删入口`run-package-regression.ps1`，资格仍为`remaining107-qualification`。`fda5-forms-red`构建退出0，19项9通过10失败；candidate为58/62，暴露同引用实例身份和异常隔离；candidate2为60/62，剩余官方样本页数widget重复属性；核对父子V/DV/Ff等值后candidate3为66/66。子代理提出null可选键及父属性被覆盖隐藏，`fda5-forms-review-red`5项1通过4失败。修复后`fda5-forms-final`三项目构建退出0，**74/74通过、0失败/跳过、测试退出0**，筛选仅PdfPigPackageFormTests/PdfPigPackageInspectorTests/PackagePdfValidatorTests，不是全套或监管资格。源码清单SHA`531bf1cfc8fc3a1b3bd8f4de63d87175d9ece8359965b7f0645ea8a020154c5c`，5/5交付输入匹配；TRX SHA`039dafa9c97ba9cce4d6d0987c1a9a08d091042b502c973667743360f5e54a08`，原件与备份一致。子代理限定增量静态复审PASS，未将业务规则或运行报告计入静态结论。六轮证据副本位于`backups/fda5-20261120/evidence/`。

本批六轮共12个build/bin、build/obj在进程结束、绝对路径及无链接检查后送入Windows回收站，可恢复。首次回收受转换后的显式禁删ACE阻挡，确认目标仍在后仅解除指定目录的禁删ACE，再完成回收；未永久删除。证据和资格目录保留。

下一步仍是FDA5：绑定目标regional XML实际leaf/form-type及祖先application信息，再按官方来源确定其他表单、分组和历史模板适用范围。现有PackageFileInspection引用契约尚未包含完整表单/申请绑定，不用文件名或普通文本猜测。FDA6及后续按固定队列继续。

**FDA 3/4 捕获前输入形态。** FDA第4页，High，2016-05-31生效。新增认证API `POST /api/package-validation-inputs/preflight`，显式选择Directory、ZipContainer、SingleFile，要求applicationId、path、role、profileSnapshotId，可传精确standardsSnapshotId；拒绝省略role、数字枚举及rootRelativePath等未知字段。完整输入范围由请求path确定，不强造序列根。直接文件触发3，含一个文件的目录/ZIP不触发3；完整无文件容器触发4，零字节文件仍计为文件。损坏ZIP、限额、路径/链接拒绝、取消/超时、扫描变化保持原错误，不构造空包结论。

扫描复用原reader安全检查和限额，两次完整扫描的清单/文件摘要/空目录/归档摘要一致后才返回，最终摘要后再次检查取消；修正根目录重叠判断。预检绑定快照、FDA来源、当天选择日期、输入digest及限额，scope=InputShapeOnly且isReadyForFinalization=false。JSON完整写入唯一pending后不覆盖原子发布，ID为确切UTF-8字节SHA-256；`GET /api/package-validation-inputs/preflight/{id}`只读冻结内容、校验hash/schema、不创建目录、不重新扫描。并发同内容碰撞验证已有结果，不覆盖。正常已捕获包也纳入3/4，文件/交付v30。

有效红测 `remaining107-red3` 33项中1通过32失败；早期CA1822、缺using、MSB3101缓存写入拒绝不计业务红灯。candidate3为38/40，review-red三项均失败，分别复现GET写目录、根路径重叠和HTTP枚举绑定；candidate4为43/44，唯一同摘要并发碰撞UnauthorizedAccessException，已加入仅在最终文件存在时的严格校验恢复。新增限定预检pending→JSON晋升许可，禁止覆盖/错名/跨目录/链接；`remaining107-qualification` 8类进程、14移动路径、22夹具路径检查通过，verification摘要 `9d26bc20d7321572a48f6cc7caf000d36a6beea0e9abcee5dbde1f20407fcf73`。

本机入口 `scripts/local-check/native-no-delete/run-package-regression.ps1`。`remaining107-input-final` 三项目构建退出0，826项中824通过、2旧reader夹具失败：DirectoryAndAncestorLinksCannotReadExternalFiles在链接回收/夹具保留失败；EmptyDirectoriesAndUnselectedTopLevelFilesParticipateInDigest在ZIP移动保留失败，后续digest断言未执行。未放宽相关许可、未改测试；同程序集以 `-BuildFrom .artifacts/local-check/remaining107-input-final/build` 运行 `remaining107-input-final-selected`，**824/824通过，0失败/跳过，测试退出0**，包括本批44项和相关旧回归。筛选式含上述2项及既有3类排除，不能声称reader完整类或全仓库通过。源码清单摘要 `bdea53921a4bb3056a490738bbc78b7fce9bfba38f05d84492680ce2673cdaee`，14输入匹配；TRX摘要 `a8442c5eef7081b740ada7eaf4f520e25dfc9efa997c463f1768c180d39813c8`，源与备份一致。目录99条校验通过，canonical `1f6f272b4e4f056688ea46b89d9e838b6239fef2693e334f54398bd3b7b7d696`。子代理限定静态审查已收口。证据副本在 `backups/fda3-4-20261119/evidence/`。

下一项FDA5表单申请类型/编号与us-regional.xml匹配：已确认当前PDF事实仅有基础属性和导航信息，缺少AcroForm/XFA字段事实及官方表单字段映射；先补对应读取与来源证据，再实现规则，不以普通文本猜测字段。FDA6及之后仍按固定队列顺序推进。

子代理终态确认824/824及三项目构建退出0，限定交付PASS；复核时使用构建摘要备份，主线程收尾再次确认原build/summary.json仍可读取，不据此推断此前不可读原因。本批10个候选的20个build/bin、build/obj目录经进程终止、范围和链接核验后送入Windows回收站，可恢复；资格目录、原始回执及备份保留，没有永久删除。

**FDA 1426 delete多带checksum（Partial）。** FDA第19页，Low，独立生效日2013-04-30；ICH规范6-4规定delete的checksum值为空。目标ICH/US叶显式delete带任何非空checksum（包括空格和制表符）报告Info/Fail；缺失与空字符串不触发本条，DTD要求仍单独报告。其他已知operation不触发，未知/缺失/默认operation及默认checksum证据保持未评估。固定规则顺序1408→1425→1426共享预算，三条均预置未完成结果并保存已知失败快照；文件引擎及交付绑定v29，STF范围仍Partial。

`fda1426-red` 25项中1通过、24缺规则失败。实现后一次补齐3项默认属性、预算4/5的三规则分配、进入1426取消保留前项失败、真实inspector首个1426失败后超时，共7项；原入口中断用例追加尚未开始1426唯一NE断言。最终 `fda1426-final` **730/730通过，0失败/跳过，构建与测试退出0**（25项1426、共用中断测试共16项，其余既有回归），完整命令筛选式及资格路径见本轮summary.json。源码清单摘要 `395618ecf257b7079051fc7e9e2ee9295dcb08856ac122b90efc39d95b5a5c28`，7输入匹配。目录97条通过，canonical `9f51d0ac9600d3c2d326f4b8bf731f932cbdd84c7a946012924340693dcf5f62`。子代理限定静态审查PASS。证据备份 `backups/fda1426-20261119/evidence/final/`，源与备份TRX摘要一致：`3464245e565c9a8ee10112f42fa549df2d40355d2c15f2f3f5a521206fdae014`。

本次用户指定的1408、1425、1426均完成失败复现、限定实现、Windows定向回归、只读审查及台账，按各自原子项提交；三条保持Partial，不宣称STF覆盖或全项目/外部/Formal资格。后续未指定的新规则不在本次交付内。

1426子代理终态复审确认730/730及两项摘要，最终PASS。本轮10个候选共20个build/bin、build/obj目录均在进程树结束、路径和链接核验后送入Windows回收站，可恢复；原始日志/TRX/源码清单/资格证据及backups副本保留，没有永久删除。

**FDA 1425 遗漏checksum（Partial）。** FDA第19页，Low，2008-03-10生效。目标ICH/US叶显式new/append/replace缺少或仅有空白checksum时报告Info/Fail，非空值不在本项检查格式。delete不报遗漏；未知/缺失/默认operation和默认checksum证据保持未评估。仅无命名空间checksum可供值，历史内容叶缺checksum不作为目标缺陷。沿用1408的来源/日期/中断结果保留并按规则编号共享预算；引擎及交付绑定v28，STF范围仍Partial。

本轮证据根同上。`fda1425-red` 26项中1通过、25缺规则失败；初候选694/694通过。按只读审查补真实历史new缺值、双规则共享预算、跨规则取消、默认属性，以及真实caller中1408中断后1425仍唯一NE的断言；产品语义未再改变。最终 `fda1425-final3` **698/698通过，0失败/跳过，构建与测试退出0**（26项1425、共用中断测试增至9项，其余为既有回归）；final2同为698通过但尚缺最后三行caller断言，保留为候选。源码清单摘要 `4ecf42c847dcff1fd90ace88184583df55ebd7aa298a3012919ce19508944d99`，7输入匹配。目录96条通过，canonical `5f2e25d554d0510f2fa8100f8b37f8b2d81b051cf73e576a54e3cd860ff1089a`。子代理限定静态审查收口PASS。证据备份 `backups/fda1425-20261119/evidence/final/`。下一项1426使用独立2013-04-30生效日，尚未实现。

**FDA 1408 无效checksum-type（Partial）。** FDA第19页，Low，2008-03-10生效；ICH规范6-10以MD5为算法示例。目标ICH/US叶显式类型按OrdinalIgnoreCase接受MD5，不Trim；空字符串、空白及其他类型报告Info/Fail，缺失或默认属性保持NotEvaluated。此判定不依赖operation，delete或未知operation不能遮蔽已提供的无效算法。绑定FDA/ICH归档原件和选择日期，历史骨架不算目标缺陷，STF范围仍未覆盖。文件引擎与交付绑定v27。

证据根 `.artifacts/local-check/continue-20261119-checksums/`，本机入口 `scripts/local-check/native-no-delete/run-package-regression.ps1`。`fda1408-red` 26项中1通过、25缺规则失败；`fda1408-final` 正常回归663/663通过。只读审查发现同步属性规则超时会遗漏规则结果与已知失败，新增中断契约红测 `fda1408-interruption-red` 0/2。修复为预置NE、规则进度回调和每次Fail的独立快照，保持取消传播；新增两个真实PackageFileInspector deadline用例和快照不变性断言。最终 `fda1408-final2` **668/668通过，0失败/跳过，构建与测试退出0**（26规则、5中断、637既有）。源码清单摘要 `e5b942a13e3a791574c614952f380b7a026786247bea5fd75ab8ed6eb55f2cfd`，7输入匹配。目录95条通过，canonical `fcbea53755db450ad065512f3b80da0aab42b9001fe52e9cee87e5869ec7c3d8`。子代理限定增量复审PASS。回执/TRX/源码清单另存 `backups/fda1408-20261119/evidence/`。

本轮开始时上次qualification和1391原回执仍缺失，未冒用旧资格；使用既有build.ps1/verify.ps1在本轮 `qualification/` 重新资格化，8类进程探针、14移动路径、18夹具回收检查通过，verification SHA `baabbccf5724f335d8a108ab61067b8251d77f3a5403a6d63183fd0ecc636a0a`，三份资格回执亦已备份。下一步按顺序1425、1426，每项独立红测、实现、回归、审查和提交。

**FDA 1391 index-md5.txt格式。** FDA 4.7第18页，Low，2008-03-10生效。文件引擎及交付绑定v26要求目标index-md5.txt恰好32个ASCII十六进制字节；允许大小写，拒绝尾部空格、LF/CRLF、BOM、UTF-16及非ASCII字节。仅判断格式，不比较摘要值。捕获长度非32时直接判格式失败，32时最多读取33字节验证内容和EOF；缺文件、IO错误或流与捕获长度不一致保留NotEvaluated，用户取消传播。检查独立于XML是否已解析完整；不借用历史文件。1374与1391分别预置未完成结果，共享预算并按RuleId替换结果。

本机禁删入口 `scripts/local-check/native-no-delete/run-package-regression.ps1`，证据根 `.artifacts/local-check/continue-20261118/`。`fda1391-red` 构建退出0，25项中1项ICH隔离通过、24项缺规则失败。首候选因旧资格build.json缺失而在构建前终止；未绕过禁删保护。补充XML未完成时的32字节读取、分段流、短流、额外字节、IO及取消后，`fda1391-candidate2` **637/637通过，0失败/跳过，构建与测试退出0**（36项1391、此前601项回归）。完整筛选式及保护器路径见summary.json。源码清单摘要 `70179103559266c41b91a18a55eda56c6cf6d016fd05ce665be3daddcb084d6b`，6个交付输入匹配。目录94条校验通过，canonical摘要 `9adbe9352c3bb13fe048f7d742a94b31e7aa070efcfe6bb9b5e424fc5da26fcf`。子代理限定静态审查PASS。

**证据可用性与资格恢复。** 1391执行期间发现原path-qualification只剩fixture-retention，随后此前已读取的fda1374-final也只剩fixtures，原始回执缺失原因未查明；本轮明确回收目标仅为各候选build/bin、build/obj。1374的601项终态及摘要已于缺失前核验，不能再宣称其原路径回执仍可复读。使用现有build.ps1、verify.ps1在新目录 `path-qualification-20261119-r2` 重新资格化：8类进程探针均符合预期，14项移动路径、18项夹具回收检查通过；verification摘要 `001e8078e78223b0b0d39e4d812669dcba72913f3a9bced3732063ea5c57a829`。本次637项重新执行此前601项范围，未以旧结果替代。新资格三份回执及candidate2的构建/测试日志、进程回执、summary、TRX、源码清单复制至 `backups/fda1391-20261119/evidence/`；TRX两处摘要一致：`aa19d4fbd8a98062cd0c29c2a3790b9b8408d5dbfffe4fdafd398f8f2cc7e18a`。

下一项1408（无效checksum-type）已核对FDA第19页，与1425缺失checksum、1426 delete多带checksum分别处理；1408/1425生效于2008-03-10，1426生效于2013-04-30。三项均尚未实施，后续继续先复现、定向回归、只读审查和独立提交。1362仍待STF解析。

1391子代理终态复审确认637/637、退出码0及源码清单摘要，交付PASS。candidate2的build/bin和build/obj在进程、路径和链接检查后送入Windows回收站，可恢复；red部分回执也已缺失，其剩余build/bin保留待后续核对，未扩大清理。新资格目录和证据备份继续保留。

**FDA 1374 实际MD5匹配（Partial）。** FDA 4.7第18页，Low，2008-03-10生效；ICH规范2-2、6-2要求各叶文件及index.xml校验值。文件引擎及交付绑定v25比较目标ICH/US叶的有效md5声明与捕获文件实际MD5，显式历史清单可提供被引用字节，但历史骨架的声明不报告为目标错误。目标index-md5.txt仅在32字节ASCII hex时与index.xml比较，不裁剪行尾。缺文件/历史、无效格式/类型、未知operation、未来/外部/query路径均保持NotEvaluated。声明大小写不影响摘要匹配；发现项保留Actual/Expected、源属性位置及文件。STF等未读取XML范围继续Partial。

本机禁删入口 `scripts/local-check/native-no-delete/run-package-regression.ps1`，证据根 `.artifacts/local-check/continue-20261118/`。`fda1374-red` 构建退出0，31项中1项ICH隔离通过、30项缺规则失败。补充来源、不同历史字节、空query、operation及index编码边界后，`fda1374-candidate` 596/596通过。审查确认index读取超时会丢失此前叶失败，`fda1374-interruption-red` 4/5通过，唯一失败为Expected Fail / Actual NotEvaluated。修复为每次已知失败及时保存独立结果快照，保留原超时/显式取消控制流，并补首个失败定位和真实MD5断言。最终 `fda1374-final` **601/601通过，0失败/跳过，构建与测试退出0**（42项规则、5项异常/预算、554项既有回归）。完整筛选式及保护器路径见summary.json。源码清单摘要 `aec58c227bff8ea97121be6326366bf0d89aae3b0607d9d13a73971f2e6ad157`，8个交付输入匹配。目录93条校验通过，canonical摘要 `7642dade2dc4cd5f5102116dba32219c89cbd43e89e9f512d1188dfd8c6128ec`。子代理限定增量复审PASS。

下一项1391独立检查index-md5.txt格式：恰好32个ASCII十六进制字节，尾部空格、LF/CRLF、BOM与非ASCII不接受；缺文件未评估，匹配值由1374负责。失败测试已准备，尚未实施。1362仍待STF解析证据。

本批1374的red/candidate/interruption-red/final四个候选共8个build/bin、build/obj目录，确认进程树结束、路径在证据根内且无链接后送入Windows回收站，可恢复；日志、TRX、源码清单及夹具保留，没有永久删除。

**FDA 1368 多余属性（Partial）。** FDA 4.7 第17页，Low，2008-03-10生效。文件引擎及交付绑定v24检查目标ICH标准章节的显式属性；完整祖先链必须符合固定节点定义，属性QName必须在对应白名单中。命名空间声明和DTD默认属性不产生多余属性发现。固定XML资产清单摘要 `71246a025b0a8cbb97a57e7eca16919a50cbd69c7fa573936656529f6d3d50a1` 绑定节点schema及ICH DTD；子代理静态比对159个标准章节的属性名集合与有效DTD ATTLIST，未发现差异。区域、叶、扩展节点和STF范围仍未资格化，Partial报告边界保持。

证据根 `.artifacts/local-check/continue-20261118/`，执行本机禁删入口 `scripts/local-check/native-no-delete/run-package-regression.ps1`。`fda1368-red` 构建退出0，19项中1项ICH隔离通过、18项因缺少规则结果失败。`fda1368-candidate` **554/554通过，0失败/跳过，构建与测试退出0**，含19项1368及此前535项选定回归；完整筛选式和保护器路径见该目录 `summary.json`。源码清单摘要 `e8cf2d19dbc251a0797762ca5ea31390d726cf04c890431b7dcb5fb74fb96338`，6个交付输入匹配。独立目录92条通过，canonical摘要 `ea1244f160da0d5817208d016c15e465fe0a3212d8fa4aeb8ec6435d34423edb`。子代理静态及终态审查PASS。

下一项1374已核对FDA第18页（Low，2008-03-10）和现有FILE-MD5逻辑：捕获清单已有真实文件MD5，通用检查区分FILE_CHECKSUM_MISMATCH、INVALID_FILE_CHECKSUM及缺文件证据。下一批先以失败用例区分摘要不符、格式/类型错误、文件缺失与历史未提供，再建立官方1374结果；尚未实现。1391的index-md5.txt格式是独立项，不并入1374。1362仍待STF解析证据。

**FDA 1357 M2–M5必需属性（Partial）。** FDA 4.7 第16页，Medium，2008-03-10生效。固定ICH DTD SHA-256 `c094aa2bded99564ade8ac78ae1540f95e518461b8eebe9a3063f67a165c2731` 授权四类目标ICH章节：M2.3.S/M3.2.S的substance、manufacturer，以及M2.7.3/M5.3.5的indication。文件引擎及交付绑定v23检查精确祖先链中的非空白值，缺失定位到属性/元素并报告Warning / Fail。错误命名空间、错误位置和区域骨架同名元素不归入该ICH范围；来源、日期、预算及未完成证据保持明确，已知失败优先。STF属性尚未解析，规则Partial，局部Pass在报告中降为NotEvaluated。

本机禁删入口同1344，证据根 `.artifacts/local-check/continue-20261118/`。`fda1357-red` 构建退出0，22项中1项ICH隔离通过、21项因缺少1357结果失败。`fda1357-candidate` 因CA1859私有方法返回类型要求而构建失败、未执行测试；改为实际数组类型后，`fda1357-final` **535/535通过，0失败/跳过，构建与测试退出0**，包含22项1357及此前513项选定回归。完整筛选式和保护器路径见该目录 `summary.json`。源码清单摘要 `c8b840a13e04eb0fdf685dd965248a95a8855f80aabae45db1b319a620e553a5`，6个交付输入匹配。独立目录91条通过，canonical摘要 `ae333ae5be1d50227e9c5495b77171739f346199955ec891a24970ff1386f00e`。子代理限定静态审查PASS。

下一项依据已核对FDA第17页：1362是STF重复业务属性，当前ICH/区域骨架读取不构成STF覆盖，继续NotAssessed；1368可先独立评估ICH章节多余属性，例如M4/M5中额外manufacturer，STF范围继续单列。尚未开始1368实现。

2026-11-19本轮回收：1344及1357的red/candidate/final六个候选共12个build/bin、build/obj目录，在确认进程树结束、路径范围和无链接后送入Windows回收站，可恢复；日志、TRX、源码清单、进程回执及夹具保留。没有永久删除。

**FDA 1344 属性首尾空格（Partial）。** FDA 4.7 第 16 页，Low，2008-03-10 生效。XML读取器 v2 为显式属性保存 `ExplicitValue`：XML字符引用/实体解码后、DTD类型规范化前的值。已有 `Value`、`IsDefault` 和身份解析语义保持不变；DTD默认属性不伪装为显式输入。辅助读取器逐元素同步、禁用外部解析并保留字符/取消限制。无法获得显式值时保留未评估。文件引擎及交付绑定 v22 对目标ICH/US骨架全元素属性首尾 U+0020 报告 Info / Fail，定位属性实际前缀、行列和源文件；历史骨架不计作目标错误。STF及其他XML范围未资格化，报告仍为Partial，局部Pass降为NotEvaluated。

本机禁删入口 `scripts/local-check/native-no-delete/run-package-regression.ps1`，证据根 `.artifacts/local-check/continue-20261118/`。`fda1344-red` 构建退出0，19项中1项ICH隔离通过，3项在显式原值缺失断言失败，15项因缺少规则失败。`fda1344-candidate` 19/19通过，但尚未更新目录摘要，未作为最终验收。更新摘要并补非叶、默认属性、替代前缀、字符引用、缺命名空间、历史隔离和Partial报告断言后，`fda1344-final` **513/513通过，0失败/跳过，构建与测试退出0**，包括26项1344、39项XML读取器和448项既有定向回归。完整筛选式、保护器路径见该目录 `summary.json`；未纳入旧PhysicalDefects组，以及XML组中的自定义区域文件移动和缺区域文件模拟两类用例。XML夹具仅把captures改到已资格的validation/captures。源码清单摘要 `13f9334400dc6c68e73f5ebfbb67dd734c08a42a71f61c22bfadb7639f32879d`，10个交付输入匹配。独立目录检查90条通过，canonical摘要 `2f888057af5785f2f6df524cb25d2f1bc32563539b11d931e784169e55697386`。子代理限定只读审查PASS。

下一项1357已核对FDA第16页与ICH DTD：M2.3.S/M3.2.S的substance、manufacturer，M2.7.3/M5.3.5的indication为必需属性；现有NODE-CONTEXT已有通用缺属性发现，但尚无1357官方结果。下一批先复现缺失、空白、有效值与STF未覆盖边界，继续独立实现/回归/审查/提交。

**FDA 1323 叶引用缺少对应文件。** FDA 4.7 第 15 页规定 High，2022-03-01 生效。ICH Q&A 36 第 12 项要求 href 指向的文件存在；跨序列引用的地区许可单独评估。文件引擎 v21 只从目标 ICH/US 骨架取叶，按大小写精确核对显式捕获的目标及历史文件清单。文件缺失报告 Error / Fail，并定位实际 XLink 属性、行列和叶。未提供历史、未来序列、外部/无效路径、查询参数、来源缺失、骨架未读完整或预算不足保留 NotEvaluated。delete 的多余 href 由 1051 报告；历史骨架的坏叶不归入目标 1323。

证据根 `.artifacts/local-check/continue-20261118/`。`fda1323-red` 在测试项目构建时发生 MSB3101 缓存写入拒绝，不计业务红灯。源码不变的新目录 `fda1323-red2` 构建退出 0，26 项中 ICH 隔离对照通过、25 项因缺少规则结果失败。实现后 `fda1323-candidate` 446/446 通过。子代理只读审查通过并建议补两项跨规则/历史隔离断言；测试增量复审通过后，`scripts/local-check/native-no-delete/run-package-regression.ps1` 运行 `fda1323-final`，**448/448 通过，0 失败/跳过，构建与测试退出 0**，含 28 项 1323 用例。完整筛选式及保护器路径见该目录 `summary.json`。源码清单摘要 `a6ec793cdec11738df9533302f25ea4a637cdd798859603dcc491464a6168a18`，7 个交付输入与最终构建一致。独立目录检查退出 0，canonical 摘要 `7c3f80b7c6ac458d14ee3612b2417cb3a018ace3c7b75069ea67b5fcf1546849`。这是 Windows 选定回归，不代表全仓库或外部符合性资格。

下一项 FDA 1344 属性首尾空格已核对官方第 16 页（Low，2008-03-10）。现有读取器使用 DTD 验证；本机内存 XML 最小样例证实 ID 类型的 `" token "` 被规范化为 `"token"`，CDATA 的 `" label "` 仍保留空格。下一批先用失败用例区分原始显式属性、DTD 默认属性和已规范化值，再决定原始值采集方式。1344 保持 NotAssessed；1357 另涉及 STF 条件，尚未实现。

2026-11-19 收尾：确认相关进程树均已停止、路径位于本批证据根内且无链接后，将 `fda1038-candidate`、`fda1323-red`、`fda1323-red2`、`fda1323-candidate`、`fda1323-final` 各自的 `build/bin` 和 `build/obj` 共 10 个目录送入 Windows 回收站，可恢复。首次回收因禁删ACL拒绝，分步解除已结束产物目录的继承禁删项后成功；未永久删除。各目录原始日志、TRX、进程回执、源码清单和夹具证据保留。

**FDA 1038 申请表和附信操作（Partial）。** FDA 4.7 第 6 页规定 Low，2013-04-30 生效。文件引擎 v20 检查目标 US 区域骨架中申请表、行政申请表和附信位置的显式操作，支持中间 node-extension。append/delete 报告可定位的 Info / Fail；replace 因官方允许纠正错误文档的例外而保持 NotEvaluated。缺失、默认或未知操作、未读完整及预算限制也保留未评估。new 在组件中通过，报告仍按 Partial 降为 NotEvaluated，不视为完整资格。

证据根 `.artifacts/local-check/continue-20261118/`。`fda1038-red` 的 12 项均因缺少规则结果失败。`scripts/local-check/native-no-delete/run-package-regression.ps1` 运行 `fda1038-candidate`，**420/420 通过，0 跳过，构建与测试退出 0**，其中 12 项直接针对 1038。完整筛选式和保护器路径见该目录 `summary.json`。源码清单摘要 `95f4f3a2b60a5d4b2a8a9bcdea731d6a9e8ca91c7441dad0e7462f93814d5ff5`。独立目录校验退出 0，目录摘要 `99118b0c7ed226f00461e248ee02d8f946fbea0588a02199f4649de7629a4da0`。子代理静态只读审查通过。下一项评估 FDA 1323 的叶引用文件存在性；既有路径和跨申请证据依赖继续保留。

**FDA 1130 必需文件校验值（Partial）。** FDA 4.7 第 9 页规定 Low，2013-04-30 生效。文件引擎 v19 读取目标序列所交付 ICH 主 DTD 的 MD5，与 [ICH 发布的校验清单](https://admin.ich.org/sites/default/files/inline-files/ich-ectd-3-2_md5_0.zip) 中的 `1d6f631cc6b6357f0f4fe378e5f79a27` 比较。新采集的 FDA 快照绑定清单 ZIP 原件，SHA-256 为 `40f9a3f83d6ff5de7b4a46793ca2e1c4e9bab7df30b4ba2c79f8ffcf4b512224`。文件存在且摘要不同时，在 1130 发现项中给出目标文件、期望值和实际值。文件缺失时由 1119 报告缺失，1130 为 NotEvaluated。区域 DTD、样式表和 STF 等条件性支持文件的适用范围待核实。

证据根 `.artifacts/local-check/continue-20261118/`。`fda1130-red` 11 项均因缺少规则结果失败。`scripts/local-check/native-no-delete/run-package-regression.ps1` 运行 `fda1130-final`，**408/408 通过，0 跳过，构建与测试退出 0**。其中 11 项直接针对 1130。完整筛选式和保护器路径见该目录 `summary.json`。源码清单摘要 `d10bbd3d7ffee5842a80bc97798b3e0fed55f80cc783977ed1f8bc30acf27b1c`。`python -B scripts/tests/test_package_rule_catalog.py` 退出 0，目录摘要 `b63222498d8cbfcc648a81fed3992d70329ecec47c16a92b679a3279607cc5d5`。子代理只读审查通过。

**FDA 1119 缺少必需文件（Partial）。** 固定 4.7 第 9 页，Medium，2008-03-10 生效。ICH 3.2.2 附录 6 第 6-2 页要求每个序列交付 ICH 主骨架 DTD。文件引擎 v18 在目标清单精确检查 `util/dtd/ich-ectd-3-2.dtd`。仅历史序列或其他目录内有同名文件时仍报告目标缺失。内容校验由 1130 处理。区域支持文件、样式表及 STF 等条件性文件仍需适用性证据。

证据根 `.artifacts/local-check/continue-20261118/`。`fda1119-red` 的 12 项均因缺少规则结果失败。以 `scripts/local-check/native-no-delete/run-package-regression.ps1` 运行 `fda1119-final`，**397/397 通过，0 跳过，构建与测试退出 0**。筛选式和保护器路径见该目录 `summary.json`。源码清单摘要 `60ad47fbb5dd410e71d15cd6a2d4854cb915007d99ae094880cee4fed2c338cd`。独立目录校验退出 0，目录摘要 `b16122ed9932c9b04a13df9fea2a4741aa39f12c9650b416368f8ffdea4a568f`。子代理只读审查通过。

**FDA 1111 重复区域骨架。** 固定 4.7 第 8 页，High，2013-04-30 生效。文件引擎 v17 分别检查目标序列中的同名 `us-regional.xml` 和根下 M1 元素的区域引用。发现项可定位到重复文件或第二个叶。目标骨架未读完整时，已在清单中确认的重复文件仍报告 Fail。指回 ICH `index.xml` 的区域入口由 XML 读取阶段拒绝，报告保留出错叶的位置。

证据根 `.artifacts/local-check/continue-20261118/`。`fda1111-red` 的 12 项因缺少规则结果失败。子代理发现错误嵌套 M1 和指向 ICH 骨架的引用可导致误报，`fda1111-review-red` 中 12 项通过、2 项复现。首次修复的 `fda1111-final` 为 384/385，自引用使同一骨架重复进入生命周期字典。修复入口后以 `scripts/local-check/native-no-delete/run-package-regression.ps1` 运行 `fda1111-final2`，**385/385 通过，0 跳过，构建与测试退出 0**。其中 14 项针对 1111。筛选式和保护器路径见该目录 `summary.json`。源码清单摘要 `41380d3f52c2eb046af842058a1f64f9e9fe444f0a4dd57340d4ba164fa9c644`。独立目录校验退出 0，摘要 `7430d4604764905a827e93733c44683340d6600d6b6cf8fbb440f88e2518710b`。子代理最终复审通过。

**FDA 1314 工具目录中的多余文件（Partial）。** 固定 4.7 第 14 页，Medium，2008-03-10 生效。ICH Q&A 51 允许使用其他地区认可的支持文件。文件引擎 v16 识别目标序列 `util/` 下与已归档资产路径、SHA-256 同时匹配的文件。名称和八字节文件头都匹配 Finder 元数据时报告 Fail。其余用途待核实的文件产生可定位的 NotEvaluated。组件通过时报告仍显示 Partial / NotEvaluated。

证据根 `.artifacts/local-check/continue-20261118/`。`fda1314-red` 的 11 项均因缺少规则结果失败。使用 `scripts/local-check/native-no-delete/run-package-regression.ps1` 运行 `fda1314-candidate`，**371/371 通过，0 跳过，构建与测试均退出 0**。筛选式和保护器路径见该目录 `summary.json`。源码清单摘要 `20bf311fae0a2437f68a1523bab7d1723055ef94700492bded325e20f0d1c71a`。`python -B scripts/tests/test_package_rule_catalog.py` 退出 0，目录摘要 `a634d6db04345fbb6754c7f354b92dfe289900e9cf06d4b5d4af13a730f9bc9b`。子代理只读审查通过。

**FDA 1306 缺少对应叶的文件。** 固定 4.7 第 14 页，High，2022-03-01 生效。ICH eCTD Q&A v1.33 工作表「Q&A No. 36」第 13 项将范围定为目标序列 m1 至 m5 中的文件，并排除名为 util 的子目录。引擎 v15 解析 ICH 与区域骨架叶的相对引用，按大小写精确比对捕获路径。区域入口被拒、骨架未读完整或快照缺少来源时返回 NotEvaluated。2026-11-18 起采集的 FDA 快照包含 ICH 原件及来源记录。归档 ZIP 共 563835 B，SHA-256 为 `6c2fc32f3abea2d51c7a82e8d1f76b3228672dfd0968f1b9f912d43bb2454fdb`。[ICH 发布页](https://admin.ich.org/page/ich-electronic-common-technical-document-ectd-v322-specification-and-related-files)

证据根 `.artifacts/local-check/continue-20261118/`。`fda1306-red` 的 14 项均因缺少规则结果失败。`fda1306-candidate` 339/341 通过，两个失败定位到夹具误写 US DTD 的 XLink 命名空间。改为读取区域骨架声明并保留错误命名空间反例后，`fda1306-final` 342/342 通过。只读初审发现区域入口被拒和 ICH 原件绑定两个缺口，`fda1306-review-red` 17 项中有 2 项复现。修复后使用 `scripts/local-check/native-no-delete/run-package-regression.ps1` 运行 `fda1306-final2`，包含全部 17 项 1306 用例与官方来源快照回归。**360/360 通过，0 跳过，构建与测试退出 0**。保护器和筛选式见该目录 `summary.json`。源码清单摘要 `3a6fa4a9e9185a087f65772e7b81517296f5198f00f08aafdcc38da40f82f78e`。独立目录检查退出 0，摘要 `bbd98aaa712ead0a1b00448256411feea690848f9d086a85a75d94fb8e8eca46`。子代理复审 PASS。

**FDA 1255 章节中的文件扩展名（Partial）。** 固定 4.7 第 12 页规定 Medium，2008-03-10 生效。文件引擎 v14 按目标 ICH 骨架中叶的祖先节点确定 M2 至 M5 模块，并解析到目标清单中的实际文件。格式表采用 FDA v9.3 第 3–7 页，归档 SHA-256 `8d5b4f2ef220c16324d83e44cc50595e087ccd44d1db42ebcca4084976abae6b`。2025-08-20 修订日前的历史格式版本、区域或更细章节、其余格式及业务用途保留待评估。报告保留明确的 Fail，组件通过时仍显示 Partial / NotEvaluated。

本机禁删脚本 `scripts/local-check/native-no-delete/run-package-regression.ps1` 的证据根为 `.artifacts/local-check/continue-20261118/`。`fda1255-red` 共 13 项因缺少规则结果失败。`fda1255-candidate` 共 **327/327 通过，0 跳过，构建和测试退出 0**。包括同名 `.txt` 在不同模块中的结果、`.sdf` 带片段的引用、缺少来源、日期或文件及报告状态。命令筛选式及保护器路径见该目录 `summary.json`。源码清单摘要 `4f69f622f25b54f93ce16f838adffac277a7e2a2f12dfa1e00c650aff1f90842`。独立目录检查 `python -B scripts/tests/test_package_rule_catalog.py` 退出 0，摘要 `67ed33acd60b4b9b2e48d40e66829a7fbce685967a6908a3c08f8dace7af218a`。子代理只读审查 PASS。

**FDA 1238 文件大小（Partial）。** 固定 4.7 第 12 页规定 400 MB、Low、2008-03-10 生效。检查使用目标序列清单中的解压字节数。大于 419,430,400 B 且扩展名和文件头均匹配 PDF 时报告可定位的 Fail。400,000,001 至 419,430,400 B 的换算边界、其他大文件及数据集保留 NotEvaluated。组件通过时，报告按 Partial 转为 NotEvaluated 并阻止就绪。已知失败保留。补充来源为 FDA《Study Data Technical Conformance Guide》October 2026 第 20 页 §3.1.2，讨论超过 5 GB 的数据集拆分及未拆分版本。归档文件 SHA-256 `3455b7dc93d1866bc94684d8dd3200f15a297e8d84d36e4be63d45e36a1bf689`。数据集门槛尚待资格验证。

证据根 `.artifacts/local-check/continue-20261118/`。`fda1238-red` 5 项中 4 项因缺少规则结果失败。只读初审发现生效日前无法建立报告，`fda1238-report-red` 3 项中 2 项因 FDA 1034 和 1298 抛错。当前目录为 14 条受日期限制的 Always 规则声明生效日，交付报告沿用检查日期。生效日当天错误的 NotApplicable 仍被拒绝。文件引擎及服务绑定 v13。

本机以 `pwsh -NoProfile -File ./scripts/local-check/native-no-delete/run-package-regression.ps1` 运行，`-Evidence .artifacts/local-check/continue-20261118/fda1238-final2`，`-QualifiedGuard .artifacts/local-check/continue-20261118/path-qualification`，完整 `-Filter` 见该证据目录的 `summary.json`。最终 **314/314，0 跳过，构建与测试退出 0**，包括 20 项 1238 用例。源码清单摘要 `cd0b496feb4fee083e8df10327bf17cdb4cd9d98eb42f35fdad8f6b30404e91d`。`python -B scripts/tests/test_package_rule_catalog.py` 退出 0，目录摘要 `d0e6549e1d731eb77405d0b33ae533919c1679e6e43c686f36a49354bfb33479`。子代理复审 PASS。

**FDA 1170 缺少 modified-file 路径**。固定 FDA 4.7 第 11 页，Medium，2008-03-10 生效。目标序列叶显式执行 replace、append 或 delete 时须提供非空白的无命名空间 `modified-file`。new 允许省略。若路径有值且历史骨架中找不到对应叶，1170 通过，1153 报告缺失。文件引擎及服务绑定 v12。

证据根 `.artifacts/local-check/continue-20261118/`。`fda1170-red` 共 18 项，2 项旧快照及 ICH 对照通过，16 项因缺少规则结果失败。`fda1170-final` 构建和测试均退出 0，239/239 通过，0 跳过。子代理只读审查通过。构建输入清单 SHA-256 为 `69028bba19849c8ac5ebb0225d2052e9c491a5f1ac93182da845bd545207b83b`。目录校验退出 0，80 条，摘要 `eb901f53fb0b7f91dcbd7134124fafd55b43c2803b711678cee48fec8d24acd6`。

```powershell
pwsh -NoProfile -File ./scripts/local-check/native-no-delete/run-package-regression.ps1 -Evidence .artifacts/local-check/continue-20261118/fda1170-final -QualifiedGuard .artifacts/local-check/continue-20261118/path-qualification -Filter '(FullyQualifiedName~FdaRequiredModifiedPathTests|FullyQualifiedName~FdaModifiedLeafTargetTests|FullyQualifiedName~FdaMissingLeafPathRulesTests|FullyQualifiedName~FdaExtraneousLeafPathRulesTests|FullyQualifiedName~FdaRegionalFilePresenceTests|FullyQualifiedName~FdaEmptyDirectoryRulesTests|FullyQualifiedName~FdaFileNameRulesTests|FullyQualifiedName~FdaLeafTitleRulesTests|FullyQualifiedName~FdaLeafOperationRulesTests|FullyQualifiedName~PackageFileInspectorTests|FullyQualifiedName~PackageValidationServiceTests)&FullyQualifiedName!~PhysicalDefectsAreLocatedIndependentlyOfWriterModels'
```

**FDA 1153 查找 modified-file 所指的叶**。固定 FDA 4.7 第 10 页，Medium，2008-03-10 生效。历史序列未选或引用的骨架未读完时返回 NotEvaluated。在所选序列中确认缺少骨架文件，或完整骨架中查不到所引叶的 eCTD `ID` 时报告 Fail。重复 ID 留待评估。已找到唯一目标叶时通过查找检查。查找结果只适用于选定的输入字节，诊断定位到发起引用的目标序列叶和 `@modified-file`。

证据根 `.artifacts/local-check/continue-20261118/`。`fda1153-red` 三项均在验证底层历史证据后因缺少官方结果失败。`fda1153-final` 通过 219/219。子代理指出 `xml:id` 别名可能命中 eCTD 叶，`fda1153-alias-red` 两项中一项复现错误，真实 `ID` 对照通过。收紧精确 ID 匹配后，`fda1153-final2` 构建和测试均退出 0，221/221 通过（含 23 项 1153），0 跳过。子代理只读复核通过。构建输入清单 SHA-256 `0c3342f8e3cff3580f895988fc36bf809ec1a4c52e76807a32cdca584b1cb755`。目录校验 `python -B scripts/tests/test_package_rule_catalog.py` 退出 0，摘要 `5e32a74a7b2c8872f5456fccc2d8b9b396e4b4dfd906ced36ab993300f1b40bb`。文件引擎及服务绑定 v11。

```powershell
pwsh -NoProfile -File ./scripts/local-check/native-no-delete/run-package-regression.ps1 -Evidence .artifacts/local-check/continue-20261118/fda1153-final2 -QualifiedGuard .artifacts/local-check/continue-20261118/path-qualification -Filter '(FullyQualifiedName~FdaModifiedLeafTargetTests|FullyQualifiedName~FdaMissingLeafPathRulesTests|FullyQualifiedName~FdaExtraneousLeafPathRulesTests|FullyQualifiedName~FdaRegionalFilePresenceTests|FullyQualifiedName~FdaEmptyDirectoryRulesTests|FullyQualifiedName~FdaFileNameRulesTests|FullyQualifiedName~FdaLeafTitleRulesTests|FullyQualifiedName~FdaLeafOperationRulesTests|FullyQualifiedName~PackageFileInspectorTests|FullyQualifiedName~PackageValidationServiceTests)&FullyQualifiedName!~PhysicalDefectsAreLocatedIndependentlyOfWriterModels'
```

FDA 1154 涉及分组提交中不同申请的历史成员。`PackageInputSet` 要求所选序列属于同一申请，现有输入缺少其他申请的原始提交和分组成员证据。1154 保留 NotAssessed。下一项 FDA 1170 检查需要修改既有叶的操作是否提供 modified-file。

**FDA 1136 缺失内容路径**。固定 FDA 4.7 第 9 页，Medium，2008-03-10 生效。目标骨架上的显式 new、replace、append 叶缺少 XLink href 或值为空白时报告，并定位到该叶。delete 允许省略内容路径。文件是否存在和路径字符是否合法由各自规则检查。若区域引用带 fragment，实际交付的区域 XML 可能被跳过。文件引擎 v10 将这类包的路径检查保留为 NotEvaluated，已读骨架中有明确缺陷时报告 Fail。

本机证据根为 `.artifacts/local-check/continue-20261118/`。`fda1136-red` 构建成功，15 项均因缺少官方结果失败。`fda1136-final` 通过 195/195，随后只读审查发现区域骨架被跳过时的错误 Pass。`fda1136-skip-red` 三项中两项复现错误，另一项确认已知缺陷优先。`fda1136-final2` 因测试断言触发 CA1861 在构建阶段退出 1。修正后 `fda1136-final3` 构建、测试均退出 0，198/198 通过（含 22 项 1136 测试），0 跳过。子代理复核通过。最终构建输入清单 SHA-256 为 `b85ce0622c85c22068ca4e25cc6e7b279e9ee834bcb1c33b5c16497232bd198b`。目录独立校验退出 0，78 条，摘要 `b3be6059f9d65835f07c2b1d769b3634520843f9bbda4ca140c07d109c3ee8a0`。

最终回归命令（仓库根目录）

```powershell
pwsh -NoProfile -File ./scripts/local-check/native-no-delete/run-package-regression.ps1 -Evidence .artifacts/local-check/continue-20261118/fda1136-final3 -QualifiedGuard .artifacts/local-check/continue-20261118/path-qualification -Filter '(FullyQualifiedName~FdaMissingLeafPathRulesTests|FullyQualifiedName~FdaExtraneousLeafPathRulesTests|FullyQualifiedName~FdaRegionalFilePresenceTests|FullyQualifiedName~FdaEmptyDirectoryRulesTests|FullyQualifiedName~FdaFileNameRulesTests|FullyQualifiedName~FdaLeafTitleRulesTests|FullyQualifiedName~FdaLeafOperationRulesTests|FullyQualifiedName~PackageFileInspectorTests|FullyQualifiedName~PackageValidationServiceTests)&FullyQualifiedName!~PhysicalDefectsAreLocatedIndependentlyOfWriterModels'
```

**DEV-02 路径规则评估**。FDA 第 7 页和 ICH 3.2.2 第 12 页给出 FDA 1085 的 230 字符上限，含文件名和扩展名。起算目录待确认。序列内路径长 230 字符时，加 `0001/` 后为 235。现有 `FILE-NAMING` 对含序列号的整个清单路径计数。FDA 1102 第 8 页列出反斜杠、冒号、星号、问号、小于号、大于号、竖线和空格。`%20` 和 fragment 的判定口径待确认。两条均保留 NotAssessed。下一项候选 FDA 1153 涉及 modified-file 所引叶的查找，评估时需区分基线未提供与已提供骨架中找不到叶。

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

**DEV-02 FDA 1051/1068（已完成）**：固定 FDA 4.7 PDF 第 7 页，两条均为 Medium，2008-03-10 生效。只检查目标序列已读完整的骨架：显式 delete 带非空 XLink href → 1051；显式 new 带非空无命名空间的 modified-file → 1068。解析器按 XML 原始限定名建键；1051 以命名空间 URI 和局部名匹配，位置保留实际前缀。未知、缺失和默认 operation 不猜测；保留历史隔离、共享预算和已知失败优先。文件引擎及运行绑定 v9；33/147 已实施，114 条完整覆盖仍待评估。

失败复现 `continue-20261117/fda-path-red`：31 项中 29 项因缺少官方结果失败。Windows 终态证据在 `.artifacts/local-check/continue-20261118/fda-path-final/`：176/176 通过（本批 32 项），0 失败、0 未执行，build/test 均退出 0；构建输入清单 SHA-256 `a6504c20b3b7d9ca415ddb85e5c499c593574927e16be85b2360f6f09cdb174a`。原守卫资格目录缺失，使用固定 SDK 重新运行 `native-no-delete/build.ps1`、`verify.ps1`，资格回执在同级 `path-qualification/verification.json`。实际回归命令（仓库根目录）：

```powershell
pwsh -NoProfile -File ./scripts/local-check/native-no-delete/run-package-regression.ps1 -Evidence .artifacts/local-check/continue-20261118/fda-path-final -QualifiedGuard .artifacts/local-check/continue-20261118/path-qualification -Filter '(FullyQualifiedName~FdaExtraneousLeafPathRulesTests|FullyQualifiedName~FdaRegionalFilePresenceTests|FullyQualifiedName~FdaEmptyDirectoryRulesTests|FullyQualifiedName~FdaFileNameRulesTests|FullyQualifiedName~FdaLeafTitleRulesTests|FullyQualifiedName~FdaLeafOperationRulesTests|FullyQualifiedName~PackageFileInspectorTests|FullyQualifiedName~PackageValidationServiceTests)&FullyQualifiedName!~PhysicalDefectsAreLocatedIndependentlyOfWriterModels'
```

`python -B scripts/tests/test_package_rule_catalog.py` 退出 0，内部 77 条，canonical SHA-256 `6f3596f03f867708ea36eb20bdae8601b3f90f86f08599c9137d42acc779f8ae`。子代理只读审查 PASS，无阻断项。未覆盖旧 PhysicalDefects 组、全套验包或外部 FDA 资格；属性精确行列与两条规则争用最后一个预算名额未单独动态断言。

**DEV-02 FDA 2（已完成）**：固定官方PDF第4页要求US 3.3提交包含us-regional.xml（High，2017-04-02生效）。新增目标序列`m1/us/us-regional.xml`存在性检查；不借用显式历史/外围同名文件，不把目录或错误路径当文件；保留固定来源/snapshot/profile/日期/共享finding预算门禁。存在性Pass不替代XML合法性，空/畸形文件由定位到该区域文件的XML检查失败。文件引擎及运行绑定同步为v8。inventory为31/147，116完整覆盖仍待评估；内部catalog75条，摘要`9759f1aba4be93b6b301122ccee13b689737979ee7367c8e30aeca5ac7dcd615`。

证据`.artifacts/local-check/continue-20261117/fda2-*`：red为21项/2过19缺规则失败；首final为140项/136过4败（1预算样例未提前耗尽预算，改为FDA1204禁止字符；3旧物理缺失夹具被已资格化守卫拒绝文件回收移动，未改守卫或计入通过）。review另发现引擎VersionFor未同步，已修复并加版本一致性断言。final2/final3各144/144；后者已将空/畸形XML失败断言精确到区域文件，随后校正测试xlink命名空间，最终final4为144/144、0跳过、build/test均0、passed=true，源码manifest SHA256 `32ed93049ea7e74ccb2ab5b21796e83b7440e3fcaef94ea4eb60111766b35866`。实际命令为保留式`run-package-regression.ps1 -Evidence .../fda2-final4 -QualifiedGuard .../service-retention-20261110/qualification-2 -Filter '(FullyQualifiedName~FdaRegionalFilePresenceTests|FullyQualifiedName~FdaEmptyDirectoryRulesTests|FullyQualifiedName~FdaFileNameRulesTests|FullyQualifiedName~FdaLeafTitleRulesTests|FullyQualifiedName~FdaLeafOperationRulesTests|FullyQualifiedName~PackageFileInspectorTests|FullyQualifiedName~PackageValidationServiceTests)&FullyQualifiedName!~PhysicalDefectsAreLocatedIndependentlyOfWriterModels'`；包含23个FDA2用例及实际服务绑定，明确排除旧PhysicalDefects组，不声称该组或全套通过。catalog独立Python检查通过；最终只读review PASS，交付文件摘要匹配构建输入，无阻断项。

相邻FDA3/4初步差额：当前`PackageInputReader`仅接受可扫描目录/ZIP且必须找到准确index.xml根；非ZIP单文件经INVALID_INPUT_DATA拒绝，空目录/空ZIP经SEQUENCE_ROOT_NOT_FOUND拒绝。这是捕获前输入边界，尚无绑定标准快照的官方覆盖报告，因此3/4继续NotAssessed，不能仅把现有拒绝改名为完整FDA覆盖。后续需单独设计捕获前拒绝证据与官方适用性映射；先继续可独立实施的leaf路径/引用规则。

**Chromium短路径修复**：原profile位于深层任务目录，Unix socket超长。改为在`/data/home/ratools-browser-`前缀下用mkdtemp独占创建短profile，日志记录完整保留路径，无删除。统一命令最终`20261117T154702-7afa3f9230404239889860397546564d`、源码包SHA256 `ee30723fce8569ec60060d45ca3d628a25d68ca5eba5af11ce288e837054b034`：CLI/远端exit0、category=passed，13个步骤均exit0，真实API/InMemory与Chromium共8项业务检查通过、errors=[]。原生ZIP下载69920字节，SHA256 `4f7e06865d554e1bc1a132049b868395aae638573ee7da32a91ca4c6bd6228fa`与快照清单一致，开发快照/拒绝正式交付/新草稿与旧快照复读均通过；独立ZIP/PDF检查3/3。最终只读review PASS，无阻断项。

**DEV-01外部ZIP浏览器验收**：统一命令`pwsh -NoProfile -File ./scripts/remote-check.ps1 -Task test -Target linux -Scope backend-browser -RemoteRoot /data/home/ratools-remote -WaitMinutes 1`最终证据`20261117T161014-bd041731f0b94d108fa868f17996368c`，源码包SHA256 `87188a495d520dee7b4e66e34546cd5597259c01acccc6086d58a11b66756191`；exit0/passed，13项业务检查通过（本项新增5项），errors=[]。UI上传双根外部ZIP、选择目标0001与Unverified历史0000及显式ICH profile，真实worker完成；107条实际问题，第二页逐行与API一致，定位显示原始文本。Chromium按本次新GUID完成原生下载：源文件30字节/报告73192字节，逐字节与原始fixture/API归档匹配；报告SHA256 `475cecb1a09be510a962fb08c1c87508c073e99ef0d763f3b68d2d16f4e3bce1`。另一个禁用worker的API进程通过UI取消Pending，复读Cancelled/attempt0/reportAvailable=false，原ZIP摘要不变；不声称此步骤证明InMemory重启持久化或运行中取消。首次两个候选因新增模块未在脚手架显式同步清单而失败（`20261117T160100-d503c107647045c7a750c7bb0850e7f7`、`20261117T160613-2cb3aa0408b946ae872536a1135a3885`），最终将验收内联到既有同步入口，原模块移入本次backups回收目录，无删除、未改已有PowerShell修改。最终只读review PASS，无阻断项。

**DEV-01区域表单浏览器验收**：同一Linux命令最终`20261117T162352-bcc09bad7eea41878160197ea93f0863`、源码包SHA256 `6d0c2b95e40c6a6f837ed679677c55c59be7d2982056b6c8b2c70506d1e70e52`，exit0/passed、17项业务检查通过、errors=[]（本项新增4项，其余为回归，不能累加各轮总数）。实际输入官方字典/无效别名并预检定位；从fdast3切到fdast2保留不适用的fdasedt3，保存草稿后生成返回400定位到生效类型；修正fdasedt1后保存/刷新复读，精确比较未编辑共享字段、区域内容、集合UUID、上下文与snapshot。真实生成XML并独立验包，FDA47-1496、FDA47-2013、US-SUPPLEMENT-EFFECTIVE-COMBINATION均Pass；Development仍不就绪，旧快照manifest不变。首候选`20261117T162042-305a95e57da644618b48286b9582ed29`因测试轮询预检面板未渲染时访问null失败；只读review指出后改可选链并用上述最终候选通过。最终只读review PASS，无阻断项。

DEV-01在本计划限定的Development真实业务链路已完成，最终review PASS，关闭该项。后续继续DEV-02固定inventory的逐批评估/实现（当前30/147、117尚未完整评估），再按顺序推进DEV-03～06。此结果不等同PostgreSQL重启、Formal交付、独立外部验证器或Linux Python内容资格；Pending取消不冒充Running取消。

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
