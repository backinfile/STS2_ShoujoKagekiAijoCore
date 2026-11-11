# 闪耀预览与耗尽牌堆修复 / Core 1.4.1

## 用户原始要求

> 你分析分析，然后在core中创建一个对话解决这个问题，最后更新一个core的小版本

> 再看看新的批注

批注原文：

> 卡牌奖励的卡牌为啥是闪耀1？

> 耗尽牌堆的牌怎么是闪耀0

工作约束沿用仓库 `AGENTS.md`。本任务接手了来源对话对两条批注的定位线索，负责真实复现、最小修复、必要定向验证、录像和 Core 1.4.0 → 1.4.1 本地发行准备。奖励预览与领取应使用新生命周期，同时保留同名旧牌各自预算；耗尽档案保留耗尽状态、普通复制空壳、显式重新获得满次数。使用隔离游戏与随机 MCP 端口，静默播放、真实绘制并录制视频。修复验证后通过 MCP 更新批注状态。

## 简要计划

1. 读取批注图片和当前入口，在真实游戏探针中建立红色反馈。
2. 按归属最小修复 Core 展示与 Junna 预览，保护快照和耗尽规则。
3. 运行定向回归与合法游戏操作录像。
4. 更新 Core 1.4.1，双版本构建、严格包校验和本地发行准备。

## 起始状态

- 原检出：`main`，`2aa682488460ffeb3ecd5e15983696d6635ba36e`，干净。
- 任务分支：`codex/shine-preview-1.4.1`。
- 工作树：`../worktrees/STS2_ShoujoKagekiAijoCore/shine-preview-1.4.1`。

## 实际结果

三个问题均已确定根因并完成最小修复，Core 从 1.4.0 更新到 1.4.1。原始仓库约定完整保存在 `user-instructions.md`。

### 根因与修改

1. 奖励中的“檄文”关联预览通过 `DynamicKnowledge.Members` 取到了牌组内剩余 1 次的旧“引言”。真实领取本已生成 9 次新牌。Junna 的 `src/Combat/Knowledge/DynamicKnowledge.cs` 改为：脱离牌堆的奖励使用新关联牌，实际牌堆查看保留已有实例，重复同名牌逐张处理。
2. 档案恢复后由 `MarkArchived` 明确归零，通用卡面直接显示余量 0。Core 的 `src/Modules/Shine/ShineService.cs` 给档案展示实例增加弱引用标记，只读快照继承标记，新生命周期清除标记；`src/Modules/Presentation/ShinePresentation.cs` 在档案卡面显示上限。实际预算、存档和普通复制空壳规则保持正确。公开说明同步到 `contracts/runtime-v1.md`。
3. 箭矢释放动画等待结束后直接进入 Core 计次，未复查最后一个敌人是否已死。Junna 的 `src/Combat/Arrows/ArrowDeployment.cs` 在每次实际牌效入口检查战斗结束、拥有者存活及有效目标，并按已有规则改换目标。有效击杀和有效重放继续逐次消费。

版本同步：`Directory.Build.props`、根 Mod 清单及 `build.ps1` 默认版本均为 1.4.1。API 签名及 AssemblyVersion 保持兼容。

### 可运行反馈与验证

定向命令：`./tests/RoleIntegration/verify-shine-issues.ps1 -BaseUrl <隔离MCP>/api/v1 -Mode <模式> -Output <结果文件>`。探针经真实游戏 MCP 执行，`race` 在原生释放动画的等待期间结束战斗，随后进入原来的真实牌效入口。

