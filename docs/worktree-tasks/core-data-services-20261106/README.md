# Core 数据采集、服务端与 Web

## Original user input

> 用户明确委托按现有方案真正实现 Core，并创建多个对话分工，要求每个对话使用 skill $worktree-task，尽量一次性完成、减少交互。
>
> 你的任务是02 数据采集、服务端与Web。使用slug core-data-services-20261106，在Core规范路径创建独立worktree。
>
> 拥有范围为 src/Modules/Telemetry、src/Modules/Exports、services/telemetry、web/telemetry、deploy/telemetry、数据导出与资料构建工具，以及这些模块独立的资源/本地化子目录。顶层工程文件、公共Api/Runtime/config由01负责。
>
> 实现内容包括从Karen现用工程迁入RunHistoryUploadPatch/Queue、KarenErrorReporter/Logger/ClientErrorBuffer、完整对局与报错Python/SQLite服务、原始归档与既有Web页、部署与资源构建工具、卡牌JSON/PNG/HTML导出。自动识别原生和Mod角色、模型归属及版本。两个上传默认true，地址按Karen现行发布配置提供，由Core独立配置控制。保留既有端点路径、旧客户端协议、数据身份、队列限制、去重和重试语义，扩展来源/Core/游戏版本字段。
>
> 字段版本升级、原始数据归档、报错聚合、SQL迁移、页面过滤与转义、凭据及发布配置的处理按实现文档完成。生产配置和数据用于确认格式，验证使用本地实例与隔离数据，不进行线上切换或远端发布。已有服务端测试选择受改动影响的最小子集执行。卡面渲染走作者导出/资料构建，玩家启动只做轻量发现。
>
> 完成后提供可运行的完整提交、构建/本地服务验证结果、与01/03的接线约定、任务文档。原分支合并与清理由总装集中列出各动作请求，当前阶段保留工作树与任务分支。

源角色工程以 <karen-current-source> 表示，只读。共同规范为 README.md、docs/IMPLEMENTATION.md 和 docs/worktree-tasks/core-integration-20261106/README.md。

### Relevant follow-ups

> 03 角色接入与总装已创建，负责Karen/Junna源码修改、接收最终提交、定向验收及集中合并/清理确认。当前只有三个实现对话，不再拆子会话。
>
> 03 已接手角色接入与总装。Karen旧Telemetry/Exports/服务Web退出由我负责，请交接角色所需生命周期/注册入口、CoreSettings配置键及接收验收入口，最终提供可复验commit。游戏MCP实测由我集中协调。
>
> 配置 CoreApi.Configuration.Current，Changed(old,new)，EnableDataUpload/EnableErrorUpload/TelemetryBaseUrl。ICoreModule.Initialize(CoreContext)，Order/Id 排序，context.ModelsReady/ShuttingDown、Services、Diagnostics、Discovery。
>
> 01提供耗尽档案与历史注入入口 ShoujoKageki.Core.Runtime.Modules.Shine.ShineHistory.InjectIntoHistoryJson(string json) 返回 string，字段 core_shine_exhaust_cards 兼容读取旧 karen_shine_exhaust_cards。Telemetry历史钩子请调用它。Runtime.csproj EmbeddedResource Include=$(TelemetryEndpointPath) LogicalName=CoreTelemetryEndpoint.json，打包时可传本机现有 endpoint，只读不提交。

## Brief task plan

1. 捕获集成检出与 Git 状态，建立隔离工作树。
2. 迁入完整服务、页面、归档、部署与资料工具，保留兼容协议并增加 Core 来源元数据。
3. 按 contracts/runtime-v1.md 接入上传、错误采集和作者导出模块。
4. 使用隔离数据执行受影响的定向服务测试及必要构建，脱敏审查后提交。

## Result and changes

- 原检出为 ../worktrees/STS2_ShoujoKagekiAijoCore/core-integration-20261106，原分支 task/core-integration-20261106，原 HEAD 96103f47ff10699ce3476f48f46382940c04305c。
- 原检出工作区干净，无进行中的 Git 操作。
- 任务分支 task/core-data-services-20261106，工作树 ../worktrees/STS2_ShoujoKagekiAijoCore/core-data-services-20261106。

### 实际实现

