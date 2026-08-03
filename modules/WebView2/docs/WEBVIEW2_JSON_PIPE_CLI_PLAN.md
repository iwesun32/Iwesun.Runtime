# WebView2 JSON、管道申请与 CLI 重规划

## 1. 目标与边界

本文件记录已经落地的协议边界与迁移结果。WebView2 业务动作已纳入 CLI v3、`RuntimeDiagnosticFrame` 和 Runtime 管道租约体系；旧线格式不再兼容。

### 1.1 统一管理不等于共用一条管道

Runtime 负责所有受管管道，而不只负责 RuntimeDiagnostics。Management、WebRuntime 及后续业务模块都通过 `RuntimePipeRegistry` 申请并登记专用命名管道，获得独立的 `RequestedPipeName` 与 `ResolvedPipeName`。

- RuntimeDiagnostics 是诊断控制入口，承接 CLI 和诊断命令。
- Management、WebRuntime 是相互独立的业务管道，不把业务消息塞入 RuntimeDiagnostics。
- Diagnostics Proxy 根据登记租约把统一 JSON frame 转发到目标专用管道。
- 业务宿主持有专用服务端实例、执行命令并在退出时释放租约。

因此，统一的是申请、重名避让、登记、寻址、生命周期、代理转发和 JSON frame；物理管道仍按功能隔离。禁止退回“所有模块共用一个 CLI/Diagnostics 管道并混发不同消息”的旧模式。

```text
CLI 文本 / 业务 API
        ↓
语义命令 JSON
        ↓
RuntimeDiagnosticFrame（权威协议）
        ↓
4 字节 little-endian 长度 + UTF-8 JSON
        ↓
Diagnostics 直接执行 / Proxy 转发 / WebView2 执行
```

## 2. 现状问题

### 2.1 WebView2 JSON

`WebRuntimePipeClient` 当前使用私有 `PipeMessage`：

- `Command` 是数字枚举，缺少可独立理解的语义字段。
- `PayloadJson` 是 JSON 字符串，导致 payload 二次编码。
- 没有统一的 `status`、`data`、`meta`、`requestId` 语义。
- 单次 `ReadAsync` 假定一次读取完整 JSON，不是稳定的字节流边界。
- WebView2 和 Diagnostics 各自维护 JSON 默认选项。

### 2.2 管道申请

`RuntimePipeRegistry.AcquirePipe` 当前不能保证返回的名称唯一：

- 进程内只检查 `Leases`，不等于操作系统实际管道占用。
- `EnsurePipeCreatable` 创建后立即释放，检查和真正启动服务之间有竞争窗口。
- Windows 命名管道默认允许同名多实例，普通创建成功不能代表名称未占用。
- 名称检查和字典写入不是同一个原子操作。
- 现有后缀 `-01` 不符合 `_001` 规范。
- `AnnouncePipe` 可以宣告一个已被其他活动租约使用的管道名。

## 3. JSON 权威协议

WebView2、Diagnostics 和 CLI 共用 `RuntimeDiagnosticFrame`。

### 3.1 请求帧

- `header.schema`：协议版本。
- `header.frameType`：`request`、`response`、`event`。
- `header.requestId`：每次请求唯一。
- `header.correlationId`：响应关联原请求。
- `header.source` / `destination`：转发链路节点。
- `command.domain`：WebView2 使用 `web.runtime`。
- `command.target`：目标实例或后端 ID。
- `command.action`：功能动作。
- `command.args`：类型化 JSON 参数，不再存放 JSON 字符串。

### 3.2 响应帧

- `status.ok`、`status.code`、`status.message`、`status.retryable`。
- `data`：类型化 JSON 结果。
- `extStatus`：功能特有的结构化状态。
- `meta.durationMs`：执行耗时。

新协议不再使用 `PayloadJson`、数字管道命令或只有字符串的 `Error`。

## 4. 管道唯一申请规则

