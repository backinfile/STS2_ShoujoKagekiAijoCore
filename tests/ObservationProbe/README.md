# Common 预载与纹理实机探针

构建工具依赖实际游戏与 BaseLib；启动前阅读 KarenSTS2MCP 当前 README。

```powershell
dotnet build tests/ObservationProbe/ObservationProbe.csproj -p:GameDir=$BetaGame
./tests/ObservationProbe/prepare-game.ps1 -GameDir $BetaGame -CorePackage $CorePackage -McpDirectory $McpMods -Instance beta
```

脚本创建独立游戏入口和用户目录、随机 MCP 端口，最小化／禁止焦点启动，使用 silent_test=true、muted=false。数据上传关闭，报错仅指向回环地址。原游戏与用户配置保持原样。复用该实例时：

```powershell
./tests/ObservationProbe/restart-preload.ps1 -Instance beta -Name repeated -Draw
```

原始日志位于 out/tmp/core-observations/<instance>/，instance.json 记录端口和本任务进程。等待 MCP 主菜单与 Common Complete，再核对 Invalid Task ID、全部引擎错误和完整资源数量。观察 Logger 不修改原始日志。实际战局中通过 MCP run_command 执行 core_observations border，检查实际 Core 的纹理恢复；录制遵循游戏 MCP 规则。
