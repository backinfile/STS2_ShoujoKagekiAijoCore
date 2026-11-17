# 2026-11-17 巡检修复 OBS-034／046／052／054

## 原始输入

### 用户提供的工作区约定

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

### 委派要求

请使用 $worktree-task（$worktree-task），实际排查并修复 2026-11-17 巡检中当前 4 项待处理问题：Core 的 OBS-034、OBS-046、OBS-052，以及 Karen 的 OBS-054。原始用户要求：“创建新对话处理（别忘了处理完成后更新文档）”。此前用户已要求用 worktree-task 处理此系列问题。请完成具体修改、必要验证和可审阅结果，并将阶段及最终处理结果主动同步到统一待修复文档，不要只交付分析或计划。

仓库与证据：
- 当前 Core 开发仓库：<Core 当前开发检出>。
- 当前 Karen 开发仓库：<Karen 当前开发检出>。
- 统一巡检文档与原始证据仓库：<error-watch-evidence-repository>。其 docs/待修复问题.md、artifacts/error-watch/triage-policy.json、state.json 是统一状态来源。此旧检出有既有未提交改动和删除文件，必须保护；不要在这里修改 Karen 游戏源码。
- 先读取这些仓库的 AGENTS.md，以及统一文档、config.json、triage-policy.json、state.json 的最新内容。
- 最新完整证据在旧证据仓库 artifacts/error-watch/2026-11-17/：triage-review.json 的 info、reopen_info 记录本次新增／重新定位范围；details.json 是完整调用栈与启用 Mod／Core 遥测；candidate-mapping.json、root-summary.json、review.json、verification.json 和 OBS-034／052／054-source.cs 保存归属、计数与源码证据。
- 最新成功全量基线为 14,924 类、411,991 次上报，747 页、1,786 份变化详情全部完成。不要修改或推进此成功采集基线及各指纹已观察计数／时间。
- 本次任务标识使用 error-watch-20261117 或其唯一变体。此前 error-watch-20261116 已完成原范围修复，不要复用其结果文件或覆盖历史记录。

按优先级处理：
1. OBS-034，Core/P1，新待定位范围 2 次、2 指纹、单指纹最高 1 会话。v0.113.1，Karen 1.0.0、Core 1.9.5、RitsuLib 0.6.14、Windows。ShuffleMoveOrderPatch.Transpiler 报 Expected one batch Add call in CardPileCmd.Shuffle，初始化中断。当前 Core 仍严格匹配一处六参数批量 Add。读取原组合真实 Shuffle IL、Harmony 补丁及顺序，查明改写来源并保持视觉与 Hook 顺序。原 OBS-034 的 46b5793 仅为旧 Karen 公共实现迁出，原 1 次历史记录与迁移结论保留；新 Core 范围不被该迁移覆盖。完整指纹见 reopen_info.OBS-034。

2. OBS-046，Core/P1，仍部分修复中，2 次、2 指纹。原 NativeBoundary 报告已查明是百闻牌 1.0.7 的 CardPlayContextPatch 将调用改为缓存委托 PlayWithRitsuHooks；该入口及唯一直接 Task 转发链适配已随 Core 97a67a4 合并，保留。此次继续处理 RitsuBoundary effects=0 的原 37 Mod 组合，确认真实委托调用改写来源。先读取 Core docs/worktree-tasks/error-watch-20261116/README.md 与 artifacts/task-deliverables/error-watch-20261116/result.json 的 remaining_work，原始详情在 2026-11-16 和 2026-11-17 证据目录。不要因百闻牌入口已修复或两份报告同为路由问题而将剩余范围标为完成。若无法取得准确原包／组合，应写清缺少的具体证据及已验证边界，保留剩余状态。

