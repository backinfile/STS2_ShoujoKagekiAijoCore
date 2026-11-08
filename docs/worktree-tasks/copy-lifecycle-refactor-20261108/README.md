# 三项目复制与生命周期重构

## Original user input

### 1. Workspace guidance

# AGENTS.md instructions for <core-repository>

<INSTRUCTIONS>
# Global workspace conventions

- Create Git worktrees under `../worktrees/<projectName>/<task-slug>` relative to the repository root. Do not place worktrees directly alongside the repository in its parent directory.

<!-- set-global-rules:start -->
## 用户协作约定

### 表达偏好

请在所有回答及你撰写的内容中遵循以下表达偏好：

1. 直接说明结论、事实、理由和必要步骤，语气自然、明确。详细程度按任务需要展开，保留有价值的解释、推导、依据、例子和细节。精简的对象是赘述。

2. 对准备添加的反面解释、范围声明、保留说明和风险提醒，先判断：删去这句话，是否会实质改变我对答案、结果状态或下一步行动的理解？如果不会，且它主要用于预防假想误解、提前免责或凑齐注意事项，就直接删除。不要换一种委婉说法保留同样的冗余信息。

3. 优先正面陈述“是什么、能做什么、已经做了什么、为何如此”。避免无端追加“这只是……不代表……”“这并不意味着……”，避免主动罗列没做什么、未涉及什么和不保证什么。“不是……而是……”仅在对比或纠错确有帮助时使用，避免成为惯用句式。

4. 让结论的强度与证据一致。将必要的对象、条件、数据范围和实际状态直接写进结论。证据充分时明确判断；确有不确定性时，指出具体不确定之处及其影响。避免反复堆叠“可能、一定程度上、相对而言”等弱化词。

5. 直接回答问题、解释机制、纠正错误所需的否定句正常使用。对于我明确要求说明的事项，以及省略后会造成实质误解的重要限制、失败或未完成事项，应准确交代。根据重要性放在相关结论旁或简短的状态说明中，优先用一句话说清具体事实及影响，下一步按需补充。避免孤立地写“未验证”“不保证”“仅供参考”，也不要机械增加“风险提示”“局限性”等段落。

6. 撰写文档、介绍、邮件等成稿时，按目标读者的需求组织正文。必要的制作过程说明放在交付说明中；成稿聚焦读者需要的内容，避免混入对自身工作范围的解释、假想争议和例行免责声明。

7. 输出前自行检查并清理上述赘述，直接交付整理后的答案。无需复述这些偏好，也无需宣布你已遵守。

### 修改、设计与测试

- 进行修改任务时使用最小改动。
- 进行设计类任务时，文本内容面向使用者生成。明确区分“实现要求”和“面向用户的文本”，实现要求只用于指导实现，不写入面向用户的文本；用户侧内容保持简洁、易理解。少用标语和 slogan。界面文案只保留名称、数量、状态和必要的按钮文字。操作提示、辅助说明、副标题、帮助文案及设计解释，仅在我明确要求时添加。功能通过布局、图标和交互体现。交付前删除所有自行添加的多余说明。
- 进行任务时，只自动执行验证当前改动所必需的最小测试；完整、全面的测试等全部工作完成后，再向用户说明测试范围并请求同意，取得同意后执行。
<!-- set-global-rules:end -->


--- project-doc ---

# Global workspace conventions

- Create Git worktrees under `../worktrees/<projectName>/<task-slug>` relative to the repository root. Do not place worktrees directly alongside the repository in its parent directory.

<!-- set-global-rules:start -->
## 用户协作约定

### 表达偏好

请在所有回答及你撰写的内容中遵循以下表达偏好：

1. 直接说明结论、事实、理由和必要步骤，语气自然、明确。详细程度按任务需要展开，保留有价值的解释、推导、依据、例子和细节。精简的对象是赘述。

2. 对准备添加的反面解释、范围声明、保留说明和风险提醒，先判断：删去这句话，是否会实质改变我对答案、结果状态或下一步行动的理解？如果不会，且它主要用于预防假想误解、提前免责或凑齐注意事项，就直接删除。不要换一种委婉说法保留同样的冗余信息。

