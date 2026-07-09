### 2.5 协议统一（v2）
### 2.5.1 执行策略
# CLI 统一命令协议设计文档

> **状态**: ACTIVE | **日期**: 2026-07-09 | **版本**: v2.0

---

## 1. 当前问题分析

### 1.1 命令格式混乱

当前 CLI 命令存在以下不一致：

| 问题 | 示例 |
|------|------|
| 命名风格混杂 | `set-pipe` / `setPipe` / `setPipeOutput` / `setInput` / `setfileoutput` / `setFifoDepth` |
| 缩写不统一 | `s` / `ss` / `ssn` / `sg` / `sp` — 无规律 |
| 连字符不一致 | `bp-list` / `resumeAll` / `queryPoints` / `set-pipe` / `focus-point` |
| 别名过多 | `focus-point` / `fp` / `focus` — 三重命名 |
| 缩写过长 | `breakpoint-list` / `breakpoint-enable` — 冗长 |
| 无统一语法 | `set-pipe true`、`enable dns`、`bp-enable bp-001` — 参数位置不一致 |

### 1.2 架构问题

- 基础命令和复合命令在同一个 switch 中，无法扩展
- 复合命令（`status`、`quiet`、`focus-*`）硬编码在 `Program.cs`
- 配置文件只是正则匹配，不支持命令组合
- 新命令只能通过修改 CLI 源码添加

---

## 2. 新协议设计原则

| 原则 | 说明 |
|------|------|
| **统一语法** | 所有命令遵循 `[namespace.]verb [target] [args...]` 格式 |
| **两级分层** | 基础命令（原子操作） + 复合命令（JSON 配置组合） |
| **简洁易记** | 动词优先，短别名，一致连字符 |
| **完备可扩展** | 新命令只需 JSON 配置，无需改 CLI 源码 |
| **管道无关** | 同一命令语法适用于 diagnostics / webview2 / management 管道 |

---

## 2.0 分层边界（核心约束）

1. 管道层是协议层：只认 JSON 帧，且 JSON 是真实执行契约。
2. CLI 层是交互层：采用接近 PowerShell 的命令语法，仅做人机输入适配。
3. 设计顺序固定：先定义 JSON 帧模型与语义，再定义 CLI 如何包装到 JSON。
4. 任何 CLI 语法优化都不得反向改变 JSON 协议字段语义。

### 2.0.1 双层职责

| 层 | 输入 | 输出 | 职责 |
|---|---|---|---|
| CLI 语法层 | 文本命令 | 语义命令 | 解析动词、目标、参数、别名 |
| 管道协议层 | 语义命令 | JSON 帧 | 封装 header/command/status/data 等协议字段 |

### 2.0.2 从 CLI 到 JSON 的固定流程

CLI 文本 -> 语义命令 -> JSON envelope -> 命名管道发送

其中：
- 语义命令是中间边界模型，禁止跳过该层直接由字符串拼 JSON。
- JSON envelope 必须可独立被其他客户端复用，不依赖 CLI 上下文。

---

## 2.1 命令分类（先分类，再设计结构）

> 本节定义的是管道协议层的分类，不是 CLI 语法别名。

### 2.1.1 一级类别（category）

| 类别 | category 值 | 语义 | 典型命令 |
|------|-------------|------|----------|
| 指令类 | `instruction` | 改变宿主状态、触发动作 | `sw.enable`、`bp.resume-all`、`host.stop` |
| 检索类 | `query` | 只读查询，不改变状态 | `sw.list`、`host.info` |
| 报告类 | `report` | 请求汇总/诊断报告（通常较重） | `status`、`debug-dns`(汇总模式) |
| 流类 | `stream` | 订阅、拉取、消费事件流 | `host.events`、未来 `events.subscribe` |
| 控制类 | `control` | 会话与协议控制动作 | `hello`、`ping`、`cancel` |
| 系统类 | `system` | 运行时/管理级动作 | `shutdown`、`rescanHost` |

### 2.1.2 二级操作（operation）

每个 `category` 下统一使用有限操作词，避免 action 漫游：

| category | 允许 operation |
|----------|----------------|
| `instruction` | `set` / `enable` / `disable` / `invoke` / `resume` / `stop` |
| `query` | `get` / `list` / `inspect` / `navigate` |
| `report` | `summary` / `diagnose` / `audit` |
| `stream` | `pull` / `subscribe` / `unsubscribe` / `ack` |
| `control` | `hello` / `ping` / `cancel` / `capabilities` |
| `system` | `shutdown` / `reload` / `save` / `rescan` |

