# 三项目重构验证记录

实机基线为 Core 1.1.0 r7、Karen 双版本包，以及保留 Junna 当前开发内容的整合源码快照。公开 API 程序集版本维持 1.0.0.0。

## 通过项

| 项目 | 正式版 v0.107.1 | 测试版 v0.111.0 |
| --- | --- | --- |
| Karen、Junna 单人 | 2 个场景通过 | 2 个场景通过 |
| 双人组合 KK、KJ、JK、JJ | 4 个场景通过 | 4 个场景通过 |
| 当前 95 张实现卡的基础／升级牌面 | 190 个牌面、1,547 条检查通过 | 190 个牌面、1,547 条检查通过 |
| 31 组特效各播放三次 | 93 次通过 | 93 次通过 |
| 完整／低／关闭特效等级 | 三档通过 | 三档通过 |
| 结算、配置、路由、表现、存档、旧存档迁移离线入口 | 六个入口通过 | 六个入口通过 |

矩阵共 12 个场景、20 次角色执行、1,012 条外层断言；每次角色执行还运行 20 项原生结算检查。覆盖实际出牌、重放、箭矢部署与批次结算、同牌主和跨牌主复制、永久牌耗尽归档、升级和独立上限、空壳、重新获得、异步移牌、原生存档及 Packet 往返、同步玩家数据和错误规则指纹拒绝。多人场景比较两端的玩家、牌堆、档案、箭矢和敌方状态，混合角色追加核对能力、遗物和原生随机数快照。最终场景日志没有错误或校验和不一致。

特效等级测试保留原生能量显示与常驻音乐界面；关闭档自定义可见特效和光带绘制均为零。纯特效播放前后的规则、随机数和卡牌快照相同。准备阶段有意生成和移除的测试牌不计作纯特效操作。

Core 引导／择版 18 项、Bootstrap 8 项、包工作流 39 项通过；包工作流的上传器为离线假实现。最终 r7 包通过 Strict 校验，两个角色的 SDK 锁文件指向同一份 Core。r7 的 892 条 API 表面记录与已通过旧版 325 项兼容核对的 r4 完全一致，包含签名、常量和可选参数默认值。

## 性能与资源

| 游戏版本 | 首次创建中位数／最大值 | 帧耗时中位数／P95／最大值 | 超过 33.3 ms | 重复预热 |
| --- | --- | --- | --- | --- |
| 正式版 | 1.53 / 7.51 ms | 16.67 / 16.96 / 20.21 ms | 0 / 8102 帧 | 0.0228 ms |
| 测试版 | 1.40 / 7.32 ms | 16.66 / 16.98 / 36.67 ms | 2 / 8091 帧 | 0.0328 ms |

两版各预热 311 个资源、30 个真实绘制批次，记录 597 个绘制调用。首次播放没有出现“超过 33.3 ms 且超过重复播放峰值 1.5 倍”的独有卡顿。缓存卸载后资源仍有效，重复准备没有重建预热视口；每次测试特效及其分离前景层正常释放。

## 修复与验证边界

完整链路新增修复：复制快照重复设置 Owner、原生克隆清空 DeckVersion 导致永久牌被当作临时牌、重新获得的永久生命周期标记没有跨序列化保留。Junna 删除不再调用的形态切换代码，JA17 按现有牌面修正为 9／12 点伤害。JT02、JK37B、JK49、JA67 的测试期望已按当前工作台规则更新。

早期测试脚本的问题均在最终复验前修正：首次提示处理、MCP 多人控制台测试扩展、读取上一位玩家的旧结果、原生出牌未结束就请求结束回合、复用游戏目录残留测试模组、按枚举正负猜测自定义牌堆、把准备阶段设置闪耀次数当作特效副作用，以及把常驻音乐控件当作特效。最终通过记录保留当前场景，早期尝试另存。

游戏操作统一通过 KarenSTS2MCP。窗口全程最小化、无抢焦点，silent_test=true / muted=false；本机静音，录像保留 engine_mix 内部音轨。两版使用隔离用户目录、随机 MCP 端口和 --force-steam=off。多人测试传输限制在回环地址，MCP 多人控制台桥接只存在于测试插件。已查看合法出牌录像帧和完整／关闭档截图。

Core、Junna 构建无警告；Karen 保留原有 31 个编译警告。三项目完整暂存索引和压缩包内文本经隐私检查。隐私工具对保留的 .example 测试域名统一识别为合成数据。