3. 优先正面陈述“是什么、能做什么、已经做了什么、为何如此”。避免无端追加“这只是……不代表……”“这并不意味着……”，避免主动罗列没做什么、未涉及什么和不保证什么。“不是……而是……”仅在对比或纠错确有帮助时使用，避免成为惯用句式。

4. 让结论的强度与证据一致。将必要的对象、条件、数据范围和实际状态直接写进结论。证据充分时明确判断；确有不确定性时，指出具体不确定之处及其影响。避免反复堆叠“可能、一定程度上、相对而言”等弱化词。

5. 直接回答问题、解释机制、纠正错误所需的否定句正常使用。对于我明确要求说明的事项，以及省略后会造成实质误解的重要限制、失败或未完成事项，应准确交代。根据重要性放在相关结论旁或简短的状态说明中，优先用一句话说清具体事实及影响，下一步按需补充。避免孤立地写“未验证”“不保证”“仅供参考”，也不要机械增加“风险提示”“局限性”等段落。

6. 撰写文档、介绍、邮件等成稿时，按目标读者的需求组织正文。必要的制作过程说明放在交付说明中；成稿聚焦读者需要的内容，避免混入对自身工作范围的解释、假想争议和例行免责声明。

7. 输出前自行检查并清理上述赘述，直接交付整理后的答案。无需复述这些偏好，也无需宣布你已遵守。

### 修改、设计与测试

- 进行修改任务时使用最小改动。
- 进行设计类任务时，文本内容面向使用者生成。明确区分“实现要求”和“面向用户的文本”，实现要求只用于指导实现，不写入面向用户的文本；用户侧内容保持简洁、易理解。少用标语和 slogan。界面文案只保留名称、数量、状态和必要的按钮文字。操作提示、辅助说明、副标题、帮助文案及设计解释，仅在我明确要求时添加。功能通过布局、图标和交互体现。交付前删除所有自行添加的多余说明。
- 进行任务时，只自动执行验证当前改动所必需的最小测试；完整、全面的测试等全部工作完成后，再向用户说明测试范围并请求同意，取得同意后执行。
<!-- set-global-rules:end -->

# Core 项目约定

## 项目概况

- `ShoujoKagekiAijoCore` 是少女歌剧系列 Mod 共用的基础 Mod，逻辑 Mod ID 固定为 `ShoujoKagekiAijoCore`。
- 仓库地址为 https://github.com/backinfile/STS2_ShoujoKagekiAijoCore 。文档中的项目入口使用 GitHub 地址，工作路径使用仓库相对路径。
- 技术栈为 C# / .NET 9、Godot 4.5.1（MegaDot）、Harmony、BaseLib 3.4.7。当前适配正式版 v0.107.1 和测试版 v0.111.0。
- Core 独立维护并作为创意工坊 Mod 发布。仅 Karen 内嵌完整 Core；Junna 及其他 Mod 通过创意工坊依赖独立 Core 条目。
- 读取与修改源码、本地化和文档统一使用 UTF-8。机制扩展优先复用游戏原生 Model、Hook、Cmd 和 BaseLib。

## 阅读与代码入口

1. `README.md` 面向玩家与 Mod 作者，说明能力、配置、安装和接入方式。
2. `docs/IMPLEMENTATION.md` 记录引导、择版、存档、箭矢结算、服务端和发布架构。
3. `contracts/runtime-v1.md` 与 `src/Api.V1/` 是公开接口及模块协作入口，修改时保持一致。

| 位置 | 内容 |
| --- | --- |
| `src/Bootstrap/` | 引导程序集、候选发现、择版与完整性检查 |
| `src/Api.V1/` | 提供给其他 Mod 的稳定接口 |
| `src/Runtime/` | 业务入口、模块生命周期、来源发现与公共服务 |
| `src/Modules/` | 闪耀、公共配置、表现、音乐、遥测和导出 |
| `src/Patches/` | 五轮书等公共兼容补丁 |
| `resources/ShoujoKagekiAijoCore/` | 公共资源与各语言本地化 |
| `sdk/` | 包校验、角色接入与 Karen 内嵌打包工具 |
| `services/telemetry/` | Python / SQLite 对局与报错服务 |
| `web/telemetry/`、`deploy/telemetry/` | Web 页面及部署文件 |
| `tools/telemetry/` | 数据整理、资料与导出校验工具 |
| `tests/` | 离线回归和独立游戏测试模组 |

