# 闪耀牌奖励定向游戏检查

在隔离游戏中加载本探针、Core 1.8.0、Junna 与 KarenSTS2MCP。组合场景另加载 Karen，独立场景仅安装 Core + Junna。按实际版本设置 `GameDir`、`CoreApiPath` 和 `Sts2Api` 编译。

- `shine_reward_fixture run` 执行候选池、权重、回退、限制、旧 ID、序列化和标记检查，`report` 读取结果。
- `prepare` 准备最后一次闪耀的升级牌和公共再生产；随后通过合法出牌与选牌执行，`inspect` 读取永久牌组、手牌、档案和奖励状态。
- `prepare-rewards` 准备两份战后闪耀奖励及正常战斗收尾；`show-rewards` 直接展示两份原生自定义闪耀奖励，分别验证分组选择与领取。

使用独立用户目录、随机 MCP 端口、最小化静默启动和真实绘制，将日志、截图、录像及最终包哈希保存在 `out/tmp/<task>/`。测试源码不进入正式发行包。
