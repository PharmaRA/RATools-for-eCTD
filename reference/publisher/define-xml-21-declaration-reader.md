# FDA 1735/1736 前置：Define-XML 2.1 声明事实

本项仅实现纯内存、有界的声明读取，不提供 XSD 验证、CDISC 模型合规、FDA 适用性或完整规则结论。没有接入当前包验证管线；不能减少剩余待完整覆盖数量。

## 查证来源与准确边界

FDA Study Data Technical Conformance Guide October 2026，现有固定归档 `official-sources/2026-11-18/fda-study-data-technical-conformance-guide.pdf`，SHA-256 `3455b7dc93d1866bc94684d8dd3200f15a297e8d84d36e4be63d45e36a1bf689`。本轮读取物理第23、30、33页，分别明确 SDTM、ADaM、SEND 数据集由 define.xml 提供对应元数据。该指南不直接定义 XML 字段。

字段结构参考 [CDISC 官方组织仓库固定提交](https://github.com/cdisc-org/data-definition-engine/tree/6738f18dc203f8fe29a5920fc6a33d0779fe0bd8/schemas/define/2.1)。本轮从 GitHub 原始文件/内容接口读取原件并计算 SHA-256，未下载运行生成器或第三方库：

| 文件 | SHA-256 | 本项使用 |
| --- | --- | --- |
| define2-1-0.xsd | `62618c24aa046ecc3f4cb332e95de1281b1d7cbff12702ff2012ed65f3171f18` | 顶层命名空间与 include |
| define-extension.xsd | `6fc1ab186a03b5a811b1d9243a54219d8cbc3b4f9dbd216dd7a7addec1149e76` | MetaDataVersion、ItemGroupDef 扩展结构 |
| define-ns.xsd | `3f3db11167a93854a2e7bc922ece01cad22d912fdad63adef2216d21fb35237a` | Standard、Standards、leaf、StandardOID 属性 |
| define-enumerations.xsd | `02c4c4716c136087610ece1164dd13bedb5b335f3c485f87b432cd5a38341161` | 版本及标准名称/类型/状态定义，仅核对，不据此推定模型 |
| ../../odm/1.3.2/ODM1-3-2-foundation.xsd | `3f94ee334d63757430054d160990e3ad4588400a35c5b06f8dba6302b317764c` | Study、MetaDataVersion、ItemGroupDef 层级和属性 |

注意：该官方仓库的 `define-extension.xsd` 注释带有 draft 字样。这是可追溯的字段结构参考，不冒称经过完整发布资格确认的正式模式包；未归档完整模式依赖或嵌入标准快照，后续规则资格仍需独立来源与冻结绑定。

2026-11-27续核[CDISC官方发布页](https://www.cdisc.org/standards/data-exchange/define-xml/define-xml-v2-1)：页面当前显示Define-XML v2.1.11、Package Update 6 April 2026，明确标准文件需登录获取；未尝试登录、读取凭据或将仓库draft参考当作正式包。固定FDA Study Data Guide物理50–51页确认define描述提交数据元数据，SEND/SDTM使用data-tabulation-data-definition、ADaM使用analysis-data-definition。FDA1735并未要求先完成全部CDISC合规验证，因此不能把完整模型验证额外当作该规则前提；但当前声明字段正式来源、适用性与全范围绑定仍须闭合。这些依赖继续留队，不因网站登录依赖停止其他可独立规则开发。

命名空间按展开名称处理：ODM `http://www.cdisc.org/ns/odm/v1.3`、Define `http://www.cdisc.org/ns/def/v2.1`、XLink `http://www.w3.org/1999/xlink`。不混用 ICH backbone 中不同的 xlink URI。

## 已支持的事实

- 仅单个直接 Study/MetaDataVersion、显式 DefineVersion=`2.1.0`；其他版本/多上下文/Include/xml:base 返回 Unsupported。
- 保存直接 Standards/Standard 的 OID、Name、Type、Version、Status，以及直接 ItemGroupDef 的原始解码属性；不 trim、折叠大小写或把名称转换成标准化身份。
- 文件叶仅取当前 MetaDataVersion 或其直接 ItemGroupDef 下的 def:leaf，保存 ID 和 xlink:href；不访问或解析目标。
- StandardOID/ArchiveLocationID 精确关联；缺失为 Unspecified，空值/找不到为 Missing，重复目标键为 Ambiguous。缺 StandardOID 不擅自采用唯一标准；Resolved 仅表示唯一声明引用，不证明实际数据集存在、属于该模型或完整合规。
- 同一份复制字节绑定 SHA-256；字节/节点及属性/深度/解码字符分别限额。禁 DTD、外部实体及外部解析器；XML 失败/超限不发布部分事实；取消传播。ReadComplete 仅代表支持范围的读取结束。

## 后续依赖

已增加 `PackageCapturedDefineXmlReader`：必须显式选择捕获源和逻辑路径，不按扩展名识别；前后验证稳定性并重比source/input/manifest/limits身份，核清单唯一长度、完整读/EOF与SHA。选择摘要包括读取器版本、原始/有效解析限额与捕获限额；有效限额取两者更严者。Missing、OutsideSelection、InputChanged、Unavailable、LimitExceeded分开，外层Read保留内层Invalid/Unsupported/LimitExceeded，不将其变为空define；外部取消优先传播。已验证分段非可寻址流及释放失败，不要求读取器访问外部资源。

已增加 `PackageDefineDatasetInspector`：从一个明确define捕获文件出发，将唯一文件叶声明关联到同序列清单中的实际内容，使用既有XPT读取器，不按扩展名推定格式。仅处理简单可打印ASCII相对路径，允许同序列内的`.`/`..`，跨序列、百分号、查询/片段、平台和外部URI保持UnsupportedPath且不打开目标。重复声明保留各自身份，同路径内容只读一次；不同目标共享字节/行/单元格预算，清单和关联遍历有工作量限额。单文件不可读可保留其他独立事实；捕获身份变化、整组终验失败或工作量中断清空结果。TraversalComplete仅表示关联遍历和终验完成，不保证所有目标可读，也不表示模型、STF或研究身份合格。

已增加 `PackageStfDefineInspector`：显式选择当前STF文件与其中的define叶地址，核对当前捕获、规则/资产/基线/限额、实际STF归属与原始显式叶href，复用define→XPT关联结果。按同一STF中的精确文件路径分别标记Referenced、MultipleLeaves、NotReferenced、Unknown，重复引用同一叶保留次数但不冒充多个叶；另一个同study-id的STF不能补齐本STF引用。存在未知引用时不能把未匹配当确定未引用。只处理当前index叶与同序列简单相对路径，不扩写历史研究供应。

分别保留STF study-id、Define Study OID/数据集声明、实际XPT member和逐行STUDYID（行号、确定性、解码状态及原值）。缺列、不可解码、可能填充行不伪装完整；80字节全空白行不能全部视为最多79字节的合法填充，确定空值仍独立保留。Read及StudyIdsComplete仅表示相应读取/关联完整性，不能证明这些不同标识应相等；既有1738的TS STUDYID/SPREFID政策未改。原始XML不重新打开；整组终验/身份失败不发布事实。

已增加数据集身份字面比较：分别比较Define Name、SASDatasetName与已完整读取的实际XPT member，输出Unknown/Equal/Different，不trim或折叠大小写，不借文件名替代member。缺值、空值、缺文件、不可读或非完整XPT保持Unknown；定义的非空OID按精确值计数并保留唯一性，重复声明不任选也不抹去各自原值。UniqueDeclarationOid只表示当前声明集合中的非空键唯一，不是XSD有效性；Equal/Different只描述已读字符串，不能推定监管规则或模型合规。选择摘要使用`stf-define-cross-v2`。

下一闭合标准声明/真实数据的模型资格、STF标签和DM/ADSL供应的规则语义。FDA 1735/1736仍需中心/日期/历史供应政策和正式来源资格，自动发现/全部STF范围及实际业务接入尚未完成。不得仅凭文件名、STF标签或本读取器的声明创建Fail/Pass。
