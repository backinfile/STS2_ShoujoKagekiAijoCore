# 2026-11-16 巡检修复 OBS-045–OBS-053

## 原始输入

### 工作区约定

```markdown
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
- 技术栈为 C# / .NET 9、Godot 4.5.1（MegaDot）、Harmony、BaseLib 3.4.8。当前适配正式版 v0.107.1 和测试版 v0.113.0。
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
- 桌面引导默认尝试新游戏版本：按不高于当前游戏的最新 Runtime 基线择版，maximumGameVersion 仅作构建参考，不作为拒绝后续版本的条件。更新后用实际新版做必要定向验证。
- 加载顺序固定为 BaseLibCompatibilityHelper → BaseLib → Core → 角色 Mod。清单依赖、SDK 检查与面向玩家的加载提示同步维护。

## 共享机制与迁移

- Core 提供闪耀相关能力、长颈鹿启动画面、衍生牌标记、音乐播放器、对局及报错收集、Web 服务和公共 patches。
- 闪耀牌由属性与原生模型自动发现。角色提供具体牌效和必要回调，公共状态、计次、耗尽、档案、空壳及表现由 Core 处理。
- 闪耀在实际牌效结算时扣除。箭矢的部署、等待和额外 Effect 保持其预算，每次实际箭矢结算与重放分别计次，整批合法结算结束后统一耗尽。箭矢调度与牌型实现由 Junna 维护，接入约定写入实现文档。
- 临时复制使用独立预算。已耗尽档案的普通复制与转交生成公共空壳，显式重新获得建立满次数的新生命周期，并保留升级、附魔和成长数据。
- 旧 Karen 存档的普通牌负哨兵恢复为无闪耀状态，旧规则与耗尽档案继续兼容。存档迁移需要覆盖字段缺失、旧字段及新字段并存的输入。
- 从角色迁入代码与资源时沿用已有行为、名称和本地化正文。移动本地化键时同步引用，中、英、日文逐项核对原值。文案改写、卡名、牌效和数值调整需要用户明确要求。
- 五轮书、招财异鱼等公共遗物补丁统一放入 `src/Patches/`，作用范围按遗物拥有者的角色识别。
- 少女歌剧工作台源码与部署由本仓库 `tools/workbench/` 维护，内容通过 `shoujo_workbench` MCP 读取与修改，使用 `modId` 选择 Core、Karen、Junna 或只读的游戏本体；旧 `junna_workbench` 连接默认进入 Junna。编辑流程见 `tools/workbench/AGENTS.md`，部署见 `tools/workbench/deploy/README.md`。共享机制迁移按已实现的牌效接入。

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

```

### 任务委派

请使用 $worktree-task（$worktree-task），实际排查并修复 2026-11-16 巡检新增的 OBS-045 至 OBS-053，共 9 项：Core 6 项、Karen 3 项。原始用户要求：“创建一个新对话来处理这些问题”。按 P1 优先推进，完成具体代码修改、必要验证和可审阅结果，不能只停在分析或计划。

仓库与证据：
- Core 当前开发仓库：<core-development-repository>。
- Karen 当前开发仓库：<karen-development-repository>。这里才是当前实现；不要在旧检出修复游戏源码。
- 统一巡检文档及本地证据仓库：<error-watch-evidence-repository>。其 docs/待修复问题.md、artifacts/error-watch/config.json、triage-policy.json、state.json 为统一状态来源。该旧检出有既有未提交内容和删除文件，必须保持原状。
- 最新完整巡检证据在旧检出的 artifacts/error-watch/2026-11-16/：triage-review.json 包含全部 9 项的完整指纹、次数、会话、版本、源码位置、初步原因与下一步；details.json 为完整调用栈、启用 Mod 及 core_telemetry；candidate-mapping.json、OBS-045-source.cs 至 OBS-053-source.cs、verification.json 保存审阅与源码证据。先完整读取这些资料及两个开发仓库的 AGENTS.md。
- 巡检共 13,211 类、367,687 次上报，9 项关联 75 次上报、52 个指纹。现代协议的 mod_version 可能是 Core 版本，Karen/Core/Junna 各自版本必须从 mods 与 core_telemetry 读取，不能混用。

