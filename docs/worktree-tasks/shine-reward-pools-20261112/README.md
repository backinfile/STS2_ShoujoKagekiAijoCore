# 闪耀牌奖励规则与公共再生产

## 用户原始要求

1. `$worktree-task` 闪耀牌奖励是实现在哪了？junna获取闪耀牌奖励的池子是啥
2. 你来整理闪耀牌奖励的规则，并允许掉落时，选择当前玩家的卡池（仅少歌角色，没有闪耀牌的角色按karen卡池掉落）  闪耀的再生产 也作为通用规则进行掉落（移动到Core中实现）

用户提供的工作区约定与 skill 原文分别保存在 user-instructions.md 和 worktree-task.md。外部路径已使用用途占位符，skill 引用为 `$worktree-task`。

## 简要计划

1. 在两个隔离工作树中整理 Core 公共奖励接口、选池与权重规则。
2. 迁移再生产牌、必要资源和三语原文，接入 Karen 并兼容旧牌存档。
3. 双版本编译并通过游戏 MCP 定向验证选池、奖励和再生产，记录证据。
4. 提交并同步各自 main，按 skill 请求合并与清理确认。

## 基线

- Core：main，8fcaf7b5ea1f36fc7f6fe81fa0d569aa66e606ae，原工作区干净。
- Karen：main，f0ef747d3a82c0a6d8c5d00393bea1fdf1903e76；原有 5 个已跟踪未提交文件保留，哈希记录于 Core 工作树 out/tmp/shine-reward-pools-20261112/original-karen-status.json。
- 工作树：../worktrees/<projectName>/shine-reward-pools-20261112。

## 实际结果

- Core 1.8.0 新增 `shine-rewards@1`：`ShineRewards.GetCandidates`、`Roll`、`Create`、`Add` 和公共再生产原型；牌可通过 `IShineRewardEligibility` 提供掉落条件。
- 少歌接收者先取自身合法闪耀池，无候选或非少歌接收者回退 Karen；按当前单人／多人条件、基础牌、except 和具体牌限制筛选。公共再生产独立参与，保留 42／42／15／1 权重和空类别回退。
- 再生产牌、奖励对象、分组领取、恢复、数量标记、特效、两张原始图片和三语文本迁入 Core；保留再生产与标记 ModelId，原卡效果和正文不变。
- Karen 保留现有调用入口作为公共 API 的转发，保留跨角色限制；更新 SDK 能力依赖与清单最低 Core 1.8.0，并迁移相关工具引用、资源登记和源文哈希。
- 规则文档位于 Core 的 `docs/shine-card-rewards.md`，公开接口契约及 SDK 说明同步更新。

## 验证结果

- Core、Karen 双版本完整构建成功，Core 无编译警告；真实游戏版本为正式版 v0.107.1、测试版 v0.111.0。
- 每版组合安装（Core + Karen + Junna）20 项、独立安装（Core + Junna）19 项规则检查，共 78 项通过；每种安装验证完整 100 个权重结果、角色池、空池回退、排除、公共再生产、旧 ID 与奖励保存恢复。
- 四种安装均通过合法出牌执行“永久升级牌耗尽 → 公共再生产选牌 → 永久重获”，保留升级、恢复满次数并耗尽再生产自身；截图和静默有声录像已查看。
- 两版公共奖励分组领取均通过：两份奖励合并展示，选择一张后正确加入牌组并完成未选项收尾。测试版通过实际战斗胜利验证；正式版通过原生自定义奖励入口验证。
- 30 项中英日文本与迁移前值逐项一致，两张原图 SHA-256 一致，共 32 项迁移检查通过。
- 四种安装的 Core、Karen／Junna 文件与最终测试包逐文件 SHA-256 一致；Karen 特效静态清单检查通过。
- 所用 Junna 正式版包在普通战后奖励生成时触发其现有 `DynamicKnowledge.RewardEntrances` 异常：原生 `CardCreationOptions` 尚无 pool 时调用 `GetPossibleCards`。该失败记录保留，公共奖励界面改用直接入口完成验证。
- Karen 本地化总检有 4 项基线问题，逐项证明其正文与哈希均未被本次改动改变：闪耀奖励关键词、Mr. White 遗物、报错上传设置标题与旧解锁按钮哈希。迁移项检查全部通过。
- 仅执行本次改动必要的定向验证。完整多人矩阵及特效性能专项未执行。

证据均位于 Core 工作树 `out/tmp/shine-reward-pools-20261112/`：`live/summary.json`、`live/cases/`、`reward-ui/`、`reward-ui-direct/`、`migration-checks.json`、`localization-baseline-findings.json`、`final-package-hashes.json`、`package-install-checks.json`。失败尝试保存在 `live/attempt-history/` 和 `reward-ui/`。

## 集成状态

实现与定向验证已完成。完整暂存索引文件检查通过，提交使用公开 noreply 身份；Core 严格包校验通过。Karen 原有 5 个未提交文件的字节哈希未变，其完整补丁已通过对任务结果的重新应用检查。用户已确认将任务结果合并回各自 main 并清理任务分支；证据先保存在 Core 原目录的忽略目录中。

## 后续用户消息

用户对合并确认的完整回复：

```json
[{"questionItemId":"[\"request_user_input_async\",\"call_155b7de259f643a6bd42f011feb8ae71\",0]","question":"是否将 Core b7312d8、Karen a9e886b 合并回各自 main？选择清理时，会先将测试包、报告、截图和录像保存到 Core 原目录 out/tmp/shine-reward-pools-20261112/，再删除本次两个工作树、任务分支及其中的临时文件；Karen 原有 5 项未提交改动会保留。","answer":"普通 42、罕见 42、稀有 15、闪耀的再生产 1   这个概率还有吗？"}]
```

该回复询问当前权重，当时尚未确认合并或清理。核对实现后确认分类权重仍为 42／42／15／1，空类别保持全部合法候选均匀回退。

用户后续确认（按时间顺序，保留原文）：

> 合并吧

> 合并并清理分支

用户已确认合并及本次任务分支清理。按此前列明的范围，先把测试包、报告、截图和录像保存在 Core 原目录 `out/tmp/shine-reward-pools-20261112/`，再清理两个任务工作树与任务分支，保留 Karen 原有 5 项未提交改动。最终集成及清理校验结果写入该目录的 `integration.json`、`preservation-manifest.json` 与 `cleanup.json`。
