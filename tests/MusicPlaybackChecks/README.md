# 音乐播放器内存恢复定向检查

```powershell
dotnet build tests/MusicPlaybackChecks/MusicPlaybackChecks.csproj
& $MegaDot --headless --path tests/MusicPlaybackChecks
```

直接编入生产 `KarenMusicPlayback`，使用实际 Godot 音频节点和静音 WAV。外围替身只提供配置、音量路由、演出优先级和资源入口。覆盖本次启动内重建后的播放、暂停、进度、临时演出中断恢复及关闭／目录更换。退出／继续的游戏生命周期另通过真实游戏 MCP 检查。
