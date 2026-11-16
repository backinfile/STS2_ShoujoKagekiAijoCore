# 巡检实机探针

仅部署到带 KarenSTS2MCP 的隔离测试游戏。以对应真实游戏引用和受测 Core API 构建，再复制 `CoreErrorWatchIntegration.dll` 与同名清单到测试游戏 mods：

```powershell
dotnet build tests/ErrorWatchIntegration -p:GameDir='<game-directory>' -p:Sts2Api=beta -p:CoreApiPath='<candidate-core-api>' -o out/tmp/error-watch-20261116/fixture
```

通过 MCP 开发者命令运行：

- `error_watch check`：检查空／未绑定／释放的 NCard、工厂 Postfix、奖励登记与原生保存恢复、无 Owner 预览。
- `error_watch geometry`，随后轮询 `error_watch report`：实际绘制正常屏幕位置和淡出年龄序列，统计 Godot 原生三角化错误及节点释放。
- `error_watch prepare-sunlight`：在隔离战斗中准备升级阳光、1 次剩余闪耀和分组奖励；随后用 MCP 正常出牌结束战斗、选择并确认奖励。
- `error_watch inspect`：核对永久牌组中的卡牌升级、闪耀状态、登记状态及耗尽档案。

正式版使用 `Sts2Api=stable`。探针强制真实渲染以取得最小化窗口截图；按 MCP README 配置 `silent_test=true`、`muted=false`、引擎初始最小化及禁止抢焦点。该探针直接修改测试对局，只能放在隔离游戏，不能随发行包上传。

本次证据与逐项结论见 `docs/worktree-tasks/error-watch-20261116/README.md`。
