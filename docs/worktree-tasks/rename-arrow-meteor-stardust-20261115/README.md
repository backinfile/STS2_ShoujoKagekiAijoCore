# 术语改为流星与星屑契合

## Original user input

### 消息 1：工作区约定与环境

# AGENTS.md instructions for <repository-root>

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

- 《杀戮尖塔 2》星见纯那角色 Mod，ID：`ShoujoKagekiJunna`；核心机制为箭矢、知识与伤害加成。
- 技术栈：C# / .NET 9、Godot 4.5.1（MegaDot）、BaseLib 3.4.8
- `src/Combat/` 按箭矢、卡牌、伤害、知识分组，`src/UI/` 存放 HUD、卡面与动画，`src/Models/` 定义游戏模型。目录调整沿用已有命名空间及 Godot UID，保持类型引用兼容。
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
- 箭矢可用物理箭、言叶或星：分别表现瞄准与命中、话语的传递与抵达、追寻与飞行轨迹。按实际子类型选图，攻击箭矢体现主动攻击，准备与辅助箭矢体现准备、等待及完成后的收益。
- 伤害加成的通用契合是纯那的日常锻炼，围绕体能、基本功、射术、反复练习与训练成果；带伤害的训练牌同时体现主动出手。
- 知识契合由学习行为或有明确内容与来源的知识承担。典籍先确认纯那在原作或角色访谈中阅读、引用、演绎的直接联系，再选择内容；人物档案围绕对应人物的身份、经历与专长。同组主牌与关联牌围绕同一知识关系，可呈现释义、例证、推导、应用与成果。
- 能量的通用契合使用星星，以点亮、汇聚、留存和延续对应收益；箭矢中的星突出轨迹与抵达。有直接原作或知识来源的牌可沿用原名，以具体内容衔接收益。跨体系牌以主体系契合为主。
- 卡名优先选对应场景中已核对的连续原词，保持原意；缺少合适原词时选自然、完整的常用概念、动作或物件名。普通卡名通常7字以内，保留原句优先于机械限字；核对常见谐音及用户已否定的名称。原文、译名、概括名和设计解释分别记录。不要使用自造词。
- 卡名、契合与卡图共同体现牌的用途：攻击突出出手、弹道、命中或进攻性的论辩与言叶运用；防御突出抵挡、保护与应对；准备和积累采用相应动作与物件。配图优先用原作画面，在实际卡框与箭矢遮罩中检查脸、手、武器和动作方向，清理字幕、水印及录制界面干扰。不同牌采用有区分的构图，相邻帧或同一动作的换裁切也纳入重复检查。
- 改名、换图或重选契合后，同步基础与升级文本的牌名引用、关联牌、知识组标题、契合池、出处及提示，并复查相关卡面、引用跳转、重名和重复图。复用备用或退役素材时，按新牌当前效果更新解释与关联，保留素材出处。

# 图片素材处理

所有生成的图片在交付前都必须使用本地去 AI 摩尔纹工具处理一遍，去除摩尔纹与生成伪影。优先复用项目已安装的 Artifact Cleaner，按原尺寸对比处理前后，检查眼镜、发丝、弓弦和服装边缘；保留生成原图，并记录本地处理工具、参数与成品路径。

# STS2 MCP 连接与使用
使用 MCP 操作 STS2 时，需要读取其mcp readme文档。默认使用最小化+静音来启动游戏进行测试, 需要选择一个随机端口号避免和其他任务冲突。

不测试教程，解锁等内容时，直接以满解锁（unlock all）状态启动游戏即可。

使用mcp进行任务时，需要录制视频给用户看效果， 录制前准备好环境，录制中尽量不要调用cmd等其他直接修改的操作了，应使用游戏合法操作继续进行。

</INSTRUCTIONS>
<environment_context>
  <cwd><repository-root></cwd>
  <shell>powershell</shell>
  <current_date>2026-11-15</current_date>
  <timezone>Asia/Shanghai</timezone>
  <filesystem><workspace_roots><root><repository-root></root><root><Karen-repository></root><root><card-research-repository></root><root><Nana-project></root><root><STS2-MCP-repository></root><root><Core-repository></root><root><visualizations-workspace></root></workspace_roots></filesystem>
</environment_context>

### 消息 2：任务

$worktree-task  将工作台和mod中所有“箭矢”修改为流星， 原先流星这张牌和相关引用改为“星屑”。 星屑这张牌你来重新找个契合

### 消息 3：技能正文

<skill>
<name>worktree-task</name>
<path><worktree-task-skill></path>
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
- For merge and cleanup confirmation, use `request_user_input_async` when available and permitted; otherwise ask in chat. Ask one short, direct question with the action choices. One explicit confirmation may approve both merging and the listed cleanup; the original task request alone is not approval.
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
6. Show the target branch, task commit, change summary, verification results, any original uncommitted state, and proposed cleanup items once; reuse this summary if already shown. Ask directly whether to merge and clean up, merge and keep, or defer.
7. Only after confirmation, merge the synchronized task branch back without creating an additional integration commit when possible. If integration would overwrite uncommitted state, preserve the worktree and report that integration is pending instead.
8. Verify the committed trees match, checks pass, the original branch contains the consolidated task commit, and pre-existing uncommitted state was preserved.
9. After successful integration verification, remove this run's clean worktree, task branch, and disposable temporary files covered by the cleanup approval, after proving needed content is preserved. Reuse combined approval from step 6 without asking again. If only merging was approved, retain these items until the user approves cleanup; request further confirmation for any additional cleanup items.

