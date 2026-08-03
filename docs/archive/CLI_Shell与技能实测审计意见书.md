# Runtime CLI Shell 与技能实测审计意见书

审计日期：2026-07-13
审计对象：Iwesun Runtime 1.0.19
验证宿主：AIGateway 统一 Runtime Host
验证方式：安装版 `iwrt.exe`、独立 RuntimeDiagnostics 管道、真实 Shell 交互

## 一、审计结论

Runtime CLI v3 Shell 的主体功能已经可用：目标管理、远程命令、虚拟路径、变量、历史、清理和安全退出均具备实际实现；安装版技能内容与 Codex 本地技能副本完全一致。

本次实测发现两个需要修正的实现问题：

1. 变量没有在全部 Shell token 中统一展开。
2. Runtime 虚拟路径没有继承 `target use` 选定的当前目标。

这两个问题不会破坏普通单管道 CLI 命令，但会影响 Shell 的多目标操作和上下文一致性。当前建议判定为：**基本可用，带限制通过；修复后再转为完整通过。**

## 二、验证环境

- 安装目录：`C:\Program Files\Iwesun\Runtime`
- CLI：`bin\Iwesun.Runtime.Cli\iwrt.exe`
- CLI/FileVersion：`1.0.19.0`
- WebView2/FileVersion：`1.0.19.0`
- Diagnostics Debug/Release：`1.0.19.0`
- 测试管道：`AIGateway.ShellSkillTest`
- 测试端口：`12437`

测试结束后已显式执行 `lifecycle.shutdown true`，宿主退出码为 0，没有遗留测试进程。

## 三、通过项目

以下功能已通过真实交互验证：

- `iwrt shell` 可以进入交互模式。
- `target add/list/use/current/remove` 可以管理目标。
- `@gateway host.summary` 可以临时选择目标并执行命令。
- `host.summary` 在当前目标有效时返回标准 `rtdiag/2.0` Frame。
- 使用 `iwrt --pipe=NAME shell` 启动时，`pwd/cd/ls/get/root` 虚拟路径功能可用。
- `set/unset/vars/history/clear` 本地上下文功能可用。
- 变量作为完整远程命令 token 时可以展开，例如 `$command` → `host.summary`。
- `clear` 会清空变量并把当前虚拟路径恢复到 `/`。
- 未定义目标返回 `CLI_TARGET_NOT_FOUND`，不会猜测相似管道。
- `exit` 和 `quit` 只关闭 Shell，不向宿主发送 Stop、Wakeup 或 shutdown。
- Shell 退出后宿主仍可通过普通 CLI 查询。
- 宿主必须通过显式 `lifecycle.shutdown true` 停止。

## 四、问题一：变量未在全部 token 中展开

### 4.1 规格要求

设计规格 `2026-07-13-cli-context-shell-design.md` 第 79 行规定：

> `$name` 和 `${name}` 在 token 化之后、参数绑定之前展开。

该描述没有排除 Shell 本地命令、目标选择 token 或虚拟路径参数。

### 4.2 复现步骤

```text
set pipe AIGateway.ShellSkillTest
target add gateway $pipe
target list
```

实际结果：

```text
gateway=$pipe
```

随后 `host.summary` 尝试连接名为 `$pipe` 的管道并返回 `CLI_CONNECT_TIMEOUT`。

其他复现：

```text
set path /lifecycle
cd $path
pwd
```

实际路径为：

```text
/host/$path
```

目标变量同样不展开：

```text
set destination gateway
@${destination} host.summary
```

实际返回：

```text
CLI_TARGET_NOT_FOUND: Target '${destination}' is not defined.
```

### 4.3 对照结果

变量作为远程命令名时可以展开：

```text
set command host.summary
$command
```

该命令执行成功，说明变量表和部分 token 展开逻辑已经存在，但没有统一应用到本地命令参数、路径参数和 `@target` token。

### 4.4 整改建议

- 在 Shell tokenizer 完成分词后，对所有非引号禁止展开的 token 执行同一变量展开步骤。
- 展开应先于本地命令分派、`@target` 解析、虚拟路径归一化和远程参数绑定。
- 未定义变量必须统一返回 `CLI_CONTEXT_VARIABLE_NOT_FOUND`，不得发送管道请求。
- `history` 继续保存用户原始输入，不保存敏感变量展开后的值。

## 五、问题二：虚拟路径没有继承当前目标

### 5.1 复现步骤

使用不带 `--pipe` 的 Shell：

```text
iwrt shell
target add gateway AIGateway.ShellSkillTest
target use gateway
target current
host.summary
get /host
```

实际结果：

