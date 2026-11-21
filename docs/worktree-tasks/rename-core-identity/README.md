# Core 发行标识改名

## 用户要求

> Mod ID、SDK 程序集名和发行包名 也需要修改

> 别忘了兼容旧ID的配置项

> 合并到三个本地 main 并清理本次工作树

## 计划

更新 Core 的 Mod ID、SDK 及运行程序集、资源根和发行包名，使用 2.0.0 标识首次采用新身份的发行。沿用接口签名和存档字段键。迁移旧 ID 对应的配置文件并保护已有新配置。

## 结果与验证

- Mod ID、引导程序集、三个 Runtime 和发行根统一使用 `ShoujoKagekiCore`。SDK 程序集为 `ShoujoKagekiCore.Api.V1.dll`，`AssemblyVersion` 仍为 1.0.0.0。与 1.9.7 的 API 比对仅报告 Mod ID 与资源根两个已知常量值变化。
- 首次启动从旧 Core 配置文件继承现有设置，新配置存在时直接使用新配置。三种语言的 UI 文本保持原值，存档扩展字段键沿用 1.x。
- Core 2.0.0 在 v0.107.1、v0.111.1、v0.113.0 游戏引用下构建，SDK 严格包校验和 41 项离线打包检查通过。ZIP 位于主检出的 `dist/ShoujoKagekiCore-2.0.0.zip`。
- v0.111.1 隔离实机通过旧配置迁移、新配置优先、旧文件保留与旧包混装拦截。Karen、Junna 和新版 Core 一起启动成功。实机结果位于 `out/tmp/rename-role-mcp/result.json` 和 `out/tmp/rename-legacy-package-mcp/result.json`。
- 最终 `core.bundle` SHA-256 为 `80de978ff2acb6ee7c92743ebd5fa096cc7dac31cf34523438c798c7c65b2433`，发行 ZIP SHA-256 为 `8a30dc7cc45776f20d25a1757d8d498acba95d539fcfc521aeb4d827558ef240`。打包检查使用本地模拟上传器。
