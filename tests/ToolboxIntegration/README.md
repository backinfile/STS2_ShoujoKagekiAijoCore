# 工具箱与空壳定向回归

仅用于隔离游戏。先读 KarenSTS2MCP README，按本任务版本配置静默录音与最小化。

```powershell
dotnet build tests/ToolboxIntegration/ToolboxIntegration.csproj -p:GameDataDir=<game-data-dir>
```

将 CoreToolboxProbe.dll、CoreToolboxProbe.json、core.requirements 放入隔离游戏的 mods/CoreToolboxProbe/。进入已有角色对局后，通过 MCP run_command 执行 core_toolbox_check。

探针读取原生工具箱 BeforeHandDraw 实际使用的 GetUnlockedCards 候选源，断言不含空壳；同时核对 TokenCardPool、保留的无色视觉池、原有模型 ID、本地化名称和说明。同包再生产标题作为资源加载对照。返回 status:error 表示断言失败。

补充用真实工具箱进入首回合，检查三张候选与截图；生成空壳核对仍可显示和正常消耗。与复制相关的旧行为使用 ResolutionChecks --copies-only 定向回归。
