# 蚱蜢归还牌回归

先读取 KarenSTS2MCP 当前 README。需要 Windows、Python、.NET 9、目标游戏目录中的 BaseLib / KarenSTS2MCP，以及与待测源码一致的完整 Core 包。

```powershell
$GameDir = '<目标游戏目录>'
$CorePackage = '<完整 Core 包>'
dotnet build tests/TheftIntegration/TheftIntegration.csproj `
    -p:GameDir=$GameDir -p:Sts2Api=beta -o out/tmp/theft/probe
python tests/TheftIntegration/run.py --game $GameDir --core $CorePackage `
    --probe out/tmp/theft/probe/CoreTheftProbe.dll --output out/tmp/theft/run
```

正式版将 `Sts2Api` 改为 `stable`，使用对应版本的游戏与依赖。每次运行使用新的输出目录。运行器创建独立游戏目录、用户目录及随机回环端口，最小化静默启动，通过 MCP 调用游戏内 `core_theft run` / `core_theft status`，结束时核对进程路径并关闭本次启动的进程。

探针在真实战斗中调用原生 `SwipePower.Steal`、`BeforeDeath`、归还奖励序列化与领取入口，检查：

- 普通牌保留无闪耀状态。
- 已实际消耗次数的牌和已升级牌保留剩余次数与上限。
- 恢复次数、增加上限或战斗升级后，归还牌保留被偷时的永久预算。
- 奖励保存后恢复再领取仍保留升级、成长字段和预算。
- 独立临时预算不覆盖永久来源。

使用原生打击牌附加闪耀状态及保存字段，隔离角色牌效。原生偷牌状态附着在当前战斗敌人上，由探针直接触发归还入口。

未修复时，永久来源 `2/7`、战斗牌恢复到 `7/7` 的场景会在 `Return reward preserves Shine` 断言失败，归还奖励实际仍为 `2/7`。

`result.json` 的 `passed=true` 表示全部断言通过。输出目录包含逐项结果、移牌前后及动画结束后的截图、日志、游戏/Core 版本、实际 DLL/PCK 哈希与窗口状态记录。
