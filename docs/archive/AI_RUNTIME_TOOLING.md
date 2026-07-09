# AI 运行时工具

> **状态**: CURRENT | **最后更新**: 2026-07-08

本文描述 Iwesun Runtime 项目提供的面向 AI 的运行时工具，结合两个可复用的运行时表面：

- `Iwesun.Runtime.Diagnostics`：运行时诊断、反射对象访问、监控点、FIFO 输出和事件
- `Iwesun.Runtime.WebView2`：WebRuntime 客户端模型和管道客户端，用于兼容的 WebView2 主机（如 AIGateway）

宿主应用（如 DDNS Snap）当前托管诊断运行时，不托管 WebView2 会话，但共享的 WebView2 客户端打包在 CLI 中，一个 AI 工具可在兼容主机运行时操作两个运行时表面。

## 整体架构

```text
AI / 操作员
  → Iwesun.Runtime.Cli
  → 诊断管道或 WebRuntime 管道
  → 真实运行进程
  → JSON 结果
  → AI 读取结果并决定下一步动作
```

AI 不直接接收命名管道消息，而是运行 CLI，CLI 拥有管道帧处理、命令序列化、超时和 JSON 输出。

## 项目组成

```text
src/Iwesun.Runtime.Diagnostics/
  运行时诊断内核：开关板、FIFO、Hub、反射、监控管道

src/Iwesun.Runtime.WebView2/
  共享 WebRuntime 请求/结果模型和 WebRuntime 管道客户端

src/Iwesun.Runtime.Cli/
  一个可执行 CLI，用于诊断命令和可选的 WebRuntime 命令
```

## 诊断运行时

诊断运行时默认静默：

```text
globalEnabled = false
pipeOutputEnabled = false
fileOutputEnabled = false
all sections = false
all output points = false
```

源路径只检查一个布尔值：

```csharp
RuntimeOutputSwitch.Enabled
```

该布尔值是管道输出和文件输出的 OR。Section 和输出点过滤在后台泵中后续进行。

### 运行时流程

```text
ILogger / RuntimeOutput
  → RuntimeOutputSwitch.Enabled
  → DiagnosticSwitchboard FIFO<string>
  → 后台泵
  → RuntimeDiagnosticHub 事件
  → RuntimeDiagnosticsMonitor 命名管道
  → Iwesun.Runtime.Cli
```

### 反射目标

运行时对象必须有意注册：

```csharp
hub.RegisterObject("agent.worker", worker, new RuntimeDiagnosticObjectAccess
{
    AllowReadAllPublic = true,
    InvokableMembers = ["TriggerCycle", "RunCycleNowForDiagnostics"]
});
```

可写和可调用成员必须白名单化，不得暴露任意方法执行。

## AI 工作流

AI 使用 CLI 与运行时交互的典型工作流：

1. **检查状态**：`status` 确认服务运行中
2. **列出监控点**：`list` 或 `points --section=xxx` 查看可用监控点
3. **聚焦监控**：`focus-point <point-id>` 或 `focus-text <keyword>` 打开相关输出
4. **实时监控**：`monitor` 接收实时事件流
5. **快照查询**：`invoke <target> <method>` 获取对象快照
6. **控制操作**：通过 CLI 发送控制命令（如触发更新、唤醒设备）
7. **恢复静默**：`quiet` 关闭所有监控输出

## 常用 AI 命令

以下示例使用发行版可执行文件 `iwrt.exe`（或同名命令 `iwrt`），不使用源码模式 `dotnet run`。

### 基础检查

```powershell
iwrt status
iwrt list
```

### 聚焦诊断

```powershell
# 打开管道输出
iwrt pipe on

# 聚焦特定监控点
iwrt focus-point agent.worker-cycle

# 聚焦包含关键词的监控点
iwrt focus-text heartbeat

# 聚焦整个 section
iwrt focus-section-points pipeline
```

### 实时监控

```powershell
# 开始实时监控（持续输出事件）
iwrt monitor
```

### 快照查询

```powershell
# 获取根容器快照
iwrt invoke service.root-snapshot GetSnapshot

# 获取诊断开关板状态
iwrt snapshot diagnostics.switchboard
```

### 清理

```powershell
# 恢复静默模式
iwrt quiet
```

## 安全约束

- 反射目标必须显式注册，白名单化可调用成员
- 诊断管道默认不启用，需显式打开
- 密钥不在诊断输出中暴露
- CLI 不执行任意代码，只发送预定义命令并统一包装为 `RuntimeDiagnosticFrame`
- 文件输出默认关闭，避免意外写入敏感数据

## 相关文档

- 运行时诊断 → [RUNTIME_DIAGNOSTICS.md](RUNTIME_DIAGNOSTICS.md)
- CLI 使用 → [IWESUN_RUNTIME_CLI.md](IWESUN_RUNTIME_CLI.md)
- 统一界面原则 → [../04-interface/UNIFIED_INTERFACE.md](../04-interface/UNIFIED_INTERFACE.md)