### 2.1.3 分类约束

1. `query` 不得产生可观测副作用（日志除外）。
2. `instruction` 必须返回明确执行状态，禁止 silent mutate。
3. `stream` 响应可分片，必须包含游标或确认点。
4. `report` 可以内部调用多条 query/instruction，但对外只暴露单一 request/response。

---

## 2.2 统一管道帧结构（Wire Envelope v2）

### 2.2.1 传输层不变

- 仍使用 4-byte little-endian length-prefix + UTF-8 JSON。
- 本次升级只替换 JSON 载荷结构，不改命名管道读写流程。

### 2.2.2 Frame 顶层结构

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "request",
    "category": "query",
    "operation": "list",
    "requestId": "01J2...",
    "correlationId": "01J2...",
    "timestamp": "2026-07-09T10:30:00Z",
    "source": "cli",
    "destination": "runtime"
  },
  "command": {
    "domain": "switchboard",
    "target": "diagnostics.switchboard",
    "member": null,
    "args": {
      "section": "dns"
    }
  },
  "status": {
    "ok": true,
    "code": "OK",
    "message": "success",
    "retryable": false
  },
  "extStatus": {
    "module": "diagnostics",
    "details": {}
  },
  "data": {},
  "meta": {
    "durationMs": 3,
    "page": null,
    "traceId": "..."
  }
}
```

### 2.2.3 字段职责

| 区块 | 必填 | 说明 |
|------|------|------|
| `header` | ✅ | 协议版本、类别、操作、链路标识 |
| `command` | request 必填 | 语义命令体（domain/target/member/args） |
| `status` | response/event 建议 | 标准状态码和可重试标识 |
| `extStatus` | 可选 | 业务扩展状态，避免污染标准码 |
| `data` | 可选 | 业务返回数据或事件载荷 |
| `meta` | 可选 | 时延、分页、追踪、分片信息 |

### 2.2.4 三种帧类型

| frameType | 用途 | 方向 |
|-----------|------|------|
| `request` | CLI/客户端发起命令 | client -> runtime |
| `response` | 单次命令响应 | runtime -> client |
| `event` | 主动推送事件/分片结果 | runtime -> client |

---

## 2.3 请求/响应/事件示例

### 2.3.1 检索类请求（query/list）

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "request",
    "category": "query",
    "operation": "list",
    "requestId": "req-001"
  },
  "command": {
    "domain": "breakpoints",
    "target": "diagnostics.breakpoints",
    "args": {}
  }
}
```

