# Core 1.0.1 简化审查

## Original user input

- worktreetask 更新core，继续review下整个项目，看看还有哪些代码可以简化
- 或者目录结构，代码等可以优化
- 新版的core可以一并review
- 同步最新 Karen 和 Core 后继续（推荐）

## Brief task plan

- 原分支 master，起点 f2bedf5aea4fc07d046168d6628b1bcd5c144897，原检出干净。
- 工作树位置为 ../worktrees/STS2_ShoujoKagekiAijoCore/core-review-simplify-20261106。
- 检查远端和现行包，审查 Core V1、引导、模块、保存、表现、遥测及 SDK 边界，实施小范围等价简化。
- 运行直接相关的回归和双版本编译，提交后分别请求合并与清理确认。

## Result and changes

### 已完成

- 现行源码为 Core 1.0.1。远端没有分支可同步，使用本地 master 与现有完整构建目录作为基线。
- BundleSelection.Select 保留统一的候选冲突检查，直接取得最高兼容候选，移除重复冲突检查及结果数组。
- ShineVfx.WillDeplete 每次使用同一份 ShineState，移除三个随后失去用途的内部转发方法。
- 闪耀牌堆逐帧数量查询直接读取档案列表长度，避免为 Count 复制完整快照列表。
- tests/ResolutionChecks 增加 --presentation-only，覆盖模板、普通牌、剩余次数、扣次前后、待耗尽和永恒的 8 项预警条件。
- API V1 公开类型和签名保持兼容，发行版本仍为 1.0.1。源码简化提交用于后续发行，未将其覆盖到现有官方包。

### 定向验证

- 现有官方 1.0.1 包通过 SDK 校验及 FoundationChecks 的 16 项检查。简化后的择版实现重新运行同组检查通过。
- --presentation-only 的 8 项检查通过。
- Runtime 在正式版 0.107.1、测试版 0.111.0 的实际游戏引用上分别编译，均零警告、零错误。
- Karen 配套任务使用官方 1.0.1 API 双版本编译通过，Core 1.0.0 被新的依赖最低版本拒绝。
- 本轮验证覆盖等价简化、编译与 SDK 元数据。完整游戏、单人/多人矩阵和性能专项按用户后续要求执行。
- 原 master 的 HEAD 和干净状态保持不变。全部跟踪文档的相对路径复核通过，完整暂存索引共 888 个文件，隐私检查零项发现。

### 审查发现与后续候选

1. SDK 与引导包校验出现规则差异。隔离副本将 beta 目标指向 Stable Runtime 后，sdk/validate-package.ps1 接受，而 BundleSelection.Validate 以 Unexpected runtime identity for beta 拒绝。复现副本及日志位于 artifacts/validator-parity。适合统一校验规则并增加一致性检查。
2. CoreDiscovery.Refresh 对每个模型重复查找同一程序集的所属 Mod，单次刷新中的程序集归属可以复用，保持后续刷新重新读取 Mod 状态。
3. src/Modules/Presentation 同时承载闪耀、音乐、启动画面与绘制工具，可按机制分组。resources/ShoujoKagekiAijoCore/scenes/shine_pile_button.tscn 当前引用 C# 文件路径，移动时需一起维护。
4. 保存桥保留了迁移前的自定义处理器骨架。后续与 BaseLib 扩展保存通路整合时，需保持旧字段、读档作用域、未知字段和网络序列化行为。
5. Karen 的多处历史探针继续引用迁入 Core 后已删除的类型，配套任务已复现并记录。共享测试适合归入 Core/tests，角色工具维护其专属场景。

### Git 状态

- 本轮改动归为一个任务提交，采用 Codex 的公开 noreply 身份。
- 工作树、任务分支和 artifacts 中的基线、编译与隔离复现文件保留，等待单独清理确认。
