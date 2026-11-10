# 闪耀纹理读取定向检查

使用项目要求的 MegaDot 4.5.1 .NET 编辑器执行：

```powershell
dotnet build tests/PresentationChecks/PresentationChecks.csproj
& $MegaDot --headless --path tests/PresentationChecks
```

检查直接编入生产 ShineCardBorder 源码，使用真实 Godot 图像和纹理对象；NCard 替身只提供与测试无关的 Control 类型表面。覆盖空 Atlas、同资源来源恢复、默认区域、嵌套来源恢复、纹理变化失效及透明来源替换后的重读。失败返回非零退出码。