### 2.3.2 指令类响应（instruction/enable）

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "response",
    "category": "instruction",
    "operation": "enable",
    "requestId": "req-002",
    "correlationId": "req-002"
  },
  "status": {
    "ok": true,
    "code": "OK",
    "message": "point enabled",
    "retryable": false
  },
  "data": {
    "target": "pipeline.dns.update"
  }
}
```

### 2.3.3 流事件帧（stream/pull）

```json
{
  "header": {
    "schema": "rtdiag/2.0",
    "frameType": "event",
    "category": "stream",
    "operation": "pull",
    "requestId": "req-003",
    "correlationId": "req-003"
  },
  "status": {
    "ok": true,
    "code": "PARTIAL",
    "message": "batch 1/3",
    "retryable": false
  },
  "data": {
    "events": [
      { "kind": "runtime.breakpoint.hit", "message": "bp-dns-before-write" }
    ]
  },
  "meta": {
    "page": { "cursor": "next:abc", "hasMore": true }
  }
}
```

---

## 2.4 CLI 到 Wire 的映射规则

### 2.4.1 映射公式

`[namespace.]verb target [key=value]` ->

1. 解析出 `domain/operation/target/args`
2. 依据命令注册表填充 `category`
3. 组装 v2 envelope 发到管道

### 2.4.2 示例

| CLI | category | operation | domain | target |
|-----|----------|-----------|--------|--------|
| `sw.list` | `query` | `list` | `switchboard` | `diagnostics.switchboard` |
| `sw.point.enable p1` | `instruction` | `enable` | `switchboard` | `diagnostics.switchboard` |
| `host.events 20` | `stream` | `pull` | `host` | `diagnostics.monitor` |
| `status` | `report` | `summary` | `composite` | `runtime` |

---

## 2.5 协议统一（v2）

### 2.5.1 执行策略

1. Monitor 入口只接收 `rtdiag/2.0` Frame 请求。
2. 无 `header.schema` 或 schema 不匹配时，直接返回 Frame 错误响应。
3. Hub 对外统一入口为 `ExecuteFrameAsync`。
4. CLI 所有命令最终包装为 Frame 发送，不再保留 v1 入口。

### 2.5.2 CLI 语义到 v2 字段映射

| 语义字段 | v2 位置 |
|---------|---------|
| `TargetId` | `command.target` |
| `Action` | `header.operation` |
| `Member` | `command.member` |
| `Args` | `command.args` |
| `Value` | `command.args.value` |

---

## 3. 统一语法规范

### 3.1 命令格式

```
[namespace.]verb target [key=value ...] [--flags]
```

- `namespace` — 可选，命令分组（如 `bp`、`hook`、`web`）
- `verb` — 必需，操作动词（如 `list`、`get`、`set`、`enable`）
- `target` — 可选，操作目标（如 `section-id`、`point-id`、`targetId`）
- `key=value` — 可选，命名参数
- `--flags` — 可选，布尔标志

### 3.2 动词规范（统一 10 个动词）

| 动词 | 缩写 | 含义 | 示例 |
|------|------|------|------|
| `list` | `ls` | 列出/查询 | `bp.list` — 列出所有断点 |
| `get` | `get` | 读取单个 | `ref.get agent.state` — 读取 agent.state |
| `set` | `set` | 设置值 | `sw.set pipe=true` — 开启管道输出 |
| `enable` | `on` | 启用 | `bp.on bp-001` — 启用断点 |
| `disable` | `off` | 禁用 | `bp.off bp-001` — 禁用断点 |
| `toggle` | `tg` | 切换 | `sw.tg global` — 切换全局开关 |
| `watch` | `wc` | 监视 | `data.wc dns` — 监视 dns section |
| `resume` | `go` | 恢复/继续 | `bp.go bp-001` — 恢复断点 |
| `stop` | `halt` | 停止/终止 | `host.stop` — 关闭宿主 |
| `exec` | `x` | 执行/调用 | `ref.exec agent.state.Increment` — 调用方法 |

### 3.3 命名空间规范

| 命名空间 | 全称 | 作用域 |
|---------|------|--------|
| `sw` | switchboard | 诊断开关板（全局控制、section、point） |
| `bp` | breakpoints | 断点管理 |
| `hook` | hooks | 事件钩子管理 |
| `reg` | registry | 注册表查询 |
| `ref` | reflection | 反射对象访问 |
| `data` | data | 数据监视输出 |
| `host` | host | 宿主管道信息 |
| `web` | webview | WebView2 控制 |
| `svc` | service | DDNS Snap Service 管理 |
| `agent` | agent | DDNS Snap Agent 管理 |

### 3.4 参数语法

```
# 位置参数
sw.enable dns                     # 启用 dns section

# 命名参数
sw.set section=dns enabled=true   # 设置 dns section 状态

# 布尔标志
sw.set pipe=true                  # 开启管道
sw.set pipe=false --persist       # 关闭管道并持久化

# 路径参数
ref.get agent.state.Counter       # 读取 agent.state.Counter
ref.navigate Profile.Addresses[0] # 导航对象路径