3. OBS-052，Core/P2，新未覆盖范围 10 次、7 指纹、单指纹最高 3 会话；同一编号累计 35 次、27 指纹。v0.107.1／v0.113.1，Core 1.9.4／1.9.5，Windows。新增 TypeLoadException（CardLocation 签名反射）、InvalidProgramException（Harmony.UpdateWrapper 生成无效程序）、NotSupportedException（STS2_Things.Monsters.MycorrhizalBulwark 方法无法取得入口）。当前 97a67a4 仅隔离外部原始 IL 预读的 MissingMemberException；签名反射和补丁生成／入口阶段仍在该保护范围之外。实际目标方法、上游 ABI 与本方适配边界需确认，定向处理并保留有效钩子及业务异常传播。原 MissingMember 隔离修复与 25 次／20 指纹的原处理范围保持已合并。
特别区分：本次 41 次、38 指纹的 model.invalid-method-body 是已经隔离并继续安装的外部失效方法诊断，按上游问题忽略；不要再次当作 Core 安装失败。triage-policy.json 的 resolved_scopes、current_open_scope、current_open_fingerprints 已区分新旧范围，不能用旧修复结果覆盖新范围。

4. OBS-054，Karen/P2，27 次、2 指纹、单指纹最高 1 会话。v0.113.1，Karen 1.0.0、Core 1.9.5，SpeedX 0.11.12、RitsuLib 0.6.14，Windows。NKarenPromiseStarNode.UpdatePlacement 读取已释放 SNCreatureVisuals.GlobalTransform，抛 ObjectDisposedException，_Process 反复进入。当前仅检查父 NCreature 有效性，没有验证 Visuals；检查子视觉替换／释放、星节点停止清理或重绑定时序。复核速度组合、离房／恢复和约定表现。该失败点独立于 OBS-047 已修复的取牌节点工厂空值，不要合并成同一个已修复范围。

版本与归属：
Karen/Core/Junna 版本从 mods 与 core_telemetry 读取，不能把旧字段 mod_version 直接套给其他 Mod。Core 1.9.5 存在不同实现，同版本不能证明实际使用了修复交付包。OBS-050 另有 14 次、12 指纹待实际包哈希和目标返回类型核验，当前不认定为原修复复发；如用于本次包身份核对，保留该证据边界。只有出现具体新证据才改变既有归属，不扩大重修其他已完成条目。纯外部 Mod、自身业务异常和通用日志仍按用户人工忽略规则处理，保留稳定编号和理由。

执行与交付：
- 按 $worktree-task 捕获各仓库原检出、分支、HEAD 和未提交状态；在 ../worktrees/<projectName>/<task-slug> 创建隔离工作树，先写 docs/worktree-tasks/<task-slug>/README.md，修复与验证在工作树中完成。记录完整用户要求，Git 文档使用脱敏摘要、相对路径与用途占位符，不把本提示中的绝对路径写入受版本管理文档。
- UTF-8、最小改动；只运行与改动直接相关的最小必要构建／回归及定向游戏检查。运行行为按各仓库要求用 KarenSTS2MCP 和隔离环境，静音／隐藏或最小化；涉及移牌须保存并查看前后截图，动画结束核对手牌模型与节点。不要自动扩大为全面测试。
- 每仓库按技能整理任务提交，给出实际根因、改动、受测游戏／Mod／依赖版本、包哈希、通过／失败／剩余项；交付具体可审阅结果后按技能请求合并和清理确认。推送、部署、工坊上传及发版另行安排。
- 必须主动更新统一文档 <error-watch-evidence-repository>/docs/待修复问题.md，并同步该证据仓库 artifacts/error-watch/triage-policy.json、state.json 的处理状态及修复交接元数据。阶段完成、等待合并或合并后都及时更新实际状态：修复中、待合并、已合并待上线、已发布待原组合核验。不要等用户再提醒更新文档，也不要仅因接收任务就标已修复。
- 更新中央文档时从最新未提交内容最小修改，保留原计数、指纹、人工备注、忽略结论、旧修复与验证记录。OBS-034／052 要明确新旧子范围；OBS-046 仍有剩余范围时保留部分修复状态。修复结论被最小对照证明属于纯上游时，可按人工规则标忽略并记录依据。不得重置中央仓库现有改动，不要改写全量成功采集基线。
- 两个开发仓库分别保存 artifacts/task-deliverables/error-watch-20261117/result.json，包含任务记录路径、每项范围与状态、真实根因、提交、合并／发布状态、验证证据、产物哈希及剩余工作。中央证据目录保存交接同步信息以供后续每日巡检读取，原始私有证据只留在忽略目录。