问题清单：
1. OBS-045，Core/P1，36 次、22 指纹：CardExecutionModule.NativeInvocation.Transpiler 在 AutoPlay.MoveNext 报 hooks=1、entries=0，Core 初始化中断。涉及 v0.107.1、v0.113.0、v0.113.1，Core 1.9.4；核对原组合 IL、Harmony 顺序及 RitsuLib 0.6.13。
2. OBS-046，Core/P1，2 次、2 指纹：CardPlayRoutingModule.RitsuBoundary 报 effects=0，NativeBoundary 报 effects=0、results=1；作为同一牌效路由入口兼容边界审阅。
3. OBS-047，Karen/P1，5 次、4 指纹：TrackOutgoingCard → PromisePileCardCreationPositionPatch.Postfix 在约定取牌、约定之塔链中空引用；原组合含 HyperSpeed。先区分空节点、空 Model 或生命周期问题。
4. OBS-048，Core/P1，3 次、3 指纹：ShineResultMovement.Postfix 空引用，回合末 FlushPlayerHand 导致 combat turn loop died；当前无条件读取 __state.Count，核对前置补丁被跳过的组合。
5. OBS-049，Core/P1，4 次、4 指纹：CoreShineCardRewardChoice.SelectCards → SpecialCardReward.OnSelect 领取 KAREN_SUNLIGHT 时提示必须先登记 RunState。正常生成路径已有 RunState.CreateCard，需核对保存恢复、重建与原对象领取链；包含 Windows/Android 报告。
6. OBS-050，Core/P1，2 次、2 指纹：Core PatchAll 为 CardCmd.Exhaust 安装补丁时 __result 与 Task<CardPileAddResult?> 返回类型不兼容；当前 ShineResultExhaust.Prefix 使用 ref Task。具体原组合冲突补丁待定位。
7. OBS-051，Karen/P2，5 次、1 指纹：KarenCarryingGuilt.AddExtraArgsToDescription 以空 Owner 调用 Core CountDepletedKinds，ConditionalWeakTable 报 key 为空；描述只检查 IsCanonical，伤害倍率也直接传 card.Owner。
8. OBS-052，Core/P2，15 次、12 指纹：ModelExecutionModule.Install/Patch 扫描方法时遇到 AfterDeath、SetAnimation 等失效方法引用而中断。底层无效方法所属 Mod 待定位；Core 当前逐目标安装缺少隔离，且 Patch 成功前先加入 _patched。确认实际目标、上游归属与 Core 安装边界后处理，保留原始异常及有效钩子，不能把普通第三方自身异常一概吞掉。
9. OBS-053，Karen/P2，3 次、2 指纹：Godot 原生格式栈直接指向 NKarenBaseballVfx.ImpactLight → DrawLight → DrawColoredPolygon，Invalid polygon data, triangulation failed。检查四边形顶点、退化面积、有限值和尺度边界，保持特效等级治理。

这些是待验证的排查结论，不能把推测直接当作已确认根因；若最小对照证明某项是纯外部 Mod 问题，按既有人工规则保留编号并写明忽略依据。保持根因级合并，避免按指纹重复修复。不得靠整体停用核心机制、删除校验或吞掉异常来伪造通过。

已完成项目要保留：
OBS-040 的 v0.113.1 择版修复 d73f501、配套 Core 11a96ad，OBS-041 标题 Amount 修复 46de4cc，均已核验随 Karen v1.0.0（内嵌 Core 1.9.4）发布，状态为已发布、待原组合核验。新增旧 v0.1.8/v0.2.1 报告不能据此认定修复复发。保留其余修复记录和 28 项人工忽略结论。