# 混合
bp.on bp-001 timeout=0            # 启用断点，无限超时
```

---

## 4. 基础命令规范（原子操作）

### 4.1 基础命令定义

基础命令是**不可再分的原子操作**，直接映射到 `RuntimeDiagnosticFrameCommand`，并最终封装为 `RuntimeDiagnosticFrame`。

```json
{
  "name": "sw.enable",
  "aliases": ["sw.on"],
  "help": "Enable a switchboard section or global",
  "targetId": "diagnostics.switchboard",
  "action": "enable",
  "params": {
    "section": { "type": "string", "position": 0, "required": false }
  }
}
```

### 4.2 完整基础命令清单

#### sw — Switchboard 控制

| 命令 | 缩写 | 说明 | action |
|------|------|------|--------|
| `sw.list` | `sw.ls` | 快照 switchboard 状态 | `snapshot` |
| `sw.enable [section]` | `sw.on` | 启用全局或 section | `enable` |
| `sw.disable [section]` | `sw.off` | 禁用全局或 section | `disable` |
| `sw.set pipe=true\|false` | — | 设置管道输出 | `setPipeOutput` |
| `sw.set file=true\|false` | — | 设置文件输出 | `setFileOutput` |
| `sw.set file-path=<path>` | — | 设置文件路径 | `setFilePath` |
| `sw.set fifo=<depth>` | — | 设置 FIFO 深度 | `setFifoDepth` |
| `sw.reload` | — | 重载配置 | `reloadConfig` |
| `sw.save` | — | 保存配置 | `saveConfig` |
| `sw.points [filter]` | `sw.ps` | 查询输出点 | `queryPoints` |
| `sw.point.enable <id>` | — | 启用输出点 | `enablePoint` |
| `sw.point.disable <id>` | — | 禁用输出点 | `disablePoint` |
| `sw.emit [section] [kind] [msg]` | — | 发射自测事件 | `emitTest` |

#### bp — 断点管理

| 命令 | 缩写 | 说明 | action |
|------|------|------|--------|
| `bp.list` | `bp.ls` | 列出所有断点 | `list` |
| `bp.enable <id>` | `bp.on` | 启用断点 | `enable` |
| `bp.disable <id>` | `bp.off` | 禁用断点 | `disable` |
| `bp.resume <id>` | `bp.go` | 恢复断点 | `resume` |
| `bp.resume-all` | `bp.goall` | 恢复所有断点 | `resumeAll` |

#### hook — 事件钩子

| 命令 | 缩写 | 说明 | action |
|------|------|------|--------|
| `hook.list` | `hook.ls` | 列出所有钩子 | `list` |
| `hook.enable <id>` | `hook.on` | 附加钩子 | `attach` |
| `hook.disable <id>` | `hook.off` | 分离钩子 | `detach` |

#### reg — 注册表

| 命令 | 缩写 | 说明 | action |
|------|------|------|--------|
| `reg.list` | `reg.ls` | 查询所有注册表 | `list` |
| `reg.list watchpoints` | — | 查询监视点 | `list` |
| `reg.list breakpoints` | — | 查询断点 | `list` |
| `reg.list hooks` | — | 查询钩子 | `list` |

#### ref — 反射访问

| 命令 | 缩写 | 说明 | action |
|------|------|------|--------|
| `ref.get <targetId> <member>` | — | 读取成员 | `get` |
| `ref.set <targetId> <member> <value>` | — | 设置成员 | `set` |
| `ref.exec <targetId> <method>` | `ref.x` | 调用方法 | `invoke` |
| `ref.nav <targetId> <path>` | — | 路径导航 | `navigate` |

#### data — 数据监视

| 命令 | 缩写 | 说明 | action |
|------|------|------|--------|
| `data.watch <section>` | `data.wc` | 监视 section 输出 | `enable` + `setPipeOutput` |
| `data.unwatch <section>` | `data.uw` | 停止监视 | `disable` |

#### host — 宿主管理

| 命令 | 缩写 | 说明 | action |
|------|------|------|--------|
| `host.list` | `host.ls` | 列出诊断目标 | `list` |
| `host.info` | `host.i` | 主机扫描信息 | `host` |
| `host.rescan` | `host.rs` | 重新扫描主机 | `rescanHost` |
| `host.events [count]` | `host.e` | 消费事件 | `events` |
| `host.stop [--force]` | `host.halt` | 关闭宿主 | `shutdown` |

---

## 5. 复合命令规范（JSON 配置组合）

### 5.1 复合命令定义

复合命令由**多条基础命令按顺序执行**组成，定义在 JSON 配置文件中。

```json
{
  "name": "focus",
  "aliases": ["f"],
  "help": "Focus on one output point: open pipe, enable section, enable point",
  "steps": [
    { "command": "sw.set pipe=true" },
    { "command": "sw.enable $section" },
    { "command": "sw.point.enable $id" },
    { "command": "sw.enable" }
  ]
}
```

### 5.2 复合命令类型

#### 顺序执行（Sequential）
```json
{
  "name": "status",
  "aliases": ["st"],
  "help": "Full status: monitor + switchboard + targets",
  "mode": "sequential",
  "steps": [
    { "command": "ref.get diagnostics.monitor" },
    { "command": "sw.list" },
    { "command": "host.list" }
  ]
}
```

#### 管道执行（Pipeline — 前一步输出作为后一步输入）
```json
{
  "name": "debug-dns",
  "aliases": ["ddns"],
  "help": "Debug DNS pipeline: focus dns section, enable bp, watch",
  "mode": "sequential",
  "steps": [
    { "command": "sw.set pipe=true" },
    { "command": "sw.enable dns" },
    { "command": "bp.on bp-dns-before-write" },
    { "command": "data.watch dns" },
    { "command": "sw.enable" }
  ]
}
```

#### 条件执行（Conditional）
```json
{
  "name": "safe-shutdown",
  "aliases": ["ssd"],
  "help": "Safe shutdown: resume all breakpoints, then stop",
  "mode": "sequential",
  "steps": [
    { "command": "bp.resume-all" },
    { "command": "host.stop", "delay": 1000 }
  ]
}
```

### 5.3 复合命令的特殊语法

```json
{
  "steps": [
    { "command": "sw.set pipe=true" },
    { "command": "sw.enable $section", "skipOnError": true },
    { "command": "sw.point.enable $id", "delay": 500 },
    { "command": "sw.enable", "condition": "$pipeEnabled" },
    { "command": "host.events 10", "capture": "events" },
    { "command": "echo $events", "if": "captured" }
  ]
}
```

### 5.4 变量替换

复合命令支持 `$param` 变量替换，从 CLI 输入或持久化内存中获取：

```json
{
  "name": "focus",
  "params": {
    "id": { "type": "string", "position": 0, "required": true },
    "section": { "type": "string", "from": "memory", "key": "section" }
  },
  "steps": [
    { "command": "sw.enable $section" },
    { "command": "sw.point.enable $id" }
  ]
}
```

---

## 6. 配置文件格式

### 6.1 完整 Schema

```json
{
  "version": 2,
  "meta": {
    "name": "ddnssnap",
    "description": "DDNS Snap CLI commands"
  },
  "pipes": {
    "diagnostics": "DdnsSnap.RuntimeDiagnostics",
    "webview2": "AIGateway.WebRuntime",
    "management": "DdnsSnap.Service.Ui",
    "agent": "DdnsSnap.Agent.Ui",
    "desktop-ui": "LOCAL\\DdnsSnap.UI"
  },
  "memory": {
    "section": "dns",
    "count": 100,
    "targetId": "diagnostics.switchboard"
  },
  "baseCommands": [
    {
      "name": "sw.enable",
      "aliases": ["sw.on"],
      "help": "Enable a switchboard section or global",
      "transport": "diagnostics",
      "targetId": "diagnostics.switchboard",
      "action": "enable",
      "params": {
        "section": { "type": "string", "position": 0, "required": false }
      }
    }
  ],
  "compositeCommands": [
    {
      "name": "status",
      "aliases": ["st"],
      "help": "Full status: monitor + switchboard + targets",
      "mode": "sequential",
      "steps": [
        { "command": "ref.get diagnostics.monitor" },
        { "command": "sw.list" },
        { "command": "host.list" }
      ]
    }
  ]
}
```

### 6.2 命令定义字段

| 字段 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `name` | string | ✅ | 命令名（dot-separated for namespace） |
| `aliases` | string[] | — | 别名列表 |
| `help` | string | ✅ | 帮助文本 |
| `transport` | string | ✅ | 传输类型（diagnostics/webview2/management/agent/desktop-ui） |
| `targetId` | string | — | 诊断目标 ID |
| `action` | string | — | Hub action |
| `params` | object | — | 参数定义 |
| `mode` | string | — | 复合命令模式（sequential/parallel/conditional） |
| `steps` | object[] | — | 复合命令步骤列表 |

### 6.3 参数定义字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `type` | string | 参数类型（string/int/bool/flag） |
| `position` | int | 位置参数索引 |
| `required` | bool | 是否必需 |
| `default` | any | 默认值 |
| `from` | string | 来源（memory/arg） |
| `key` | string | 内存中的键名 |

---

## 7. 旧入口清理结果

1. 旧 CLI 入口（legacy `Program.cs` 命令分支、`BuildCommand`、旧 composite switch）已移除。
2. 旧配置源 `Iwesun.Runtime.Cli.commands.json` 已删除。
3. CLI 仅接受 `Iwesun.Runtime.Cli.commands.v2.json` 定义的统一命令。
4. 未在 v2 配置中的命令一律返回 Unknown command，不再隐式回退。

---

## 8. 使用示例（现行）

```bash
# 全面状态
> st

# 聚焦 DNS 调试
> focus dns pipeline.dns.update

# 断点调试流程
> bp.ls                         # 查看断点
> bp.on bp-dns-before-write     # 启用断点
> sw.on dns                     # 开启 dns 输出
> sw.pipe true                  # 开启管道
> sw.on                         # 全局开启
# ... 等待断点命中 ...
> bp.go bp-dns-before-write     # 恢复执行

# 反射访问
> host.ls                       # 列出目标
> ref.get agent.state Counter   # 读取属性
> ref.nav diagnostics.selftest Counter  # 路径导航
> ref.x diagnostics.selftest Increment  # 调用方法

# 事件钩子
> hook.ls                       # 查看可用钩子
> hook.on auth-login             # 附加钩子

# 安静模式
> mute

# 关闭
> host.halt
```

---

> **文档状态**: 已生效（single-entry v2），旧入口已清理。
