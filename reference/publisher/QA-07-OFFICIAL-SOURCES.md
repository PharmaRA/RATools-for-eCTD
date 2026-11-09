# QA-07 官方来源与独立验证器输入核验

核验日期：2026-11-09。对应[固定关闭清单](QA-07-CLOSURE-CHECKLIST.md)的 QA07-05-S/V。

**官方原件下载阻塞已解决。** 本次保存或重新核对了29个 FDA/EMA 成功原件，冻结了 URL、取得 UTC、长度、SHA-256 和适用日期。05-S 的原件取得与离线来源快照已完成；生产 `StandardsProfileSnapshot` 的来源资源绑定仍待完成，因此不关闭05-S完整条款。05-R的规则映射、05-V的实际独立验证报告和Formal资格继续开放。

## 固定交付

- [来源快照](official-sources/2026-11-09/source-snapshot.json)：`qa07-official-sources-2026-11-09`，SHA-256 `67cfb2e3d35631945cd8ba66c0deed6044ce26bc9bd514411b4953caad24ba84`。
- [验证器可用性记录](official-sources/2026-11-09/validator-availability.json)：保留历史候选和本轮查到的实际输入，不包含许可证文件或私有配置。
- [来源验证回执](official-sources/2026-11-09/verification.json)：原件/历史资产/提取输出的逐项检查结果与来源快照摘要。
- 原件归档位于同一目录；6份字节一致的EMA文档引用既有 [3.1.1 manifest](../eu-m1/3.1.1/manifest.json) 对应原件。来源快照同时绑定历史 `standards-scope.json`、EMA manifest、`decisions.json` 的摘要。
- 请求回执、失败响应、文本提取和核验脚本保留在 `.artifacts/local-check/official-sources-20261109/`。新归档以 Git 属性禁用换行转换，原件不能因检出平台改变字节。

该来源快照是本次查证的固定输入，不是已经激活的生产标准快照。历史报告使用的 FDA criteria 4.5 和旧快照保持原身份；新取得的4.7需要后续规则映射与新快照绑定。

## 官方资料与适用性

