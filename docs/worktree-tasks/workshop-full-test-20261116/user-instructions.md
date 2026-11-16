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

# Codex 项目说明

## 重要参考路径

- 反编译游戏本体路径：`<decompiled-game-source>\`
- STS1 爱城华恋 Mod（老 Mod）路径：`<sts1-karen-mod-source>\`
- BaseLib 代码路径：`<baselib-source>`
- RitsuLib 代码路径：<ritsulib-source>
- STS2 Godot 日志路径：`<user-home>\AppData\Roaming\SlayTheSpire2\logs\godot.log`
- STS1 游戏本体代码路径：`<steam-sts1-install>\desktop-1.0`
- Karen 正式版开发游戏：`artifacts\dev-game\stable\game`（v0.107.1）
- Karen 测试版开发游戏：`artifacts\dev-game\beta\game`（v0.111.0）

## 项目基本信息

`STS2_ShoujoKagekiAijoKaren` 是一个《Slay the Spire 2》角色 Mod，为游戏新增可玩角色 Karen，并配套实现卡牌、Power（能力）、遗物、音画表现和多套自定义战斗机制。

项目位于：

- `.`

当前项目主要依赖：

- Godot 4
- C# / .NET 9
- Harmony
- BaseLib

## 代码结构定位

这个项目主要基于 STS2 原生 `Model / Hook / Cmd / Patch` 体系做扩展，而不是在原版系统外另建一套独立玩法框架。Karen 的主要实现大致分布在以下层级：

- 角色定义层：`src/Core/Models/Characters/Karen.cs`
- 卡牌与 Power 模型层：`src/Core/Models/`
- 系统扩展层：`src/Core/ShineSystem/`、`src/Core/PromisePileSystem/`、`src/Core/GlobalMoveSystem/`、`src/Core/ExtraReplaySystem/`、`src/Core/DisableRelicSystem/`
- 工程接线层：`src/Core/Patches/`
- 资源与本地化层：`scenes/`、`images/`、`materials/`、`audio/`、`localization/`

## Mod 主入口

安装包主入口在 `loader/Bootstrap.cs`，按游戏版本载入正式版或测试版实现；具体 Mod 初始化在 `src/ShoujoKagekiAijoKaren/MainFile.cs`。

初始化流程主要包括：

1. 读取当前程序集，并调用 `ScriptManagerBridge.LookupScriptsInAssembly(assembly)`。
2. 通过 `ModConfigRegistry.Register(ModId, new KarenModConfig())` 注册配置。
3. 接入 Core 音乐仲裁，创建 `Harmony` 实例并注册角色补丁。公共闪耀、保存、采集、标签、启动画面和播放器由 Core 初始化。

`src/ShoujoKagekiAijoKaren/MainFile.cs` 中的 `ModelDbAllCharactersPatch` 会给 `ModelDb.AllCharacters` 做 `Postfix`，把 `ModelDb.Character<Karen>()` 插入角色列表，并清空 `ModelDb` 内部的 `_allCharacterCardPools` 和 `_allCards` 缓存，确保角色和卡池缓存重新生成。

这意味着 Karen 角色是通过 `ModelDb` 补丁接入游戏，而不是依赖外部注册表文件。

## Karen 角色配置

角色定义位于 `src/Core/Models/Characters/Karen.cs`。

基础配置包括：

- 角色 ID：`karen`
- 性别：`CharacterGender.Feminine`
- 起始生命：`72`
- 起始金币：`99`
- 名字颜色：`#FB5458`
- 对话颜色：`#8B2A2D`
- 地图绘制颜色：`#FB5458`
- 远程目标线颜色：`#FF7A7DFF`
- 卡池：`KarenCardPool`
- 药水池：`IroncladPotionPool`
- 遗物池：`KarenRelicPool`
- 起始遗物：`KarenHairpinRelic`

起始卡组共 10 张：

- `KarenStrike` x4
- `KarenShineStrike` x1
- `KarenDefend` x4
- `KarenFall` x1