Report the resulting branch, commit, verification, cleanup state, and task document path.

</skill>

### 消息 4：重名处理

当前还有一张稀有牌已叫“星屑”（3费，等待3回合，造成8/12伤害并打出2张自身）。原“流星”改名后会重名，这张原有“星屑”如何处理？

由你为原有“星屑”另选名称与契合


### 消息 5：部署确认

工作台新发布包已构建，类型兼容与字段合并定向检查已通过。是否批准将其部署到现有 vipcloud 工作台服务器，并通过 MCP 原子更新 1041 个条目？部署会保留在线数据库、资产和现有连接配置，并备份旧发布入口。

批准部署并更新工作台

### 消息 6：合并与清理确认

是否将 Junna 和 Core 的上述任务提交合并回各自 main，并清理本次两个工作树、任务分支和临时文件（先保存发行包、录像及验证报告）？

合并并清理

## Brief task plan

1. 从 main 的 d5c7690571103b68eb5f29058164fb789562ca87 创建 codex/rename-arrow-meteor-stardust-20261115，工作树 ../worktrees/STS2_ShoujoKagekiJunna/rename-arrow-meteor-stardust-20261115。
2. 读取工作台当前条目、风格与版本，核对术语、原流星 JA81B、既有星屑 U_CARD_005、关联引用和契合素材。
3. 先区分牌名引用与原作用词，再统一修改工作台内容、Mod 本地化、运行卡池和必要的工具及类型判断。
4. 完成改动所需的定向验证，记录结果，提交单个任务提交并同步原分支。
5. 展示具体结果后，按 $worktree-task 确认合并和清理。


## Result and changes

已完成实现、部署与定向验证。用户已确认将两个任务提交合并到各自 main，并清理本次工作树和分支。交付物保留于Junna主目录artifacts/task-deliverables/rename-arrow-meteor-stardust-20261115/。

- 工作台程序在 Core 的 tools/workbench/ 维护。前端筛选、编辑器、卡框、统计、牌型与回合数判断统一使用“流星”，兼容旧序列化类型；稳定 ID、字段键、类型标识、类名与命名空间保持原样。
- 云端 MCP 在同一事务中提交1042个条目，工作台由r5297更新为r5298。在用、备用、删除区及机制、词条、知识组、流派、风格、契合池、Power与根元数据同步。公开数据与实际页面脚本中的旧术语“箭矢”均为0处。
- JA81B“流星”改为“星屑”；JA81、JA76、JA82、JZ05的基础／升级牌面、Power提示、结算说明和契合引用同步。JA81“化作流星的箭”的原作牌名保留。
- 星屑采用《99 ILLUSION!》的连续原词“星屑”，演唱为スタァライト九九組，作词为三浦香、月蝕會議；以先前攻击留下的碎光承接记录伤害、延迟抵达并消散。名称及配图出处分别保存。歌词来源：https://www.uta-net.com/song/256739/ ；官方发行：https://revuestarlight.com/music/99-illusion/ 。
- 原有U_CARD_005按用户确认另取名“宇宙之沙”，取《Star Divine》的连续原词“宇宙の砂”，演唱为スタァライト九九組，作词为中村彼方；契合星粒命中后分出两枚、等待后继续攻击。歌词来源：https://www.uta-net.com/song/236409/ 。
- 两张牌保留现有原作图片，已查看实际图片和游戏卡框；更新名称、契合池、原词、中文译名、出处及内部解释。两名在当前纯那牌池唯一，与Karen当前中文卡名无重名。
- 所有1138张云端卡牌共26762个保护字段核对通过；费用、数值、目标、等待、关联ID、资产ID和布置行为保留。Mod的112张运行牌保持原值，仅更新术语与相关牌名。工作台已有的JK37C措辞及JA53抽牌数量修改保留在云端，未并入本次Mod规则修改；运行卡池沿用原r5294来源标记。
- 服务器切换前备份SQLite和原发布入口，三个工作区的全部表内容摘要在程序切换前后一致。最终内容仅由MCP提交，版本、历史与回执按原事务机制保存。

验证：

- 工作台类型迁移2项通过；MCP快照、字段合并、版本、冲突及旧类型恢复的定向检查通过（组合运行13项）。
- 运行元数据校验通过：196条卡牌规则、31条Power规则及保存字段身份。正式版与测试版C#构建均为0警告、0错误，PCK与Core1.9.4 SDK打包成功。
- 游戏v0.113.1隔离实例、随机MCP端口、满解锁、最小化与静默内部录音，启动120次采样均未抢焦点。
- 实际悬停显示“星屑”、流星类型和关键词；JA81伤害后目标生命40→30，星屑在第3回合结算10点伤害使目标30→20，宇宙之沙在第4回合结算8点使目标20→12，并留下两枚3回合布置。
- 48.2秒演示仅使用悬停、正常出牌和结束回合；准备命令均在录制前执行。原始录像、截图、报告和发行包存于out/tmp/rename-arrow-meteor-stardust-20261115/及dist/；合并清理前保存到主目录artifacts/task-deliverables/rename-arrow-meteor-stardust-20261115/。
- 全部跟踪文档已检查本地路径、私人信息和凭据；命中的连接示例密钥为既有占位符。Core一份历史文档的服务器路径改为用途占位符。

原主目录状态：Junna在开始时只有docs/worktree-tasks/full-card-resolution-localization-20261105/selection-demo.mp4的未提交删除；Core开始时干净。合并前再次核对。
