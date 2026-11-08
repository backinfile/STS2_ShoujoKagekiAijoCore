# Core V1 协作契约

程序集 ShoujoKagekiAijoCore.Api.V1，命名空间 ShoujoKageki.Core.V1，AssemblyVersion 固定 1.0.0.0。API 源码在 src/Api.V1，模块运行契约在 src/Runtime/Modules.cs。

## 工程与模块

src/Runtime/ShoujoKagekiAijoCore.Runtime.csproj 自动编入 src/Modules/**/*.cs、src/Patches/**/*.cs 和 src/Compatibility/**/*.cs。模块只新增自身子目录。Telemetry 与 Exports 由数据任务拥有，其余游戏客户端由客户端任务拥有。

无参具体类实现 ShoujoKageki.Core.Runtime.ICoreModule，提供 Id、Order、Initialize(CoreContext)。按 Order、Id 排序单次初始化。配置服务在所有模块前建立，Shine 为 -500，表现与公共补丁为 0，Telemetry 为 100，Exports 为 200。模块返回时只完成挂钩，依赖原生模型的工作订阅 context.ModelsReady。退出订阅 context.ShuttingDown，模块亦可实现 IDisposable。

context.Services.Add<T>(instance) 注册唯一服务，Get<T>() 获取。配置、发现与诊断由 Runtime 在模块启动前注册。context 提供 ReleaseVersion、GameVersion、DataDirectory、ResourceRoot、Configuration、Diagnostics、Discovery。

## 配置与诊断

CoreApi.Configuration.Current 或 context.Configuration.Current 返回 CoreSettings 不可变快照。Update(settings with { ... }) 持久化后发 Changed(old,new)，Reset 恢复 Core 默认。配置只读取 Core 自身文件并升级 FormatVersion，不读取角色旧配置。

CoreSettings 提供 EnableInformationCollection 总开关与 EnableDataUpload、EnableErrorUpload 子开关，均默认 true。总开关关闭时暂停上传和报错采集，保留子开关值。TelemetryBaseUrl 保持不变。地址为空时由 Telemetry 使用随发行携带的默认接收地址，数据任务拥有该默认地址来源。音乐、衍生牌标记、闪耀表现和补丁配置在 src/Api.V1/Configuration.cs。DerivedCardLabel 使用 Always／ShoujoOnly／Hidden，默认 ShoujoOnly；旧 ShowDerivedCardLabel 属性保留。ShinePileButton 的 Auto 保存值对应仅少歌角色显示。EnableShineEffects 默认开启，统一控制闪耀常驻光效、临界提示和耗尽动画；旧 EnableShineDepletionAnimation 属性保留为同一开关的兼容别名，读取配置时显式新字段优先；VfxLevel 统一供 Core 与 Karen 使用。BookOfFiveRingsPatch 与 LuckyFyshPatch 各自使用三档 CombatCardGainRelicMode，默认 ShoujoOnly；旧统一属性保留，设置时同时更新两项，新分项可分别覆盖。

Diagnostics.Report(severity,source,code,message,error) 写日志并发布 Reported。Snapshot 返回只读诊断记录。Telemetry 可订阅 Reported 并消费原生日志，避免把自身发送错误再次入队。

## 发现与生命周期

Discovery.Mods 返回 Id、Version、Source、Loaded。Discovery.Models 返回完整 Id、Kind、ModId、ModVersion、ModelType，源自本体已经注册的模型和程序集归属。Refresh 重新生成快照并发布 ModelsChanged。ModelsReady 在本体模型数据库完成初始化后发布。启动时不运行卡面导出。

DataDirectory 是 Core 独立用户数据目录，可在其下建立模块队列。用户数据与日志进入 Git 忽略目录。

## 资源与本地化

