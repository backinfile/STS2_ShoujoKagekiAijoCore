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
