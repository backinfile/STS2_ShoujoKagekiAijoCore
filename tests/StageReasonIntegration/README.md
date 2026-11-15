# 共享诅咒迁移定向探针

按真实游戏版本设置 `GameDir` 和 `Sts2Api` 构建本目录工程，将 `CoreStageReasonProbe.dll`、测试清单与 Core 需求声明安装到隔离游戏。进入战斗后通过 KarenSTS2MCP 执行 `core_stage_reason_check`，再用 `core_stage_reason_check status` 获取结果。

探针核对旧 ID 解析、全局诅咒池唯一注册及原生候选、角色／无色／Token／闪耀奖励池排除、原图和三语原文、旧／新／缺失保存字段恢复。随后使用原生自动出牌结算三次，检查普通消耗保留余量、永久耗尽档案和满预算重新获得。测试会在当前隔离对局中获得并耗尽一张诅咒。

同一包分别验证 Core 独立加载和 Core＋Karen 加载，避免角色依赖或重复注册。探针不进入发行包。
