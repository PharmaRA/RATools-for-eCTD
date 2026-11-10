# FDA 4.7 第二批：公司名称与分组提交一致性

2026-11-10。在已核实的FDA4.7全文基准上增加四条独立规则，完整出处仍在[147条清单](fda/4.7/criteria-inventory.json)和[固定官方原件](official-sources/2026-11-09/fda-criteria.pdf)。

| 官方编号 | 本批检查 | 官方严重级别 | 生效日期 | PDF页 |
| --- | --- | --- | --- | --- |
| 1454 | applicant-info下company-name不能缺失或为空白 | Low | 2013-04-30 | 20 |
| 2023 | 同一分组提交各申请的submission-sub-type一致 | High | 2015-06-15 | 46 |
| 2031 | 同一分组提交各申请的application-type一致 | High | 2015-06-15 | 48 |
| 2032 | 同一分组提交各申请的submission-type一致 | High | 2015-06-15 | 48 |

分组检查只比较同一个us-regional.xml的application-set内各application，不处理跨序列活动关系。2031只读取申请自身的application-number属性，不纳入cross-reference-application-number。精确Ordinal比较，不去掉空白或猜测官方枚举；单申请没有分组冲突，字典合法性仍由后续规则负责。

每个申请必须有唯一、非空的被比较字段，缺值或结构歧义记NotEvaluated。其他申请已经出现不同值时仍保留Fail，不能因中间缺值而掩盖冲突。同一申请里的重复字段只说明结构歧义，不作为不同申请间的冲突。发现项保留真实属性值、字段路径、XML位置和来源文件。

沿用固定官方来源摘要、选择日期、生效日期、解析完整性及DTD前置；旧目录不会获得其未声明的四条新规则。区域引擎升级为v4，避免把新增诊断行为与旧引擎混同。既有52条目录保持原政策，仅新增四条至56条；官方库存当前13项Implemented、134项NotAssessed。没有改变完整库存、外部对照或Formal资格门禁。

证据根`.artifacts/local-check/fda47-grouped-20261110/`。red-1因MSBuild重写AssemblyReference缓存被禁删保护拒绝而失败，不计业务红灯；red-2实际22项全部因缺少规则结果失败。初版green-1含前350项和新22项，共372/372通过。随后只读review发现属性Actual误用元素文本、同一申请重复字段误作分组冲突两项问题，追加六个结构反例及已有属性断言，并单独保留红灯和修复验收，不能以初版372通过代替最终验收。

补充`red-review`为28项、16通过/12失败/0跳过，四类预期失败各三项。修复后最终`green-2`为 **378/378通过、0失败、0跳过**；新增28项与red-review名称多重集合完全一致，另350项与前次联合回归名称多重集合完全一致，含原9项服务测试。三项目Windows实际MSBuild通过，18个交付构建输入与manifest一致，目录独立来源/摘要校验通过。构建、测试和目录检查均有禁删守卫与整树停止回执，详见[第二批固定验收回执](fda/4.7/grouped-verification.json)。

本批不增加新的PostgreSQL、浏览器或外部验证器资格。编号与九项服务测试回收的已完成修复见[独立验收记录](QA-07-APPLICANT-AND-RETENTION.md)，整体剩余门禁见[QA-07关闭清单](QA-07-CLOSURE-CHECKLIST.md)。
