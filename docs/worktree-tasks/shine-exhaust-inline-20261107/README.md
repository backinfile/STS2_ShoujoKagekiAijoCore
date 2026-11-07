# 闪耀与消耗同行显示

## 用户原始要求

> 闪耀关键字有个patch忘了， 当和消耗关键字相邻的时候会显示为一行

合并确认问题：补丁 a48444f 已通过双版本编译和三语言 15 项实机检查。是否合并到 main，并清理本任务工作树、分支及临时游戏？前后截图和检查结果会保留。

用户回复：

> 合并并清理

工作区约定沿用根目录 `AGENTS.md`：最小改动、隔离工作树、最小必要验证，合并与清理依 `$worktree-task` 确认。

## 简要计划

1. 核对 Karen 原有关键字排版实现与 Core 闪耀描述入口。
2. 将相邻的消耗与闪耀显示在同一行，保留各语言原文及剩余次数颜色。
3. 使用双版本真实引用编译并通过游戏 MCP 定向复验。

## 起始状态

- 原检出分支：`main`，HEAD：`2c1b8b3e8e29b75f19c1d0c08fce3fe8a87961da`，工作区干净。
- 任务分支：`codex/shine-exhaust-inline-20261107`。
- 工作树：`../worktrees/STS2_ShoujoKagekiAijoCore/shine-exhaust-inline-20261107`。

## 实际变更与验证

已完成：在 `src/Modules/Presentation/ShinePresentation.cs` 的现有 `ShineDescriptionPatch` 中补回相邻消耗与闪耀的同行显示，代码净增 4 行。

- 仅当卡牌具有消耗关键字、原描述末尾为当前语言的完整消耗标记时省去换行，形成“消耗。闪耀3。”。
- 沿用中、英、日文原文、标点和剩余次数红色标记。不带消耗的闪耀牌及普通消耗牌保持原排版。
- Karen 现存 `CardKeywordInlinePatch` 处理固有与虚无；本次修复直接位于 Core 的闪耀追加入口。

### 验证

- 正式版 v0.107.1 与测试版 v0.111.0 的 Runtime 使用各自真实游戏引用编译，均 0 警告、0 错误。
- 正式版通过 `KarenSTS2MCP` 操作隔离游戏，三种语言共 15 项真实卡牌描述检查全部通过，覆盖手牌、牌组、升级预览、红色余量、无消耗闪耀牌与普通消耗牌。修复前同一探针通过 6 项、失败 9 项；修复后通过 15 项、失败 0 项。
- 水分补充的 MCP 悬停前后截图已保存并查看，确认由两行恢复为同一行。
- 实机包沿用既有 1.0.1 资源并替换本次 Stable Runtime，更新完整性清单后通过 `sdk/validate-package.ps1`。
- 隔离游戏使用随机端口、独立用户目录、最小化与禁止焦点配置，`silent_test=true`、`muted=false`、`minimized_startup_guard=true`。临时探针逐帧 `ForceDraw` 完成最小化状态的真实绘制与预热；测试进程已核对路径后关闭。
- `git diff --check` 通过；已检查跟踪文档、提交身份及完整暂存内容中的私人信息、凭据与本地绝对路径。

### 证据与哈希

证据位于 `out/tmp/shine-exhaust-inline/`（忽略目录）：`baseline-checks.json`、`fixed-checks.json`、`before.png`、`after.png`；临时游戏与探针也位于该目录。

| 项目 | SHA256 |
| --- | --- |
| 实机测试包 core.bundle | `17053FDD854334093467684300C54CDD763D2DB6313FE41FA1817A5E82802311` |
| Stable Runtime | `C56495E1657B6D6F45A4F9841DEFA7A85708CBEE79CBBCCA710B0A0208F6C798` |
| Beta Runtime | `53DEA7B4C43239FF54241E36A199AA40A3471B238C176BC4C417304651524F4E` |

### 集成状态

用户已明确确认“合并并清理”。前后截图、两份检查结果和哈希清单已复制至原检出的 `out/tmp/shine-exhaust-inline-20261107/`，逐文件 SHA256 一致。将此记录与修复作为同一提交快进合并至 `main` 后，核对提交树并清理本任务工作树、分支与临时游戏；合并前原检出仍为起始提交且保持干净。
