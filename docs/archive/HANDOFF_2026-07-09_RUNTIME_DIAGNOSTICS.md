# Iwesun Runtime.Diagnostics 升级交接日志

> 日期: 2026-07-09
> 当前操作者环境: Windows / VS Code Copilot 会话
> 账号切换背景: 当前登录账号为 `admin`，Git 未完成可推送配置；后续预计切换到 `lyh` 继续工作。

## 任务主题

本轮持续任务主题是：**全面升级 Iwesun.Runtime.Diagnostics**。

目标包括：

1. 统一 Runtime Diagnostics 的代码接口、协议接口和文档界面。
2. 删除旧入口，不再保留旧命令模型作为外部入口。
3. 同步 Ddns Snap 对 Runtime.Diagnostics 的消费方式。
4. 将文档整理为新的稳定界面，旧资料归档，不再混在活跃入口中。

## 当前总体状态

**任务尚未完全结束，但核心升级主线已经打通。**

当前结论：

1. **代码主链已完成升级**。
2. **活跃文档界面已完成收口**。
3. **两个仓库当前构建通过**。
4. **Git 账号/推送链路尚未完成，因此还没有形成最终提交与推送闭环**。

## 已完成事项

### 一、协议与代码升级

1. 诊断外部协议已统一到 `RuntimeDiagnosticFrame`，协议语义为 `rtdiag/2.0`。
2. `RuntimeDiagnosticsMonitor` 已切换为 **Frame-only** 外部入口。
3. `RuntimeDiagnosticHub` 已提供并使用 `ExecuteFrameAsync` 作为统一对外执行入口。
4. 旧的 `RuntimeDiagnosticCommand` / `RuntimeDiagnosticResult` 外部语义已退出主入口。
5. CLI 已切换到新的 Frame 请求链路与 `commands.v2.json` 语义。

### 二、跨仓库消费链同步

1. Ddns Snap Service 侧 `runtime.diagnostics` 已切换到 `RuntimeDiagnosticFrame`。
2. Ddns Snap Agent 侧 `runtime.diagnostics` 已切换到 `RuntimeDiagnosticFrame`。
3. Ddns Snap 相关诊断测试已迁移到 Frame 调用方式。

### 三、文档界面整理

1. Runtime 活跃文档界面已收缩为：
   - `docs/README.md`
   - `docs/RUNTIME_DIAGNOSTICS.md`
   - `docs/IWESUN_RUNTIME_CLI.md`
2. 历史资料已迁移到 `docs/archive/`。
3. 已新增 `docs/archive/README.md` 作为 Runtime 归档索引。
4. 旧 AI / Skill 文档、阶段性设计文档、迁移资料已从活跃入口移除。

### 四、验证结果

2026-07-09 已验证：

```powershell
dotnet build Iwesun.Runtime.slnx -c Release --nologo
```

结果：**构建成功**。

## 尚未完成事项

### 一、Git / 推送闭环未完成

1. 当前账号为 `admin`，Git 未配置完成，无法直接进行规范推送。
2. 当前会话中 `D:\Git Space\Runtime` 未能作为可用 Git 仓库直接执行提交/推送闭环。
3. 后续切换到 `lyh` 后，需要先确认：
   - Git 仓库根是否正确
   - `user.name` / `user.email` 是否已切换
   - 远端认证是否已恢复可用

### 二、交付闭环未完成

1. 还没有做最终提交拆分策略确认。
2. 还没有形成最终 commit / push / PR（如需要）。
3. 还没有在 `lyh` 账号环境下做最终一次提交前审查。

### 三、可选收尾项

1. `docs/design/` 当前已为空目录；内容已经归档，但目录本身未删除。
2. 如后续需要，可在正常 Git 工作流下删除该空目录占位。

## 建议的下一步（切换到 lyh 后）

1. 先在 Runtime 仓库根确认 Git 可用：

```powershell
git status
git config user.name
git config user.email
```

2. 再核对本次升级关键入口文件：
   - `AGENTS.md`
   - `copilot-instructions.md`
   - `docs/README.md`
   - `docs/RUNTIME_DIAGNOSTICS.md`
   - `docs/IWESUN_RUNTIME_CLI.md`
   - `docs/archive/README.md`

3. 然后执行一次 Release 构建复核：

```powershell
dotnet build Iwesun.Runtime.slnx -c Release --nologo
```

4. 最后决定提交策略：
   - 若只提交 Runtime 仓库，建议按“代码升级”和“文档/归档整理”拆分提交。
   - 若与 Ddns Snap 联动提交，需同步查看 Ddns Snap 交接日志。

## 与 Ddns Snap 的关系

本次升级不是 Runtime 单仓库孤立改动，**Ddns Snap 已同步适配并且已通过构建**。

继续工作时应把两个仓库视为一组联动改造，而不是只看 Runtime 单边状态。
