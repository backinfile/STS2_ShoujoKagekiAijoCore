# Core Mod 列表顺序修正

## Original user input

### 工作区指导

# AGENTS.md instructions for <junna-repository>

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

# 项目概况

- 《杀戮尖塔 2》星见纯那角色 Mod，ID：`ShoujoKagekiJunna`；核心机制为飞矢、知识与伤害加成。
- 技术栈：C# / .NET 9、Godot 4.5.1（MegaDot）、BaseLib 3.4.8
- `src/Combat/` 按飞矢、卡牌、伤害、知识分组，`src/UI/` 存放 HUD、卡面与动画，`src/Models/` 定义游戏模型。目录调整沿用已有命名空间及 Godot UID，保持类型引用兼容。
- `data/card-pool.json` 为编译时嵌入的运行卡池，`ShoujoKagekiJunna/` 为游戏资源，`docs/design/` 为工作台，`docs/implementation/` 为实现记录，`tests/Integration/` 为独立测试模组。公共构建配置在 `Directory.Build.props`。
- 构建：`./build.ps1 -CorePackage <Core发行目录>`；产物为 `dist/ShoujoKagekiJunna/`，运行时依赖独立 Core 与 BaseLib 工坊条目。`tools/prepare-workshop.ps1 -CoreWorkshopId <Core工坊ID> -PreviewImage <预览PNG> -OutputDirectory <新目录>` 生成仅含 Junna 的工坊工作区及必需物品配置。

# 卡牌效果实现

- 实现卡牌效果时，优先查找游戏本体中的类似效果，参考其实现。

# 卡牌入堆位置

- 生成或复制到抽牌堆时必须显式指定位置。未明确置顶或置底的生成、复制及知识附带发牌使用 `CardPilePosition.Random`，复用原生生成事件与同步随机数。定向抽取先置顶再走正常抽牌，永久牌组发放按牌组规则处理。修改时按[卡牌入堆位置约定](docs/implementation/card-pile-placement.md)核对位置、数量、版本、原牌序及实际入口，保留同类回归结果。

# 工作台编辑

