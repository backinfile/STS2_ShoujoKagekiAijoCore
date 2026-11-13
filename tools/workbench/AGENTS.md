# 工作台维护与内容编辑

- 本目录是少女歌剧工作台的维护源，前端、MCP、SQLite 和部署脚本在 Core 修改。Junna 的旧目录保留为兼容客户端和历史基线。
- 内容统一通过 MCP 读取与提交。先调用 workbench_list_mods 确认工作区，后续为每次调用指定 modId：core、karen、junna、sts2。sts2 为固定只读资料，提交、恢复、图片上传均拒绝；资料更新通过核对游戏原生导出后发布源码完成。省略 modId 兼容原纯那入口。
- 先 workbench_get_entity 读取内容和 version，再以 expectedVersion 提交 workbench_commit。每次新提交使用新的 UUID requestId，重试沿用原请求与编号。
- 更新已有条目使用 updates/unset，并在 expectedFields 中提供所改字段原值，原先缺失的字段列入 expectedAbsent。创建、删除和整体替换使用 before/after。
- 同条目不同字段可自动合并；同字段冲突整批拒绝。保留草稿，读取当前内容，合并后以新版本和新请求编号提交。相关条目放入同一事务。
- 主台在原服务器持久化；源码构建中的快照仅用于生成页面，部署沿用在线数据库。私有连接配置、数据库、日志、导出与凭据放入 Git 忽略目录。
- Junna 设计沿用其仓库 docs/design/AGENTS.md 与工作台 styles 中的已确认约定；迁移工作台不改变牌名、牌效、数值和本地化。
- 日常只运行与改动直接相关的验证；多工作区路由与隔离回归为 workspace-registry.test.cjs。部署流程见 [deploy/README.md](deploy/README.md)。
