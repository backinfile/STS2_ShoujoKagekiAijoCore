# Core 独立工坊发布与角色依赖

## Original user input

> 这个项目也会作为创意工坊mod发布，只有karen mod是内嵌当前mod的，其他mod都直接通过创意工坊mod依赖即可

> 你来修改相关文档，并且按mod结构调整这个项目

> 你也在这个项目中创建好发布流程相关脚本

## Brief task plan

1. 将 Core 清单作为项目根目录的正式 Mod 清单，构建产物保持可独立安装和工坊上传的完整 Mod 目录。
2. 调整角色 SDK 打包：Karen 内嵌完整 Core，其他角色生成依赖元数据并使用独立 Core 工坊依赖。
3. 补充发行文件准备、严格校验与官方 ModUploader 上传脚本。
4. 同步项目约定、玩家说明、实现文档及 SDK、发布文档；定向验证两种打包模式、离线发布流程和现有候选择版。

## Result and changes

- 起始分支：`main`；起始提交：`3b6061fc47464d50ad38e86178f00a4d8d3b438f`；原工作区干净。
- 任务分支：`codex/core-workshop-distribution-20261107`。
- 工作树：`../worktrees/STS2_ShoujoKagekiAijoCore/core-workshop-distribution-20261107`。
- 已完成，待确认合并到 `main`；工作树与任务分支保留。

### 实际变更

- 将正式 `ShoujoKagekiAijoCore.json` 从 SDK 模板移至项目根目录，构建从该清单生成完整 Mod。
- 增加根目录 `workshop.json`，声明 Core 的工坊信息和 BaseLib 必需物品。
- SDK 仅为 `ShoujoKagekiAijoKaren` 复制完整 Core；其他 Mod 只打包角色与需求、锁文件，并拒绝残留 Core 的上传目录。锁文件继续记录构建时使用的官方发行包。
- 新增 `prepare-release.ps1`，生成完整 Mod ZIP、SHA256 清单、发行信息和官方 ModUploader 工作区。
- 新增 `publish-workshop.ps1` 与 `tools/release/validate-workspace.ps1`，区分首次新建和已有条目更新，上传前复验包完整性、内容目录、预览图、依赖及条目 ID，传播上传器失败结果。
- `sdk/validate-package.ps1 -Strict` 排除未列入包清单的文件和链接。引导失败提示同步独立 Core 更新来源，保留原择版行为。
- 更新 `AGENTS.md`、`README.md`、`sdk/README.md`、`docs/IMPLEMENTATION.md`，新增 `docs/RELEASING.md` 与定向回归 `tests/PackagingChecks.ps1`。

### 验证结果

- `tests/PackagingChecks.ps1`：38 项通过，覆盖 Karen、Junna 与普通 Mod 的打包、原位重打包、内嵌包一致性、旧 Core 残留拒绝、独立工坊目录、ZIP 逐文件 SHA256、发行信息、首次新建、沿用条目更新、上传失败及上传前拦截额外文件。
- `tests/FoundationChecks/`：16 项通过，覆盖单包、同内容副本、最高兼容版本、功能与目标回退、同版本冲突、API 与依赖要求、完整性和路径校验。
- 所有项目 PowerShell 脚本语法检查、根 Mod 清单与构建引用检查、`git diff --check` 通过。
- 首次使用 `--no-restore` 运行 FoundationChecks 时，新工作树缺少资产文件；使用空本地 NuGet 源完成离线还原后检查通过。
- 测试基于原检出既有 Core 1.0.1 包，目标信息为正式版 0.107.1、测试版 0.111.0、BaseLib 最低 3.4.7；夹具替换当前 SDK 并更新摘要。本次未重新构建游戏 Runtime，上传使用本地桩，未连接 Steam 发布。
- 测试证据：`out/tmp/packaging-checks-7f4661e999e54a58a909fa89ddacfd14/`（忽略目录）。
- 测试包 `core.bundle` SHA256：`8d5daeff7ea9b33a802407ebfb91f5299b93cb46ee435a3d8f04da49e47795b0`。
- 测试 ZIP SHA256：`1fcb248dfd8cae27e0bcbc5014341c2e9a40abc093507bd0cf6e8a3690eb00af`。
- 已审查跟踪文档、包内文本与待提交文件，未发现本地绝对路径或凭据；提交身份为 GitHub noreply。
- 实际发布需提供工坊预览 PNG 与官方 ModUploader 路径；首次成功发布后取得 Core 工坊 ID，供其他 Mod 声明依赖。