- 修复前 `preview` 明确返回 `Remaining=1`，`race` 返回永久牌 `Remaining=0`、`effects=1`、档案增加且永久牌移除。档案卡面为红色 0；原始复制/重获规则同时检查通过。
- 最终发行包在 beta v0.111.0 上通过 27 项问题定向断言：奖励和领取、两个同名旧实例、重复关联牌、读档、档案/快照卡面、空壳复制、重新获得、动画期间最后敌人死亡、原目标死亡后改换目标、三次有效重放和自身击杀。
- 原有 14 项闪耀普通/升级/超上限显示检查通过。存档和复制的既有 `--save-only`、`--copies-only` 定向检查通过，包含字段缺失、旧字段及新旧字段并存。
- 原生档案详情页可见模型保持 `Remaining=0, Maximum=9`，卡面显示 9；已查看截图。实际知识组预览中的副本也显示正确。
- 两段录像分别记录奖励浏览/领取/档案详情，以及正常部署星坠后用打击击杀最后敌人、查看永久星坠仍为 1。录制中仅执行正常出牌和只读界面操作。录像均已封装，内部有声，0 丢帧。
- 使用隔离用户目录、随机端口、满解锁、`--force-steam=off`、最小化、禁止焦点、`silent_test=true`、`muted=false`。核对路径后已停止本任务游戏。
- Core 与配套 Junna 均使用正式版 v0.107.1、测试版 v0.111.0 的真实游戏引用编译，0 警告、0 错误。Core 完整双版本包严格校验通过。
- 测试安装的 9 个清单文件逐项匹配最终发行包 SHA256。已审查全部已跟踪文档、ZIP 内文本及提交身份，未检出凭据或本机绝对路径。

### 发行与证据

工作树发行目录为 `out/releases/1.4.1/`，包含 Core ZIP、配套 Junna ZIP、官方 SDK 锁文件、哈希及 Core 工坊工作区。

交付已复制并逐项核对到原检出的 `artifacts/task-deliverables/shine-preview-1.4.1/`，其中 `release/` 为发行内容，`evidence/` 为截图、录像、红绿结果及审查记录。

- Core bundle SHA256：`98abcdc23056abbc0a1320ee46bd7ee503628c9f548fcbbba268ccd3d71e6e5b`。
- Core ZIP SHA256：`b08df50645976b81900043abf33bf16d8785ba81257ed588af54df64d2505062`。
- Junna ZIP SHA256：`08fdf93fe78bb96f969e01e542aa6522fee090bfc8c1833bfd78ce5d97caed7b`。
- 录像：`evidence/previews.mp4`、`evidence/star-last-use.mp4`。
- 两条批注已通过原游戏 MCP 以最新 revision 标为 resolved，用户原文保持不变，更新后 revision 分别为 19 和 13。

### 集成与发布状态

用户已确认「合并并清理」。Core 来源提交 `34f9276` 与 Junna 来源提交 `f1d5fa2` 已分别快进合并到各自 `main`。Junna 的 15 个并行未提交文件已保留，唯一重叠文件经无冲突三方预检后集成并恢复原修改，其余文件逐项核对原始 SHA256；原暂存状态与 stash 栈保持不变。本次清理仅处理已获批准的任务工作树、分支和隔离游戏目录，发行包与证据保留。工坊上传还需明确授权及现有 Core 条目 ID。

## 用户追加要求

> 还有一个问题，闪耀1的星坠，在结算前敌人就死了，为啥还是耗尽了，你也解决这个问题

追加计划：覆盖释放动画期间最后敌人先死、目标先死但仍有其他敌人、星坠有效击杀和重放。

## 后续检查要求

> 我已只读核对当前最小改动。请在耗尽档案的正常查看入口中顺手确认右键放大/详情页的卡面也采用同一展示值。目前 ArchivedModels 是 ConditionalWeakTable 的实例标记，而 CardCopies.Snapshot/MutableClone 以及 Junna KnowledgeGroupPreview 会创建预览副本，需确认这些实际调用是否会丢失标记重新显示闪耀0。若该路径确实克隆档案卡，请在适当快照入口保留仅用于展示的标记，并保证 Reacquire 的新生命周期正确。只需对实际可达入口作定向验证，无需扩大测试矩阵。

已验证原生详情和知识组副本，快照标记与新生命周期重置已纳入实现及回归。

## 合并与清理确认

确认问题：

> Core 34f9276、Junna f1d5fa2 已提交并验证。是否合并到各自 main，并清理本任务的两个工作树、分支和隔离游戏目录？发行包及录像会保留。依据 worktree-task SKILL.md 的明确要求，原始任务请求不等于合并与清理授权；Junna 的并行未提交修改会保留，若阻碍安全集成则保留对应工作树。

用户答复：

> 合并并清理