`RuntimeEntry` 建立公共服务后按 `ICoreModule.Order`、`Id` 初始化模块。依赖原生模型的操作订阅 `CoreContext.ModelsReady`，退出清理订阅 `ShuttingDown` 或实现 `IDisposable`。Runtime 工程自动编入 Modules、Patches 与 Compatibility 下的源码。

## 公开接口与双版本

- 公开程序集为 `ShoujoKagekiAijoCore.Api.V1`，命名空间为 `ShoujoKageki.Core.V1`，当前 `AssemblyVersion` 固定为 `1.0.0.0`。
- 发布优先保持源代码与二进制兼容。V1 中已发布的类型、签名、默认行为和序列化含义保持稳定，通过兼容的增量能力扩展。确需破坏兼容时建立新的 API 主版本，并保留原主版本所需的业务适配。
- 角色引用 API，具体规则与服务通过 Core 提供的公共入口调用。角色接入所需的能力声明由官方 SDK 生成。
- 引导 DLL 保持小而稳定，业务更新集中在按发行版本组织的 Runtime、资源与 SDK 中。择版校验所有已安装角色的要求，最终只初始化一份 Runtime 和一份公共配置。
- 程序集、资源、`core.bundle`、`core.requirements` 与包校验数据随同一发行维护。对局保存规则版本及必要配置快照，读档沿用该局规则。
- 正式版和测试版分别编译各自的 Runtime，并在各自真实游戏引用上验证改动。共享实现与版本适配分层维护。
- 加载顺序固定为 BaseLib → Core → 角色 Mod。清单依赖、SDK 检查与面向玩家的加载提示同步维护。

## 共享机制与迁移

- Core 提供闪耀相关能力、长颈鹿启动画面、衍生牌标记、音乐播放器、对局及报错收集、Web 服务和公共 patches。
- 闪耀牌由属性与原生模型自动发现。角色提供具体牌效和必要回调，公共状态、计次、耗尽、档案、空壳及表现由 Core 处理。
- 闪耀在实际牌效结算时扣除。箭矢的部署、等待和额外 Effect 保持其预算，每次实际箭矢结算与重放分别计次，整批合法结算结束后统一耗尽。箭矢调度与牌型实现由 Junna 维护，接入约定写入实现文档。
- 临时复制使用独立预算。已耗尽档案的普通复制与转交生成公共空壳，显式重新获得建立满次数的新生命周期，并保留升级、附魔和成长数据。
- 旧 Karen 存档的普通牌负哨兵恢复为无闪耀状态，旧规则与耗尽档案继续兼容。存档迁移需要覆盖字段缺失、旧字段及新字段并存的输入。
- 从角色迁入代码与资源时沿用已有行为、名称和本地化正文。移动本地化键时同步引用，中、英、日文逐项核对原值。文案改写、卡名、牌效和数值调整需要用户明确要求。
- 五轮书、招财异鱼等公共遗物补丁统一放入 `src/Patches/`，作用范围按遗物拥有者的角色识别。
- Junna 工作台内容通过该项目的 `junna_workbench` MCP 读取与修改，沿用该项目 `docs/design/AGENTS.md`。共享机制迁移按已实现的牌效接入。

## 配置与数据服务

- 公共配置保持简洁，使用 `CoreSettings` 快照与 `Changed` 通知，由 Core 自己持久化和升级。
- 对局上传、报错上传默认开启，默认接收地址沿用 Karen 发行配置，通过构建参数注入。配置文件、队列与服务凭据分别存放。
- 五轮书等战斗内获得卡牌遗物补丁提供启用、仅对少歌角色启用、关闭三档，默认仅对少歌角色启用。规则配置在开局时保存到对局。
- 衍生牌标记有独立开关。音乐播放器及其配置统一维护，使用现有音量、播放控制和快捷键入口。
- 对局与报错上传、服务端处理和对应 Web 页面在本仓库维护。修改协议时保留旧记录读取、重试幂等及队列恢复行为。
- 使用隔离数据库和回环接收地址验证上传，读取凭据由本地环境提供。服务运行与部署说明见 `services/telemetry/README.md`。

## 构建与发布

```powershell
./build.ps1 -StableGameDir <stable-game> -BetaGameDir <beta-game> -Godot <megadot-console> -TelemetryEndpointPath <local-endpoint-config>
```

