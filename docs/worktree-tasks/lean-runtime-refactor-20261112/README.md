# 三项目简洁重构

## 用户原始输入

完整按序记录见 [USER_INPUT.md](USER_INPUT.md)。

## 起点

- 原分支 main，原提交 ea9517dde854e5499969b975a7d84462a29aacc1。
- 工作树 ../worktrees/STS2_ShoujoKagekiAijoCore/lean-runtime-refactor-20261112。
- 原检出已有 0 个未提交文件；保留原状，不带入本任务。

## 简要计划

1. 删除重复内部包装和校验，保留公开 V1 与保存格式兼容。
2. Karen 用 BaseLib 扩展替代古老牙齿、初始遗物升级及药水图片补丁，固定原模型身份。
3. Core 统一结算、异常和退出收尾，确保逐项释放并保留主异常。
4. Junna 分离知识关系读写、隔离能力克隆计数，并集中免费和付费出牌流程。
5. 统一临时移牌执行与上传通道队列的生命周期，删除被替代实现。
6. 执行改动所需的最小离线检查、双版本编译与游戏 MCP 定向验证，整理结果后请求合并及清理确认。

## 结果与验证

重构与定向验证完成，用户已批准合并和清理。

- Core 1.7.0 增加 `card-staging@1`，已有 V1 签名、程序集版本和保存协议保持兼容。
- 内部 `Cleanup` 集中异常保留和逐项释放；模型作用域保持原异步上下文，退出通知和模块清理各执行一次。
- 两角色使用同一临时 Play 执行入口；对局与报错按通道和端点复用上传队列。
- 删除模块重复退出订阅和被替代队列管理代码。

## 验证结果

- 正式版 v0.107.1、测试版 v0.111.0：三项目及探针均编译成功；Core、Junna 和最终探针 0 警告；Karen 保留原有 26 项编译警告。
- Core V1 524 个已发布类型及成员签名保留，程序集版本 `1.0.0.0`；完整包严格清单校验通过。
- 生命周期 11 项、退出清理 4 项通过；上传队列 13 组定向检查通过，包括请求仍在途时切换及切回端点，并发请求数保持 1。
- 两版真实游戏分别通过 43 项定向断言，覆盖模型与图片身份、原生转换升级及附魔继承、知识查询无保存副作用、能力克隆隔离、生成牌回收、双异常保留、选择同步及移牌异常恢复。
- 两版手牌模型/可见节点均为 5/5 → 0/0，无缺失和额外节点；已查看前后截图与真实绘制录像帧。
- 两版全程最小化，0 次抢焦点，`silent_test=true`、`muted=false`，录像音源为 `engine_mix`。
- 最终 25 个包内文件与两版实测安装目录逐项核对 SHA-256，一致；Core、Karen 复用已有资源 PCK，Junna 同步最新 main 后重新构建完整资源包。
- 测试准备阶段的探针注册与取消重试断言已修正，失败记录与最终通过记录分别保留；最终断言全部通过。
- 证据清理前保留至 Core 原检出的 `out/tmp/lean-runtime-refactor-20261112/`，包含最终包、逐文件哈希、规则结果、截图、录像与隔离日志。未执行完整多人矩阵和性能专项。
- 三项目所有已跟踪文档及提交索引在提交前进行隐私审查，任务记录只使用相对路径和用途占位符。

## 合并前同步

- 已取得用户合并与清理授权；清理仅限本次三个工作树及同名任务分支。
- Junna 同步 main 的 `6e7c31d`，保留 r5007 卡池及最新牌效；自动合并无冲突。重新完成双版本构建与各 43 项游戏定向断言。
- 同步前后的包与验证记录分别保留；清理前逐文件核对保留证据哈希。

## 本项目最终测试包 SHA-256

| 文件 | SHA-256 |
| --- | --- |
| `core.bundle` | `b69241f6a6335a4dff76a6d658b174cee38aadf064c4189a72076fbc95e91640` |
| `lib/1.7.0/api/ShoujoKagekiAijoCore.Api.V1.dll` | `f16c601a7e9c0f9089f338baa9fd98ab841bd9b1b5b1a4a439af3156d11da2fb` |
| `lib/1.7.0/beta/ShoujoKagekiAijoCore.Runtime.Beta.dll` | `ba2c4ca9e851e5813ba06ed99c2032ccde23b6e220bcbbcc8cf0e10b77e4c9f3` |
| `lib/1.7.0/stable/ShoujoKagekiAijoCore.Runtime.Stable.dll` | `8498f0045bdcdeb2ddb1eb4c113487c91407dbd83712d672bd2703fe21821fe6` |
| `resources/1.7.0/Core.Resources.pck` | `b90eb992d2e70dac37069130dada2e7ecafaaa977cb4458c13e4ccf20afbf523` |
| `sdk/BundleValidation.cs` | `1a8614b8282064f68d061f7dc755858216092a72b1f835f3f7874d1f5753adc4` |
| `sdk/CharacterLoader.cs` | `f10e98f4c0172cc5db9d2a8a43d2a89658c95dc882bbecfb1f61d43c8a98e5ff` |
| `sdk/pack-character.ps1` | `3b66e7c4af869f96c26c153349f7fb109e7f51e0dc87688748e28eed9b5704b9` |
| `sdk/validate-package.ps1` | `76cb398a3f980ec4a25b3f945e127580a79526b5ff4008ecdef397ec637abb4d` |
| `ShoujoKagekiAijoCore.dll` | `63db4a227c09fdcef2b36dea33f76111b6d43a858697fae4083517c9d6ae1f6d` |
| `ShoujoKagekiAijoCore.json` | `ce4018e25fd1fdc9e248df4f204e237ca9dd0d34aace66865f036c62f683e14d` |
