# 2026-08-10 公开仓库数据事件处置记录

> 状态：Mitigated in HEAD; history cleanup approval pending
> 关联发现：[F-03 公开仓库跟踪运行时上传数据](../reviews/2026-08-10-code-review-and-development-roadmap.md#f-03-p0-公开仓库跟踪运行时上传数据)
> 负责角色：PharmaRA 仓库管理员，直至实际数据所有者接手
> 首次引入 commit：`73388e3739ac1931c411d596b6df9bb5a8212519`
> 当前版本删除 commit：`3e9ac8295f89abe4abd4b6430fe63497b8a00533`

## 1. 结论

历史中的 PDF 应按机密监管申报材料处理，不能按公开测试夹具处理。必须协调执行 Git 历史重写；仅从当前 `master` 删除文件不能完成处置。

本记录不重复正文中的申办方、产品代号或监管申请标识，避免在新的可搜索文件中再次扩散这些字段。

## 2. 定性证据

- 文件位于运行时上传目录，而不是受控的测试 fixture 目录。
- PDF 共 16 页，正文 16 次明确标记为 confidential。
- 正文包含申办方、产品代号和监管申请标识。
- PDF metadata 显示它由 Microsoft Word 在首次提交当天生成，不具备可证明的公开来源或再分发授权。
- 文件 SHA-256：`A0835580BF946F37690721600CD7B6321C274F387296641F71CD654E9DDB133E`。
- Git blob：`5aa3edc3209e66cf08c6bc16d7b5072f066be5d3`。
- 没有发现 email 或 URL；存在数字标识和多处类似电话号码的文本。该事实不降低文档本身的商业/监管机密级别。

## 3. 已完成缓解

- [x] 从当前 HEAD 删除 `src/RATools.Api/App_Data/uploads/**` 下 29 个已跟踪文件。
- [x] 保持运行时上传目录被 `.gitignore` 排除。
- [x] 增加 `scripts/tests/test_repository_hygiene.py` 并接入 CI，阻止运行时数据、数据库、私钥和非示例环境文件重新进入 Git 索引。
- [x] 当前 HEAD 高置信凭据签名扫描未发现 private key、AWS access key、GitHub token、Slack token 或 certificate block。

## 4. 尚未完成的处置

- [ ] 仓库管理员明确批准协调式历史重写和远端 force push。
- [x] 盘点 GitHub 可见 refs：1 个 branch（`master`）、0 个 tag、0 个 fork、13 个已关闭 PR；collaborator clone 仍需仓库管理员确认。
- [x] 使用隔离 bare clone 完成 `git-filter-repo 2.47.0` 演练，不修改主工作区或远端。
- [ ] 在隔离的全新 clone 中使用 `git-filter-repo >= 2.47` 和 `--sensitive-data-removal` 删除所有 refs 下的 `src/RATools.Api/App_Data/uploads/**`。
- [ ] 对重写后的全部 refs 运行数据/secret scan，并证明目标 blob 不再 reachable。
- [ ] 暂停写入、备份远端 refs、临时调整 branch protection 后，执行受控 mirror force push。
- [ ] 按 GitHub 官方流程向 Support 提供 affected PR 数、First Changed Commit 和 orphaned LFS 信息，请求清理 PR 引用、cached views 和 unreachable objects。
- [ ] 通知 fork/clone 持有者清理旧历史；协作者必须 rebase 或重新 clone，不能 merge 旧历史。
- [ ] 确认没有旧 clone 或 fork 将污染历史重新推回远端。

## 5. 历史重写演练证据

2026-08-10 在 `.artifacts/history-rewrite-rehearsal` 内完成本地隔离演练：

- 工具：`git-filter-repo 2.47.0`，参数为 `--sensitive-data-removal --invert-paths --path src/RATools.Api/App_Data/uploads/`。
- 解析 232 个 commit，重写其中 231 个；`master` 的可达 commit 数重写前后均为 230。
- First Changed Commit：`73388e3739ac1931c411d596b6df9bb5a8212519`。
- 重写前本地候选 HEAD：`98e0d11602e4b1ce69f3ae1c99c0b03acc8e9beb`。
- 重写后 rehearsal HEAD：`5268003139dbdcd3c5c82e8d4868ce252d4a117f`。
- 两个 HEAD 的 tree 均为 `bc58e3b7a8a1a52310a0ccb181fee5c692f1ceb3`，证明当前文件内容除历史身份外没有变化。
- `git log --all -- src/RATools.Api/App_Data/uploads` 输出为空。
- 敏感 blob `5aa3edc3209e66cf08c6bc16d7b5072f066be5d3` 在 repack 后不可达。
- LFS 未启用，没有需要迁移的 LFS 对象。

通过 GitHub API 得到的远端清单不等于远端清理完成。正式执行仍必须从 GitHub origin 的全新 clone 开始，让 `--sensitive-data-removal` 获取服务器暴露的全部 refs，并向 GitHub Support 报告受影响 PR。

## 6. 执行前检查单

历史重写是破坏性仓库操作，只能在维护窗口中执行。负责人必须在执行前确认：

1. 所有维护者已收到停写通知。
2. 远端所有 heads/tags 的不可变备份已经生成并限制访问。
3. GitHub branch protection 和 Actions 影响已评估。
4. 已明确谁执行 force push、谁复核、谁联系 GitHub Support。
5. 已准备 clean clone/rebase 通知模板和回滚条件。

## 7. 推荐验证

隔离 clone 中至少验证以下不变量：

```text
git log --all -- src/RATools.Api/App_Data/uploads
# no output

git cat-file -t 5aa3edc3209e66cf08c6bc16d7b5072f066be5d3
# must fail after reflog expiry/cleanup in the rewritten clone

python3 scripts/tests/test_repository_hygiene.py
# exit 0
```

还必须运行完整后端、前端、smoke 和 secret scan，确保重写没有遗漏 refs 或破坏构建基线。

## 8. 参考流程

- [GitHub：从存储库中删除敏感数据](https://docs.github.com/zh/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)

GitHub 官方流程明确指出：force push 之后，敏感对象仍可能存在于 clone、fork、pull request refs 和 cached views；完整处置需要协作者协调以及 GitHub Support 后续清理。
