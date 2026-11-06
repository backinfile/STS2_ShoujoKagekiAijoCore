# Core 客户端与共享机制

## Original user input

> 用户明确委托按现有方案真正实现 Core，并创建多个对话分工，要求每个对话使用 skill $worktree-task，尽量一次性完成、减少交互。
>
> 你的任务为 01 基础工程、固定引导、稳定契约与配置。使用 slug core-foundation-20261106，从 Core 集成检出创建独立任务 worktree。
>
> 立即先确定并发布协作契约，包含模块初始化与自动发现、版本化 API、生命周期/日志/诊断入口、配置读取通知、资源与本地化目录、服务容器或已有等效入口、模块源码的编译包含方式。
>
> 完整实现固定 BootstrapProtocol=1、候选收集与共同兼容选择、完整业务/资源一致性校验、单次初始化、同 ID Core 收敛、本地及多工坊包混装、加载顺序与失败诊断，严格核对已安装游戏源码或程序集，不能用单目录加载冒充多包择版。stable/beta分别构建适配并提供 SDK 与可用随包目录。
>
> 用户已把分工收敛为3个对话，你就是唯一的01 Core客户端与共享机制会话，请立即按扩大后的职责继续，保留已创建的 task/core-foundation-20261106 与原始捕获信息。
>
> 01拥有所有Core游戏客户端基础与公共机制实现，包括固定引导/SDK/Runtime/双版本兼容、配置及自动发现、闪耀状态/实际结算协议/回调/档案/牌堆/空壳/复制重放移牌/旧存档兼容、共享特效绘制与音频、长颈鹿、衍生标记、音乐播放器及全部相关配置、src/Patches公共补丁与三档遗物限制、只读诊断。保持模块内聚并尽量复用Karen现有可用实现。
>
> 唯一除外的Core客户端目录为 src/Modules/Telemetry、src/Modules/Exports 及数据服务资源，交02负责。03负责Karen与Junna源码接入和总装验收。各角色源码此阶段只读，由03统一修改。
>
> 游戏实测由03集中协调，避免并发占用。当前阶段保留任务分支和工作树，合并与清理由03集中请求确认。

## Brief task plan

原检出为 ../worktrees/STS2_ShoujoKagekiAijoCore/core-integration-20261106，原分支 task/core-integration-20261106，原始 HEAD f6e054fdd6c7c38e425d9962a637117e3862fb8d。原检出干净，无活动 Git 操作。任务分支 task/core-foundation-20261106，任务工作树 ../worktrees/STS2_ShoujoKagekiAijoCore/core-foundation-20261106。

1. 先发布 contracts/runtime-v1.md 及 API、模块骨架。
2. 按两版实际游戏程序集实现多候选择版、归属与去重，构建 SDK 与资源包。
3. 实现统一配置、闪耀实际结算和归档、旧存档桥接，迁入音画、衍生标记、音乐与公共补丁。
4. 进行必要定向构建与协议验证，交接游戏验证入口，脱敏审查文档，合并最新本地集成分支形成可复验提交。

## Result and changes

已完成客户端实现与必要离线定向验证，提交交角色接入与总装任务继续组合构建及游戏验证。

