# Iwesun Runtime 1.0.28 阶段交接记录

更新时间：2026-07-19 10:20（Asia/Shanghai）

## 1. 当前工作区

- 仓库：`D:\Git Space\Runtime`
- 分支：`main`
- 当前提交：`13d95b3`
- 工作区存在大量尚未提交的源码、文档和新增文件；这些是本阶段累计成果，不能清理、回退或用旧提交覆盖。
- 本阶段没有执行 `git add`、提交或推送。
- 继续工作前先阅读仓库根目录 `AGENTS.md`、`copilot-instructions.md` 和
  `.github/instructions/copilot-access-rules.instructions.md`。

## 2. 已完成的发布

Runtime 1.0.28 已通过唯一全量发布入口重新构建、暂存、自检并生成 MSI：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File scripts\release\build-runtime-setup.ps1 `
  -ProductVersion 1.0.28
```

最终产物：

- MSI：`D:\Git Space\Runtime\artifacts\setup\Iwesun.Runtime.Setup.msi`
- 大小：`2,876,658` 字节
- SHA-256：`CFC7EFF172A94334A3905FBE6FC7D8B0F900F8B8FC29FC10A94569E1C9689DE7`
- 程序集文件版本：`1.0.28.0`
- ProductVersion：`1.0.28+13d95b3ff9191a1a4f6e32e4d7d30ac7d93d95df`
- Debug 全量构建：0 警告、0 错误
- Release 全量构建：0 警告、0 错误
- 安装树自检：通过
- 安装版 SampleHost Debug/Release 引用来源及 DLL 哈希验证：通过

发布内容包括 Diagnostics Debug/Release、CLI、Data、RecordStore、WebView2、RemoteConsole、Protocol、
SampleHost、WebView2 SampleHost、JSON 配置、35 份 Markdown 文档、集成技能和发布自检脚本。

## 3. RemoteConsole 当前能力

远程 PowerShell 端到端功能已实测通过：

- RemoteConsole 服务端与 CLI 使用独立命名管道连接。
- CLI 可以创建远端隔离工作区。
- `file.upload` 可以把 CLI 所在机器的本地文件分块上传到远端工作区。
- 经审批的 PowerShell 命令可以在远端执行，并实时返回标准输出、错误输出和退出码。
- 上传后可通过经审批的 `Copy-Item`、`Expand-Archive` 在远端继续复制或展开；最终权限由服务账号的
  Windows ACL 决定。
- 当前没有 `file.download`，不能写成双向文件传输。
- 上传接口不允许直接逃逸远端隔离工作区。
- MSI 携带 RemoteConsole 程序，但不自动创建 AI 账号，也不自动注册或启动 Windows 服务；账号、凭据和
  服务登记仍由管理员执行。

对应功能场景：

```powershell
dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug --no-build -- `
  --child --scenario remote-console-cli
```

已通过的关键检查包括：服务连接、CLI 工作区创建、文件上传、命令提交、输出跟随和正常结束。

## 4. 本轮同步的文档和技能

优先从以下权威文档继续：

- `docs/REQUIREMENTS_ACTIVE.md`
- `docs/RELEASE_NOTES.md`
- `docs/IWESUN_RUNTIME_REMOTE_CONSOLE.md`
- `docs/IWESUN_RUNTIME_CLI.md`
- `docs/IWESUN_RUNTIME_QUICK_START.md`
- `docs/05-runtime-tooling/RUNTIME_RELEASE_PACKAGING.md`
- `skills/iwesun-runtime-integration/SKILL.md`
- `skills/iwesun-runtime-integration/references/remote-console.md`
- `skills/iwesun-runtime-integration/references/record-store.md`

发布自检 `scripts/release/verify-runtime-install.ps1` 已增加 RemoteConsole 文件能力边界检查，要求安装版文档
明确出现 `file.upload`、`file.download` 和 `Copy-Item`。实现使用 Windows PowerShell 5.1 兼容的
`IndexOf(..., StringComparison.Ordinal)`。

## 5. 重要边界和未完成事项

1. 当前 MSI 已可交付，但尚未在本轮执行安装、覆盖安装、卸载或 Windows 服务登记测试；除非用户明确要求，
   不要直接改变已安装系统状态。
2. RemoteConsole 目前缺少远端到 CLI 本地的下载命令；这是明确能力边界，不是文档遗漏。
3. RemoteConsole 的 UI 审批/监控界面仍以框架为主，当前权威操作端是 CLI。
4. `D:\Git Space\Data` 的 RecordStore V2 功能与安全验证已经收口，但正式晋升仍受固定 50 万条 Source
   内存 `81.4 MiB` 阻断，目标是旧基线的 1.25 倍以内。不要把 Runtime 1.0.28 打包成功表述成 RecordStore
   V2 已正式晋升。
5. 工作区包含大量跨阶段修改。任何提交前都要先审计完整差异，并确认 `D:\Git Space\Data` 等跨仓引用的
   提交边界；不要只提交最后几份文档而遗漏依赖源码。
6. `git diff --check` 已通过，仅存在既有 LF/CRLF 转换提示。

## 6. 发布过程中的已解决问题

- 第一次全量暂存遇到 Data/Runtime Data Debug PDB 映射锁；关闭编译服务器并等待并行消费者构建结束后，
  完整重跑成功。不要复用失败时的暂存目录。
- 新增文档自检最初使用双参数 `String.Contains`，Windows PowerShell 5.1 不支持；已改为兼容写法并重新
  完成全量发布。

## 7. 新任务建议起点

新任务首先读取本记录和 `docs/REQUIREMENTS_ACTIVE.md`，然后根据用户的新指令继续。若下一步是安装验证，
应先核对当前已安装版本和正在运行的 Runtime/RemoteConsole 进程，再决定覆盖安装还是先卸载；若下一步是
提交推送，应先完整审计 Runtime 与 Data 两个仓库的工作区差异、版本一致性和发布产物对应关系。
