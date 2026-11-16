# 迁移「存在于舞台的理由」至 Core

## 用户原始要求

> 存在于舞台的理由这张牌应该放在core中吧

> 移动过去吧，处理好卡池等内容


> 合并并清理，保留测试证据
工作区约定见仓库根目录 AGENTS.md；本任务使用 `$worktree-task`。

## 简要计划

1. 核对模型、诅咒池及闪耀奖励池行为，迁入 Core 并保持原规则与模型 ID。
2. 迁入原有图片和中英日本地化，清除 Karen 重复模型及注册。
3. 对两版真实引用进行构建，并通过游戏 MCP 定向核对池、闪耀和旧存档恢复。
4. 审查、提交并同步原分支；合并与清理在用户确认后执行。

## 起始状态

- Core：`main`，`b22abbda5ad2accbefad6cd980625876a9871ab1`，工作区干净。
- Karen：`main`，`46de4cc59cde61287526580df4a556c745dacfff`，工作区干净。
- 两仓库均使用 `codex/move-stage-reason-20261115` 分支，工作树为各自 `../worktrees/<projectName>/move-stage-reason-20261115`。

## 实际变更与验证

### 变更结果

- Core 新增 `src/Modules/Shine/CoreStageReason.cs`，使用 `CustomID("KAREN_STAGE_REASON")` 保持原模型身份，通过 BaseLib `Pool(CurseCardPool)` 唯一注册到全局诅咒池。费用、诅咒类型、消耗、闪耀 3、不可升级和空牌效保持原样。
- 原图迁入 `resources/ShoujoKagekiAijoCore/images/cards/stage_reason.png`，字节与 Karen 原图一致；三语 `KAREN_STAGE_REASON.title/description` 按原值迁入 Core。
- Karen 移除旧模型、源资源及导入文件、三语键和全局诅咒池的重复注入，保留困意等其他内容。
- Core 版本更新至 1.9.5，Karen 最低 Core 依赖同步为 1.9.5。README 与实现文档补充共享诅咒约定。
- 新增 `tests/StageReasonIntegration/`，维护针对实际卡池、资源、存档与出牌结算的游戏内探针。

### 验证结果

使用正式版 v0.107.1、测试版 v0.113.0 的真实游戏程序集，BaseLib 3.4.8、MegaDot 4.5.1 m.12。

| 验证 | 结果 |
| --- | --- |
| Core 双版本 Runtime 与引导构建 | 通过，零警告、零错误 |
| Karen 双版本实现与引导构建 | 通过，零错误；两版各有 24 项警告，均位于本次未修改的源码 |
| Core 与 Karen 资源 PCK 重建 | 通过，挂载后旧 Karen 图片和三语键均不存在 |
| 完整 Core 测试包校验 | `tests/FoundationChecks` 27 项通过 |
| 原图字节、三语标题及空描述比对 | 全部一致 |
| Core 独立加载，铁甲战士对局 | 正式版、测试版各 24 项基础检查通过 |
| Core＋迁移后 Karen，华恋对局 | 正式版、测试版各 30 项检查通过 |
| 双版本真实绘制与移牌收尾 | 已查看前后截图；`karen_check_hand` 均为 6 个模型、6 个可见节点，无缺失或多余节点 |
| 最终游戏日志 | 四组定向用例及补充截图用例无错误或未处理异常 |

定向探针覆盖：旧完整模型 ID、Core 来源、全局诅咒池唯一注册及原生抽取候选、角色／无色／Token／闪耀奖励池排除、原图与三语原文、缺失字段初始化、旧字段、新字段及旧新字段并存恢复、原生三次出牌逐次扣减、普通消耗保留永久预算、永久耗尽档案及满预算重新获得。

初版探针错误地把可变牌传入只接受规范模型的 `CombatState.CreateCard`；修正为规范模型并绑定永久来源后通过。初次最小化截图包含转场，补充验证改为后台无焦点真实绘制，截图已重新核对。本次所启动的游戏进程均在核对路径后退出。

本机只有 Windows PowerShell 5.1，测试打包按构建脚本的相同步骤调用 dotnet 和 MegaDot，并通过 .NET `FoundationChecks` 验证生成的 Core 包。原图、本地化与最终测试包的完整 SHA-256 清单记于任务忽略目录的 `migration-audit.json`。

### 测试产物与哈希

Core 任务工作树内的 `out/tmp/move-stage-reason-20261115/` 保存测试包、构建日志、运行脚本和 JSON 结果。主要证据为：

- `standalone-stable-final/result.json`、`standalone-beta-final/result.json`。
- `karen-stable-final/result.json`、`karen-beta-final/result.json`。
- `karen-stable-visual2/`、`karen-beta-visual/` 内的前后截图、最终结果、手牌一致性记录及日志。
- `migration-audit.json`。

| 最终 Core 测试包文件 | SHA-256 |
| --- | --- |
| `core.bundle` | `d8e9fc9431240d788969ed2af28d8c58146d9e0edc5585640e1585c634f4bc17` |
| `lib/1.9.5/stable/ShoujoKagekiAijoCore.Runtime.Stable.dll` | `8f7dd36cb8c246355045b386e50f8fb2a04fc7ebf088cef78183535e16249b87` |
| `lib/1.9.5/beta/ShoujoKagekiAijoCore.Runtime.Beta.dll` | `02062babe410c7fe080ba50555a30ed6a15ac1bb0636b6896d72e8dc8b1550d3` |
| `resources/1.9.5/Core.Resources.pck` | `76fe7e099449cc9b093d92d7400dcb0b0ece1a0eadd8200016fe73607e5ec031` |

Karen 最终测试资源包 SHA-256 为 `e1a4b711712f5d5d72951d4eff2aecd37263a380cfa8c110f726e0f9c0e2ac70`。

### 集成与清理记录

2026-11-16，用户明确授权“合并并清理，保留测试证据”。两个原检出仍在起始 `main`，工作区干净；本次测试证据和最终测试包已复制至 Core 的忽略目录 `artifacts/task-deliverables/move-stage-reason-20261115/`，逐文件 SHA-256 比对通过。临时游戏目录及其用户目录列为清理项。Core 的 `main` 在合并前推进至 `7ec3f2f`，新增存读档测试与记录；同步这些提交保留其内容，运行源码和资源与本次已测实现没有重叠。Karen 的 `main` 仍为 `46de4cc`。

合并按已审阅的 Core `9fd5e0f`、Karen `2659a4c` 实现提交执行，并补充本条授权记录；目标为各自 `main`。最终分支、提交、工作树清理与证据核验结果记入上述归档目录的 `integration-result.json`。任务文档和原实现提交随仓库保留。
