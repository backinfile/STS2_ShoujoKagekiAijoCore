# artifacts 目录整理

## 用户原始要求

用户提供的完整仓库约定见 `user-instructions.md`，仓库位置使用 `<repository-root>`。

> artifact目录中有一些东西可以清理清理，你来分析整理下

## 用户追加要求

> 也看看还有没有没用的worktree

## 起始状态

- 原检出：`main`，`906a9ee05684724014808754d77239b0fae2975a`，干净，无进行中的 Git 操作。
- 分支：`codex/artifacts-cleanup-20261111`。
- 工作树：`../worktrees/STS2_ShoujoKagekiAijoCore/artifacts-cleanup-20261111`。

## 简要计划

1. 统计 `artifacts/` 的文件、体积、代码及任务记录引用。
2. 对比 ZIP 内文件和解压目录的 SHA256，识别可恢复的重复副本；核对失败构建暂存和测试着色器缓存。
3. 保留唯一证据、历史脱敏候选、旧存档、私有配置与当前 Core 工坊工作区，准备明确的清理清单和目录索引。
4. 完成定向核对、提交记录；按 `$worktree-task` 确认主分支合并和列明的清理后执行。

## 分析结果

`artifacts/` 被 Git 忽略，审计时共有 395 个文件、354626136 字节（338.20 MiB）。其中 `task-deliverables/` 为 268.06 MiB，主要空间来自发行包及其解压副本。

`build.ps1` 在构建开始时建立 `package-<GUID>` 暂存目录，成功后移动到发行输出。当前两个暂存目录分别为空，以及只包含 1.4.0 程序集和 SDK、缺少资源 PCK 与 `core.bundle` 的失败构建残留。

### 已执行的清理清单

以下路径均为仓库相对路径。共 9 个目标、131 个文件、112101681 字节（106.91 MiB）；保留的原文件为 264 个、242524455 字节（231.29 MiB），另增加目录索引与本次审计记录。

| 目标 | 文件数 | 字节 | 依据 |
| --- | ---: | ---: | --- |
| `artifacts/task-deliverables/core-observations-20261108/package/ShoujoKagekiAijoCore/` | 9 | 19749843 | 与同任务 ZIP 的全部文件逐项 SHA256 一致 |
| `artifacts/task-deliverables/upload-domain-separation-20261108/ShoujoKagekiAijoCore/` | 9 | 19720383 | 与同任务 ZIP 的全部文件逐项 SHA256 一致 |
| `artifacts/task-deliverables/shine-preview-1.4.1/release/characters/ShoujoKagekiJunna/` | 7 | 63586841 | 与配套 Junna ZIP 的全部文件逐项 SHA256 一致 |
| `artifacts/package-5b750ae62f284a97954e3a407f5ec72a/` | 8 | 861780 | 不完整的旧构建暂存 |
| `artifacts/package-77f20511dc12410fae5ddfaba21d3804/` | 0 | 0 | 空暂存目录 |
| `artifacts/import.log` | 1 | 216662 | 旧 Godot 导入日志，下次构建重新生成 |
| `artifacts/import-errors.log` | 1 | 3220 | 旧 Godot 导入日志，下次构建重新生成 |
| `artifacts/core-integration-20261106/stable/user/AppData/Roaming/SlayTheSpire2/shader_cache/` | 45 | 3895201 | 隔离测试的可再生着色器缓存 |
| `artifacts/core-integration-20261106/beta/user/AppData/Roaming/SlayTheSpire2/shader_cache/` | 51 | 4067751 | 隔离测试的可再生着色器缓存 |

### 保留与整理

- 全部 ZIP、录像、截图、验证报告、日志证据、旧存档与接收器数据库保留原路径。
- `artifacts/private/` 是实际构建输入，保留；`artifacts/.gdignore` 继续阻止 Godot 导入本地材料。
- `artifacts/repair-audit-20261107/core-history-final.git/` 是尚待历史替换确认的脱敏候选，保留；该任务的 1.0.1 受测包也无完整替代副本，保留。
- 最新 Core 1.4.1 的 ZIP 和 `release/workshop/` 均保留，分别用于手动分发与待授权的工坊发布。
- 已在原检出落地 `artifacts/README.md`，列明现有目录用途和 3 组 ZIP 的原路径恢复方法。旧证据目录保留位置，历史保留清单继续作为原始快照。
- 审计、预演结果和执行脚本已保存至原检出的 `artifacts/task-deliverables/artifacts-cleanup-20261111/`。审计清单只含相对路径、大小及摘要，不含配置正文。

