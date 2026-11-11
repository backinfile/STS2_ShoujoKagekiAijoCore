# 游戏内音乐恢复探针

按目标游戏版本构建 `MusicSessionIntegration.csproj`，传入 `GameDir` 和 `Sts2Api`。只安装到隔离测试游戏，清单依赖 BaseLib、Core、Karen、KarenSTS2MCP，并通过 Core SDK 生成 `core.requirements`。

通过游戏 MCP 执行 `core_music_session status|play|pause|seek|exit`。`play` 选择曲库第 4 首，`seek` 跳至 20 秒，`pause` 切换播放状态，`exit` 调用真实返回主菜单入口。分别在播放和暂停时退出，再通过 MCP 的 `menu_select` 选择 `continue`，核对曲目、播放／暂停状态与进度。

该探针只读取本地播放器并驱动播放控制，不放入发行包。
