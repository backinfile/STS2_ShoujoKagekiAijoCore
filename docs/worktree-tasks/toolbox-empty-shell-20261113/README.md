# 工具箱空壳候选与本地化修复

## 用户原始输入

> 开局工具箱出现了这张

附带截图：工具箱三张候选中的右侧牌显示 cards.SHOUJOKAGEKI-CORE_EMPTY_SHELL.title 与对应 description 键，0费技能、消耗；截图原件保存在忽略的任务证据目录。

> 你查查修复下

初始工作区约定原文见 user-instructions.md；用户截图机器路径以用途占位符处理。

> 最后遇到机械师的时候游戏卡住了，你看看啥问题，修下，我记得之前修过

## 起始状态

原分支 main，原 HEAD 18ce0fe3319d982f5568003b9b6d7104b57864f7，原检出干净。
工作树 ../worktrees/STS2_ShoujoKagekiAijoCore/toolbox-empty-shell-20261113。

## 简要计划

1. 建立真实工具箱候选与空壳本地化的定向失败回路。
2. 以最小改动修复模型池与本地化接入，保持空壳复制／存档行为。
3. 最小必要回归与 MCP 实机验证，保留截图、日志、最终包哈希。
4. 整理任务提交，同步 main 后请求具体合并与清理确认。

## 当前状态

修复与定向验证完成，用户已明确批准合并至两仓库main、备份后安装Karen0.1.9完整包（现有配套Core原位更新为1.9.3），并清理本次两个工作树及任务分支。实际执行结果记录于组合交付目录的installation-result.json。

## 根因与变更

1. CoreEmptyShell 虽然是 Token 稀有度且隐藏图鉴，仍通过 Pool 属性注册到了 ColorlessCardPool。原生工具箱直接使用该池的 GetUnlockedCards，未按 Token 稀有度排除它，因此空壳进入候选。
2. 实际模型 ID 为 SHOUJOKAGEKI-CORE_EMPTY_SHELL，本地化只存在 CORE_EMPTY_SHELL.* 键。中英日三语均补齐实际键，值沿用原文，并保留旧键。没有修改模型 ID、牌效或翻译正文。
3. 空壳改为注册 TokenCardPool，无色视觉池仍保留。Core 发行版本升至1.9.3；Karen 角色 DLL 不需变更，完整 Karen 包附带同一 Core1.9.3。
4. 新增真实游戏探针 tests/ToolboxIntegration，读取工具箱实际候选源、现有模型 ID、三语打包资源及空壳名称／说明；更新相关说明。

## 定向验证

- 有效基线为 Core1.9.2，游戏v0.113.0：core_toolbox_check 返回错误；候选数55、toolboxContainsShell=true、localized=false，牌名及说明与用户截图中的键一致。
- 同包的“闪耀的再生产”显示正常，排除资源包整体漏载。
- Core1.9.3同一路径：候选数54、toolboxContainsShell=false、localized=true，Token池及原有无色视觉池正确；模型 ID 保持不变，中英日实际PCK内键均存在且值与原文一致。
- 真实工具箱两次三选一均不含空壳。最终清晰截图候选为箭雨、连射、拳斗。
- 合法生成空壳后名称为“空壳”，正文为原生“消耗”；0费打出后能量3→3、敌方生命不变，卡牌正确移入消耗堆。相关截图已查看，内部音轨录像完成且0丢帧。
- 真实Beta引用上的 ResolutionChecks --copies-only 通过，包含耗尽牌复制仍生成空壳、独立临时预算和永久预算回写。
- 正式版v0.107.1引用及测试版v0.113.0引用均编译通过，0警告0错误；正式版是编译验证，实机使用v0.113.0。
- FoundationChecks 20项通过；Core完整包Strict校验通过；最终实机日志没有ERROR、Exception或Invalid Task ID。
- 探针初次准备遗漏core.requirements而启动失败，补齐独立探针目录与需求声明后才开始有效红测；该准备失败不计入用户缺陷复现。

## 交付与安装

- 交付目录：artifacts/task-deliverables/toolbox-empty-shell-20261113/，包含Core1.9.3完整包、Karen0.1.8附带Core1.9.3的完整包、ZIP、逐文件哈希、原始用户截图和定向证据。
- 沿用现有Karen7文件及其能力需求，通过官方SDK重新生成配套锁文件，未额外引入第二个Core实例。
- Steam本体与其他测试进程未修改或终止。安装待确认；更新需重启游戏后生效。
- 原检出main与原有任务工作树保持原状。

## 建筑师后续

用户新增建筑师结局卡住修复要求，空壳提交与包继续保留。该缺陷在 <Karen项目>/docs/worktree-tasks/architect-ending-20261113/README.md 独立记录；合并与安装尚未执行。

华恋结局修复已完成：第二组对话缺少r标记，BaseLib重建后丢失可重复属性，历史胜利2次以上没有候选。Karen0.1.9补齐三语21处键名，正文不变；用户进阶10原局的字节一致副本已成功恢复并完成胜利，原始存档未改。与Core1.9.3空壳修复组成Karen完整更新包，准备统一确认两仓库合并、安装及清理。

## 用户合并、安装与清理授权

<send_user_message_question_reply>
[{"questionItemId":"[\"request_user_input_async\",\"call_fefd9aa5c61547b3a1221f450b97a52a\",0]","question":"是否将 Core c42d98a、Karen 691fbd3 合并回各自 main，备份后安装 Karen 0.1.9 完整包（将现有配套 Core 原位更新到 1.9.3），并清理本次 toolbox-empty-shell、architect-ending 两个工作树及任务分支？","answer":"合并、安装并清理（推荐）"}]
</send_user_message_question_reply>

授权所列提交为确认时的修复提交；确认后仅追加本授权记录并整理任务文档，生产文件和已验证包保持不变。