也可使用 `SHOUJO_STABLE_GAME`、`SHOUJO_BETA_GAME`、`GODOT_BIN` 提供环境路径。完整产物默认位于 `dist/ShoujoKagekiAijoCore/`，包括引导、API、双版本 Runtime、PCK、校验清单和 SDK。

- 根目录 `ShoujoKagekiAijoCore.json` 为正式 Mod 清单，`workshop.json` 为独立 Core 工坊配置。发布流程见 `docs/RELEASING.md`。
- 使用 `sdk/validate-package.ps1` 校验完整发行目录，发布时加 `-Strict` 排除未列入校验清单的文件。`prepare-release.ps1` 生成 ZIP、哈希与独立工坊工作区，`publish-workshop.ps1` 调用官方 ModUploader 上传。
- 角色通过 `sdk/pack-character.ps1` 生成需求声明和构建锁文件；仅 `ShoujoKagekiAijoKaren` 将完整 Core 并列复制到上传目录，其他 Mod 在工坊配置中声明独立 Core 条目的依赖。
- 检查清单中的 BaseLib → Core → 角色依赖顺序。Karen 内嵌包与独立工坊包使用同一官方发行产物，核对 Core 文件哈希及并存时的择版结果。
- Godot/MegaDot 打包使用具备正常编辑器目录和 .NET 运行库访问权限的 Windows 环境。遇到编辑器配置目录或程序集访问错误时，先核对执行权限。
- 发布核对最终源码、资源、版本、依赖、包内文件及哈希，排除测试插件、凭据、日志和用户存档。工坊上传与服务部署按用户授权执行。

## 定向验证与游戏 MCP

- 日常任务自动执行与当前改动直接相关的最小必要验证。完整双版本单人/多人矩阵、性能及特效专项先说明范围，取得用户同意后执行。
- 引导与择版检查在 `tests/FoundationChecks/`，原生异步结算回归在 `tests/ResolutionChecks/`，真实角色集成探针在 `tests/RoleIntegration/`。根据改动选择对应入口。
- 运行行为通过真实游戏入口复验。涉及存档时核对普通牌、闪耀预算、耗尽档案与空壳，涉及箭矢时核对部署、实际结算、重放及批次收尾。
- 使用 `KarenSTS2MCP` 操作游戏，先读取其当前 README，再核对游戏日志与 HTTP 接口。测试时使用游戏 MCP。
- 默认最小化启动、禁止抢焦点、随机选择可用 MCP 端口。使用 `silent_test=true` 与 `muted=false` 保持本机静默并保留内部录音，截图与录像保持真实绘制。沿用当前 MCP README 的启动与录制流程。
- 两版进程使用独立用户目录、日志和端口，启动参数使用 `--force-steam=off`。核对进程路径后再停止本任务启动的游戏。
- 不涉及教程或解锁时以满解锁状态测试。录制前准备环境，录制中使用合法游戏操作继续，证据保存在工作树的 `out/tmp/<task>/`。
- 涉及移牌时保存并查看前后截图，核对模型与可见节点一致。图片导出检查可见像素与内容，完成和异常路径均核对临时节点及视口释放。
- 特效受公共等级配置控制。GPU 预热需要真实绘制与渲染完成，预热过程保持规则、随机数、牌堆及收益状态，临时资源在完成或异常时释放。
- 使用生成图片时，交付前按角色项目约定运行本地 Artifact Cleaner，保留原图并记录参数与成品路径。
- 测试记录写明所用版本、最终包哈希、实际通过项、失败项与待验事项。

## Git 与隐私

- 修改任务使用 `$worktree-task`，工作树位于 `../worktrees/STS2_ShoujoKagekiAijoCore/<task-slug>`，保留原检出与原有未提交状态。
- 任务记录放入 `docs/worktree-tasks/<task-slug>/README.md`，记录用户原始要求、简要计划、实际变更和验证结果。合并与清理分别遵循 skill 的确认流程。
- 文档和证据清单使用仓库相对路径，仓库外资源使用用途占位符。凭据、日志、存档、数据库、抓包及本地连接配置放入忽略目录。
- 提交前检查完整暂存索引、压缩包内文本及提交身份中的私人信息与凭据。图片、视频和音频内容按交付需要另行复核。

