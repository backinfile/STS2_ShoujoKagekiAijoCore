# 少女歌剧工作台

[公开工作台](https://junna.17181718.xyz/) 顶栏可切换游戏本体、Core、华恋和纯那。编辑入口沿用原私有长链接。纯那保留现有主台；Core、华恋初始为空，可在编辑页新增内容。游戏本体固定为只读；v0.113.0 资料包含716张卡牌、315件遗物和8位先古之民，每位先古之民按三个选项池展示，共用遗物在对应位置重复列出。卡牌、机制、词条、Power、遗物、药水、事件、契合池、筛选和历史沿用原工作台。

前端、服务端和部署代码统一由 [Core 仓库](https://github.com/backinfile/STS2_ShoujoKagekiAijoCore) 的本目录维护。运行实现迁自 Junna 提交 e6e288bf9080b6951549b6489cd847cf96e64bff；共享绘制与设计领域接口保留原标识，以兼容纯那内容和浏览器草稿。

## MCP

使用 Node 24，先在本目录执行 npm ci。私有 local-save.config.json 包含 remoteURL、remoteToken，分别为原编辑长链接（去掉末尾斜杠）及其密钥；也可用 SHOUJO_WORKBENCH_CONFIG 指向忽略目录内的配置。配置不纳入 Git。

MCP 的 STDIO 入口为 node tools/workbench/mcp-server.cjs，可注册为 shoujo_workbench。已有 junna_workbench 连接继续默认操作纯那。命令行客户端：

~~~powershell
node tools/workbench/mcp-client.cjs workbench_list_mods
node tools/workbench/mcp-client.cjs workbench_get_entity <参数JSON文件>
~~~

参数中的 modId 可为 core、karen、junna 或 sts2（本体只读）；各次调用显式指定，避免工作区混淆。编辑流程见 [AGENTS.md](AGENTS.md)。

## 构建与验证

通过 workbench_get_state（assetURLs: true）导出 bundle，用于原址部署构建：

~~~powershell
node tools/workbench/deploy/build-package.cjs <Junna快照JSON> <忽略目录中的输出路径> <私有连接配置JSON>
~~~

独立只读构建：

~~~powershell
npm --prefix tools/workbench run build -- --snapshot <当前Mod快照JSON> --output <输出目录> --mod junna
~~~

本体数据维护见 [原生资料导出](native-export/README.md)。

多工作区定向回归：node --test tools/workbench/workspace-registry.test.cjs。现有 MCP 保存、冲突及草稿回归按改动选择。服务器路径、备份和原址切换见 [部署说明](deploy/README.md)。
