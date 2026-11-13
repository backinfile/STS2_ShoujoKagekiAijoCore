# 适配复核转来的修改要求

用户明确要求将本次整体复核结果通知本对话并进行修改。请接续当前游戏版本适配工作，按仓库 AGENTS.md 和 worktree-task 流程，以最小改动修复以下两项已确认遗漏，并完成直接相关的最小必要验证。

1. services/telemetry/app.py:206–208 忽略新版 is_custom_game。本机 v0.113.0（16ac0fe3）RunHistory 已将自定义局表示为 game_mode="standard"、is_custom_game=true；旧 GameMode.Custom 被移除。当前 derive 仍只读取旧模式，因此新版自定义局被归为 standard，在 modifiers=[] 时还按标准局推断华恋初始牌。已使用现有 test_app.fixture 调用真实 derive 复现：输入 standard + is_custom_game=true + 无 modifiers，输出 mode=standard，并推断 KAREN_DEFEND、KAREN_FALL、KAREN_SHINE_STRIKE、KAREN_STRIKE。请正确识别新版自定义标记，保留旧版 game_mode="custom" 的读取，并检查模式筛选、统计和已有记录重算/迁移是否需要同步。注意：字符串 dread、blitz 当前可以入库，不要误改为“新增模式均被拒绝”；Web 筛选目前只有标准、每日、自定义，可按本次适配需要补齐。

2. src/Modules/Configuration/CoreUnlockAll.cs:38 缺少 Blitz 进阶解锁。新版 ProgressState 有独立 MaxBlitzAscension，NBlitzScreen.InitializeSingleplayer/InitializeMultiplayerAsHost 直接读取它；AscensionManager.maxAscensionAllowedBlitz=5。当前全部解锁只设置角色 MaxAscension 和 MaxMultiplayerAscension，新档案 Blitz 仍停留 0。请在新版分支补齐，同时保持 v0.107.1 编译兼容。

其余已复核结论：Core ScopedChoiceContext.GameActionType 转发、Core/Junna HoverTip 只读 ID 适配、纯那 DamageTargetData 与 ModifyDamageHookType 完整转发、华恋宝箱字段及角色选择参数、MCP 商店同步与完整网格模型适配均符合新版原生实现；Core 战斗结束空选牌修复合理。CurlUpPower 在 v0.111.0 与 v0.113.0 实现相同，纯那新增收尾属于修复原有延迟结算兼容缺口。

本次只读审查没有修改任何跟踪源码，git status 干净。Core 当前 Beta 源码已重新编译，零警告零错误；原 v0.113.0 交付包已复制到本仓库忽略目录后通过 20 项 FoundationChecks。原交付两场自然局均为标准模式，不能算 Blitz/Dread 或多人实机覆盖；全面矩阵需按用户规则另行取得同意。

复核证据可在 Core 仓库 out/tmp/update-review-20261113/ 查阅：native-0111/ 和 native-0113/ 为本机对应真实 DLL 的完整反编译，另有关键类型 diff，package/ShoujoKagekiAijoCore/ 是用于包检查的交付副本。真实游戏引用和 BaseLib 3.4.8 仍可沿用先前适配交付目录。请在本对话落实修改并向用户汇报。