- `target current` 返回 `gateway=AIGateway.ShellSkillTest`。
- `host.summary` 成功连接 `AIGateway.ShellSkillTest`。
- `get /host` 没有使用当前目标，而是尝试连接默认 `DdnsSnap.RuntimeDiagnostics`，最终返回 `CLI_CONNECT_TIMEOUT`。

### 5.2 对照结果

以下启动方式下虚拟路径正常：

```powershell
iwrt --pipe=AIGateway.ShellSkillTest shell
```

随后执行：

```text
get /host
cd /lifecycle
ls
```

均能连接正确宿主并返回 Runtime Frame。

这说明虚拟路径路由使用了 Shell 启动参数中的固定 endpoint/pipe，却没有读取 `target use` 设置的当前目标。

### 5.3 整改建议

- `RuntimeVirtualPathRouter` 执行远程读取前，应通过统一的目标解析器获取有效目标。
- 目标优先级建议固定为：一次性 `@target` → Shell 当前 target → Shell 启动 `--pipe` → 配置默认值。
- `target remove` 删除当前目标时，应明确回退到启动默认目标或清空当前目标，并在提示符/`target current` 中可见。
- 虚拟路径与普通远程命令必须共享同一目标解析结果，避免一个 Shell 同时连接两个不同宿主。

## 六、技能审计

已核对以下文件：

- 安装版：`C:\Program Files\Iwesun\Runtime\skills\iwesun-runtime-integration\SKILL.md`
- 安装版：`C:\Program Files\Iwesun\Runtime\skills\iwesun-runtime-integration\references\json-cli.md`
- Codex 本地技能对应副本

SHA256 对比结果：

| 文件 | 安装版与本地副本 |
| --- | --- |
| `SKILL.md` | 完全一致 |
| `json-cli.md` | 完全一致 |

技能当前正确说明了：

- CLI v3 为当前标准。
- 优先使用 CLI，不手写管道客户端。
- `iwrt shell` 提供上下文、变量和虚拟路径。
- `exit/quit` 只退出客户端。
- 多宿主使用 `target add/list/use/current/remove` 或 `@name command`。
- 不猜测相似管道。
- 推荐使用轻量 `host.summary` 和分页列表。

技能需要补充的内容：

1. 给出一段完整可执行的 Shell 示例，而不只列出能力名称。
2. 明确变量应该适用于命令名、普通参数、本地命令参数、路径和 `@target`。
3. 明确虚拟路径必须使用当前 target；如果暂未修复，应临时注明必须使用 `iwrt --pipe=NAME shell`。
4. 增加连接失败码的排障示例，特别是 `CLI_CONNECT_TIMEOUT` 中的实际 `pipeName` 和 `targetAlias`。
5. 说明分页列表在 1.0.19 中返回 `Data.Items/Total/Offset/Limit/Returned/HasMore`，避免客户端仍把 `Data` 当作数组。

## 七、建议的标准使用方式

在上述问题修复前，单宿主 Shell 建议显式指定管道：

```powershell
iwrt --pipe=AIGateway.RuntimeDiagnostics shell
```

Shell 内：

```text
pwd
get /host
cd /lifecycle
ls
set command host.summary
$command
exit
```

多目标远程命令建议暂时使用字面目标和管道名：

```text
target add gateway AIGateway.RuntimeDiagnostics
target use gateway
host.summary
@gateway task.list
```

在变量问题修复前，不建议使用：

```text
target add gateway $pipe
cd $path
@${target} host.summary
```

在虚拟路径目标问题修复前，不应假定 `target use` 会改变 `get/ls/cd` 的远程目标。

## 八、验收清单

建议增加自动化 Shell 验收测试：

- [ ] `$name` 和 `${name}` 在远程命令名中展开。
- [ ] 变量在远程参数中展开。
- [ ] 变量在 `target add` 管道参数中展开。
- [ ] 变量在 `cd/ls/get` 路径参数中展开。
- [ ] 变量在 `@${target}` 中展开。
- [ ] 未定义变量返回 `CLI_CONTEXT_VARIABLE_NOT_FOUND`，且服务端未收到请求。
- [ ] `target use` 同时影响普通远程命令和虚拟路径命令。
- [ ] `@target` 一次性目标不污染当前目标。
- [ ] `exit/quit/EOF` 不停止宿主。
- [ ] `clear` 清空变量、参数记忆、历史和虚拟路径。
- [ ] 分页列表返回结构与技能说明一致。
- [ ] Shell 失败命令不会结束 REPL。

## 九、最终意见

Shell 的总体架构和安全边界是合理的，1.0.19 已经具备日常使用价值。当前问题集中在“上下文统一性”：变量展开没有覆盖全部 token，虚拟路径没有使用当前目标。建议优先修复这两个点，并同步技能示例和自动化验收。完成后即可把 Shell 从“带限制通过”提升为“完整通过”。