执行约定：
按 $worktree-task 在两个当前开发仓库分别捕获原分支/HEAD/未提交状态，创建 ../worktrees/<projectName>/<task-slug> 隔离工作树，先写任务记录；该对话负责协调全部 9 项。统一巡检文档尚未提交的既有内容必须保留，不能拿旧检出作为 Karen 游戏实现起点。UTF-8、最小修改，只做直接相关的最小必要构建、回归与游戏定向验证，不自动扩大到全面矩阵。按仓库要求使用 KarenSTS2MCP、隔离数据目录、静默/最小化；涉及移牌保存并查看前后截图，动画结束核对手牌模型与节点。优先复用现有受测包和工具，校验最终代码、资源与产物。

每项记录真实根因、归属、修复提交、受测游戏/Mod/依赖版本及包哈希、实际通过项和剩余待验项。每仓库按技能整理为一条任务提交，交付具体可审阅结果后按技能请求合并与清理确认；推送、部署和发布由用户另行安排。

完成或阶段完成时，将每项状态、任务记录、提交与结果文件同步回旧证据仓库 docs/待修复问题.md 及本地 triage-policy.json、state.json，保留稳定编号、人工备注和历史修复；不要推进或改写巡检成功计数基线。结果在各开发仓库 artifacts/task-deliverables/error-watch-20261116/result.json 保存机器可读记录，区分修复中、待合并、已合并待上线、已发布待原组合核验。全部私有原始证据留在忽略目录，Git 文档只使用相对路径和用途占位符，不把本提示里的绝对路径写入受版本管理的文档。


### 工坊检索补充

> 你来在创意工坊中找找吧

### Steam 登录补充

> 已登录 Steam，可以继续

### 测试方式补充

> 你测试要用mcp mod 静音+最小化

### 合并与清理确认

确认范围：将 Core `15c39a8`、Karen `54fa9a5` 快进合入各自 main；删除本轮两个工作树、任务分支和其中临时文件，保留已归档的包与证据。

用户回复：

> 合并并清理本轮工作树与分支

## 简要计划

1. 保存原检出状态，读取巡检原始证据，建立直接复现与负对照。
2. 按 P1 顺序修复补丁边界、移牌、奖励恢复，再处理预览和几何。
3. 执行双基线构建与直接相关定向回归，使用 KarenSTS2MCP 留存游戏证据。
4. 同步统一巡检状态，每仓库整理一条任务提交，提交合并与清理审阅。

## 起点

- 原分支：`main`
- 原 HEAD：`4a273e2b6e62709e69cd17f30ad701de1ee0cdfe`
- 原检出状态：干净。
- 任务工作树：`../worktrees/STS2_ShoujoKagekiAijoCore/error-watch-20261116`。
- 原始证据：`<error-watch-evidence-repository>/artifacts/error-watch/2026-11-16/`，私有原文不入 Git。

## 实际结果

本轮为阶段交付：8 项修复通过定向验证，OBS-046 原生百闻牌入口已修复，RitsuBoundary 原报告仍修复中。用户已批准合并到两个 main 并清理；本记录与修复共同保留在任务提交中，集成与清理的最终状态见各开发仓库 `artifacts/task-deliverables/error-watch-20261116/result.json`。

双版本 Runtime、Karen 程序集与完整资源包构建通过；正式版 v0.107.1／Beta v0.113.0 真实引用定向回归分别 12／14 项通过。真实游戏 v0.107.1、v0.113.1 通过 KarenSTS2MCP 操作，启动和运行均核对 `silent_test=true`、`muted=false`、最小化保护；隔离用户目录和端口，禁止抢焦点。双版节点生命周期、奖励登记／原生保存恢复、描述预览、Godot 绘制、约定取牌及回合末通过，手牌模型与可见节点一致，前后截图已查看。Beta 最终组合含 BaseLib 3.4.8、RitsuLib 0.6.13、MoreDefectAnimations 0.17.5、HowNikaidoHiro v0.7.5、HyperSpeed 1.2.4、百闻牌 1.0.7。

