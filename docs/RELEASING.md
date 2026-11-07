# Core 发布流程

Core 作为独立创意工坊 Mod 发布，仅 Karen 内嵌完整 Core；Junna 及其他 Mod 使用独立 Core 条目依赖。独立发布与 Karen 内嵌使用同一份官方构建产物。

## 1. 构建完整 Mod

使用 PowerShell 7、.NET 9、MegaDot 4.5.1，以及正式版和测试版的真实游戏目录。版本由 `build.ps1 -Version` 指定，根目录 `ShoujoKagekiAijoCore.json` 是 Mod 清单来源，构建时将清单版本同步为发行版本。

```powershell
./build.ps1 -StableGameDir $StableGame -BetaGameDir $BetaGame -Godot $MegaDot `
  -TelemetryEndpointPath $LocalEndpointConfig -Version 1.0.1 `
  -OutputDirectory dist/ShoujoKagekiAijoCore-1.0.1
```

输出目录包含 Mod 清单、引导 DLL、API、双版本 Runtime、资源 PCK、`core.bundle` 和 SDK，可作为完整 Mod 安装到游戏 mods 目录。每次构建使用新输出目录。

```powershell
./sdk/validate-package.ps1 -CorePackage dist/ShoujoKagekiAijoCore-1.0.1 -Strict
```

严格校验核对发行身份、API 身份、必需文件和 SHA256，并拒绝未列入 `core.bundle` 的额外文件及链接。

## 2. 准备发行文件与工坊工作区

根目录 `workshop.json` 保存 Core 的标题、简介、可见性、标签与 BaseLib 工坊依赖。预览图通过本地参数传入，使用小于 1 MB 的 PNG。

首次发布：

```powershell
./prepare-release.ps1 -CorePackage dist/ShoujoKagekiAijoCore-1.0.1 `
  -PreviewImage $PreviewPng -OutputDirectory out/releases/1.0.1 `
  -ChangeNote '首次发布'
```

更新现有条目时传入已有 Core 工坊 ID，脚本将其写入新工作区的 `mod_id.txt`：

```powershell
./prepare-release.ps1 -CorePackage dist/ShoujoKagekiAijoCore-1.0.1 `
  -PreviewImage $PreviewPng -OutputDirectory out/releases/1.0.1-update `
  -WorkshopId $CoreWorkshopId -ChangeNote $ReleaseNotes
```

`-WorkshopConfig` 可指定其他本地工坊配置。输出目录必须是新目录，且位于源 Core 包之外。

```text
out/releases/<release>/
  ShoujoKagekiAijoCore-<version>.zip
  checksums.sha256
  release.json
  workshop/
    workshop.json
    image.png
    mod_id.txt               更新条目时存在
    content/
      ShoujoKagekiAijoCore/   完整 Mod
```

ZIP 内以 `ShoujoKagekiAijoCore/` 为顶层目录，可用于手动安装和 Karen 内嵌。`checksums.sha256` 记录 ZIP 与工坊内容中 `core.bundle` 的摘要，`release.json` 记录发行号、接口版本及游戏目标。工坊元数据、预览图和条目 ID 位于 `content/` 外，上传内容只包含完整 Core。

## 3. 上传或更新工坊条目

使用 [Mega Crit 官方 ModUploader](https://github.com/megacrit/sts2-mod-uploader)，启动并登录 Steam。脚本从上传器目录执行官方 `upload -w` 命令并检查退出码，调用前重新严格校验内容、预览图和工坊依赖。

首次创建条目：

```powershell
./publish-workshop.ps1 -Workspace out/releases/1.0.1/workshop `
  -Uploader $ModUploader -CreateNew
```

成功后，官方上传器在工作区生成 `mod_id.txt`。保存这个 Core 条目 ID，后续发布时传入 `prepare-release.ps1 -WorkshopId`，并交给其他 Mod 作者声明工坊依赖。

更新已有条目：

```powershell
./publish-workshop.ps1 -Workspace out/releases/1.0.1-update/workshop `
  -Uploader $ModUploader
```

更新要求工作区已有有效 `mod_id.txt`。`-CreateNew` 用于首次创建，有条目 ID 的工作区不能使用该参数。上传失败保留工作区和条目 ID，日志由官方上传器写入其目录。若上传器提示需要接受工坊协议，按提示完成后在工坊页面核对可见性。

Core 的工坊必需物品为 [BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127)。Junna 及其他角色在各自 `workshop.json` 的 `dependencies` 中填写 BaseLib 和 Core 的数字工坊 ID，同时在游戏 Mod 清单中保留 BaseLib、ShoujoKagekiAijoCore 的 Mod ID 与最低版本。

## 4. Karen 内嵌与发布核对

Karen 构建使用本次完整 Core 包中的 SDK：

```powershell
$CorePackage = 'dist/ShoujoKagekiAijoCore-1.0.1'
& "$CorePackage/sdk/pack-character.ps1" -CorePackage $CorePackage `
  -CharacterDirectory $KarenModDirectory -OutputDirectory $KarenUploadContent `
  -ModId ShoujoKagekiAijoKaren
```

SDK 生成 Karen 的需求与锁文件，并将 Core 完整目录复制到 Karen 上传内容。其他 Mod 使用同一命令及自己的 ModId 时，只生成角色目录、需求和锁文件。

发布记录写明游戏版本、BaseLib 版本、`core.bundle` SHA256、ZIP SHA256，以及实际通过项、失败项和待验项。核对独立 Core 与 Karen 内嵌包的摘要一致，清单依赖顺序为 BaseLib → Core → 角色。定向验证通过后再上传；完整双版本单人、多人和性能矩阵按项目约定另行确认执行。

维护打包或上传脚本时，可对已有完整 Core 包运行离线定向检查。检查在 `out/tmp/` 内复制测试夹具，替换为当前 SDK，并使用本地上传器桩验证发布分支和失败路径：

```powershell
./tests/PackagingChecks.ps1 -CorePackage $ExistingCorePackage
```
