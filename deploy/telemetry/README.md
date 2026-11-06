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