## 简要计划

1. 捕获原检出并保留中央文档已有修改，建立独立工作树。
2. 按指纹提取真实调用栈与组合，构造失败对照，逐项定位和最小修复。
3. 执行相关构建、回归与 MCP 定向验证，记录包哈希及实际剩余范围。
4. 同步中央文档与巡检交接，整理单仓库任务提交，等待合并和清理确认。

## 结果与变更

已完成两阶段修复与定向验证，最终快照已获合并与清理确认；原组合尚未定位的本方边界继续处理中。原检出为 main，起点 `7934b6937431a0443e21bcd4933f34bf65043d3d`，原检出干净。工作树为 `../worktrees/STS2_ShoujoKagekiAijoCore/error-watch-20261117`。

## 追加用户输入

用户追加约定（原文）：
“添加一个约定： 与其他mod兼容不足出现的问题，能简单处理的就处理下，不能的直接搁置即可（在文档中注明搁置）”

请立即将此作为当前修复任务的处理原则：对与其他 Mod 的兼容问题，能以小范围改动直接处理并做必要验证的就处理；需要复杂适配、较大改动或持续深入排查而无法简单解决的，标记“搁置”，在统一 docs/待修复问题.md 中注明兼容对象和搁置原因，并同步 triage-policy.json、state.json 及任务结果的真实状态，保留编号、计数、原修复范围和证据。不要为这些兼容项继续投入复杂组合重建或大幅改造。请据当前进展评估 OBS-034、OBS-046、OBS-052 的兼容子范围，区分已解决、简单可修和应搁置范围；Karen 自身 OBS-054 仍按自身缺陷修复。搁置与“已修复”或“忽略，不处理”分开，后续出现简单可行方案或用户重新安排时再处理。

本巡检对话正在将同一约定补入中央仓库 AGENTS.md、统一待修复文档的规则段和 triage-policy.json 的通用规则元数据，不会在这一步改动各项处理状态或采集计数。请写文档／本地状态前重新读取当前文件，保留并发的规则更新；也请将用户原文追加到本任务的用户输入记录。

第一阶段计划（已由后续归属澄清调整）：不重建复杂兼容组合；OBS-034／046 保留证据并评估搁置，OBS-052 只处理可由最小对照验证的安装边界。OBS-054 继续修复自身视觉生命周期。

## 本轮实际结果

按后续归属澄清继续修复 Core 自身边界，第一阶段的搁置判断已撤销。每仓库保留一条任务提交，最终快照已获用户合并与清理确认；原开发检出保持原 HEAD 和干净状态。首轮获准快照及证据保留于 `artifacts/task-deliverables/error-watch-20261117/phases/first-review/`，后续结果、受测包及私有证据归档到同级 `result.json`、`package-followup/` 和 `evidence/followup/`。

| 范围 | 本轮修复及验证 | 继续跟踪 |
| --- | --- | --- |
| OBS-034，新增 2 次／2 指纹 | 修复 Core 重复转换与 Task<T> 批量 Add 转发边界；外部包装器保留完整结果，仅 Shuffle 丢弃已执行视觉结果。真实 v0.113.1＋RitsuLib 0.6.14 中包装器收到 6 条结果，批量执行一次，移牌 Hook 抽牌一次，手牌 5→6 且模型／节点一致。 | 原 32 Mod 的精确改写补丁、实际 IL 和顺序仍待定位，保持本方修复中。旧迁移 46b5793 的 1 次历史范围保留。 |
| OBS-046 | 修复重复 Native／Ritsu 转换及等价委托匹配，保留原委托；真实 RitsuLib 0.6.14 的 before、middleware-before、effect、middleware-after、after 各一次。歧义边界仍拒绝，记录调用、补丁及 MVID。 | 原 RitsuLib 0.6.13／37 Mod 的委托改写来源仍待定位，保持部分修复中；97a67a4 百闻牌入口适配保留。 |
| OBS-052，新增 10 次／7 指纹 | 保留外部 CardLocation 签名隔离；修复将编译器私有实现回调误当模型入口的扫描范围。官方 YuWanCard 0.5.13 的 InflationPower.<get_GoldGuard>b__6_2 精确复现旧 InvalidProgramException，最终源码扫描及实机安装通过。Things 1.25.27 最终扫描通过，报告回调不再补丁，命名方法仍安装。公开生成入口、接口实现、原生覆盖、继承牌效、泛型结果与业务异常传播均保留。 | 原 InvalidProgram 指纹未记录目标，不能全部归因于 YuWan；Things 原入口异常未在独立扫描复现，仍需原运行时组合核验；CardLocation 原报告包身份待核对。 |
| OBS-054，27 次／2 指纹 | Karen 约定星在取视觉变换前检查当前 Visuals 有效、未待释放且已入树；父节点待释放时停止处理并清理。原包精确复现 ObjectDisposedException，最终六项生命周期及离房重入后的再次六项通过。 | SpeedX 0.11.12 原触发组合待上线核验。Karen 生产源码与首轮获准的 5cc9e56 相同。 |

