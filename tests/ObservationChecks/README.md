# 巡检边界定向回归

以正式版 v0.107.1 或 Beta v0.113.0 的真实游戏引用运行：

```powershell
dotnet run --project tests/ObservationChecks -p:GameDir='<game-directory>' -p:Sts2Api=stable
dotnet build tests/LegacyModelFixture -p:LegacyGameDir='<v0.111.0-game-directory>'
dotnet run --project tests/ObservationChecks -p:GameDir='<beta-game-directory>' -p:Sts2Api=beta -- tests/LegacyModelFixture/bin/Debug/net9.0/LegacyModelFixture.dll
```

覆盖 OBS-045／046 的原生、外部直接转发、百闻牌缓存委托、未知不透明入口拒绝和中间件／原牌效各一次；OBS-048 的真实 Harmony 高优先级 Prefix 短路；OBS-050 双版本返回类型和精确泛型 Postfix；OBS-052 旧方法体隔离、重试记录及普通异常传播。

`LegacyModelFixture` 故意以旧游戏引用构建失效模型，仅供离线回归。不要把测试程序集放进发行包。合成的 Ritsu 转发回归不能替代 OBS-046 未复现原组合的验证。
