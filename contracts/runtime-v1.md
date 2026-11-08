# Core V1 协作契约

程序集 ShoujoKagekiAijoCore.Api.V1，命名空间 ShoujoKageki.Core.V1，AssemblyVersion 固定 1.0.0.0。API 源码在 src/Api.V1，模块运行契约在 src/Runtime/Modules.cs。

## 工程与模块

src/Runtime/ShoujoKagekiAijoCore.Runtime.csproj 自动编入 src/Modules/**/*.cs、src/Patches/**/*.cs 和 src/Compatibility/**/*.cs。模块只新增自身子目录。Telemetry 与 Exports 由数据任务拥有，其余游戏客户端由客户端任务拥有。

无参具体类实现 ShoujoKageki.Core.Runtime.ICoreModule，提供 Id、Order、Initialize(CoreContext)。按 Order、Id 排序单次初始化。配置服务在所有模块前建立，Shine 为 -500，表现与公共补丁为 0，Telemetry 为 100，Exports 为 200。模块返回时只完成挂钩，依赖原生模型的工作订阅 context.ModelsReady。退出订阅 context.ShuttingDown，模块亦可实现 IDisposable。

context.Services.Add<T>(instance) 注册唯一服务，Get<T>() 获取。配置、发现与诊断由 Runtime 在模块启动前注册。context 提供 ReleaseVersion、GameVersion、DataDirectory、ResourceRoot、Configuration、Diagnostics、Discovery。

## 配置与诊断

CoreApi.Configuration.Current 或 context.Configuration.Current 返回 CoreSettings 不可变快照。Update(settings with { ... }) 持久化后发 Changed(old,new)，Reset 恢复 Core 默认。配置只读取 Core 自身文件并升级 FormatVersion，不读取角色旧配置。

CoreSettings 提供 EnableInformationCollection 总开关与 EnableDataUpload、EnableErrorUpload 子开关，均默认 true。总开关关闭时暂停上传和报错采集，保留子开关值。TelemetryBaseUrl 保持不变。地址为空时由 Telemetry 使用随发行携带的默认接收地址，数据任务拥有该默认地址来源。音乐、衍生牌标记、闪耀表现和补丁配置在 src/Api.V1/Configuration.cs。DerivedCardLabel 使用 Always／ShoujoOnly／Hidden，默认 ShoujoOnly；旧 ShowDerivedCardLabel 属性保留。ShinePileButton 的 Auto 保存值对应仅少歌角色显示。EnableShineEffects 默认开启，统一控制闪耀常驻光效、临界提示和耗尽动画；旧 EnableShineDepletionAnimation 属性保留为同一开关的兼容别名，读取配置时显式新字段优先；VfxLevel 统一供 Core、Karen 与 Junna 使用。BookOfFiveRingsPatch 与 LuckyFyshPatch 各自使用三档 CombatCardGainRelicMode，默认 ShoujoOnly；旧统一属性保留，设置时同时更新两项，新分项可分别覆盖。

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

## 1.1 公共处理入口

API 程序集版本继续固定为 1.0.0.0，发行能力通过 core.bundle 的 apiVersion=1.1.0 和 features 声明；已有接口保持原签名，新能力通过独立接口注册。

- IModelExecutionScopes.Register 在真实模型异步方法入口建立作用域。作用域在同步调用返回时恢复调用者，方法自己的异步延续保留捕获的上下文。原生出牌计次共用这一次模型扫描与挂钩。
- ICardPlayRouting.Register 提供出牌中间件和可选结果牌区。中间件用 next(play) 继续后续处理。原生和 RitsuLib 出牌入口由 Core 统一适配，保留第三方前后回调；挂钩在 ModelsReady 时安装。
- ICardMovementPipeline.Register 按注册顺序等待原生移牌完成后的异步处理；CardMovement.Source 保留原生来源。ICardMovementEvents 的已有同步通知继续保留。
- CardCopies.Preview 返回不登记到战斗的独立预览；ForDeck、ForCombat 明确创建作用域。Acquire 通过原生 CardPileCmd 获得永久牌及可选手牌实例，保留成长、升级、附魔和复制用途规定的闪耀状态，并回收未加入牌区的失败实例。
- Presentation.CoreEffects 统一特效等级、粒子数量与重绘节奏；CanvasGeometryBatch、LightRibbon、CoreResources 为共享绘制和资源加载入口。IPileViewEffects 和 IShinePileView 提供公共牌堆表现。
- ITemporaryMusicPlayback.AcquireTrack(owner,path,loop) 以可释放租约接管音乐；嵌套租约、乱序释放和更高优先级暂停都保留底层曲目、位置与暂停状态。角色退出时释放租约。

玩家状态通过 BaseLib ExtendedSaveTypes 注册 ShoujoKagekiAijoCore.player-state@1，文件与网络共用保存载体。档案卡使用原生 SerializableCard 的 JSON 类型信息，保留 BaseLib 扩展字段。旧 extra_fields 仅用于读取迁移，并在读取时转为注册载体，支持直接规范化和网络传输。新旧数据并存时新载体优先。网络读取核对 Core 指纹，未知规则拒绝恢复；发行 saveProtocol 和 networkProtocol 均为 2。

SDK 与引导均执行 src/Bootstrap/Bundle.cs；发行将同一源码放入 sdk/BundleValidation.cs，严格校验额外拒绝未列出文件和文件系统链接。角色打包统一调用 SDK，按实际使用声明新增能力。

## 1.2 快照与选择

发行 apiVersion 为 1.2.0，公开程序集 AssemblyVersion 仍为 1.0.0.0。ICardCopyService 与已有接口的成员保持不变；新服务通过 ICardSnapshots 和 ICardSelection 注册。

- CardCopies.Snapshot<T>(source) 经 ICardSnapshots.Capture 返回脱离牌区的独立原模型，保留运行时字段、费用、转换形态、投入及闪耀来源语义。快照不登记到战斗，也不因零余量替换为空壳；调用者将其作为数据读取，不移入实际牌区。
- Preview、ForDeck、ForCombat 与 Shine.Copy 仍按 CardCopyPurpose 创建实际复制结果。耗尽后的普通复制和转交生成空壳；Reacquire 通过原生 SerializableCard 恢复永久属性并建立满余量新生命周期。
- ForCombat 使用原生克隆保留本场状态。同场实际牌的 TemporaryCopy 保留原生 CloneOf，预算独立、DeckVersion 为空。其他战斗复制在原生登记完成后设置显式 permanentSource；该来源必须属于接收玩家的永久牌组。跨玩家转交的所有权差异集中在双版本适配器处理。
- CardSelection.Choose(context,cards,player,canSkip=false,title=null) 经原生 FromChooseACardScreen 处理 1 至 3 个选项，保留联机同步与第三方原生挂钩。空列表返回 null；必选收到 null 或返回未提供的对象时抛出异常。标题按调用作用域恢复，选择结束或异常时平衡已打开的选择上下文。

使用快照和选择分别声明 card-snapshots@1 与 card-selection@1。角色包同时要求 minCoreVersion=1.2.0。
