# 本体资料导出

本体工作区 sts2 随 Core 发布只读资料。当前来源为测试版 v0.113.0（程序集摘要见 native-game.json 中 nativeSource）。包含716张卡牌、315件遗物、8位先古之民；弃用占位卡和弃用占位遗物从资料表排除。每位先古之民包含三个选项池，共用遗物分别列入，条件与抽取关系单独保存。

## 原生文本与费用

NativeExport.csproj 是独立资料导出 Mod。使用当前版本的真实游戏引用构建：

~~~powershell
dotnet build tools/workbench/native-export/NativeExport.csproj -c Release -p:GameDir=<所选版本游戏目录> -o <忽略目录中的导出Mod产物>
~~~

在独立游戏目录和独立用户目录中加载此 Mod，设置环境变量 SHOUJO_NATIVE_EXPORT 为忽略目录中的输出 JSON。Mod ID 与程序集名为 ShoujoNativeReferenceExport；按 KarenSTS2MCP 当前 README 配置最小化、禁止焦点、silent_test=true、muted=false、随机端口及 --force-steam=off。进入主菜单后自动导出原生 CardModel 的基础／升级费用、文本和关键词，以及 RelicModel 格式化文本。输出记录版本、语言、程序集 SHA256 与逐项错误，导入要求错误为空。

## 元数据、出现规则与三个选项池

用 ILSpy 将同版本 sts2.dll 反编译到忽略目录。import-native-game.py 复用本仓库的 PCK 校验读取器，并调用本地 spire-codex 的模型解析器取得卡池、关联牌与先古遗物引用；文本与费用由原生导出覆盖。

~~~powershell
py tools/workbench/import-native-game.py --game-dir <所选版本游戏目录> --decompiled <反编译目录> --parser-dir <本体模型解析工具目录> --runtime-export <原生导出JSON>
~~~

native-rules.json 维护出现规则、三个选项池及条件。更新版本时对照同版本 ModelDb、ActModel、RunManager、UnlockState、AncientEventModel、各先古之民和相关遗物实现重新核对，然后更新 gameVersion。导入器要求版本、中文语言、程序集摘要相符，并生成 native-game.json 与 native-provenance.json。规则中的遗物名称引用从本体中文本地化解析，附魔沿用本体译名。

发布前运行 workspace-registry.test.cjs，核对每位先古之民恰有三个选项池、候选引用完整、共用池可重复显示及全部写路径被拒绝。再通过本体页核对能量、星星、基础与升级费用和选项池布局。