OBS-052 原 MissingMember 隔离的 25 次／20 指纹及 97a67a4 修复保留；41 次／38 指纹的已隔离 `model.invalid-method-body` 继续忽略。OBS-050 的 14 次／12 指纹仍待实际包身份核验。

### 验证与产物

- 最终源码定向托管检查共 46 项：正式版 v0.107.1 为 21 项，Beta v0.113.1 为 25 项，覆盖初始化边界、模型安装及既有路由／转发回归。官方 YuWan／Things 实际业务程序集最终扫描均通过。旧候选的对应失败日志和新候选通过日志均已归档。
- Core 正式版 v0.107.1／Beta v0.113.0 真实引用 ExportRelease 构建均 0 警告、0 错误；Karen 正式版 v0.107.1／Beta v0.113.1 构建 0 错误，保留既有 23 项可空警告。资源 PCK、引导和公开 API 源码未变，沿用首轮已验证文件；官方 SDK 重建 bundle 哈希及角色包锁，最终严格包校验通过，18 个实机安装文件逐一匹配候选 SHA-256。
- 最终实机为 Windows v0.113.1，BaseLib 3.4.8、Core 1.9.5 候选、Karen v1.0.0、RitsuLib 0.6.14、YuWanCard 0.5.13、KarenSTS2MCP 0.1.1。MCP 使用独立用户目录、随机端口、`--force-steam=off`、`silent_test=true`、`muted=false`、最小化及禁止抢焦点。两阶段共 12 张截图已查看、10 次手牌模型／节点一致；仅停止已核对路径的本轮进程。
- 首轮约定放／取牌、回合末、离房重入通过；后续验证 Core 初始化、有效 YuWan 钩子、真实 Ritsu 路由各一次及带移牌 Hook 的泛型洗牌转发。SpeedX 原组合与完整矩阵未运行。原有 MCP 设置提示反射诊断保留。
- 集中待修复文档、策略、状态和交接按最终范围主动同步。14,924 类／411,991 次、747 页／1,786 详情、指纹计数／时间、历史子范围和人工忽略均保留；未归属的本方失败点继续待定位。

### 复现入口

```powershell
dotnet run --project tests/InitializationBoundaryChecks -c Release -p:Sts2Api=stable -p:GameDir=<stable-game>
dotnet run --project tests/InitializationBoundaryChecks -c Release -p:Sts2Api=beta -p:GameDir=<v0.113.1-game>
dotnet build tests/SignatureModelFixture -c Release -p:FixtureGameDir=<v0.113.1-game>
dotnet run --project tests/ModelInstallationChecks -c Release -p:Sts2Api=stable -p:GameDir=<stable-game> -- tests/SignatureModelFixture/bin/Release/net9.0/SignatureModelFixture.dll
```