## 主要机制

Karen 当前的核心机制包括：

- Shine（闪耀）：卡牌可拥有闪耀值，打出时通常递减；耗尽后会进入专门的 Shine 处理流程。
- PromisePile（约定牌堆）：通过 `SpireField` 附着到玩家战斗状态上的虚拟牌堆，支持加入、抽取、切换、清空和模式切换。
- GlobalMove（全局移牌）：监听卡牌在牌堆之间的迁移，为卡牌模型补充额外的移动触发点。
- ExtraReplay（额外重播）：用于修改卡牌的实际播放次数。
- DisableRelic（禁用遗物）：通过 `DynamicVar` 和可打出条件控制，让部分卡牌与可禁用遗物数量挂钩。

这些机制之间存在交叉联动，例如 Shine 会影响结果牌堆流向，PromisePile 会与 Power、视图和 VFX 联动，ExtraReplay 会介入 `Hook.ModifyCardPlayCount`。

## 常用阅读入口

读取文档是优先使用UTF8格式。
第一次接手项目时，建议优先阅读：

1. `docs/01.文档总览与索引.md`
2. `docs/02.项目基本信息.md`
3. `docs/03.项目架构与流程.md`
4. `docs/04.卡牌与效果系统.md`
5. `docs/06.Karen核心系统.md`
6. `docs/09.工程技术与BaseLib.md`
7. `docs/10.本地化与开发规范.md`

如果目标是写卡或修改 Power，优先看：

- `docs/04.卡牌与效果系统.md`
- `docs/05.卡牌与Power汇总.md`
- `docs/10.本地化与开发规范.md`

如果目标是修系统、补 Hook 或排查补丁，优先看：

- `docs/03.项目架构与流程.md`
- `docs/06.Karen核心系统.md`
- `docs/09.工程技术与BaseLib.md`

## 其他

+ 文件编码优先使用UTF-8。 读取或修改本地化文件和代码文件时必须使用UTF8编码。
+ 卡牌稀有度术语：普通=`CardRarity.Common`，罕见/蓝卡=`CardRarity.Uncommon`，稀有=`CardRarity.Rare`。

## STS2 MCP 连接与使用

- 使用 MCP 操作 STS2 时，使用 `<karen-sts2-mcp-source>` 中的 `KarenSTS2MCP`，不要连接旧 `STS2_MCP` 服务。先在游戏日志中确认 `[Karen STS2 MCP]` 已加载，再访问 `http://127.0.0.1:15527/api/v1/singleplayer?format=json` 验证接口。
- 游戏内开发者命令通过 `POST /api/v1/singleplayer` 的 `{"action":"run_command","command":"..."}` 执行；关闭设置界面使用 `{"action":"close_settings"}`，也可用 `menu_select` 的 `back` 选项。测试时不要使用 Computer Use。
- 先读 `docs/external/KarenSTS2MCP.md`；普通状态和动作字段可参考 `docs/external/STS2MCP/raw-simplified.md`，完整字段和动作说明见 `docs/external/STS2MCP/raw-full.md`。后两份是上游参考，端口及新增动作以本项目文档为准。

## Karen 独立双版本开发环境