</INSTRUCTIONS>

<environment_context>
<cwd><core-repository></cwd>
<shell>powershell</shell>
<current_date>2026-11-08</current_date>
<timezone>Asia/Shanghai</timezone>
</environment_context>

### 2. Review request

整体review下 core karen junna三个项目，看看有没有可以简化的代码，有没有写的糟糕的特殊处理，有没有可以进行通用处理的地方

### 3. Refactoring direction

你按重构方式来写，不需要最简化处理。你先写个简要计划

### 4. Implementation authorization

$worktree-task  直接全部完整修改吧

### 5. Invoked skill

<skill>
<name>worktree-task</name>
<path>$worktree-task</path>
---
name: worktree-task
description: Use when completing a repository change in an isolated Git worktree, consolidating its task changes into one commit, and merging it back to the starting branch.
---

# Worktree Task

Complete the requested change in a temporary worktree, then integrate it safely into the branch that was current when the task began.

## Rules

- Capture the original checkout, branch, and revision once; never re-infer the integration target.
- Refuse an active Git operation. If the original checkout is dirty, warn that its uncommitted changes will not enter the worktree, preserve them untouched, and proceed from the current branch's latest commit by default.
- Place the worktree at `../worktrees/<projectName>/<task-slug>` relative to the repository root, unless repository instructions override it.
- Perform all modification and verification work inside the worktree.
- Keep a task document under `docs/worktree-tasks/<task-slug>/` inside the worktree and include it in the task commit, so it survives integration and worktree cleanup.
- Sanitize every document tracked by Git or being added to Git, including existing unchanged documents. Replace sensitive data such as private personal details and credentials with descriptive placeholders while preserving meaning.
- Use repository-relative paths in persisted documents and express worktree locations relative to the repository root. Never persist local absolute paths, including in quoted user input or Markdown links. Use descriptive placeholders for paths outside the repository and `$skill-name` for local skill references. Tool calls and safety checks may use absolute paths internally.
- Do not perform remote operations or rewrite existing original-branch history unless explicitly requested.
- For merge and cleanup confirmation, use `request_user_input_async` when available and permitted; otherwise ask in chat. One explicit confirmation may approve both merging and the listed cleanup; the original task request alone is not approval.
- Preserve the task branch and worktree whenever a safety check or integration fails.

## Task Document

Use `docs/worktree-tasks/<task-slug>/README.md` by default, with the same task slug as the worktree. Create it before implementation. Keep the input record complete and the plan and results concise:

- **Original user input:** Record the full text of every task-related user message in chronological order, including introductory guidance, skill invocations, constraints, and follow-up corrections. Preserve the original wording and formatting, applying only the sanitization above. Append later messages in full.
- **Brief task plan:** Record the intended approach and main steps; update it when the approach changes.
- **Result and changes:** After implementation, summarize the actual completion status, changed files and behavior, verification results, and any remaining work or limitations. If work stops early, record partial progress accurately.

Before each commit, check the input record against the user messages, finalize the result section, and review all Git-tracked documents and documents being added for sensitive data and local absolute paths, including quoted input and link targets. If synchronization changes the implementation or verification results, update the document in the synchronization commit. Preserve the document with the task changes and include its path in the final report.

## Workflow

1. Check the repository state and refuse conflicting worktree paths or branches. Record any original uncommitted state so it can be protected and verified later.
2. Create a uniquely named task branch and worktree from the captured original branch's latest commit. Create the task document with the sanitized user input and brief plan.
3. Establish a clean baseline, implement the task, and verify it. Complete the task document's result section, then consolidate all task changes, including the document, into one commit before synchronization unless the user or repository policy requires multiple commits.
4. Merge the latest local original branch into the task branch. Resolve conflicts only in the worktree, then verify and commit the synchronized result.
5. Confirm the original checkout is still on the captured branch, its committed HEAD is unchanged since synchronization, and any pre-existing uncommitted state remains intact. If HEAD advanced, repeat synchronization; stop after three races.
6. Show the target branch, task commit, change summary, verification results, any original uncommitted state, and proposed cleanup items. Request one confirmation with choices to merge and clean up, merge and keep, or defer.
7. Only after confirmation, merge the synchronized task branch back without creating an additional integration commit when possible. If integration would overwrite uncommitted state, preserve the worktree and report that integration is pending instead.
8. Verify the committed trees match, checks pass, the original branch contains the consolidated task commit, and pre-existing uncommitted state was preserved.
9. After successful integration verification, remove this run's clean worktree, task branch, and disposable temporary files covered by the cleanup approval, after proving needed content is preserved. Reuse combined approval from step 6 without asking again. If only merging was approved, retain these items until the user approves cleanup; request further confirmation for any additional cleanup items.