游戏探针 `tests/InitializationBoundaryIntegration/` 用官方 SDK 生成需求后独立安装，经 MCP 执行 `core_boundary arm`，正常打出 Karen 防御，再执行 `core_boundary report`；`core_boundary shuffle` 验证泛型转发及移牌 Hook，`core_boundary status` 核对真实 YuWan 扫描结果。Karen `tests/PromiseVisualLifecycleIntegration/` 的 `promise_lifecycle check`／`report` 验证六项生命周期。测试插件不进入交付包。

### 最终程序集 SHA-256

- `Core:lib/1.9.5/beta/ShoujoKagekiAijoCore.Runtime.Beta.dll`：`14698345803f5e37ed9df6da7855759c1c9c34c73eecc3d53b9e0e63d9d37867`
- `Core:lib/1.9.5/stable/ShoujoKagekiAijoCore.Runtime.Stable.dll`：`d5c84ee67168c9b19d47c3673d94c7f10ecbb7b6bffd1210967106c592567fb2`
- `Karen:ShoujoKagekiAijoKaren.dll`：`5aee49eef725ca3144433d9986949f61778e7611a6d996427ec2f304a010d664`
- `Karen:lib/ShoujoKagekiAijoKaren.Beta.dll`：`16d22affd16e9bd079c2da7f3ac3fef30f4895618772b28907bac608ed962091`
- `Karen:lib/ShoujoKagekiAijoKaren.Stable.dll`：`01e6cc6839cb474ac6c5d5be2670f10c04fa0915bf40febb631c9a7c06c799e7`

## 后续用户输入与继续处理

### 合并与清理确认

```text
<send_user_message_question_reply>
[{"questionItemId":"[\"request_user_input_async\",\"call_99203a4406ef4d17a8d0e3fdcf0c9d82\",0]","question":"将 Core 668cace、Karen 5cc9e56 合入各自 main，并如何处理本轮工作树和分支？","answer":"合并并清理本轮工作树和分支"}]
</send_user_message_question_reply>
```

### 继续处理 Core

core是自己的mod，要处理的

处理调整：Core 属于本系列自有 Mod。OBS-034、OBS-046 和 OBS-052 剩余范围恢复处理中，继续定位和修复 Core 的初始化／模型扫描边界。保留第一阶段已验证修复与工作树；完成新增处理范围后更新交付，再执行最终合并与清理。

### 巡检交接补充的归属澄清

用户最新澄清（原文）：“core是自己的mod”。请据此明确归属：Karen、Core、Junna 都是本方自有 Mod；三者自身代码、公共实现及相互协作的问题按自身缺陷处理。“能简单处理就处理，否则搁置”的兼容约定仅针对本系列与第三方 Mod 之间的兼容不足，不能因为 Core 是公共库、依赖或归属暂不清楚就适用搁置。

请复核本轮已搁置的 OBS-034、OBS-046 及 OBS-052 子范围：有具体证据属于第三方兼容且无法简单处理的，可以继续搁置，注明兼容对象与依据；尚未证实第三方归属、仍属于本方具体失败点待定位的，保留／恢复本方待定位或修复状态，不因排查复杂就套用兼容搁置规则。保留已验证修复、提交、原范围与统计基线，按真实复核结果更新统一待修复文档、triage-policy.json、state.json 和任务结果，记录用户此次澄清。此条不授权额外全面测试、合并、发布或部署。

本巡检对话正在把自有 Mod 的边界写入中央 AGENTS.md、待修复文档、通用巡检策略与定时巡检提示；请更新文件前重读并保留这些规则修改。

## 最终合并与清理确认

```text
<send_user_message_question_reply>
[{"questionItemId":"[\"request_user_input_async\",\"call_9a35d94801804404b59c1cf9527d024b\",0]","question":"将最终 Core 96c14e0、Karen 36fc48f 合入各自 main，并如何处理本轮工作树和分支？","answer":"合并并清理本轮工作树和分支（保留已归档包与证据）"}]
</send_user_message_question_reply>
```

用户批准 Core `96c14e0`、Karen `36fc48f` 合入各自 `main`，并在核验成功后清理本轮工作树及分支，保留已归档包与证据。确认后仅追加此记录，不改变受测生产源码。最终提交、合并和清理实况记录于 `artifacts/task-deliverables/error-watch-20261117/result.json`。