| 来源 | 本轮原文确认的版本 | 日期及含义 | 直接定位 |
| --- | --- | --- | --- |
| FDA eCTD Validation Criteria | 4.7 | PDF修订日和官网Date Support Begins均为2026-10-16；每条规则另有Effective Date | [PDF](official-sources/2026-11-09/fda-criteria.pdf)第2、3页；[官方标准页](https://www.fda.gov/drugs/electronic-regulatory-submission-and-review/ectd-submission-standards-ectd-v322-and-regional-m1) |
| FDA Technical Conformance Guide | 1.9 | 文档September 2024；官网支持日期2024-09-09 | [指南](official-sources/2026-11-09/fda-technical-guide.pdf)修订史与标准页 |
| FDA M1 specification / DTD | 文档2.6 / DTD3.3 | 文档修订日2025-03-31；官网DTD支持日期2015-06-15、要求日期2022-03-01 | [M1文档](official-sources/2026-11-09/fda-m1-specification.pdf)第1页；[官方DTD ZIP](official-sources/2026-11-09/fda-us-dtd.zip) |
| FDA CTOC / File Formats / PDF | 2.3.5 / 9.3 / 4.1 | 官网支持日期分别为2025-02-18、2025-08-20、2016-09-21 | 来源快照中的对应原件及标准表 |
| EMA EU M1 / Validation Criteria / DTD | 3.1.1 / 8.2 / 3.1 | 2025-06发布；2025-10-01起接受；2025-12-01起强制 | [官方EU M1页](https://esubmission.ema.europa.eu/eumodule1/index.htm)、[时间表](<../eu-m1/3.1.1/documents/Implementation Timeline EU M1 IG v3.1.1 and validation criteria v8.2.pdf>)第2–3页 |

FDA准则第3页明确：Effective Date表示FDA开始执行某一规则的日期，TBD表示尚不执行该规则。第47页的规则2024、2025、2026均为 **2027-01-16** 生效；截至本轮取得日，不能仅因资料版本为4.7就把这三条当成已生效规则。2024、2025的severity为High，2026为Medium。官网的Support Begins、Requirement Begins与逐规则Effective Date分别保存，不能互相替代。

FDA原文第3页还说明错误码2–7不由其商业现成验证产品验证。后续05-V必须记录独立工具的实际覆盖边界，不能假定有一份工具报告就覆盖全部FDA准则。

EMA当前网页与重新取得的原文均确认上述版本和日期；这不自动确认某个申请的procedure、country、language或提交类型。当前[区域矩阵](REGIONAL-QUALIFICATION-MATRIX.md)仍为5个selected trial、6个pending。

## 下载与字节验证

本次36条下载尝试中29条取得有效原件：FDA21个，EMA8个（含各自来源页）。首次请求的7个EMA资产返回HTTP 200，但正文均为244字节的Request Rejected；全部排除在成功清单之外。使用同一官方资产URL加 `?download=1` 后取得7个真实原件，成功与失败回执分别保留。不能将HTTP状态单独用作来源有效性判断。

新取得的EMA六份文档、ZIP本身及ZIP内六个DTD/mod/XSL均与历史manifest和当前仓库字节摘要一致。FDA官方DTD ZIP内唯一的 `us-regional-v3-3.dtd` 为35,113字节，SHA-256 `d45139785dd6aa7eb3a60a23854ec2ea7d5d17c7c87b9ca12ab268baafc1dbec`，与仓库副本一致。此对照解决该副本的来源查证，不等于区域业务规则资格。

十份FDA属性字典均已解析为XML并记录官方root、version-number与AsOf原值；解析时忽略DTD声明且禁用外部解析。不得将AsOf原值混同官网支持日期。

13份PDF共286页由PyMuPDF 1.28.2在已资格的Windows原生禁删保护下分两次提取（11份/284页与2份/2页），source SHA、文本SHA与页数逐项绑定。两次提取根进程exitCode均为0；每次2个创建进程均被保护并计入Job，最终整树停止。辅助控制台进程由Job收尾，不声明所有子进程都以0退出。原始PDF仍是权威输入，文本仅作定位辅助。

这些是来源/工具证据检查，不计入后端1824项，也不重新运行无关产品测试。所有失败响应及过程证据保留，没有物理删除。

## 独立验证器实际可用性

FDA当前标准页记录其使用 **LORENZ eValidator 25.2**，Tool in Use as of Date为2026-08-25。这是FDA部署信息。项目历史 `decisions.json` 中的本机候选为 **22.1.0.02 rev.14638**，本轮没有把两者当成同一工具版本。

本轮检查了PATH入口、三个标准Windows卸载注册表分支、两个Program Files根的匹配目录名、用户/公共桌面和开始菜单的匹配项，以及匹配进程名，未定位当前可运行安装。该有界检查不能证明其他位置没有便携安装。已找到G0-05保留的公开22.1指南文本；它能证明产品说明，不能证明当前本机许可证、规则包或执行状态。

指南2.4.1/2.4.2明确Basic和ONE不支持scripting mode；8.2介绍console入口，但入口存在不等于获准使用。需要继续取得以下非秘密输入：实际安装入口或合法生成的外部报告、edition与允许的调用方式、区域profile及regulatory update精确版本、固定包/变体的输入摘要和原始报告。无需提供许可证密钥、序列号或口令。

## 下一步与关闭边界

05-S后续将此固定来源集绑定到新生产标准快照的资源与出处；05-R逐规则处理FDA4.5→4.7差异、未来生效条件及完整区域矩阵。05-V取得上述实际工具输入后，对相同字节包及变体运行独立验证并解释差异。03-F的Formal实现和04-P的Linux Python内容宿主仍按原清单验收。

本轮没有修改历史来源清单的资格状态，没有直接激活生产profile，也没有减少任何固定关闭条件。