- src/Modules/Telemetry 按 ICoreModule Order 100 初始化，使用 Core 自身配置、默认端点资源和真实原生历史钩子，调用 01 的 ShineHistory 注入完整耗尽快照。角色无需注册公共采集入口。
- 迁入持久队列、错误 Logger、托管异常采集、有界清理和聚合。按端点隔离、限额、去重、有限重试及 Retry-After 保留。对局 409 完成投递，报错 409 进入 rejected 诊断记录。旧 Karen pending/errors 文件原子迁入 Core 独立目录。退出卸载 Logger 与定时器并停止新请求。
- services/telemetry 保存完整 Python/SQLite 接收服务、旧会话兼容、原始归档、备份与派生事实升级。新增 core_telemetry v2，上下文独立记录 Core/API/游戏版本和实际模型来源，旧 mod_version 继续保留 Karen 含义。事实格式升级到 3，重建前备份并保留原始身份、载荷及接收时间。
- web/telemetry 保存全部既有页面与 721 项静态资源，增加 Core 版本、游戏目标与 Mod 来源过滤。动态字段转义、CSV 公式防护及读取鉴权保持生效。
- src/Modules/Exports 按 Order 200 接入作者命令 core exportcards/exportjson。覆盖已发现角色与牌池、完整模型 ID、来源及版本，完整导出生成基础/升级 PNG、引用卡牌、JSON 和转义 HTML。启动阶段运行轻量发现，作者命令执行渲染。
- tools/telemetry 提供独立 PCK 读取、原生资料提取和作者资料构建。deploy/telemetry 组装服务发行目录，沿用既有部署身份、端点映射、读取控制、备份和监控，真实 HTTPS origin 首次注入并从既有配置恢复。

### 接线与兼容决定

- 01 提供 contracts/runtime-v1.md、Api.V1、Runtime、ICoreModule 自动发现、设置、诊断、模型发现及 ShineHistory。顶层工程嵌入默认端点配置。02 的生产代码直接调用真实历史接口。
- 03 已收到退出旧 Karen 遥测、历史和导出入口的交接，角色包携带 Core 游戏端，服务源码与 Web 在 Core 维护。旧发行端点文件保留为本机只读发布输入。
- 原生 Category.Entry 保持既有 Entry 统计键，完整身份保存于 model_full_id、model_sources 及资料 full_id。额外命名空间的完整卡牌 ID 独立统计，避免后缀混同。对局去重身份继续使用原平台、种子、开始时间及玩家集合。
- 原生模型缺省 ModVersion 取游戏版本，其他缺省版本使用 unknown。报错保留完整现有 Mod 清单和独立 Core 上下文。

### 验证

- 8 项旧服务定向测试通过，覆盖重复/冲突、归档、事务重建、旧会话、鉴权、错误幂等、环境及页面搜索/分页。
- test_core_protocol.py 的 5 项新增测试通过，覆盖新格式、旧身份、原始字节、版本隔离、字段限额、数据库重开及随机端口 HTTP 页面/上传/过滤。
- 上传队列 6 组回归、报错缓冲/队列 8 组回归通过，包括报错 409 保留。Web 数据一致性 9 组检查通过，两个页面脚本与全部 Python 入口语法检查通过。
- 作者资料构建使用隔离目录验证模型完整身份、来源、缩略图及路径包含约束。读取正式版真实 PCK 并提取一张原生卡图成功，图像摘要校验通过。
- deploy/telemetry/build_release.py 已组装本地可部署目录，服务健康、页面及 v2 请求在回环随机端口与临时 SQLite 中通过。
- Telemetry/Exports 使用 Godot.NET.Sdk/4.5.1、01 实际 Api.V1/Runtime.Modules 与两版真实游戏引用完成独立链接编译，正式版 0.107.1、测试版 0.111.0 均为 0 警告、0 错误。该独立编译以已读取的 ShineHistory 精确公共签名作为临时编译接缝，接缝仅位于忽略的 tools/telemetry/.local，完整 Runtime 构建在总装时链接 01 的真实 ShineHistory/Archive/Service。
- 提交前以公开 noreply 提交身份执行完整暂存索引隐私检查，覆盖 771 个文件，零项发现。所有跟踪文档已复核本机绝对路径与敏感文本。

### 交付状态

- 实际游戏中的 Logger 回调、历史钩子和卡面 PNG 渲染由 03 按集中 MCP 实测安排验证。此次完成的验证范围为上述定向检查。
- 完整 Runtime 总装、游戏实测及最终随包发行由 03 接续。工作树、任务分支和本地验证目录保留，原分支合并与清理由总装集中请求明确确认。
- 可复验入口见 services/telemetry/README.md 与 deploy/telemetry/README.md。

- 最终组装目录已通过独立加载检查，使用包内 web 并在临时 SQLite 与随机端口成功提供健康、页面和查询端点。
- 提交前只读核对原集成分支仍为 96103f4，merge-base 与该 HEAD 相同，原检出保持干净。自动审批拒绝了包含分支同步合并的组合命令，理由为需要总装明确合并确认，该命令未执行。后续检查与提交单独进行，分支合并由 03 集中请求确认。