## 验证

- 执行前对全部 395 个文件建立 SHA256 基线，预演及实际清理前均再次核对通过。
- 4 组 ZIP 共 35 个文件与对应解压文件的路径、数量、大小和 SHA256 全部一致；其中 25 个文件属于已删除的 3 组重复目录，另外 10 个文件是保留的当前 Core 工坊包。
- 预演验证 9 个目标均位于当前仓库 `artifacts/` 内，目录无符号链接或联接点。实际清理前完整清单、SHA256 和相关运行进程核对通过；执行后 264 个保留原文件的 SHA256 全部一致。
- 当前运行的游戏进程未引用待整理的 `artifacts/` 路径；不停止其他任务的进程。
- 本次为本地文件整理，验证范围为 ZIP 内容核对、文件指纹、清理边界和预演。

- 提交前审查 43 份已跟踪及待加入文档、目录索引和 16 个 ZIP 文本条目，私人路径及凭据模式命中为 0；提交身份使用 GitHub noreply 地址。

## 工作树补充核对

核对 Core、Karen、Junna 的 `git worktree list --porcelain`、标准工作树父目录、未提交状态、与各自 `main` 的祖先关系、`git cherry`、任务记录及运行进程。三仓库 `git worktree prune --dry-run --verbose` 均无可清理的失效登记，标准父目录下也无未登记的任务目录。

- Core：只有原检出和本次 `artifacts-cleanup-20261111` 工作树，没有其他闲置工作树。本次工作树在任务集成并保留审计材料后清理。
- Karen：只有原检出，没有额外工作树。
- Junna：有 7 个额外工作树，目前均有保留依据。

| Junna 工作树 | 核对结果 |
| --- | --- |
| `act2-knowledge-event-20261111` | 1 个未合并提交，另有未提交的图片、本地化、代码和任务记录 |
| `arrow-ui-review-20261110` | 工作区干净，保留未合并的箭矢表现与性能优化提交；最新任务记录要求保留试用工作树 |
| `card-count-subsystems-20261110` | 工作区干净，1 个未合并的设计文档提交 |
| `copy-lifecycle-refactor-20261108` | 工作区干净，2 个未合并且无完整等效补丁的提交，包含业务重构与记录；任务记录明确继续保留 |
| `junna-a0-playtest-20261107` | 工作区干净，1 个未合并的试玩报告提交，完整录像仍在忽略目录 |
| `junna-a0-playtest-20261109` | 工作区干净，1 个未合并提交，包含修复、报告及回归；录像、存档检查点和日志仍在工作树 |
| `workbench-full-card-tests-20261111` | 已提交 HEAD 属于 main，但另有大量未提交代码、资源和测试；两个真实游戏进程及对应崩溃处理进程仍使用此工作树，对应测试聊天正在运行 |

Core 的 `codex/copy-lifecycle-refactor-20261108` 是已合并分支，其工作树早已移除；它不占用额外工作树目录。`master` 相对 main 仍有 7 个独有提交。此次不增加旧工作树或旧分支删除目标。

上述核对为只读操作，三个仓库的原检出、分支、工作树、未提交文件和运行进程保持原样。最初 9 个 artifact 清理目标及待确认范围保持不变。

## 执行与集成状态

已按用户确认清理原检出的 9 个目标、131 个文件，删除 112101681 字节。保留的 264 个原文件共 242524455 字节，逐文件 SHA256 均与清理前一致。已落地 `artifacts/README.md` 和 `artifacts/task-deliverables/artifacts-cleanup-20261111/` 下的审计清单、预演结果、脚本及执行回执 `result.json`。

用户已授权将本任务记录快进合并到 `main`，并清理本次工作树、任务分支及已保留的临时审计文件。集成与清理的最终实测状态记录在 `artifacts/task-deliverables/artifacts-cleanup-20261111/integration.json`；原检出状态和受保护分支在集成前后核对。其他项目的工作树和分支继续保留。

## 用户确认

> 是否执行已验证的清理方案：删除 3 份 ZIP 解压副本、2 个构建暂存目录、2 份导入日志及两版测试着色器缓存，共释放 106.9 MiB；保留全部 ZIP、录像、报告、存档、私有配置和最新 Core 工坊工作区，落地目录索引，并将 3552d75 的整理记录合入 main？同时可清理本次工作树和分支。[完整清单](docs/worktree-tasks/artifacts-cleanup-20261111/README.md)。仓库指定的 $worktree-task 明确要求确认，原文为 “the original task request alone is not approval.”

用户答复：

> 执行清理、合并并清理工作树
