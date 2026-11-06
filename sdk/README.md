# Core SDK

引用发行目录中 core.bundle 的 apiAssembly，关闭引用的复制本地选项。程序集版本固定为 1.0.0.0，命名空间为 ShoujoKageki.Core.V1。

角色清单声明 BaseLib 3.4.7 和 ShoujoKagekiAijoCore 1.0.0 依赖。角色构建完成后调用发行目录的 sdk/pack-character.ps1。

```powershell
& "$CorePackage/sdk/pack-character.ps1" -CorePackage $CorePackage `
  -CharacterDirectory $CharacterDirectory -OutputDirectory $WorkshopUpload -ModId $ModId
```

脚本支持 CharacterDirectory 与 OutputDirectory/ModId 为同一目录，生成 core.requirements 与 core.package.lock，并将完整 Core 并列复制到上传目录。已存在不同 Core 时使用新的上传目录。

自动编译与模块接线见 contracts/runtime-v1.md。构建 Core 时设置 SHOUJO_STABLE_GAME、SHOUJO_BETA_GAME 和 GODOT_BIN，或向 build.ps1 传入对应参数。TelemetryEndpointPath 是本机发布输入，接收地址文件进入忽略目录。每次构建指定一个新的 OutputDirectory。
