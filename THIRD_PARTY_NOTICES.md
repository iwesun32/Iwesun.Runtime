# 第三方组件与许可

Runtime 自有代码的非商业限制不覆盖下列第三方组件，也不削减其原许可证授予的权利。
发布使用框架依赖部署；.NET 运行时和 Microsoft Edge WebView2 Runtime 由用户按其各自条款安装，不在本项目中重新许可。

| 发布依赖 | 版本 | 许可证与来源 |
| --- | --- | --- |
| Microsoft.Extensions.* | 10.0.9 | MIT；Microsoft / .NET 项目 |
| System.Diagnostics.EventLog | 10.0.9 | MIT；Microsoft / .NET 项目 |
| System.ServiceProcess.ServiceController | 10.0.9 | MIT；Microsoft / .NET 项目 |
| Microsoft.Web.WebView2 SDK | 1.0.4078.44 | 包内 Microsoft BSD 三条款许可及 NOTICE |

完整解析列表和原始许可/通知文件位于 [legal/third-party](legal/third-party/)。这些文件随 MSI/ZIP 发布载荷保留。SDK 的许可不替代浏览器 Runtime 的许可。

WiX、测试框架、Windows App SDK 等源码构建工具或可选工程依赖仍遵循其 NuGet 元数据和原始条款；除构建产物实际携带的组件外，不视为 Runtime 自有软件。修改打包范围时必须重新收集对应声明。

生成列表使用 `scripts/release/collect-third-party-notices.ps1`。脚本根据已还原项目的 NuGet 依赖和包内许可证建立清单，遇到新的许可证类型要求复核。