Core 原生异步结算、牌效路由与载荷定向回归通过；Karen 特效目录检查通过（70 类型、38 所有者、158 资源）。严格包校验通过，独立 Core 与 Karen 内嵌 Core 逐文件哈希相同。仅执行本次改动所需定向检查。

私有证据保存在 Core 工作树 `out/tmp/error-watch-20261116/`；Karen 受测包在 Karen 工作树 `out/tmp/error-watch-20261116/candidate-bwp/`。机器结果同步至各当前开发检出 `artifacts/task-deliverables/error-watch-20261116/result.json`。统一巡检记录同步至 `<error-watch-evidence-repository>` 的文档、triage-policy.json 与 state.json，保持成功采集基线、计数、28 项人工忽略和 OBS-040／041 历史不变。

工作中已快进同步 main 至 `a46d0cb`，保留同期 Hopper 预算修复；本任务未改动 ShineService。

| 编号 | 归属 | 状态与处理 | 直接验证 | 剩余事项 |
| --- | --- | --- | --- | --- |
| OBS-045 | Core | 已批准合并。MoreDefectAnimations 0.17.5 的 FlakAutoPlayPatch 将原生 OnPlayWrapper 改为静态 Dispatch 转发，导致直接调用计数为 0。 沿唯一外部 Task 转发链适配真实调用点，保留原包装器和边界计数。 | 发布版 Core 1.9.4＋官方 MoreDefectAnimations 0.17.5 在 v0.113.1 复现 hooks=1、entries=0；最终候选＋MoreDefectAnimations＋RitsuLib 双版启动通过；未知不透明入口仍拒绝安装 | MoreDefectAnimations 0.17.3 历史包与所有原玩家完整组合待上线核验 |
| OBS-046 | Core | 部分修复，继续定位。NativeBoundary 的原报告由百闻牌 1.0.7 的 CardPlayContextPatch 改写为 PlayWithRitsuHooks；该包装器通过缓存委托调用 RitsuLib。另一份 RitsuBoundary effects=0 报告的实际改写来源仍待定位。 核对百闻牌缓存委托确实指向 RitsuLib 后保留包装器；同时支持唯一直接 Task 转发链中的 Ritsu 牌效委托。 | 官方百闻牌 1.0.7＋发布版 Core 1.9.4 在 v0.113.1 复现 effects=0、results=1；最终候选＋百闻牌 1.0.7＋MoreDefectAnimations 0.17.5＋HowNikaidoHiro v0.7.5＋HyperSpeed 1.2.4＋RitsuLib 0.6.13 启动、出牌、约定取牌、回合末和奖励领取通过；双版合成转发回归与中间件／原牌效各执行一次通过；HowNikaidoHiro v0.7.5 单独对照不复现；已检查原第二报告所列各模组的当前官方程序集 | RitsuBoundary effects=0 的原 37 Mod 组合未重建，不能宣称该指纹已修复 |
| OBS-047 | Karen | 已批准合并。特效追踪直接访问节点.Model.Owner；原生跳过视觉、未绑定或已释放节点均会破坏该前提。 工厂 Postfix 与追踪入口检查节点有效性、释放状态和 Model。 | 原包空节点／未绑定节点探针失败，修复后空／未绑定／已释放节点通过；双版约定移牌、约定之塔和回合末模型／节点核对通过，截图已查看；Beta 实测含官方 HyperSpeed 1.2.4 | 原上报中具体空节点产生时序待原组合核验 |
| OBS-048 | Core | 已批准合并。更高优先级 Harmony Prefix 返回 false 时，Shine Prefix 可被跳过，Postfix 的 __state 为 null。 缺失状态直接透传原生任务；有延迟移牌时保持原合并行为。 | 原实现空状态直接复现 NullReferenceException；真实 Harmony 前置短路回归保留相同 Task 实例；双版实机回合末弃牌和下一回合通过 | 原组合中短路 Prefix 的具体上游待核验 |
| OBS-049 | Core | 已批准合并。KarenSunlight.OnShineExhausted 将有 Owner、未登记 RunState 的 Reacquire 预览交给 ShineRewards.Create；SpecialCardReward 保持原对象并要求已登记。 Create 对可变且未登记的奖励对象清除旧 Owner，再由原生 AddCard 登记并绑定收件人。 | 发布版真实探针复现奖励未登记；中间候选暴露 already has an owner 后修正；双版预览身份、重复 Create 与原生奖励序列化恢复通过；Beta 实际耗尽升级阳光→分组奖励选择→牌组领取通过，升级 1、闪耀 3、已登记；最终百闻牌组合重新加载保存的奖励并原生领取通过 | Android 原上报平台和完整原玩家组合待核验 |
| OBS-050 | Core | 已批准合并。Beta CardCmd.Exhaust 返回 Task<CardPileAddResult?>，原 Prefix 的 ref Task 及延迟返回 Task.CompletedTask 不满足该原生结果类型。 按 Stable Task／Beta Task<CardPileAddResult?> 分支声明与返回延迟结果。 | 双版真实引用补丁安装通过；Beta 同目标精确泛型结果 Postfix 共存通过；旧延迟结果类型负对照失败，修复后满足实际原生返回类型 | 原初始化失败组合中的具体冲突补丁未定位 |
| OBS-051 | Karen | 已批准合并。非 canonical 的无 Owner 预览仍将空 Owner 传入闪耀档案；倍率回调也有相同访问。 描述与倍率在无 Owner 时使用零档案数。 | 原包真实无 Owner 预览失败；双版 canonical／无 Owner 描述及无 Owner／已绑定玩家的伤害预览通过 | 原玩家完整组合待上线核验 |
| OBS-052 | Core | 已批准合并。扫描旧游戏编译的第三方模型方法时，Harmony 读 IL 遇失效成员引用；原实现会中断后续安装并提前记为已补丁。 仅隔离外部方法原始 IL 读取的 MissingMemberException，保留目标类型／方法／程序集版本和原异常；补丁成功后才登记。 | 以 v0.111.0 真实引用构建旧 AfterDeath 方法夹具，在 v0.113.0 复现 MissingMethodException；修复后保留原异常、继续有效模型、坏方法不标记成功且可重试；模型自身 IOException 仍原样传播 | 原上报失效模型所属 Mod 未确定；新诊断可记录该归属 |
| OBS-053 | Karen | 已批准合并。小型淡出光片先加屏幕坐标，Godot 浮点有符号面积计算发生消减，正常屏幕位置也可三角化失败。 在局部原点构造光片，以绘制变换定位，并检查有限值、正尺度和有效面积。 | 真实 Godot 绘制旧实现产生 4 次原生三角化错误；最终双版相同屏幕位置／年龄序列产生 0 次原生错误，临时节点已释放；既有特效目录治理检查通过，70 类型／38 所有者／158 资源 | Android 原上报平台待核验 |

产物清单按相对路径排序，逐行使用 `sha256  path\n` 计算汇总 SHA-256：

- Core：`f62a0e5c1308e94eb798fa8b6d3c5b777b685150ab205f329688fa247d49908b`。
- Karen：`26b2ff8d6142e34ef199627455a2f516a84e0d72cc67d4cb684d7acaf2061f8a`。

官方工坊来源：[MoreDefectAnimations](https://steamcommunity.com/sharedfiles/filedetails/?id=3810696310)、[HowNikaidoHiro](https://steamcommunity.com/sharedfiles/filedetails/?id=3748231835)、[HyperSpeed](https://steamcommunity.com/sharedfiles/filedetails/?id=3754834554)、[百闻牌](https://steamcommunity.com/sharedfiles/filedetails/?id=3815122693)。官方 Steam 登录会话下载仅用于本地组合验证。

合并确认补充仅更新本任务记录，生产代码与受测包保持一致。必要私有证据与四个精确官方依赖已保存在 `<Core 当前开发检出>/artifacts/task-deliverables/error-watch-20261116/`，最终包分别保存在两个当前开发检出的同名交付目录。