少女歌剧工作台的前端、MCP 服务和部署统一由 [Core 仓库](https://github.com/backinfile/STS2_ShoujoKagekiAijoCore/blob/main/tools/workbench/README.md) 维护，本仓库的旧工作台源码作为历史基线，不再用于更新部署。工作台内容使用 Core 的 `shoujo_workbench` 并指定 `modId: "junna"` 读取和修改；旧 `junna_workbench` 连接兼容纯那默认工作区。具体流程见 [docs/design/AGENTS.md](docs/design/AGENTS.md)。

- 主台部署在 `vipcloud`，由 `junna-workbench.service` 运行。程序入口为 `<workbench-service-directory>`，主台数据为 `<workbench-state-database>`，内部连接配置为 `<workbench-private-backend-config>`。图片保存在 `<workbench-assets-directory>/`，通过 HTTP 哈希地址读取；历史和请求回执与条目一起由 SQLite 事务持久化。`state.json` 为迁移前备份。
- 公开只读页为 `https://junna.17181718.xyz/`，编辑页与 MCP 使用该域名下的 HTTPS 长路径，未设置 IP 白名单。服务器网关与 MCP 分别监听 `127.0.0.1:54828`、`127.0.0.1:54829`，HTTPS 路由位于 `<workbench-nginx-config>`。
- 当前长路径及密钥读取主目录 `docs/design/workbench/local-save.config.json` 的 `remoteURL`、`remoteToken`。编辑页地址为 `remoteURL + '/'`，MCP 接口为 `remoteURL + '/mcp'`。私有连接配置保存在 Git 忽略范围内。其他工作树复用主目录客户端及其连接配置。
- 当前会话尚未加载新工具时，使用 `node docs/design/workbench/mcp-client.cjs <工具名> <参数JSON文件>` 连接同一个 MCP 服务。入口轮换后，已启动的 STDIO 客户端需要重新连接，命令行客户端会读取最新配置。源码修改使用普通文件工具，工作台内容由云端 MCP 持久化。部署流程以 [Core 部署说明](https://github.com/backinfile/STS2_ShoujoKagekiAijoCore/blob/main/tools/workbench/deploy/README.md) 为准。
- 网页首次加载 `/snapshot.json`，之后通过 MCP 按 revision 同步增量。公开快照由独立发布进程更新；完整图片与历史在导出时读取。迁移后继续复用原有条目版本、assetId 和 requestId。

# 契合设计

以下精选自工作台的长期约定。设计前通过 MCP 读取 `styles` 中对应规则、已确认用词及相关卡牌的当前内容。

- 以当前牌效选择卡名、契合和图片，核对名称、类型、效果、体系主题、图片、组内关系与来源；契合调整采用最小改动，保留当前机制、数值及用户正在调整的牌面。
- 飞矢可用物理箭、言叶或星：分别表现瞄准与命中、话语的传递与抵达、追寻与飞行轨迹。按实际子类型选图，攻击飞矢体现主动攻击，准备与辅助飞矢体现准备、等待及完成后的收益。
- 伤害加成的通用契合是纯那的日常锻炼，围绕体能、基本功、射术、反复练习与训练成果；带伤害的训练牌同时体现主动出手。
- 知识契合由学习行为或有明确内容与来源的知识承担。典籍先确认纯那在原作或角色访谈中阅读、引用、演绎的直接联系，再选择内容；人物档案围绕对应人物的身份、经历与专长。同组主牌与关联牌围绕同一知识关系，可呈现释义、例证、推导、应用与成果。
- 能量的通用契合使用星星，以点亮、汇聚、留存和延续对应收益；飞矢中的星突出轨迹与抵达。有直接原作或知识来源的牌可沿用原名，以具体内容衔接收益。跨体系牌以主体系契合为主。
- 卡名优先选对应场景中已核对的连续原词，保持原意；缺少合适原词时选自然、完整的常用概念、动作或物件名。普通卡名通常7字以内，保留原句优先于机械限字；核对常见谐音及用户已否定的名称。原文、译名、概括名和设计解释分别记录。不要使用自造词。
- 卡名、契合与卡图共同体现牌的用途：攻击突出出手、弹道、命中或进攻性的论辩与言叶运用；防御突出抵挡、保护与应对；准备和积累采用相应动作与物件。配图优先用原作画面，在实际卡框与飞矢遮罩中检查脸、手、武器和动作方向，清理字幕、水印及录制界面干扰。不同牌采用有区分的构图，相邻帧或同一动作的换裁切也纳入重复检查。
- 改名、换图或重选契合后，同步基础与升级文本的牌名引用、关联牌、知识组标题、契合池、出处及提示，并复查相关卡面、引用跳转、重名和重复图。复用备用或退役素材时，按新牌当前效果更新解释与关联，保留素材出处。

# 图片素材处理

- 查找原作卡图时，优先在 [Xenodude Scribbles](https://xenodude.com/) 检索对应作品与话数，查看无字幕原帧并读取原尺寸图片。已确认 TV 第9话拥抱镜头来源：[第9话截图与评论](https://xenodude.com/2018/09/09/revue-starlight-ep-9-original/)。

所有生成的图片在交付前都必须使用本地去 AI 摩尔纹工具处理一遍，去除摩尔纹与生成伪影。优先复用项目已安装的 Artifact Cleaner，按原尺寸对比处理前后，检查眼镜、发丝、弓弦和服装边缘；保留生成原图，并记录本地处理工具、参数与成品路径。

# STS2 MCP 连接与使用
使用 MCP 操作 STS2 时，需要读取其mcp readme文档。默认使用最小化+静音来启动游戏进行测试, 需要选择一个随机端口号避免和其他任务冲突。

不测试教程，解锁等内容时，直接以满解锁（unlock all）状态启动游戏即可。

使用mcp进行任务时，需要录制视频给用户看效果， 录制前准备好环境，录制中尽量不要调用cmd等其他直接修改的操作了，应使用游戏合法操作继续进行。

</INSTRUCTIONS>

### 问题

co我朋友的电脑上core为啥在baselib之前了？怎么调整

### 修正要求

你来修正

### 合并与清理确认

问题：Core 排序修复已提交并通过验证。是否合并到 main，并在保留录像、报告和测试包后清理本次工作树、分支及隔离游戏临时文件？

回答：合并并清理（保留验证产物）

## Brief task plan

- 原始检出：Core main，HEAD 462bb35bbb3821f983ca29b55d8b255f773b22d6；工作区干净。
- 在 ../worktrees/STS2_ShoujoKagekiAijoCore/core-mod-list-order-20261117 隔离修改，遵循 $worktree-task。
- 先用隔离真实游戏确认旧包将 Core 写到 mod_list 首位，再删除初始化中的全局置顶操作。
- 保留原生依赖排序、逻辑 Core 身份与重复副本禁用；构建引导并验证单副本与重复副本的显示和持久化顺序。
- 仅运行相关引导与启动验证，记录录像及包摘要；完成后按约定确认合并与清理。
- 此问题已在前一轮定位到直接修改列表的四行代码，使用针对性复现确认原因，不再重复广泛假设排查。

## Result and changes

- 修改 src/Bootstrap/Bootstrap.cs：删除初始化时将逻辑 Core 插入整个 Mod 列表首位的四行代码，沿用本体排序和已有重复 Core 禁用逻辑。
- 真实游戏 v0.113.1、BaseLib 3.4.8、Core 1.9.6：旧发行包在同一探针下 passed=false，运行列表、原生 Mod 页面及已保存设置均为 Core → Helper → BaseLib；修正后单副本和双副本均 passed=true，三处顺序一致且为 Helper → BaseLib → Core → Junna。
- 两种修正场景均从 Core 在首位的旧设置启动，本体自动修正并持久化。双副本日志确认 2 个完整候选，仅 1 个 Loaded Core、另 1 个 DisabledDuplicate，Junna 正常加载。
- 引导项目使用实际游戏引用编译通过，0 警告、0 错误；tests/BootstrapChecks 原有 8 项通过。检查项目的 4 个未使用桩参数警告来自现有 Stubs.cs。测试完整包 Strict 校验通过。
- 测试引导 SHA256：a84d2576bf92986916c6d7af73b78dd7ded9c6835b6e282a5030e3d8c86676b6。包沿用原始已验证 1.9.6 Runtime、API 和资源，仅替换引导并更新 core.bundle 对应摘要。
- 证据位于 out/tmp/core-mod-list-order-20261117/：临时真实游戏探针、start-case.ps1、baseline/、fixed-single/、fixed-duplicate/ 和 package/。单副本录像 15 秒、多副本录像 10 秒，均完成封装，engine_mix 有声录制、丢帧 0；已检查截图与多副本 6 帧拼接图。
- 使用独立用户目录、现有满解锁进度副本、随机端口、最小化与 silent_test；录像中通过正常设置页按钮打开 Mod 页。全部本任务游戏进程已停止。
- 主分支在验证期间推进至 3e4945553b0b35b3a7218501be67491dcd4dc24b，新增内容为遥测语言采集，未改动引导或相关检查；提交后同步到任务分支，保留该变更。
- 用户已确认合并 main 并清理，同时保留验证产物。将完整测试包、录像、报告及复现脚本保留至 artifacts/task-deliverables/core-mod-list-order-20261117/，核对逐文件哈希后执行本次工作树和分支清理；最终结果写入交付目录 integration-result.json。