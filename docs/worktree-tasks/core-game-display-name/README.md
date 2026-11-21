# Core 游戏内显示名

## 用户要求

> 现在在游戏内的mod名称使用 “少女歌剧ShoujoKageki"

> 合并、更新本地包并清理工作树

> 如果同时存在1.x.x 会不会有问题

## 计划

修改游戏清单的显示名，校验打包后的清单，并在隔离游戏中确认加载名称。

## 结果与验证

- 游戏内 Mod 显示名为 `少女歌剧ShoujoKageki`，Mod ID 仍为 `ShoujoKagekiCore`。
- 复用 Core 2.0.0 的程序集与资源，更新清单摘要并重新制包。通过 SDK 严格校验及 ZIP 清单核对。
- v0.111.1 隔离游戏经 MCP 启动，游戏加载列表显示 `少女歌剧ShoujoKageki (ShoujoKagekiCore)`，Core 正常初始化。
- 候选 ZIP SHA-256 为 `bc8d26578a308f4448d4c3217efc2ef54bb1e9c6fa6ba56353720c908a3b84a2`。
- 本地旧候选包的备份位置为 rtifacts/task-deliverables/core-game-display-name/previous/。游戏加载列表中应只保留当前版本的 Core。
