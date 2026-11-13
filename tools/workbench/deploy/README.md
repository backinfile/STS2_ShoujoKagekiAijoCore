# 原址部署

少女歌剧工作台沿用 [原公开站点](https://junna.17181718.xyz/)、原私有编辑长链接、junna-workbench.service、回环网关与后台端口。代码由 Core 的 tools/workbench 生成。运行目录和数据目录分别维护，发布包不覆盖在线数据库。

| 位置 | 用途 |
| --- | --- |
| <workbench-service-directory> | 指向当前程序发布目录的链接 |
| <workbench-state-directory>/state.sqlite | 原 Junna 主台 |
| <workbench-state-directory>/mods/core.sqlite | Core 工作区 |
| <workbench-state-directory>/mods/karen.sqlite | 华恋工作区 |
| <workbench-private-backend-config> | 原后台端口和令牌 |
| <workbench-web-directory>/current | 四个公开工作区的当前快照 |
| <workbench-web-directory>/assets | 共享哈希资产 |

## 构建

1. 通过 MCP 读取 workbench_get_state，指定 assetURLs: true，将返回的 bundle 连同 versions 保存到忽略目录。纯那导出仍使用原工作区编号。
2. 依据 connection.example.json 准备私有构建配置，复用原 origin、prefix、secret。stateDirectory、webDirectory、backendConfig 可覆盖运行默认位置。
3. 在仓库根目录执行 node tools/workbench/deploy/build-package.cjs <Junna快照> <新发布包目录> <私有连接配置>。
4. 上传包到新的程序发布目录。安装锁定依赖：npm ci --omit=dev --ignore-scripts --no-audit --no-fund；或将 node_modules 链接到已核对的旧发布目录真实路径。

公开页和编辑页均包含 Mod 切换。本体的 native-game.json 随源码和发布包维护，只读服务不创建本体数据库；页面无编辑入口，服务端拒绝写入和上传。根路径默认纯那，其他工作区使用 mods/core/、mods/karen/ 与 mods/sts2/；编辑路径仍位于同一私有前缀下。Nginx 的原静态根目录和前缀代理可直接复用。公开资源保持 GET/HEAD，编辑 MCP 与上传继续鉴权。私有 relay.config.json 和编辑页保存在程序目录，不放入公开根目录。

## 验证与切换

1. 使用 SQLite backup 从运行主台生成隔离副本；为隔离服务使用独立端口、连接配置、状态目录和 Web 目录，验证三个页面、各 Mod 的保存、请求重试、版本冲突与快照发布。
2. 校验上传包 SHA256，将 public/assets 合入共享资产目录，保证服务用户能够写入状态和 Web 目录；程序文件由部署用户持有，私有配置限制为服务组可读。
3. 停止原服务，在同一窗口备份最新 SQLite，并记录主台各表内容摘要、当前程序链接和公开快照链接。
4. 原子切换程序链接，安装本目录的同名 systemd 单元，重启原服务。首次启动只为缺失的 Core、Karen 数据库建立空工作区；缺失或错误的 Junna 数据库会拒绝启动。
5. 核对公开三个入口、编辑页、各工作区 MCP、无效令牌拒绝、主台条目版本、历史与回执摘要。公开发布进程会读取当前数据库，按 revision 原子更新整组快照。
6. 若切换失败，将程序链接和 systemd 单元恢复到已记录的上一版本后重启；已有数据库保持原样，保留备份供核对。

工作区登记在 mods.cjs，数据库、草稿、版本和 requestId 按工作区隔离。资源按内容哈希共用，资源映射仍分别保存在各工作区数据库。正常退出会关闭网关、MCP、发布进程和数据库。
