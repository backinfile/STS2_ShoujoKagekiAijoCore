# SL 持久化回归

通过 KarenSTS2MCP 在隔离游戏中运行，覆盖 Core 玩家扩展数据及永久卡牌保存内容。

## 运行

先读 KarenSTS2MCP 当前 README。需要 Python、.NET 9、PowerShell 7（`pwsh` 在 PATH 中）、目标游戏及与源码匹配的完整 Core 包。游戏目录提供对应版本的 BaseLib 和 KarenSTS2MCP。测试自动建立独立游戏目录、用户目录、随机回环端口，最小化静默启动；保留正常绘制。

```powershell
$GameDir = '<目标游戏目录>'
$CorePackage = '<完整 Core 包>'
dotnet build tests/PersistenceIntegration/PersistenceIntegration.csproj `
    -p:GameDir=$GameDir -p:Sts2Api=stable -o out/tmp/sl-persistence/probe
python tests/PersistenceIntegration/run.py --game $GameDir --core $CorePackage `
    --probe out/tmp/sl-persistence/probe/CorePersistenceProbe.dll `
    --output out/tmp/sl-persistence/current --cycles 2
```

测试版将构建参数改为 `-p:Sts2Api=beta`，使用该版本的游戏与 MCP。每次运行使用新的 `--output` 目录；旧证据保留。测试进程通过自身句柄退出，不操作其他游戏实例。

## 断言与样本

- 初始牌组保留普通牌；另建升级后的永久牌，保存闪耀余量 `2/7` 和成长字段 `23`。
- 将另一张升级牌通过实际结算耗尽，形成余量 `0/7`、成长字段 `41` 的永久档案，并将普通复制得到的空壳加入永久牌组。
- 将五轮书与招财异鱼规则设为不同值，保存完整本局规则快照。
- 原生 `RunManager.ToSave` → JSON → `RunState.FromSerializable` 往返，重复使用同一个存档对象，再调用玩家同步入口，逐项比对完整卡牌 JSON、闪耀状态、成长字段、档案身份与规则。
- 调用原生 `SaveManager.SaveRun` 建立明确的保存检查点，再返回主菜单、停止进程、重启并续玩。默认连续两轮，比对保存前后的完整快照。
- 核对恢复后的牌堆按钮、数量、实际可见卡牌及拥有者；验证耗尽牌普通复制仍为空壳，明确重新获得恢复满次数。

保存检查点由探针明确建立，避免把当前战斗回滚到进房间时的原生行为当成持久化丢失。

## 存档格式

`--save-format` 只修改隔离存档，在第一轮退出后转换：

| 值 | 验证内容 |
| --- | --- |
| `current` | 当前注册的 Core 玩家保存字段，默认值 |
| `legacy-core` | 仅旧 `extra_fields.core_player_disposed_pile` 与 `core_rules` |
| `legacy-karen` | 仅旧 `extra_fields.karen_player_disposed_pile` 与 `core_rules` |
| `mixed` | 新字段与空的旧档案并存，验证新字段优先 |
| `empty-archive` | 人为清空已保存的耗尽档案，负向对照；应以退出码 1 报告 `SL changed archive` |

前三种旧格式样本也包含无关旧字段，保存原始转换文件供迁移检查。`--roundtrip-only` 只运行同进程 JSON 与界面检查。`--mod <角色模组目录>` 可重复传入，配合 `--character <模型 ID>` 选择角色，默认铁甲战士。

## 证据

输出包括预期快照、每轮落盘存档、每项 JSON 断言结果、移牌前截图、耗尽牌堆截图、游戏日志、游戏与 Core 版本、实际 DLL/PCK 哈希及窗口保护记录。存档和日志都位于忽略的输出目录。

只有 `result.json` 的 `passed=true` 表示该格式的正常测试通过；负向对照应失败，并由断言明确指出耗尽档案丢失。
