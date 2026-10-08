# Runtime 1.0.47-beta.1 公开 β 发布指南

[English](en/RELEASE_GUIDE.md) · [中文入门](GETTING_STARTED.md)

日期：2026-10-09。Runtime：`1.0.47-beta.1`；Networks：`3.0.0-beta.6`。
项目作者与品牌为 **iwesun**，官方仓库为 `https://github.com/iwesun32/Iwesun.Runtime`。

## 范围

包含 Diagnostics、Data/RecordStore、Networks、WebView2、CLI、RemoteConsole、样例、技能、配置、许可与技术文档，提供 Debug/Release DLL。
Tables 工程不公开、不打包；Web 开发工程不进入当前源码发布副本及安装载荷。数据结构能力来自已有 Data。

本候选汇入既有 β 之后的受管线程退出互斥、停机投递去重、HTTP 请求级取消与 Windows 接口身份修正，并同步 WebView2 当前 DOM/证据实现。
网络公开功能为并行请求、异步响应、统一关联及精确访问接口，不将消费应用的完整业务功能计入此包。

## 获取与升级

从官方 GitHub Releases 获取同一标签的 MSI、完整 ZIP、Networks 本地 NuGet 包及 SHA-256 清单。
当前 MSI 未附 Authenticode 签名，不包含 .NET 或 WebView2 Runtime 的离线安装器；下载来源与哈希都须核对。本次不上传 nuget.org。
Windows MSI 安装到 `C:\Program Files\Iwesun\Runtime`，升级前保存原安装包并按宿主规范协调退出；安装器不会替你更新其他应用已经复制的 DLL。
应用按 `lib/<程序集>/Debug` 或 `Release` 选择一致配置，重新构建并更新宿主自己的发布目录。不要混用旧 DLL。
便携包包含 `app` 和 `data` 两层；不要直接覆盖已有运行数据。执行随包 `verify-runtime-install.ps1` 验证载荷。

Runtime DLL 文件版本为 `1.0.47.0`；Networks 程序集/文件版本保持 `3.0.0.0`，应同时核对产品信息版本 `3.0.0-beta.6`。
不改变程序集文件名及正式类型名。与 `1.0.46` 的旧二进制宿主兼容验证是本候选门禁。

## 许可

本次首次公开源码采用随包 LICENSE 的非商业源码开放条款，保留版权和第三方许可声明。
商业使用及独立修改版发行按相应授权办理。根 README、治理文件和申请表是参与维护入口。
以前以其他有效条款取得的版本不因本次发布而被追溯撤销授权。

## 验证和限制

具体执行结果以 [发布清单](IWESUN_RUNTIME_1.0.47_BETA_RELEASE_MANIFEST.md) 为准。
普通 β 主要验证构建、合同、生命周期场景、二进制兼容和包结构；并不替代目标机器上的真实 WebView2 页面、WFP/精确下一跳、所有网络环境和长期高负载验收。
当前源码中仍有必须按能力查询拒绝的访问组合，不得以系统自动回退代替精确约束。

## 回退

协调停止消费宿主，恢复此前保存的 Runtime 与宿主程序包，核对 DLL 配置和版本后再启动。保留业务运行数据；出现不可逆的数据变更时遵从宿主自身迁移方案。
在完成现场验证前，不将普通 β 作为无需回退准备的生产升级。