- Godot/MegaDot 打包导出直接使用正常 Windows 权限环境（Codex 工具使用 `require_escalated`），复用现有编辑器和 .NET 运行库。2026-10-27 已复现：受限沙箱内会出现 `Could not create editor data/config directory`、`.NET: Assemblies not found` 并崩溃；同一构建在正常 Windows 环境成功。后续构建先按此环境执行，遇到同类日志优先核对目录访问权限。
- 正式版游戏及 Mod 目录：`artifacts\dev-game\stable\game`、`artifacts\dev-game\stable\game\mods`；启动 `run_stable_dev.bat`（`run.bat` 也默认启动这里）。
- 测试版游戏及 Mod 目录：`artifacts\dev-game\beta\game`、`artifacts\dev-game\beta\game\mods`；启动 `run_beta_dev.bat`。
- 两套环境各自的用户数据目录分别是 `artifacts\dev-game\stable\user\AppData\Roaming\SlayTheSpire2` 和 `artifacts\dev-game\beta\user\AppData\Roaming\SlayTheSpire2`；日志分别写入各自环境根目录的 `godot.log`。
- 启动器使用 `--force-steam=off`，不读取 Steam 创意工坊 Mod 或云存档。两个 `game\mods` 目前只部署 BaseLib、Karen 与 KarenSTS2MCP，和 Steam 安装目录 `<steam-sts2-install>\mods` 相互独立，可同时启动。
- 构建前通过 `SHOUJO_CORE_PACKAGE` 指定官方 Core 发行目录。`update_dev_mods.bat` 会构建并更新两套环境中的 Mod；`tools/dev_env.ps1` 支持 `Capture`、`Install`、`Launch`、`Smoke`、`Status`。开发环境完整说明见 `docs/dev-environments.md`。不要用 `build_local_mod.bat` 更新这些环境，因为它默认写入 Steam 安装目录。
- 上述独立环境会安装 KarenSTS2MCP，并使用各自的 `game\mods\KarenSTS2MCP.conf`：正式版端口 `15627`，测试版端口 `15628`。分别访问 `http://127.0.0.1:15627/api/v1/singleplayer?format=json` 和 `http://127.0.0.1:15628/api/v1/singleplayer?format=json`；本节前面的 `15527` 只适用于 Steam 安装目录中的 MCP。启动脚本会检查对应端口是否已被占用。

## 双版本与单人/多人测试要求

- **日常开发和创意工坊发布默认只做与本次改动直接相关的最小必要验证。** 包括适用的构建、版本/文案及产物核对，涉及运行行为时补充对应的单人或多人定向实测。完整单人/多人矩阵仅在用户明确要求全面测试时执行；批量脚本默认套件为 `smoke`。
- 游戏测试沿用最小化、静音约定。逻辑测试使用隐藏或最小化窗口；需要截图或真实绘制的专项使用隔离桌面并静音，保持画面正常更新。
- 涉及游戏运行的改动不能只以编译成功或启动日志作为兼容结论。进行全面测试时，正式版 v0.107.1、测试版 v0.111.0 都要分别做单人实测：选择 Karen 开局，进入战斗，检查起始牌、闪耀耗尽、约定牌堆及改动涉及的机制，并检查各自日志。
- 涉及移牌时必须通过 MCP 保存并查看操作前后截图；动画结束后执行只读命令 `karen_check_hand`，核对手牌模型与可见卡牌节点一致，不能只凭 `player.hand` 计数或移牌日志判定通过。《坠落》→《约定之塔》专项使用 `tools/test_promise_visuals.ps1`，`-Environment` 分别传 `stable` 和 `beta`；接口核查见 `docs/testing/beta-api-audit-2026-09-24.md`。
- 全面测试中的多人验证按下表分别覆盖正式版和测试版的同版本联机。两个进程必须使用不同的用户数据目录和日志文件，不能共享存档或 MCP 端口，也不能改动其他 Mod 项目的游戏目录。跨版本联机不属于本 Mod 的必测范围，也不纳入默认批量测试。

  | 主机 | 客户端 | 验证目标 |
  | --- | --- | --- |
  | 正式版 | 正式版 | 同版本联机流程和 Karen 机制同步 |
  | 测试版 | 测试版 | 同版本联机流程和 Karen 机制同步 |

