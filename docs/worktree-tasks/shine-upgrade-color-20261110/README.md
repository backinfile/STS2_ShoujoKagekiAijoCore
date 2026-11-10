# 闪耀升级预览变色恢复

## 用户原始要求

> 闪耀值的专属的升级时的强制变色怎么没有了

附件为升级对比截图，升级后的引言仍显示白色闪耀数值 9。附件位置以 <用户提供的截图> 表示。

工作区约定原文见同目录 user-instructions.md。

## 简要计划

1. 通过真实卡牌描述入口复现升级闪耀值缺少专属高亮。
2. 核对 Karen 原有行为并在公共描述补丁中做最小修复。
3. 双版本定向编译，通过游戏 MCP 检查升级预览及剩余次数颜色。

## 起始状态

- 原分支为 main，HEAD 为 c7f1847a171a5fe8d215d42f8411c6057cda9a8f，工作区干净。
- 工作树为 ../worktrees/STS2_ShoujoKagekiAijoCore/shine-upgrade-color-20261110。
- 任务分支为 codex/shine-upgrade-color-20261110。

## 实际变更与验证

### 原因与修复

Karen 迁移前的 ShineViewPatch 在升级预览中将闪耀数值强制显示为金色，并取当前值与上限中的较大值。Core 初次迁移提交 632f34c 只保留了低于上限时的红色与其余白色，遗漏升级预览及超上限的金色分支。

- src/Modules/Presentation/ShinePresentation.cs 接收原生描述入口的 previewType，恢复升级预览金色与补满后的显示值，同时恢复原有的超上限金色。
- 使用原生描述预览参数识别升级预览。预览查询保持实际闪耀状态。
- tests/RoleIntegration/ShineDescriptionProbe.cs 与现有 core_roles shine-description 命令提供定向回归，覆盖引言和带消耗关键字的闪耀牌。

### 验证

- 正式版 v0.107.1、测试版 v0.111.0 Runtime 均使用各自真实游戏引用编译，0 警告、0 错误。
- 测试版通过 KarenSTS2MCP 执行相同的 14 项定向检查。修复前 6 项通过、8 项失败，修复后 14 项全部通过。
- 检查覆盖普通满次数、已升级普通显示、满次数升级预览、已消耗普通显示、已消耗升级预览、超上限普通显示及超上限升级预览。每项同时核对描述查询前后的闪耀状态一致。
- 已查看原生 NCard 升级预览截图，确认引言+的闪耀9显示金色。
- 使用独立用户目录、随机端口、--force-steam=off、最小化与禁止焦点配置，silent_test=true、muted=false、minimized_startup_guard=true。临时探针逐帧 ForceDraw 保持真实绘制。测试进程按实际 EXE 路径核对后结束。
- 本次实机检查使用测试版。正式版完成定向编译。
- git diff --check 通过。文档及完整暂存索引经过凭据与本地绝对路径检查，提交身份沿用仓库的 GitHub noreply 地址。

### 证据

证据位于忽略目录 out/tmp/shine-upgrade-color/。

- baseline-checks.json 与 fixed-checks.json 保存修复前后结果。
- baseline.png 与 fixed.png 保存原生卡牌视图。
- fixed-runtime-stable.dll 与 fixed-runtime-beta.dll 保存本次验证程序集。
- privacy-audit.json 与 validation.json 保存文档审查和最终哈希。

### 集成状态

用户已确认合并并清理。修复在任务分支整理为单一提交并同步 main 后快进合并。前后截图、回归结果、验证程序集及哈希记录保留在原检出的 out/tmp/shine-upgrade-color-20261110/。清理前逐文件核对 SHA256，合并后核对提交树与原检出状态。

## 后续确认原文

确认问题

> 修复已提交为 2ebeae8，恢复升级预览和超上限闪耀值的金色显示。双版本编译通过，测试版 14 项实机检查通过。worktree-task 要求合并与清理先确认。是否合并到 main，并清理任务工作树、分支和临时游戏？截图与回归结果会保留。

用户回复

> 合并并清理
