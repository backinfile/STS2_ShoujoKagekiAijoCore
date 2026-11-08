# OBS-033 / OBS-036 公共表现与资源预载修复

## 用户原始要求

工作区约定原文见 [user-instructions.md](user-instructions.md)。

用户在少歌报错巡检对话中明确要求：“通知core项目修改，并将修改结果状态同步到这个文档”。本任务处理 OBS-033、OBS-036，其余编号、玩家计数和人工状态保持原样。统一文档由巡检对话更新；本任务提供 result.json，避免同时编辑。

请先读本仓库 AGENTS.md，使用 $worktree-task，在独立 worktree 中诊断、做最小修改和必要定向验证，保持 Core 主目录及其他任务原有修改。Core 另一个对话正在调整公共配置，使用独立工作树并在同步时保留其变更。只做与这两项直接相关的验证，全面矩阵另经用户同意。后续合并、清理沿用 skill 的具体结果审批，不套用此前上传域名任务的合并许可；本次未要求上传工坊、发布或推送。

OBS-033（Core／P1）：旧 Karen v0.1.8 内置公共闪耀在 v0.111.0、Windows 中发生 NullReferenceException。2026-11-07 22:13:59 至 22:14:02，两条报错、两个指纹，单指纹一个会话；独立 Core 版本未记录。调用链 ShineCardBorder.ReadImage → ReadContour → GetContour → Prepare → NCard.InitPool。指纹 cf411cbac7bb580e3b37c8260b880cc6095b7eb28dd6b8483fe8dbabaf273521、f95e6f31f3a2f46f5ad96b3635d48890c61ef67781f741dec337a5679b24977d。当前公共实现位于 src/Modules/Presentation/ShineCardBorder.cs，其中 AtlasTexture.Atlas.GetImage 仍有直接读取。请建立命中 ReadImage 失败边界的复现，核对空 Atlas、资源准备和纹理读取生命周期，按实际根因修复；不要仅凭旧命名空间将问题排除，也不要在尚未定位时断言 Atlas 为空就是唯一原因。

OBS-036（Core／P2）：公共资源预载 WorkerThreadPool.wait_for_task_completion 报 Invalid Task ID。原快照 v0.111.0、Windows、Junna 0.1.0，独立 Core 版本未知；2026-11-05 14:52:54 至 17:35:54，7 次、3 指纹，单指纹最高 4 个会话。指纹前缀 20c17cbe15df、236c1e2066d0、44a11aedf685。Junna 的已完成分析提供了新的归属证据：BaseLib＋MCP＋独立 Core 1.0.1 可在 Common 预载复现3次，加入 Junna 同样3次；Core 1.0.2 独立或仅与 Junna 联用对照无错误，但 Core 1.0.2＋Karen＋Junna＋RitsuLib＋强制绘制探针的完整组合仍有1次，战斗回归通过。因此不能标记已彻底解决，也不能泛化归属所有同文本引擎日志。请核对 Common 预载任务、脚本注册与并发顺序，先有实际失败回路，再修复；不通过过滤或吞掉报错交付。

证据：<Karen 项目>/artifacts/error-watch/2026-11-08/core-repair-evidence.json、junna-completed-error-analysis.md；<Junna 项目>/docs/worktree-tasks/junna-error-reports-20261108/README.md、error-analysis.md；原始验证保留于 <Junna 项目>/artifacts/task-deliverables/junna-error-reports-20261108/。

若最新源码已经覆盖某一问题，核对对应提交和实际验证后明确回报，不重复修复。完成时逐项给出根因和证据、实际修改、验证通过／失败／待验范围、任务分支及提交、是否已合并、是否已部署／发布、剩余下一步。保留任务记录和原始验证证据，在原检出 artifacts/task-deliverables/core-observations-20261108/result.json 写交付摘要（每项含 issue_id、status、root_cause、commit、merge_status、validation、next_step、evidence）。保留原有玩家计数和人工备注，精确区分“源码已修复、待合并”和“已合并、待上线核验”。

## 简要计划

1. 在真实 Godot 纹理边界与隔离游戏 Common 预载建立定向失败回路。
2. 根据实际失败定位根因，分别保留资源生命周期和加载顺序的最小修复。
3. 只运行对应离线、双版本编译与必要实机检查，保留日志及录制证据。
4. 同步 main 和配置任务的已合入变更，提交具体结果后另行确认合并和清理。

## 实际结果与验证

已完成两项最小源码修复，正在进行最终集成与联用复验。

- OBS-033：真实 Godot 探针在原实现 ReadImage 第 101 行复现 NullReferenceException；修复空 atlas 来源、失败轮廓缓存及嵌套读取。4 项真实纹理读取与恢复检查通过。
- OBS-036：强制绘制下，实际 Core 1.0.2 包在 Common 预载复现一次错误；移除 Core 的同探针对照也出现错误。预加载脚本基类、只串行场景、预载 atlas 均未完全消除错误。单个未完成资源对照完整加载 792 项、0 错误，但耗时 26,774 ms。
- 将真实场景／资源的共享依赖先行准备后，保留原并发队列：193 项共享依赖、完整 792 项、0 引擎错误。已去掉实验调度代码的实际 Core 包复验，依赖准备 294 ms，Common 完整加载 792 项约 4,214 ms，主菜单可操作。
- 实际 Core 的 MCP 纹理恢复检查返回 points=5、recovered=true；地图正常显示，未出现纹理异常。
- 两版 Runtime 及引导已在真实 v0.107.1 / v0.111.0 引用上编译，0 警告、0 错误；最终组合与存活资源释放仍继续核对。
- 同文本的其他引擎错误未纳入已修复结论，原始历史指纹的源包与资源未完整保留，结论限定于本次建立的 Common 失败回路。

起始 main 为 15acffdd1d58e8d8650355e4883949b311e6321e，原检出干净。任务分支 codex/core-observations-20261108。
