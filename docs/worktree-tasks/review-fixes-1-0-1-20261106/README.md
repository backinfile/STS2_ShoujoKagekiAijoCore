# Core 1.0.1 审查修复

## Original user input

> review下当前项目所有文件
> 修复全部问题，提升一个小版本号

本轮修复审查确认的六项问题，包括升级闪耀牌读档预算、特效设置误隐藏音乐控件、音乐优先权释放恢复、错误报告大小预算、Web 资料别名更新和筛选链接恢复。

## Brief task plan

- 原检出位于仓库根目录，原分支为 master，原始 HEAD 为 7e937ef4264fb41a1afd6444096b9dfe61d803f6，工作区干净。
- 任务工作树位于 ../worktrees/STS2_ShoujoKagekiAijoCore/review-fixes-1-0-1-20261106，任务分支为 task/review-fixes-1-0-1-20261106。
- 最小改动修复六项问题，将发行版本提升到 1.0.1。
- 执行对应定向回归、双版本构建及必要游戏验证，记录最终包哈希。
- 整理为一个任务提交，同步原分支后请求合并确认，清理单独确认。

## Result and changes

已修复六项审查问题，发行版本提升为 1.0.1。

- 闪耀反序列化期间跳过升级补满，正常升级继续补满。保存的新字段、旧字段、混合字段及临时与待耗尽状态均保留，异常退出释放读档作用域。
- 特效筛选限定到 VFX 类型，音乐播放器与音乐设置继续显示。
- 音乐优先权保留中断前音轨、位置及播放状态，最后一个优先权释放时恢复。停止、目录重载和异步加载继续按请求代次撤销。
- 错误报告固定环境快照，为 Core 上下文及文本设置 JSON 字节预算，保留累计次数和重试身份。上下文清单裁剪通过 mods_truncated 标记。
- Web 资料重建同步短 ID 别名，多个来源沿用首个来源的别名优先顺序。
- 主页面及对比模式恢复 Core 版本与 Mod 来源筛选链接。
- 更新发行默认值、Core 清单模板及当前使用说明。

### 定向验证

- 正式版 v0.107.1、测试版 v0.111.0，BaseLib 3.4.7，MegaDot 4.5.1 m.12。完整双版本发行构建均为零警告、零错误。
- 两版 tests/ResolutionChecks 的 --save-only 通过，覆盖多级升级、真实升级补满、普通牌、旧字段、混合字段和异常读档清理。
- tests/FoundationChecks 完整发行包的 16 项校验通过。
- tools/telemetry/error-tests 通过，包含大规模 Mod 清单、Unicode 和需 JSON 转义文本的 v2 报告、累计次数和稳定上下文。有界真实客户端载荷为 52,685 字节，经隔离服务 /api/errors 接收为 HTTP 201。
- tools/telemetry/test_assets.py 通过，覆盖资料重复构建、卡牌与遗物别名更新及多来源优先顺序。
- tools/telemetry/test_web.cjs 通过，覆盖动态筛选 URL 恢复以及已有筛选、图表和 CSV 的相关回归。
- 测试版经 KarenSTS2MCP 正常开局与控制台入口完成 10 项实机断言，覆盖读档余量与状态、真实升级、普通牌、嵌套音乐优先权、播放与暂停及进度恢复、特效关闭后的 HUD 可见性、音乐设置筛选和停止取消恢复。
- 游戏使用隔离目录、满解锁、随机 MCP 端口、静默音频和本地接收地址。设置确认 silent_test=true、muted=false、minimized_startup_guard=true，结束前确认主窗口最小化且不在前台，随后结束本任务启动的进程。
- 实机证据在 out/tmp/review-fixes/live-beta/，含探针响应、结果 JSON、窗口状态、截图及日志。启动预加载阶段出现一条原生 Invalid Task ID，随后所有定向断言通过。

### 产物与状态

- 完整包位于 dist/ShoujoKagekiAijoCore/。
- core.bundle 的 SHA256 为 02CF53306410DB5FD92E60FDFD0B8220A721ACF26CA9F8CE6C2CA75F64F45B5D。
- 本轮按已报告问题执行定向验证。完整双版本单人、多人矩阵及性能专项需另行确认。
- 原检出 master 保持原始 HEAD 与干净状态，任务提交后等待合并确认。工作树、任务分支与忽略目录中的产物保留，清理单独确认。
- 提交采用 Codex 机器人身份，避免现有本地 Git 身份中的私人邮箱进入提交。
