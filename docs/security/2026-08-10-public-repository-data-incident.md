# 2026-08-10 公开仓库数据事件处置记录

> 状态：Remote master rewritten; GitHub Support purge pending
> 关联发现：[F-03 公开仓库跟踪运行时上传数据](../reviews/2026-08-10-code-review-and-development-roadmap.md#f-03-p0-公开仓库跟踪运行时上传数据)
> 负责角色：PharmaRA 仓库管理员，直至实际数据所有者接手
> 首次引入 commit：`73388e3739ac1931c411d596b6df9bb5a8212519`
> 当前版本删除 commit：`92b10efa54a9c40576fb20708583fd24d67913cd`

## 1. 结论

历史中的 PDF 应按机密监管申报材料处理，不能按公开测试夹具处理。远端 `master` 已完成协调式历史重写，但 13 个 GitHub pull request refs、cached views 和服务器端不可达对象仍需 GitHub Support 清理。

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

## 4. 处置状态

- [x] 仓库管理员明确批准协调式历史重写和远端 force push。
- [x] 盘点 GitHub 可见 refs：1 个 branch（`master`）、0 个 tag、0 个 fork、13 个已关闭 PR；collaborator clone 仍需仓库管理员确认。
- [x] 使用隔离 bare clone 完成 `git-filter-repo 2.47.0` 演练，不修改主工作区或远端。
- [x] 在隔离的全新 clone 中使用 `git-filter-repo 2.47.0` 和 `--sensitive-data-removal` 删除已抓取 refs 下的 `src/RATools.Api/App_Data/uploads/**`。
- [x] 对重写后的全部 refs 运行数据/secret scan，并证明目标 blob 不再 reachable。
- [x] 生成受限的远端/本地 bundle 备份；确认 branch protection 未启用后，使用绑定旧远端 HEAD 的 `--force-with-lease` 更新唯一可写 ref `master`。
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

通过 GitHub API 得到的远端清单不等于远端清理完成。正式执行结果见下一节；13 个只读 pull request refs 仍必须由 GitHub Support 解除引用并触发服务器端垃圾回收。

## 6. 正式执行与远端验证

2026-08-10 在受限的工作区外目录中完成正式处置：

- 为当前本地全部 refs 和 GitHub 当前全部可见 refs 分别生成完整 bundle，并通过 `git bundle verify`；GitHub 备份包含 `master` 和 13 个 pull request head refs。
- 从 GitHub 全新 clone 抓取 14 个 refs，并纳入 6 个尚未推送的 Phase 0 缓解提交。
- 使用 `git-filter-repo 2.47.0` 解析 240 个 commit、重写 239 个；First Changed Commit 为 `73388e3739ac1931c411d596b6df9bb5a8212519`，未使用 LFS。
- `master` 的 231 个可达 commit 保持不变；重写前后候选 HEAD tree 均为 `a60661137901f7c47b7e39568f48247a9dea564c`。
- 将远端 `master` 从 `89f8a4c978cf114f5884380376a020a026d24056` 强制更新到清理后的 `e2fc859fcafe9a205888a27937fe7f1710ee778d`。推送前逐项确认 14 个远端 refs 与备份一致，避免覆盖并发工作。
- 推送后从 GitHub 全新单分支 clone 验证：上传目录历史和可达对象数均为 0，敏感 blob 不存在，`git fsck --full --no-reflogs` 通过。
- 当前本地 clone 的 `master` 和 12 文件 stash 均已迁移到清理后的历史；未跟踪 `.claude/` 保持不变，旧对象已在受限 bundle 外回收。
- 仓库卫生测试、控制器契约测试和全历史高置信 secret 签名扫描通过；后端通过 350 项、跳过 6 项 PostgreSQL 测试；前端 421 项全部通过。
- 远端 [Smoke](https://github.com/PharmaRA/RATools-for-eCTD/actions/runs/31371677416)、[CI](https://github.com/PharmaRA/RATools-for-eCTD/actions/runs/31371677480) 和 [CodeQL](https://github.com/PharmaRA/RATools-for-eCTD/actions/runs/31371677501) 均成功。

GitHub 拒绝客户端更新 `refs/pull/*`，属于平台预期行为。`changed-refs` 显示 13 个 pull request head refs 受影响；在 GitHub Support 完成解除引用、cached view 清理和服务器端垃圾回收前，不能把事件状态标记为完全关闭。

## 7. 执行前检查单

历史重写是破坏性仓库操作，只能在维护窗口中执行。负责人必须在执行前确认：

1. 所有维护者已收到停写通知。
2. 远端所有 heads/tags 的不可变备份已经生成并限制访问。
3. GitHub branch protection 和 Actions 影响已评估。
4. 已明确谁执行 force push、谁复核、谁联系 GitHub Support。
5. 已准备 clean clone/rebase 通知模板和回滚条件。

## 8. 推荐验证

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

## 9. 参考流程

- [GitHub：从存储库中删除敏感数据](https://docs.github.com/zh/authentication/keeping-your-account-and-data-secure/removing-sensitive-data-from-a-repository)

GitHub 官方流程明确指出：force push 之后，敏感对象仍可能存在于 clone、fork、pull request refs 和 cached views；完整处置需要协作者协调以及 GitHub Support 后续清理。
