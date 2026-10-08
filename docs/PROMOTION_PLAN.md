# iwesun Runtime 推广计划与首发文案

[English](en/PROMOTION.md) · [项目简介](../README.md) · [用户入门](GETTING_STARTED.md)

更新：2026-10-09。以下是建议与可用文案，不表示已经向外部社区发帖、获得收录或建立合作。

## 主线：先让目标开发者跑起来

主打一个具体场景：**让 AI 通过统一 CLI 观察、定位并验证 .NET 服务问题，同时规范服务生命周期。** Data、WebView2 和网络接口作为同一项目中的可独立理解能力，不把首发文章写成无重点的功能清单。
第一批读者优先选择 .NET/Windows Service 开发者、AI 编程工具使用者和需要 WebView2 自动化的应用作者。

## 建议顺序

1. **发布落地页**：中英文 README、可点击关键词、GitHub Topics、当前 Release、许可边界、简单运行步骤、测试范围与问题入口。GitHub Topics 能把项目关联到相应主题页面，但不保证搜索排名。[GitHub 官方说明](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/classifying-your-repository-with-topics)
2. **一篇可复现演示**：用 SampleHost 展示启动、查询受管任务、白名单状态观察、定位一个明确问题、验证并协调退出。可以准备 60–90 秒短演示和对应命令清单；演示尚未录制，不用概念图冒充运行证据。
3. **中文首发**：建议在 [V2EX「分享创造」](https://www.v2ex.com/go/create) 发一篇真实开发经历与可运行样例介绍；该节点面向作者展示自己的作品。先确认账号与最新社区规则，不重复跨节点刷屏。
4. **英文首发**：英文文档与样例顺畅后考虑 [Show HN](https://news.ycombinator.com/showhn.html)。官方要求作者实际参与、作品可供尝试并愿意在场讨论；不要只投介绍页，不求赞或组织投票。由作者确认并参与答疑。
5. **后续内容**：根据真实问题分别写服务退出、RecordStore 使用、WebView2 DOM 变化、网络请求关联的技术文章。每篇都给可复现案例、版本与限制，只在允许自荐的相关社区分享。

不建议第一阶段购买广告、群发私信、批量申请榜单或刷 Star。先确认陌生用户能完成首次运行，并根据实际问题改进入门文档。
AI 可继续准备教程、审查问题和处理申请；外部账号发布、代言、付费推广和合作承诺另需明确授权。

## 首发文案：中文

标题：**iwesun Runtime：让 AI 通过 CLI 调试 .NET 服务，首个公开 β 版**

我把日常用于运行时诊断与服务管理的公共基础库整理成了 iwesun Runtime，现提供首个公开 β 版本。

它的重点是让接入并获授权的 AI/运维工具，通过结构化 CLI 查看程序状态、跟踪业务执行、访问白名单成员并验证调试结果，同时统一受管进程、线程、任务和服务退出流程。
配套提供 Data/RecordStore 小型关系表结构、WebView2 控制，以及支持并行提交和异步响应的网络接口。

仓库包含中英文介绍与入门、示例、测试、Debug/Release DLL、MSI/ZIP 和 AI 接入技能。当前是 β，真实浏览器、精确网络访问和长期压力仍需目标环境验收。

许可为非商业源码开放：保留声明的非商业使用免费，商业使用另行授权，独立修改版发行须先申请认可；不是 OSI 开源许可。

欢迎试跑示例、提交可复现问题，或申请加入工作组。
项目：[iwesun Runtime](https://github.com/iwesun32/Iwesun.Runtime) · [下载](https://github.com/iwesun32/Iwesun.Runtime/releases) · [使用入门](https://github.com/iwesun32/Iwesun.Runtime/blob/main/docs/GETTING_STARTED.md)。

## 关键词

中文检索短语：.NET AI 自动调试、C# 运行时诊断、Windows 服务生命周期、受管线程任务、RecordStore 内存关系表、WebView2 CLI 自动化、并行网络请求。
英文主题：[dotnet](https://github.com/topics/dotnet)、[csharp](https://github.com/topics/csharp)、[ai-debugging](https://github.com/topics/ai-debugging)、[diagnostics](https://github.com/topics/diagnostics)、[windows-service](https://github.com/topics/windows-service)、[webview2](https://github.com/topics/webview2)、[networking](https://github.com/topics/networking)、[source-available](https://github.com/topics/source-available)。

名称始终使用 **iwesun Runtime**。不声称 OpenAI 官方维护，不使用“完全无人值守”“支持所有网络环境”“替代所有数据库”等未经验证的承诺。
