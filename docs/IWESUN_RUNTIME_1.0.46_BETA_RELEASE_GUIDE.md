# Iwesun Runtime 1.0.46-beta.1 普通 β 发布说明

> 状态：`GENERAL_BETA_PACKAGED_VALIDATED`  
> Runtime 产品版本：`1.0.46`  
> Runtime 程序集/文件版本：`1.0.46.0`  
> Runtime 产品信息版本：`1.0.46-beta.1`  
> Networks 产品信息版本：`3.0.0-beta.5`

本候选替代已安装的`1.0.43-beta.1`作为下一轮全量 Runtime 测试载荷。`1.0.44-beta.1`因构建
门禁中断而未形成完整候选，仅保留为失败记录，绝不覆盖或分发。`1.0.45-beta.1`是前一轮
完整构建基线；1.0.46以新的不可覆盖目录交付，
不上传公共NuGet源；安装、提交和推送均须另行授权。

## 1. 本轮范围

发布 Diagnostics、Data、Networks、WebView2、CLI、RemoteConsole、SampleHost、配置、技能、样例和
完整技术文档。`modules/Web`、`Iwesun.Runtime.Web.dll`、Web源码及其符号保持调试边界，不进入载荷。

各公共库同时交付顶层 Release 兼容 DLL 与 `Debug`、`Release` 子目录。名称空间、程序集名和公共类型名
不因配置改变；配置与产品信息版本可被外部查询。

## 2. 主要升级

- Networks 更新到`3.0.0-beta.5`：`OpenTcpAsync`提供长期双向流，精确下一跳策略的租约覆盖整个
  连接读写寿命；Packet Capture/Inject 合同为 DNS 代理、NAT、Socket/HTTP代理和三层转发后端预留
  统一边界。
- 网络用户不能再通过程序集版本判断包新旧：预发布3.0系列程序集版本继续为`3.0.0.0`，必须核对
  NuGet/产品信息版本`3.0.0-beta.5`及交付清单。
- WebView2 当前公共实现、CLI和文档随本候选全量收集；其节点树、DOM revision、失效刷新和生命周期
  合同以安装包中的WebView2技术文档为准。
- 1.0.43 已有的协调退出、不可逆准入和大规模分支现场清理合同继续保留；发布器从不可变的上一版
  Runtime 根目录编译旧宿主，再以新 staging 运行它。以1.0.43编译且不重新编译的宿主必须在本候选上
  完成二进制兼容门禁。

Networks详情见[beta.5升级迁移报告](../modules/Networks/docs/03-reference/NETWORKS_3_0_BETA5_UPGRADE_MIGRATION_REPORT.md)。

## 3. β 验收重点

- 宿主从安装目录引用全部 Runtime DLL，确认产品信息为`1.0.46-beta.1`，Networks为`3.0.0-beta.5`。
- AIGateway 等大量分支宿主在一个协调 deadline 内完成清理，退出后不得登记新的业务 UnitId。
- WebView2 验证 DOM 节点树导航后的 revision 刷新、失效 nodeId 自动恢复，以及停止后的补偿清理。
- 使用精确下一跳长期TCP连接时，读取/写入完成前不得释放策略；`DisposeAsync`后再核对策略清理。
- UDP、DNS、Ping、HTTP、DoH 继续按各自网络测试手册完成普通β回归；WFP真实网络矩阵仍是正式版门禁。

## 4. 交付目录

```text
artifacts\packages\Iwesun.Runtime.1.0.46-beta.1\
```

目录包含 MSI、便携 ZIP、Networks nupkg/snupkg、发布说明、发布清单和 SHA-256 清单。

## 5. 正式版阻塞项

- AIGateway 长时多分支清理与内存/句柄稳定性；
- WebView2 长时间 DOM 事件和 STA 压力；
- WFP 双真实网关并发隔离、完整网络恢复矩阵；
- 干净环境安装、升级、卸载和最终版本冻结。

发布门禁结果：根解决方案 Debug/Release/Publish 构建均为0警告、0错误；Data Debug/Release
各101项、Networks各208项、WebView2各53项通过；Diagnostics Debug/Release功能回归和1.0.43
旧二进制宿主兼容均通过；staging 自检及安装示例 Debug/Release 引用校验通过。

实际交付物哈希以发布清单与`SHA256SUMS.txt`为准。本候选尚未安装到 Program Files，亦未提交、推送
或上传公共NuGet源。