- 每组可联机的同版本测试还要覆盖角色与主客机组合：Karen 主机＋Karen 客户端、Karen 主机＋原版角色客户端、原版角色主机＋Karen 客户端，并用原版角色主机＋原版角色客户端作对照。双方轮流出牌、结束回合，检查闪耀、约定牌堆、额外重播、全局移牌等相关效果是否同步；涉及存档或断线处理的改动还要测试恢复流程。
- 测试记录逐项写明通过、失败或未测及原因；仅有编译/启动检查时明确说明尚未完成单人或多人实测。若专门调查跨版本联机，则单独记录结果，不计入日常兼容性验收。
- 现有 `run_as_host.bat`、`run_as_client01.bat` 仍指向 Steam 安装目录。测试上述矩阵时先为主机和客户端准备隔离进程与独立用户数据，不要直接用这两个旧脚本替代双版本多人验证。
- 需要全面测试时，批量复测使用 `tools/test_dev_matrix.ps1 -Suite full -UpdateMcp`；它从本地当前 MCP 源码构建两版 DLL，并生成逐项结果及两端日志。`full` 只包含双版本单人和各自的同版本多人测试；套件说明见 `docs/testing/mcp-batch-matrix.md`。

## 特效专项与发布产物校验（按需执行）

- 新增特效时，必须受现有特效等级配置控制。
- **特效覆盖、生命周期、首次/重复播放及帧耗时专项已移出默认开发和创意工坊发布流程，仅在用户明确要求时执行。** 已有工具和历史证据保留；未执行的项目记录为未测。
- 新增/修改华恋特效资源时，维护资源及首次使用准备方式登记，并按改动需要运行静态清单检查 `python tools/vfx/first-use/catalog.py --check`。普通文案、版本和其他无关改动沿用最小必要验证。
- 共享纹理、Shader、音频和生成纹理应在原生加载阶段准备；几何/alpha 轮廓应提前缓存。GPU 预热必须产生真实绘制并等待渲染完成，不能把 `ResourceLoader.Load`、隐藏节点或编译成功当作 Shader 已预热的证据。预热不得执行规则、消耗随机数序列、移牌、给予收益或播放可听声音，完成及异常路径都必须释放临时视口/节点。
- 用户明确要求特效专项时，冷进程分别测正式版和测试版，记录准备耗时、渲染完成、首帧/重复帧、超过 33.3ms 的帧、资源漏载、可见性和退出清理；逐项解释或修复长帧。检查普通/临界闪耀、耗尽、普通/空虚约定移牌、Shader/粒子、常驻叠加、选牌及影片资源。保存并查看 MCP 移牌前后截图，动画结束执行 `karen_check_hand`，检查模型和可见节点一致。
- 创意工坊发布核对最终构建、版本、文案、依赖、包内文件及哈希，并完成与改动相关的必要运行检查；完整矩阵和特效专项按用户要求执行。APK 发布额外核查最终签名 APK 的签名/对齐、完整解包及文件哈希、内嵌 Mod 与已测包一致、测试插件未混入；用对应 Android ARM64 真机检查首次安装、覆盖安装保留存档、冷启动与切后台恢复。需要真机特效帧耗时结论时，在对应设备实测。
- 校验必须针对最终源码/资源与最终产物，记录版本、包哈希、逐项通过/失败/未测及原因。缺设备或失败项时明确标为待真机验证的测试候选包，不得宣称正式发布校验通过。日常修改仍只执行相关定向验证，不自动上传工坊或扩大为完整联机矩阵。

- 需要完整特效发布校验时，执行 `python tools/vfx/first-use/release_check.py --source-dev <dev-game> --target apk --apk <最终签名APK>`；工坊使用 `--target workshop` 并提供真实完整矩阵摘要。该入口会补跑专项，按用户明确要求调用。工具说明及证据格式见 `tools/vfx/first-use/README.md`。

## Git 提交隐私

- 文档和证据清单使用仓库相对路径；仓库外资源以用途占位符表示，个人目录由运行时环境解析。
- 本地凭证、签名材料、日志、存档和抓包保存在已忽略目录中；保留进 Git 的证据先做脱敏。
- 提交前运行 `python tools/check_git_privacy.py --staged`，检查完整暂存索引、压缩包内文本及即将使用的提交邮箱；输出仅包含位置和风险类型。
- 该检查覆盖当前文件和提交身份；历史提交与图片、视频、音频的可见或可听内容分别复核。
