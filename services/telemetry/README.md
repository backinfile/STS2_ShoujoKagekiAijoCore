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

## 报错容量与压缩

默认接收上限为 20,000 类错误、250,000 条报告，可通过 `ERROR_MAX_GROUPS`、`ERROR_MAX_REPORTS` 配置。完整环境按摘要共享并 gzip 压缩，筛选元数据单独存放。首次迁移会生成 `backups/before-error-compression-*.sqlite3` 并在事务内完成。报告身份、累计计数、乱序重试和 Mod/Core 版本信息保持原有含义。

报错页面使用 `GET /api/errors?compact=1` 获取摘要，打开详情时再还原环境和调用栈。容量保护、每日预算及请求限流继续生效。

## 旧报错生命周期

普通报错连续 7 天没有有效新上报后淘汰正文，明确忽略的指纹为 3 天。`ERROR_RETENTION_DAYS` 和 `ERROR_IGNORED_RETENTION_DAYS` 可配置为 1 至 3650 天。`last_activity` 使用服务端实际接受新报告或累计次数增长的时间，重复及乱序重试保持原值。历史日志补传从本次有效接收开始计时。客户端 `first_seen`、`last_seen` 保持事件时间含义。

首次升级先生成 `backups/before-error-retention-*.sqlite3`。旧库缺少计数更新的接收时间，所有既有类别从迁移时起获得完整宽限期。迁移和清理均使用事务，上传、策略导入和清理通过同一 SQLite 写锁串行提交。

待处理、待上线核验及待原包核验记录按指纹保留。管理员通过 `--sync-error-policy <policy-file>` 原子导入巡检策略。格式如下。

```json
{
  "schema_version": 1,
  "generated_at": 1794272400,
  "valid_until": 1794294000,
  "groups": {
    "<64-character-fingerprint>": {"status": "protected", "closed_at": 0}
  }
}
```

`status` 支持 `ordinary`、`ignored`、`protected`、`closed`。结案条目记录稳定的 Unix 秒 `closed_at`，按最近服务端活动与结案时间的较晚值，再保留普通报错期限。已保护的映射只能显式结案或继续保护。已结案的时间必须稳定，重新打开时改为保护。缺失或过期策略、未映射指纹、未知活动时间均保留。策略最大有效期 72 小时，Karen 导出器使用成功巡检后 6 小时，保证次日巡检失败时旧忽略策略已经失效。`--suspend-error-retention` 立即使清理策略失效，成功重导入后恢复。

```powershell
python services/telemetry/app.py --prune-errors --dry-run
python services/telemetry/app.py --prune-errors
```

维护命令读取 `DATABASE_PATH`。首次创建 `Store` 会执行必要的数据库迁移，生产只读评估使用 SQLite 只读连接，迁移与清理预览在副本执行。dry-run 输出候选和保留原因、正文 UTF-8 字节数、扣除保留摘要后的预计清除字节数、报告条数、可移除环境及压缩字节数、空闲页和耗时。正式执行额外输出实际淘汰类别、删除报告及环境数量。事务内保存最近 90 次成功结果，异常记录 `failure_scope=transaction_rolled_back` 并由服务日志保留。

清理将正文缩为首行最多 160 字的摘要，清空调用栈和环境引用。完整报告的会话、首次接收及每报告时间明细被移除，报告 ID、累计次数、身份哈希保存在 `error_receipts`，会话只保留不可逆摘要供精确去重。环境只在确认没有类别引用时删除。旧报告重试保持幂等，累计数增长或新报告复发时恢复完整可观察状态，同时保留历史累计、首次和最近事件时间。旧报告重新建立完整明细时，`received=0` 表示原接收时间已淘汰，服务端活动仍记录当前时间，累计次数续传继续免于新报告的每日预算。

`stored_groups`、`stored_reports` 和容量限制统计活动类别及完整报告。`retired_groups`、`receipt_reports` 单独统计已淘汰摘要和重试凭据。历史摘要与去重状态继续计入数据库实际大小。默认列表只列活动类别。指纹详情返回 `data_state=retired`、`retired_at` 和 `retention_reason`，页面显示“正文已淘汰”。

巡检使用 `GET /api/errors?inventory=1`，读取活动和已淘汰指纹的轻量清单。每页最多 500 项，下一页携带 `after=<next>` 与首屏 `revision`。有效上传或清理改变清单后返回 `inventory_changed`，采集器重新读取完整分页。历史指纹缺失、计数倒退、访问失败及分页不完整均不能推进成功基线。详情采集可合并同指纹的较新计数和淘汰状态，完整性边界以已通过校验的清单修订号为准。修订号之后才出现的新指纹留给下一次巡检，服务端对其保守保留。

### 空间回收

清理释放的 SQLite 页会供后续写入复用。结果中的 `space_after` 给出实际空闲页和字节数。空闲空间同时达到 16 MiB、数据库页数的 20% 时，`vacuum_recommended` 为真。维护窗口先备份，停止采集服务和清理定时器，再使用 SQLite `VACUUM` 回收文件空间，完成后核对 `PRAGMA integrity_check`、类别及报告数量并恢复服务。预留至少一个数据库文件大小的可用空间。每日清理只做生命周期淘汰和空闲页统计。