唯一公共资源根为 res://ShoujoKagekiAijoCore/。仓库资源置 resources/ShoujoKagekiAijoCore/，语言表置该目录 localization/<language>/<table>.json。选中发行包的 PCK 在 Runtime 初始化前挂载。模块使用 CoreApi.ResourceRoot 或 context.ResourceRoot。业务和资源随同一 core.bundle 校验，资源不沿角色路径读取。

## 闪耀与实际结算

ShineCardAttribute(uses) 自动发现，旧角色适配由角色任务调用公开契约。Shine.GetState 返回只读快照。IShineDepletionHandler.OnShineDepleted 在合法批次收尾对永久来源发一次耗尽回调，临时复制使用自己的预算。

箭矢调度由 Junna 负责。每次实际牌效调用 await Shine.ResolveAsync(card, () => ExecuteActualEffect(), CardResolutionKind.Arrow)。布置和等待阶段不调用。整批外层使用 await using (Shine.BeginResolutionBatch())，批次结束统一耗尽。原生出牌与重放由 Core 挂钩同一执行器。嵌套执行同一张牌时避免重复扣次。

Shine.DepleteAfterCurrentResolution 仅请求合法结算点耗尽。Shine.Copy(card,purpose) 区分 TemporaryCopy、Transfer、Reacquire。已耗尽牌普通复制与转交返回 Core 空壳，档案重获建立新的生命周期。

公开签名以 src/Api.V1/*.cs 为准。扩展需要保持已公布签名，由客户端与角色总装协调。

## 已落地的增量契约

Shine.SetState(card, remaining, maximum, temporary=false)、ChangeMaximum、RestoreToMaximum 供真实永久牌效调整状态。Copy(Reacquire) 与 Reacquire(player,entry) 恢复完整 SerializableCard，重置临时、待耗尽和已耗尽标记。GetDepletedModels 返回独立预览实例。

IShineDepletionListener.OnShineDepleted(context) 可由遗物实现。Core 在真实永久来源耗尽时调用卡自身处理器与拥有者遗物监听器各一次。ChoiceContext 在回调时提供合法的模型栈，CombatState 保留实际结算状态。Shine.IsDepletionAnimating 查询公共耗尽表现。

IShineResolutionHandler.AfterShineResolution(context,play) 在原生 AfterCardPlayed 完成后调用。箭矢使用 ResolveAsync(card,context,play,resolution,Arrow) 重载提供实际 PlayIndex、PlayCount 与结果牌区，回调在实际牌效之后发出。RegisterDeferredResolutionPredicate 与 IsDeferredResolution 区分部署边界。Effect 不扣次数，纯部署与纯 Effect 不触发待耗尽收尾。

IMusicPlayback 通过 CoreApi.Get<IMusicPlayback>() 获取，提供 HasStream、State、ForceTrack(resPath)、SetFormPriority、AcquirePriority(owner)、Stop、PlayPause、Next、Previous 与 Changed。角色传自身完整 res:// 路径。配置修改通过唯一快照通知播放器与表现。

ICardMovementEvents 发布本体真实逐次移牌后的通知。CardReplays.GetNext/SetNext 提供下一次实际出牌预算，预览不消费，原生 AfterModifyingCardPlayCount 才清空。

core.requirements 可增加 characterFamily="ShoujoKageki"。SDK 对 ShoujoKageki 前缀的角色包自动生成该字段，三档遗物策略按拥有者角色的来源 Mod 判断。首批 Karen、Junna 保留明确身份兼容。

旧 Karen 局携带 karen_player_disposed_pile 时保存规则 karen-shine@legacy-1，保留每次原生出牌扣一次与普通消耗恢复语义。新局保存 core-shine@1。Core 原生计次基于 BeforeCardPlayed 完成后授予的许可与真实 OnPlay 入口，额外直接调用 OnPlay 不消费次数。

只读游戏命令 core_status 返回发行、游戏、API、网络指纹、来源快照、当前玩家规则及耗尽档案。SDK 入口与使用方式见 sdk/README.md。
