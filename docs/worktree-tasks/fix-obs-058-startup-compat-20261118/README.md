# OBS-058 启动兼容

原检出 `main`，起点 `1ff0a73bd6a8b079f6279893644279b2bfe6aada`，工作区干净。任务工作树位于 `../worktrees/STS2_ShoujoKagekiAijoCore/fix-obs-058-startup-compat-20261118`。

## 用户要求

> 创建一个新对话来修改吧，记得修改的core更健壮一些，不要这么容易崩溃

修复后更新统一待修复文档、巡检策略与状态，保留人工备注和成功巡检基线。Karen、Core、Junna 均为自有 Mod。保留机器可读交付记录。

## 计划

1. 核对玩家日志和实际游戏 ABI，建立类型加载与选牌转发反馈。
2. 修复目标选择、启动可见性、关键初始化失败收尾和早期诊断。
3. 定向验证后提交并同步巡检，合并与清理按 `$worktree-task` 确认。

用户补充

> 你可以看看官方公告是怎么同步测试版到正式版的

Mega Crit 的 v0.111.1 正式版公告说明本次同步包含 v0.108.0–v0.111.0 的四轮 Beta 更新。v0.113.x 的愚者、极速和恐惧继续在 Beta 分支测试。[Steam 官方公告](https://steamcommunity.com/app/2868840/announcements/?l=schinese)

## 实际修改

- 旧 Stable DLL 在 Steam 官方 Windows v0.111.1（cc1f9462）上复现了 ScopedChoiceContext.get_OwnerId 的类型加载失败。该版本还要求带 Player 参数的选牌开始方法、CardLocation 和六参数批量移牌，PlayerChoiceContext 的 GameActionType 属性在 v0.113.x 才出现。
- 增加 Main Runtime，按实际选牌与归宿 ABI 筛选候选，再比较游戏版本基线和发行包。验证完整 API 与 Runtime 类型后再建立原生关联，反射桥接只发布完整初始化成功的类型。
- Runtime 在关键补丁安装和存档注册后绑定 Core API。发生错误时撤回 Runtime 和转发补丁、清理模块及订阅、移除程序集关联和桥接。通过原生 Mod 检测事件保留失败状态，让依赖角色停在初始化前。
- 遥测初始化可单独停用。引导把阶段、游戏版本、目标、Core 发行号及包指纹写入游戏本地日志。BaseLib 提示在主菜单可关闭。早期诊断沿用本地日志，无需网络或更改上传队列。
- 选牌作用域转发模型栈、OwnerId、适用版本的 GameActionType、选牌人和选项。逐次收尾，中途某次结束失败仍会继续处理其余已打开的选牌，并返回收尾错误。

## 验证

测试 Core 1.9.7，BaseLib 3.4.8。构建引用为 v0.107.1（59260271）、v0.111.1（cc1f9462）和 Beta v0.113.1（d09d48e5）。三版通过真实游戏启动并由 MCP 确认主菜单，旧正式版和 Beta 同时加载 Karen v1.0.0 与 Junna 0.1.0。v0.111.0（41cef1ea）另通过最终 Main DLL 的 418 项类型加载。Beta v0.113.0 的独立启动未执行。

v0.111.1 在关键存档注册处注入异常。最终探针确认 Core 和两个依赖角色为 Failed，CoreApi.IsReady 为 false，反射结果、原生程序集列表和 Harmony 补丁均无失败 Runtime 的残留。MCP 确认可关闭提示并回到主菜单。各启动使用隔离配置、独立端口、最小化、禁止焦点、静默音频和关闭 Steam 集成。按路径核对后已停止本任务的游戏进程。

基础择版与完整性检查 34 项、引导检查 9 项、清理 4 项和三套真实游戏引用下的选牌转发与错误收尾通过。最终包严格校验通过。逐文件哈希、游戏程序集哈希及验证详情保存在忽略的交接记录，原始运行证据位于任务工作树的 out/tmp/obs-058/。

v0.111.1 搭配现有角色包时，Core 成功加载并进入主菜单，Karen v1.0.0 独立遇到 NExhaustVfx 类型缺失，Junna 0.1.0 独立遇到 DamageTargetData 类型缺失。两项角色兼容及玩家原始角色包的身份核对仍待处理。Core 尚待合并，发布时独立 Core 与 Karen 内嵌 Core 使用同一份更新包。

最终测试包 core.bundle SHA256 为 B57B0DE9109341247D93016876C9EBD824331EDD92DD56576C69DA55A713A197，Runtime 启动指纹为 7C1ADDF2DAC5C606BC1E79973211B861A88561048E06A306DF8500792D609AD7。