## Junna 开发内容保留

重构基线包含用户原有未提交源码、卡池和工作台内容。整合时在附加工作树分离独立开发差异；与新公共接口、模型调度和回归探针直接重叠的片段按新结构整合，其余内容继续保留为未提交差异。已分别验证独立提交内容和带原有开发内容的源码：独立内容双版本及探针编译通过，实机矩阵和卡面验证使用保留开发内容的整合快照。

原始状态、完整补丁、未跟踪文件和逐文件哈希保存在 Core 忽略目录的 preserved-junna-state 中；最终工作源码与实机快照逐项核对。原有其他工作树和用户分支保持原状。

## 最终产物 SHA-256

| 项目 | 文件 | SHA-256 |
| --- | --- | --- |
| Core | `core.bundle` | `a6ae75f2b6d1bd09b263569dd883dfe7a3f21e19ebfe894db74139e27cae0a0c` |
| Core | `lib/1.1.0/api/ShoujoKagekiAijoCore.Api.V1.dll` | `c0aba8a8c3a586029d91e790d9be9ea80cc66aa0d232e404d3d629e41b7d5225` |
| Core | `lib/1.1.0/beta/ShoujoKagekiAijoCore.Runtime.Beta.dll` | `059d650e0b11197c7affb8bf0e59d66f7fd257ae9830f3ea51df3a796d6b37cf` |
| Core | `lib/1.1.0/stable/ShoujoKagekiAijoCore.Runtime.Stable.dll` | `65ae20476b345e7e527cab4ca5e85512caeba35c113ce5dfc3b4ec2cf687e754` |
| Core | `resources/1.1.0/Core.Resources.pck` | `e8a8128ecb9ee441d0f369c1232be4c5d3b3d797d460243f5922f36592e68351` |
| Core | `ShoujoKagekiAijoCore.dll` | `08c72b5ce73b03e5e07a5ec1730e81f3d3ec72f0e5aec55b2d6834961e5712f7` |
| Karen | `core.package.lock` | `6514acfe37251083eeba32a7fb6c893fae8957fe43408c44fc23bed748c81f66` |
| Karen | `core.requirements` | `5e0768164283ccf42491b9cbca62653724f76aeb51c3263643539c8711659568` |
| Karen | `lib/ShoujoKagekiAijoKaren.Beta.dll` | `d6212a5e1e2c637e3e99ae5c1397bf7568102808e522bf2657f5b83fc87860ce` |
| Karen | `lib/ShoujoKagekiAijoKaren.Stable.dll` | `b05c581977692bac5eb3f0a8eb33e599a99855e776b1b858e97973b6058ffbba` |
| Karen | `ShoujoKagekiAijoKaren.dll` | `4d4444e12683eb533455d898ea72a6333b0b4d9a259d79c79d386c03b4b2619b` |
| Karen | `ShoujoKagekiAijoKaren.pck` | `ab971ecd4df30f657a28337933493842625c350a916e9c77f7e901c33e3441a4` |
| Junna | `core.package.lock` | `6514acfe37251083eeba32a7fb6c893fae8957fe43408c44fc23bed748c81f66` |
| Junna | `core.requirements` | `7955788108cc4d7bfc816f990610fa46a24b130f8091af292c2dede3dbe78259` |
| Junna | `lib/ShoujoKagekiJunna.Beta.dll` | `268f98e11a800b830de4ba527ffdc171c67dee42018d307b62fc32f8685b0668` |
| Junna | `lib/ShoujoKagekiJunna.Stable.dll` | `c6d265a4332d51006a2bae517a249cdc61ac7f3eb0b5d8ef3e91b4a79c891531` |
| Junna | `ShoujoKagekiJunna.dll` | `e6afcd76c9948cd8eb608d1f082b3c25b387c6191f27e2ed82663dc0d0c1a4a7` |
| Junna | `ShoujoKagekiJunna.pck` | `b83cf1248d01a7c18f7a9cdba0d53474843cb797ccb0c2b7fcf164322e30ce3f` |

## 交付与整合

用户已授权合并回三个 main，并清理本次任务的分支、四个工作树和临时游戏。最终包、JSON 报告、截图、录像及原始 Junna 备份统一保留在 Core 仓库的 out/refactor-verification-20261108/。实际 main 提交、源码一致性和清理结果写入该目录的 integration.json。
