# 完整规范文档发布工具

此工具只整理公开文本，不构建或替换 Runtime 二进制。入口是仓库中的 Git 文件清单，范围包含根规则、`docs`、公开模块文档、宿主模板、CLI 配置、集成技能和许可。Tables、Web 开发工程、私有交接、忽略目录和二进制不进入文档包。

## 检查与目录

```powershell
dotnet run --project scripts/release/DocumentationTool/DocumentationTool.csproj -c Release -- audit
dotnet run --project scripts/release/DocumentationTool/DocumentationTool.csproj -c Release -- catalog
```

`audit` 返回文件数、行数、分组及缺失相对引用的有界摘要，缺失引用使检查失败。它检查 Markdown 相对文件/目录引用，不检查网页连通性或标题锚点；跳过代码块和使用不同安装布局的 `IWESUN_RUNTIME_RELEASE_INDEX.md`。目录必须通过编辑工具更新到 `docs/DOCUMENTATION_CATALOG.md`，不从终端重定向写入。

## 不可覆盖的版本包

完成检查、提交所有文档并创建指向当前提交的文档标签后执行：

```powershell
dotnet run --project scripts/release/DocumentationTool/DocumentationTool.csproj -c Release -- package 1.0.47-beta.1-r1 docs-v1.0.47-r1
```

工具要求干净工作区、标签与 HEAD 相同、没有缺失引用；已存在的同名产物目录会拒绝覆盖。输出到 `artifacts/documentation/Iwesun.Runtime.Documentation.<revision>`，保留文本目录结构并逐文件验证复制哈希，生成 ZIP、逐文件清单、下载 SHA-256，以及包内 HTML 导航。

清单中的文件数只计算来源文本；包内另有生成的 `INDEX.html` 和 `MANIFEST.json`。部分引用指向源码或跨仓库历史路径，文档包不包含这些代码；可从同标签仓库阅读。文档包不等于二进制发布完成或目标宿主验收通过。
