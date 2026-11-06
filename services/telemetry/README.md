# Core 遥测服务

Python 3.12+ 与 SQLite。源码维护于 services/telemetry，页面位于 web/telemetry，部署入口位于 deploy/telemetry。

## 本地运行

设置 DATABASE_PATH 为隔离数据库文件，RAW_SUBMISSIONS_PATH 为隔离归档目录，READ_KEY 为至少 24 字符的本地读取凭据，PORT 为可用本地端口。执行以下命令。

```powershell
python services/telemetry/app.py
```

服务监听回环地址。GET /healthz 提供健康检查。页面及读取 API 要求 X-Observatory-Access 请求头与 READ_KEY 一致。部署反向代理负责注入读取头及页面访问控制。

| 方法 | 路径 | 用途 |
| --- | --- | --- |
| POST | /api/upload | 原生 RunHistory JSON，8 MiB 上限 |
| POST | /api/upload-session | 旧 r6 会话兼容 |
| POST | /api/errors | v1/v2 报错 JSON，64 KiB 上限 |
| GET | /api/runs、/api/stats | 对局、统计与过滤 |
| GET | /api/runs/{id}、/api/runs/{id}/raw | 历史及原始字节 |
| GET | /api/errors、/api/errors/{id} | 报错聚合及调用栈 |

## 数据格式与兼容

原生 schema_version 保持原含义。Core 对局扩展位于 core_telemetry，其 schema_version 为 2，并携带 core_version、api_version、game_target、game_version、source_mods 和 model_sources。模型来源记录完整 model_id、source_mod 和 source_version。卡牌附加 model_full_id，完整身份保存在载荷与来源映射。原生 Category.Entry 继续使用既有 Entry 统计键，附带额外命名空间的完整 ID 保持独立。旧模型输入及旧历史字段继续解析。Core 耗尽牌使用 core_shine_exhaust_cards，旧 karen_shine_exhaust_cards 继续参与历史统计。

报错 v1 原样接受，v2 增加相同 core_telemetry。旧 mod_version 仍指 Karen 版本，Core 版本独立保存。错误指纹包含版本及来源，稳定 report_id、session_id 和累计 count 保持重复及乱序幂等。409 表示报错身份冲突。

对局身份继续使用原平台、种子、开始时间和玩家席位，Core 元数据不会更换已有对局 ID。重复与冲突原始请求均按 SHA256 保留。原始归档使用受限目录、原子写入、校验读取和每日 tar.gz，容量不足时拒绝确认新数据。

派生事实格式为 3。启动发现旧格式后先备份 SQLite，再在事务内重建 facts，保留原 id、digest、payload 和 received。错误聚合复用已有 environment JSON 列保存 Core 上下文，保留旧列及报错身份。数据库缺少旧增量字段时以幂等 ALTER TABLE 补齐。

```powershell
python services/telemetry/app.py --backup <database-backup>
python services/telemetry/app.py --rebuild
python services/telemetry/app.py --archive-raw
```

对局支持原有筛选，以及 core_version、game_target、source_mod。报错支持原有筛选，以及 core_version、game_target、source_mod。SQL 值使用参数绑定，页面动态字段转义，CSV 保留公式前缀防护。

## 游戏端

TelemetryModule 按 ICoreModule Order 100 初始化。CoreSettings 的 EnableDataUpload、EnableErrorUpload 均默认开启，TelemetryBaseUrl 优先于嵌入默认配置。发布构建通过 TelemetryEndpointPath 注入现行发行配置，嵌入名为 CoreTelemetryEndpoint.json，格式为带 Url 字段的 JSON。真实地址配置由发布方保管。

历史钩子使用原生序列化与 ShineHistory.InjectIntoHistoryJson，队列目录为 Core 数据目录中的 telemetry/pending 和 telemetry/errors。启动时原子迁入旧 Karen pending/errors 队列，保留端点目录、原始载荷及重试记录。对局队列上限为 64 项、64 MiB、单条 8 MiB。报错队列上限为 32 项、2 MiB、单条 64 KiB，每分钟最多一个新请求。按端点分隔、内容去重、原子落盘、有限重试及 Retry-After 均保留。对局 409 完成投递，报错 409 保留为 rejected 文件。开关关闭时暂停对应队列，报错同时停止新采集并清空内存聚合。退出卸载日志回调和定时器。

ExportsModule 按 Order 200 初始化。模型与角色来源使用本体发现快照，PNG 渲染由作者命令启动。

```text
core exportcards [path] [scale]
core exportjson [path]
```

默认输出 user://CoreCardExports/，PNG 缩放范围为 0.25 至 4。完整导出生成基础/升级 PNG、引用卡牌、cards.json、game_info.json 与转义后的 index.html。文件名包含完整模型 ID 的摘要。exportjson 只生成 JSON 资料。

## 定向验证

```powershell
python -m unittest discover -s services/telemetry -p test_core_protocol.py
dotnet run --project tools/telemetry/upload-tests/UploadTests.csproj -- <temporary-queue-root>
dotnet run --project tools/telemetry/error-tests/ErrorTests.csproj -- <temporary-queue-root>
node tools/telemetry/test_web.cjs
```

旧服务回归用例保留在 test_app.py 和 test_errors.py，按本次改动选择具体 unittest 方法。各测试使用临时数据库与本地随机端口。