管道登记仿照文件登记的 `Name / TemplatePath / ResolvedPath` 分层，固定使用：

- `Name`：登记键，用于查询、释放和关联所有者。
- `RequestedPipeName`：调用方提交的原始申请名，保留申请意图。
- `ResolvedPipeName`：完成正规化、前缀和重名避让后真正创建的管道名。

JSON 返回不再使用含义模糊的 `BranchId` 或 `PipeName` 代替上述字段。

### 4.1 命名

对请求名 `web.runtime`，候选顺序固定为：

1. `web.runtime`
2. `web.runtime_001`
3. `web.runtime_002`
4. 依次递增

后缀为固定三位十进制；超过 `999` 后继续使用自然数，不截断数字。

### 4.2 原子申请

管道宿主在同一临界区内：

1. 生成下一候选名。
2. 检查本地活动租约。
3. 使用首实例排他语义创建真实 `NamedPipeServerStream`。
4. 保留服务端句柄。
5. 登记租约。
6. 返回同时包含服务端实例和租约快照的所有权对象。

如果排他创建报告已占用，继续尝试下一后缀；其他 IO 错误不得伪装成重名。

### 4.3 所有权

`RuntimePipeRegistry` 只维护租约索引。真正创建管道服务的宿主必须持有专用所有权对象，负责保留排他实例、启停接收循环、释放句柄和注销租约。不向调用者暴露“已检查但未占有”的管道名字符串。

## 5. CLI 重规划

CLI 分为四层：

1. **语法层**：文本分词、别名、PowerShell 风格参数。
2. **语义层**：输出与传输无关的命令对象。
3. **协议层**：把语义命令编译为 `RuntimeDiagnosticFrame`。
4. **传输层**：只处理长度前缀、连接、超时、读写与取消。

约束：

- 组合命令编译成一个 `rtdiag/3.0` batch，不重复连接。
- WebView2 与 Diagnostics 命令使用同一语义模型，只有 `domain`、`target`、`action` 不同。
- CLI 配置 JSON 定义功能映射，不定义另一套管道协议。
- 文本命令、用户别名与组合命令均是适配器，JSON frame 才是稳定接口。
- 输出默认保留完整 frame；人类摘要是可选渲染。
- CLI 始终先连接 RuntimeDiagnostics 控制入口；需要访问 Management 或 WebRuntime 时，由 Proxy 按登记键解析专用管道并转发，不要求 CLI 直接维护每条业务管道连接。

## 6. 已完成的一次性迁移

1. 抽取可共用的 JSON 默认选项、frame 模型和长度前缀传输器。
2. WebView2 使用 Frame 入口；`WebRuntimeControlRequest` 仅作为 Command typed Args，不是旧线协议适配器。
3. WebView2 服务端只支持标准 Frame，不再同时接受旧消息。
4. Diagnostics Proxy 只转发 frame，不再重建 WebView2 私有 JSON。
5. CLI WebRuntime 命令切换到统一 frame。
6. 所有消费方迁移后，删除私有 `PipeMessage`、`PayloadJson` 和数字命令枚举。
7. 实施排他管道宿主与 `_001` 自动避让，用真实多进程竞争测试验证。

## 7. 必须的验证场景

- 同进程并发申请同名管道，返回原名、`_001`、`_002` 且无重复。
- 外部进程已排他占用原名时，申请返回 `_001`。
- 申请完成到服务开始之间，其他进程不能抢占已返回的名称。
- 释放后名称可被新租约重用，旧租约保留非活动历史。
- 请求和响应被拆分为多次字节读写时仍能完整解析。
- payload 保持 JSON 原生类型，不变成带转义字符的 JSON 字符串。
- Proxy 保留 `requestId` / `correlationId`，正确传递错误码和 `retryable`。
- 旧 WebView2 Envelope、私有 PipeMessage 和脚本请求入口均不可用。
