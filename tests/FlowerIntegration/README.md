# Core 花朵定向探针

按实际游戏版本设置 `GameDir`、`Sts2Api` 与 `CoreRuntimePath` 构建本目录工程，仅安装到隔离测试游戏。

使用 KarenSTS2MCP 的游戏控制台入口运行 `flower_fixture`，再以 `flower_fixture_status` 获取异步结果。`shared-pool` 核对全角色共享池和原文；获得 `KAREN_FLOWER_RELIC` 后，`audit` 验证战后同稀有度候选、排重、重复生成、已有链式组、非战斗奖励及耗尽回退。`prepare`、`reward-check`、`claim-check` 验证原生奖励界面、花朵链条和只领取一个候选；`chest-one` 验证持有花朵时宝箱仍只有原生单个遗物且没有花朵装饰。

该探针随 Core 维护，不进入发行包。