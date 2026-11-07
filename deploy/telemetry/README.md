# 遥测部署产物

从仓库生成完整服务目录。

```powershell
python deploy/telemetry/build_release.py --output <new-release-directory>
```

输出含 app.py、error_reports.py、protocol.py、raw_archive.py、web 和 install.py。输出目录必须尚不存在。

发布方将目录作为既有服务的一个新 release 保存，在部署主机运行 install.py。已有 access.json 中的页面路径、上传路径、读取凭据、数据库与服务身份继续使用。首次安装通过 --public-origin 或 TELEMETRY_PUBLIC_ORIGIN 提供 HTTPS 域名，之后从既有 upload_url 恢复。安装脚本保留反向代理路径映射、页面控制、限流、SQLite 备份、原始归档及监控服务。

真实 access.json、service.env、客户端 endpoint 和生产数据独立保管。仓库只维护配置格式及生成程序。部署前备份数据库与归档，记录发行提交。回退选择同时识别旧、新请求格式的服务发行目录。

## Web 资料

安装 Pillow 后，从作者导出的目录构建卡牌缩略图及完整身份索引。

```powershell
python tools/telemetry/build_assets.py --export <author-export-directory>
```

可重复提供 --export 合并多个来源，或通过 --web 指向隔离资料目录。原生资料可使用 export_native_metadata.ps1 从本体程序集生成定义，再通过 build_native_assets.py 读取本体 PCK 生成图片。所有模型渲染与图片处理均发生在作者资料构建阶段。

## 页面加载与 PWA

页面按当前视图请求统计或分页对局，完整卡牌和遗物资料在打开对局详情时加载。卡牌按 60 张分批显示，CSV 仍导出全部筛选结果。手机端支持收起筛选、底部导航和独立详情面板。

PWA 清单及 Worker 使用相对作用域，沿用现有页面访问路径。支持安装提示的浏览器显示“安装”；iOS 使用 Safari 的“添加到主屏幕”。发行构建按 Web 文件摘要生成缓存版本，新版就绪后显示“更新”。静态缓存最多保留 80 项，按使用加载；API 与受保护页面始终请求服务端，断网导航显示重新连接入口。

服务端为文本响应提供 gzip，静态资源使用私有缓存和 ETag，API 保持 `no-store`。安装脚本保留应用缓存头，并允许同源 Worker 和清单；更新既有部署时需同时应用新安装脚本。

## 错误容量与存储

错误默认上限为 20,000 类、250,000 条报告，部署时可用 `--error-max-groups`、`--error-max-reports` 调整，既有容量配置会保留。对应服务环境变量为 `ERROR_MAX_GROUPS`、`ERROR_MAX_REPORTS`；最大可配 100,000 类、1,000,000 条，数据库仍受 1 GiB 容量保护。页面同时显示类别和报告占用。

相同的完整环境按内容摘要去重并以 gzip 保存，错误记录通过摘要引用；筛选使用独立轻量元数据，调用栈保持全文可搜索。首次升级自动备份数据库并在事务内迁移环境，保留指纹、报告 ID、会话和累计次数。`api/errors?compact=1` 只返回列表摘要，详情接口还原完整环境；原列表格式继续支持。

生产升级前使用数据库副本核对记录与内容，再进行部署；迁移后的 SQLite 空闲页可通过维护窗口中的 `VACUUM` 回收。回退需使用支持 `error_environments` 的发行，或在停止服务后恢复升级前备份。
