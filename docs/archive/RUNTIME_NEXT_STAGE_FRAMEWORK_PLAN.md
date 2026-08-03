# Iwesun Runtime 下一阶段框架规划

> 日期：2026-07-13  
> 基线：Iwesun Runtime 1.0.19  
> 状态：阶段性总结与证据收集规划，不代表已经批准实施。

## 1. 目的

Runtime 1.0.19 已完成 Diagnostics、CLI v3、受管执行、安全退出、Windows Service、WebView2 和统一发布体系的阶段性整合。下一阶段不立即启动大规模公共框架重构，而是等待 DDNS Snap、AIGateway 及其他消费者深入测试，通过稳定证据集中规划。

本规划用于固定测试基线、统一客户问题证据格式、列出候选演进方向并明确公共框架实施准入条件，避免把单次现象、使用错误或宿主局部问题直接扩大为 Runtime 公共修改。

## 2. 当前稳定基线

### 2.1 Diagnostics

- 标准 Runtime Host 启动、激活和协调退出；
- Debug/Release Diagnostics 独立版本；
- Debug 断点、数值断点、监视点和反射白名单；
- Release 保留输出、日志、只读反射、事件、管道和安全退出；
- RProcess、RThread、RTask 登记、状态转换、清理事件和真实完成后反登记；
- Windows Service 生命周期与 Runtime 主控协调。

### 2.2 CLI v3

- Runtime Frame v2/v3；
- 单次模式和上下文 Shell；
- 默认/显式用户配置、用户别名、增量扩展和 composite；
- Runtime 虚拟路径、上下文变量和多宿主 target；
- 连接、写入、响应和本地取消错误分类；
- 目录型查询安全分页、host.summary 和轻量 Mutation；
- exit/quit 只退出 Shell。

### 2.3 WebView2

- 与 Diagnostics 共用 RuntimeDiagnosticFrame；
- 公共会话、虚拟鼠标、虚拟键盘和输入分发；
- 预编译 C# Program 登记、执行、取消和状态查询；
- CLI v3 WebRuntime 命令；
- 与 Runtime DLL、文档和技能统一版本发布。

### 2.4 发布治理

- 唯一全量打包入口；
- 同步构建 Diagnostics、Data、CLI、WebView2 和 SampleHost；
- DLL 文件版本与 MSI ProductVersion 一致；
- Debug/Release Diagnostics 引用边界和哈希验证；
- 文档、JSON、样例、技能和 WebView2 发布状态共同交付。

## 3. 客户深测证据池

任何候选问题先进入证据池，不直接登记为公共框架缺陷。

必需证据包括：消费者项目、宿主和构建配置；Runtime/CLI/WebView2 版本；ProcessId、InstanceId、启动时间和实际管道；完整 Runtime Frame；requestId、correlationId、SnapshotVersion 和 GeneratedAt；稳定复现步骤和概率；预期与实际状态；跨宿主、跨重启和 Debug/Release 结果；是否涉及业务状态、快照、清理委托或 Program。

问题状态统一为：

- Observed：只记录到一次现象；
- Reproducible：具有稳定复现步骤；
- BoundaryConfirmed：责任边界已确认；
- Accepted：已批准进入公共规划；
- Implemented：代码完成；
- Verified：目标测试矩阵通过；
- Released：进入可比较版本的全量安装包。

证据不足的问题保持 Observed/Unconfirmed。不得把“已接受建议”写成“已经完成”，也不得把错误管道、旧 DLL 或宿主局部状态误写为 Runtime 根因。

## 4. 候选架构演进

### 4.1 有界查询和大数据响应

候选统一模型包括 path、depth、fields、offset、limit、稳定排序、maxBytes、continuationToken、SnapshotVersion 和业务摘要/分页适配器。Diagnostics、WebView2 和代理命令最终共用查询契约。

优先评估单帧分页。只有出现明确的大批量导出需求，才评估多帧分块；流式 JSON 不作为默认方案。

### 4.2 多宿主统一管理

- 明确的目标目录和宿主身份验证；
- 只读目标发现和多目标批量健康检查；
- 单目标 shutdown 与显式广播严格分离；
- 每项结果返回实际管道、ProcessId 和 InstanceId；
- 破坏性命令禁止模糊目标和自动相似匹配。

统一管理不等于共享一条业务管道。每个宿主仍保持独立控制端点。

### 4.3 状态一致性

- 配置状态、全局门控和有效状态明确分离；
- Runtime 状态快照单调版本；
- 宿主业务提供一次性快照委托；
- 顶层状态和阶段状态共享 SnapshotVersion；
- 状态转换事件与最终结果分离；
- Stopped、StoppedWithErrors、TimedOut 等结果模型。

Runtime 不分别反射读取多个变化属性后宣称它们是原子视图。

### 4.4 生命周期和异常退出

重点验证停止期间只读控制面、shutdown 回复与 Monitor 关闭顺序、取消来源、清理失败与超时、受管单元真实退出和反登记，以及 Windows Service SCM Stop、系统 Shutdown 和 CLI shutdown 一致性。

### 4.5 WebView2 深度验证

下一阶段重点收集初始化失败反向回滚、单个 Program 停止失败后的继续清理、执行中 Stop/取消/超时、Ambient 反复启停、pulse 异常、重复 Dispose，以及真实消费宿主加载发布 DLL 的联合生命周期证据。

这些验证需要真实 WebView2 会话和故障注入环境，不在纯 Runtime SampleHost 中伪造完成状态。

### 4.6 发布和消费者兼容

候选增强包括机器可读 Release Manifest、模块版本/能力/验证等级、消费者最低依赖版本、Debug/Release 能力矩阵、DDNS Snap/AIGateway 兼容矩阵、实际加载 DLL 路径检查和发布状态页自动生成。

## 5. 实施准入门槛

候选问题只有同时满足以下条件才进入编码：

1. 客户问题可以稳定复现；
2. 责任边界已经确认；
3. 已建立失败测试或可重复验证脚本；
4. 公共框架修改优于消费者局部修复；
5. 协议兼容和迁移策略明确；
6. Diagnostics、CLI、WebView2 和代理影响已评估；
7. 性能、内存和业务安全边界明确；
8. 有回滚方式；
9. 有 Debug/Release、SampleHost 和真实消费者测试矩阵；
10. 有统一发布验证方案。

## 6. 规划和发布方式

- 客户问题先集中归档，不边测边频繁发布；
- 相互独立的模块分别形成设计和实施计划；
- 公共协议变动集中评审；
- 每批只处理具有共同根因或共同接口的问题；
- 完成后统一更新代码、配置、文档、技能、SampleHost、WebView2 和安装包；
- 未经测试的条目不得从 Accepted 直接改为 Verified；
- 每次正式发布使用新的可比较版本。

## 7. 下一阶段启动条件

满足以下任一条件时启动集中规划评审：

- 两个以上消费者对同一公共边界稳定复现；
- 出现影响业务正确性、内存安全或正常退出的 P0 问题；
- 大数据响应持续超过当前止血分页能力；
- WebView2 故障注入发现公共生命周期缺陷；
- 多宿主批量管理形成明确需求；
- 累积问题能组成边界清晰、可独立发布的版本。

在此之前，1.0.19 作为稳定观察基线。