Report the resulting branch, commit, verification, cleanup state, and task document path.

</skill>

## Brief task plan

- 在 ../worktrees/STS2_ShoujoKagekiAijoCore/copy-lifecycle-refactor-20261108 工作，原分支 main，起点 d6cf8cec0bd88bb0cf876597794112b28630736f。
- 重构 Core 卡牌快照、战斗复制、永久复制、转交和重新获得入口，保持 V1 兼容。
- 重构 Junna 能力效果数据、知识关系迁移和卡池生成元数据。
- 收敛 Karen 约定牌堆模式、事件和刷新，统一原生选择扩展，删除重复兼容补丁。
- 仅执行必要双版本编译及定向回归；完整矩阵、合并与清理按约定另行确认。
- Junna 从 main 最新提交开始，原检出 64 个未提交文件按状态与哈希保留。

## Result and changes

- 新增 ICardSnapshots／CardCopies.Snapshot 与 ICardSelection／CardSelection，保持 V1 已发布接口和 AssemblyVersion=1.0.0.0；发行及角色依赖提升到 1.2.0。
- 卡牌快照、战斗复制、永久获得、转交与重新获得分流，原生所有权适配集中维护。共享选牌沿用原生同步与第三方钩子，标题及异常收尾绑定单次调用。
- 离线复制生命周期和存档恢复／真实升级检查通过；403 个已发布类型／成员签名保留。发行包严格校验通过，Karen 内嵌 Core 与独立 Core 逐文件相同。
- 更新 README、契约、SDK 接入与实现说明。

- 正式版 v0.107.1 与测试版 v0.111.0：真实引用编译、原生混合角色双人联机定向回归全部通过。每版 52 条行为断言（另有 6 条诊断证据），两端完整玩家／牌区／耗尽档案／随机状态一致。
- 实测覆盖原模型快照、耗尽空壳、永久来源、跨玩家所有权、CloneOf、临时费用、转换与投入、独立预算、永久转交入牌组前的来源标识、最后一次北极星安装与后续结算、实际传递、选牌／跳过／必选、知识组获得与旧关系读档／同步、约定能力隔离、原生移牌、Void 与无限补充。
- 四端手牌模型与可见节点数量一致，无缺失／多余节点，耗尽覆盖标记一致；查看移牌前后截图，保留合法出牌录像。全程窗口最小化，无焦点抢占。
- 原检出的 main 提交保持起点。Junna 原检出有 64 个未提交文件；其中两个工作台文件在任务期间另行更新，保持其当前内容。
- 当前状态：实现和定向验证完成；任务分支等待用户确认合并和清理。Junna 的原检出有重叠未提交内容，需要在整合时保留。

最终验证产物 SHA-256：

| 文件 | SHA-256 |
| --- | --- |
| coreBundle | 57508a4c468a43ec55451f3acf64e2230f788c7c67ffaa68b3761ea7902262bd |
| coreApi | 43c70dda9b1a5d911e19c90ec979d1d50dff1862ee6c9b8c87477720934db3eb |
| coreStable | 03300fc5df6764296c216f3b86270af65981b7e3dbc1cb35ddaf0e4859cbea33 |
| coreBeta | 2e27323063e68c0dc0b16e2f7b280519a8465e85f57c8c150c3a8f8407299284 |
| corePck | 2063366f9ec1cfaf1a6739df8100f8edf181930e36022527e20224282f6a6e63 |

本仓库构建产物：`out/tmp/copy-lifecycle-refactor-20261108/packages-r3/`。实机证据：`out/tmp/copy-lifecycle-refactor-20261108/lifecycle-live/cases/`，包括双版本 host/client 的 rules、手牌检查、截图及合法出牌录像。

提交前审查：完整暂存索引及归档文本隐私检查通过，所有受 Git 跟踪文档通过本机绝对路径检查，使用匿名任务提交身份。