- 固定 BootstrapProtocol=1，枚举本体已发现的本地、工坊及重复抑制 Core 条目，排除禁用与失败来源。选择前检查共同 API、功能、游戏与 BaseLib 范围，同版本内容冲突明确终止。
- 全量校验两版 Runtime、API、资源、引导器、清单及 SDK 摘要。相对路径越界与链接拒绝，程序集标识和 PCK 头检查。单次选择、单次初始化、有效版本同步、重复条目收敛与依赖查找优先级已接线。
- 本机两版程序集定向反编译已核对 ModManager、Mod、ReflectionHelper、ModelDb、CardModel 与 SerializableCard。正式版单程序集通过原生反射与子类型桥接，测试版使用原生多程序集归属。
- src/Api.V1 提供稳定程序集与公开契约，src/Runtime 自动包含所有模块及补丁。配置独立归属 Core，首次默认、格式升级、BaseLib 页面与快照通知齐备。CombatCardGainRelicMode 默认 ShoujoOnly，音乐设置默认折叠。
- 共享闪耀实现次数声明、真实结算入口、Effect 不计次、部署跳过、批次收尾、永久耗尽单次回调及遗物监听、完整快照档案、复制与空壳替代。异步作用域在同步入口分离，恢复已耗尽牌时创建新生命周期。
- 迁入旧 Karen 存档对象绑定、网络字段与历史 UI，读取 karen_player_disposed_pile 和旧闪耀 Props。旧 Karen 对局保存 karen-shine@legacy-1，保留每次原生出牌扣一次及普通消耗补满。新局采用 core-shine@1。
- 迁入闪耀常驻、警告、耗尽、牌堆到达与查看装饰，以及播放器、内置曲库、长颈鹿画面和衍生标记。音乐提供角色资源播放与优先权仲裁，配置即时刷新。
- 公共补丁统一拥有，包含五轮书、招财异鱼三档 Owner 策略，战斗边界使用 CombatManager.IsInProgress，以及放弃对局、手牌退出、Beta 洗牌顺序与消耗节点清理。只读命令 core_status 可检查当前状态。
- build.ps1 生成双目标完整发行目录，sdk/pack-character.ps1 验证并列随包、生成需求与摘要锁，支持原地与重复运行。sdk/README.md 记录使用方式。

### 定向验证

- stable 0.107.1 与 beta 0.111.0 的完整客户端构建均通过，最终构建零警告零错误，使用现有 BaseLib 3.4.7 与 Godot 4.5.1 环境。
- tests/FoundationChecks 通过 16 项择版、API/功能/目标/BaseLib 下限、同版本冲突、语义版本、路径越界与未选目标文件篡改检查。
- tools/verify_resources.gd 实际挂载 PCK 并读取长颈鹿纹理、空壳纹理、音频、曲库、本地化与脚本，检查通过。
- SDK 原地打包、重复运行、shine@1 需求与包含 core.bundle 的摘要锁检查通过。
- 总装任务首包实测确认重复 Core 进入一次 Runtime 初始化及两角色初始化，随后发现继承 OnPlay MethodInfo 的 Harmony 归属错误。已按 DeclaringType 与 DeclaredOnly 归一化，并避免重复注册。最终组合包的探针、真实旧存档及游戏画面由总装任务继续验证，结果写入其验收记录。

### 同步与保留

原集成检出保持 task/core-integration-20261106，原始捕获 HEAD 保留。其后追加的三对话分工更正 96103f47ff10699ce3476f48f46382940c04305c 将按工作树流程同步到任务分支。集成检出原本干净。任务分支与工作树保留，原分支合并及清理由总装集中请求明确确认。

### Relevant follow-ups

> 用户已把分工收敛为3个对话，你就是唯一的01 Core客户端与共享机制会话，请立即按扩大后的职责继续，保留已创建的 task/core-foundation-20261106 与原始捕获信息。
>
> 02负责 Telemetry/Exports 及服务Web，03负责两角色源码接入、总装和游戏实测。最终按 skill 同步集成分支追加的三对话分工更正提交。
>
> 闪耀箭矢在实际结算时扣次数，作为箭矢打出的其他闪耀牌相同。使用共享实际结算执行器和批次收尾，已删除 DeferDepletion。箭矢调度留在 Junna。
>
> Effect 明确单纯额外触发不扣。部署 wrapper 结束不能消费待耗尽请求，实际 Arrow 批次才收尾。实际结算后回调需要原生上下文与重放索引。
>
> 旧局没有 core_rules 时需明确 legacy Karen 规则快照并保留原每次出牌扣/普通消耗补满语义，不能默默当新局规则。
>
> CombatCardGainRelicPatch 以 CombatManager.Instance.IsInProgress 判断战斗，按遗物 Owner 读取规则和角色。游戏实测由总装协调，避免并发占用。
