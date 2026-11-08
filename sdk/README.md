# Core SDK

引用发行目录中 core.bundle 的 apiAssembly，关闭引用的复制本地选项。程序集版本固定为 1.0.0.0，命名空间为 ShoujoKageki.Core.V1。

角色清单声明 BaseLib 3.4.7 和 ShoujoKagekiAijoCore 1.0.0 依赖。角色构建完成后调用发行目录的 sdk/pack-character.ps1。

```powershell
& "$CorePackage/sdk/pack-character.ps1" -CorePackage $CorePackage `
  -CharacterDirectory $CharacterDirectory -OutputDirectory $WorkshopUpload -ModId $ModId
```

脚本支持 CharacterDirectory 与 OutputDirectory/ModId 为同一目录，生成 core.requirements 与 core.package.lock。锁文件记录构建时校验的 Core 发行版本与摘要，运行时按所有已启用 Mod 的需求选择兼容 Core。

| 角色 | 打包结果 | 工坊依赖 |
| --- | --- | --- |
| ShoujoKagekiAijoKaren | 角色目录与完整 Core 目录并列 | BaseLib |
| Junna 及其他 Mod | 仅角色目录与需求、锁文件 | BaseLib 和独立 Core 条目 |

只有 ModId 为 ShoujoKagekiAijoKaren 时复制 Core。其他 Mod 的上传目录已存在 Core 时，脚本要求使用新的上传目录。Karen 上传目录已存在不同 Core 时同样要求使用新目录。

游戏清单声明的依赖使用 BaseLib、ShoujoKagekiAijoCore 等 Mod ID。工坊依赖使用条目的数字 ID，写入角色自身的 workshop.json。例如：

```powershell
$workshop.dependencies = @([ulong]3737335127, [ulong]$CoreWorkshopId)
```

3737335127 为 BaseLib 条目 ID，CoreWorkshopId 使用 Core 首次发布后生成的 mod_id.txt 中的 ID。官方 ModUploader 按 workshop.json 同步必需物品。Core 自身的工坊工作区和发布命令见 [发布说明](../docs/RELEASING.md)。

自动编译与模块接线见 contracts/runtime-v1.md。构建 Core 时设置 SHOUJO_STABLE_GAME、SHOUJO_BETA_GAME 和 GODOT_BIN，或向 build.ps1 传入对应参数。TelemetryEndpointPath 是本机发布输入，接收地址文件进入忽略目录。每次构建指定一个新的 OutputDirectory。

Core 上传端点构建输入可通过 `SHOUJO_TELEMETRY_ENDPOINT_PATH` 指定；未显式指定时，`build.ps1` 读取忽略目录中的 `artifacts/private/CoreTelemetryEndpoint.json`。其中 `Url` 使用生产配置的 `upload_url`，与后台查看域名分离。

公共能力按实际引用传入 RequiredFeatures。Karen 使用 movement-pipeline@1、card-copies@1、card-snapshots@1、card-selection@1、presentation@1、temporary-music@1；Junna 使用 card-play-routing@1、model-execution@1、card-copies@1、card-snapshots@1、card-selection@1、presentation@1。使用本轮快照与选择接口的角色清单要求 Core 1.2.0。

validate-package.ps1 与引导编译同一份 BundleValidation.cs 校验实现，-Strict 同时检查未列出文件与链接。
