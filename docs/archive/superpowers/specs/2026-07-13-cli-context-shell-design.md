# Iwesun Runtime CLI 缺省配置与上下文 Shell 设计

日期：2026-07-13  
目标版本：1.0.18  
状态：待书面审阅

## 1. 目标

在不改变 Runtime 管道协议和服务端状态的前提下，为 CLI 增加：

1. 当前目录缺省用户配置加载。
2. 单次执行和上下文 Shell 两种工作模式。
3. 进程内变量、命令参数记忆和统一 Runtime 虚拟路径。
4. 面向 host、lifecycle、execution、registry、switchboard、pipes、files、reflection 的统一探索命令。

## 2. 配置文件与优先级

文件名统一为：

- `RuntimeCliSystemConfig.json`：系统路由、endpoint、commands、composites。
- `RuntimeCliSystemMetadata.json`：命令摘要、风险、能力和示例。
- `RuntimeCliUserConfig.json`：用户增量命令、别名、endpoint 和 composites。

加载规则：

1. `--config=PATH` 指定系统配置时使用指定文件；否则依次查找 CLI 程序目录、当前工作目录和嵌入资源中的 `RuntimeCliSystemConfig.json`。
2. `--user-config=PATH` 指定时只加载该用户文件；不得再合并当前目录缺省用户文件。
3. 未指定 `--user-config` 时，若当前工作目录存在 `RuntimeCliUserConfig.json`，自动增量加载。
4. 缺省用户文件不存在不是错误；显式用户文件不存在必须返回 `CLI_USER_CONFIG_NOT_FOUND`。
5. metadata 与最终系统配置同目录匹配；缺失时使用安全默认值。嵌入系统配置使用嵌入 metadata。

## 3. 工作模式

### 3.1 单次模式

```text
iwrt [global-options] <command> [arguments]
```

执行一个命令，输出一个 JSON 结果并退出。现有脚本行为、退出码和单 JSON 边界保持不变。

### 3.2 上下文模式

入口：

```text
iwrt shell
iwrt --interactive
```

Shell 启动后重复读取一行、解析、执行并等待下一行，直到 `exit`、`quit`、EOF 或取消信号。单个远程命令失败不得结束 Shell。

`exit` 和 `quit` 仅退出本地 CLI Shell，不发送任何 Runtime Frame，不触发 `lifecycle.shutdown`、Stop、Wakeup、断点恢复或被控制宿主退出。需要停止被控制程序时，用户必须显式执行 `lifecycle.shutdown` 或其明确配置的用户别名。

本地命令：

- `help [command]`
- `exit`、`quit`
- `history`
- `vars`
- `set <name> <value>`
- `unset <name>`
- `clear`
- `pwd`
- `cd <path>`、`cd ..`
- `ls [path]`
- `get [name-or-path]`
- `root`

## 4. 上下文模型

上下文只存在于当前 CLI 进程，不持久化、不写回 JSON、不发送给 Runtime 服务端。

### 4.1 显式变量

- `set name value` 设置变量。
- `unset name` 删除变量。
- `vars` 列出变量。
- `$name` 和 `${name}` 在 token 化之后、参数绑定之前展开。
- 未定义变量返回 `CLI_CONTEXT_VARIABLE_NOT_FOUND`，不发送请求。
- 变量名使用 `^[A-Za-z_][A-Za-z0-9_.-]*$`。

### 4.2 隐含参数记忆

- 仅在远程命令成功后保存已绑定的非敏感参数。
- 键为规范命令名和参数名，避免不同命令之间无意串值。
- 再次执行同一命令且省略参数时，先使用命令记忆，再使用 catalog 默认值。
- 路由所需参数也可由命令记忆补齐。
- `clear` 清除变量、命令记忆、历史和当前虚拟路径，并回到 `/`。

以下参数不自动记忆：

- destructive 命令的全部参数；
- 名称包含 `password`、`secret`、`token`、`key`、`credential`、`confirm`、`graceful` 的参数；
- JSON 对象和数组参数。

## 5. Runtime 虚拟路径

根节点：

```text
/
├─ host
├─ lifecycle
├─ registry
├─ execution
│  ├─ processes
│  ├─ threads
│  └─ tasks
├─ switchboard
├─ pipes
├─ files
└─ reflection
   └─ <target>
      └─ <member-path>
```

固定映射：

| 路径 | 默认读取命令 |
| --- | --- |
| `/host` | `host.info` |
| `/lifecycle` | `lifecycle.status` |
| `/registry` | `registry.list` |
| `/execution/processes` | `process.list` |
| `/execution/threads` | `thread.list` |
| `/execution/tasks` | `task.list` |
| `/switchboard` | `switchboard.get` |
| `/pipes` | `pipe.list` |
| `/files` | `file.list` |
| `/reflection` | `reflection.list` |
| `/reflection/<target>` | `reflection.get <target>` |
| `/reflection/<target>/<member-path>` | `reflection.get <target> <member-path>` |

路径规则：

- 绝对路径从 `/` 开始；相对路径从当前路径开始。
- `.` 保持当前节点；`..` 返回父节点；不得越过根节点。
- 重复 `/` 被归一化。
- `pwd` 只显示当前规范路径。
- `cd` 只改变本地路径；路径必须能被虚拟树解析。
- `ls` 列出固定子节点，或调用对应 list/get 命令发现动态 reflection target/member。
- `get` 读取当前路径；参数存在时按当前路径解析子路径。
- 路径映射最终仍调用 catalog 中的规范命令，不直接构造或扩大反射权限。

## 6. 别名与组合命令

- 用户别名、扩展命令和 composites 继续由 `RuntimeCliUserConfig.json` 定义。
- Shell 本地命令名保留，不允许用户 alias 覆盖 `exit`、`quit`、`set`、`unset`、`vars`、`clear`、`pwd`、`cd`、`ls`、`get`、`root`、`history`、`help`。
- 用户 alias 展开后进入同一命令绑定、上下文补值和风险检查流程。
- composite 仍是单 endpoint 的固定 batch，不获得脚本、条件、循环或会话写入能力。

## 7. 输出与错误

- 单次远程命令继续只输出一个完整 JSON Frame。
- Shell 远程命令每次输出一个完整 JSON Frame；提示符、本地命令和帮助文本允许非 JSON。
- 本地错误使用现有 `iwesun.runtime.cli.result/1.0` 结构并写入 stderr；Shell 捕获错误后继续下一行。
- EOF 正常退出码为 0；显式 `exit <code>` 不支持，避免把 Shell 变成脚本运行器。
- Ctrl+C 取消当前远程命令；连续空闲 Ctrl+C 或 EOF 退出 Shell，不向宿主发送 shutdown。
- `exit`、`quit` 和 EOF 退出后，被控制设备及其 RuntimeDiagnostics 管道继续保持原状态。

## 8. 代码边界

- `CliConfigurationLoader`：系统配置、metadata、缺省用户配置和显式优先级。
- `CliCommandExecutor`：把一次命令编译成 Frame、发送并返回退出码。
- `CliInteractiveShell`：REPL、提示符、历史和取消控制。
- `CliContext`：当前路径、显式变量和命令参数记忆。
- `RuntimeVirtualPathRouter`：路径归一化、节点枚举和命令映射。
- `CliTokenizer`：引号、转义和变量 token。
- `CliV3` 保留应用入口和兼容模型，不继续承担全部职责。

## 9. 安全边界

- 上下文不能绕过 command、endpoint、reflection whitelist 或 Debug/Release capability。
- destructive 命令不读取隐含参数记忆。
- 敏感参数不进入 history 的展开后副本；history 保存用户原始输入，并允许关闭历史显示。
- 配置解析失败、变量失败和路径失败均不得发送管道请求。
- CLI 退出、Shell 退出和管道断开不改变宿主、断点或 Stop 状态。

## 10. 验收

1. 当前目录存在 `RuntimeCliUserConfig.json` 时自动加载。
2. `--user-config` 指定文件完全取代缺省用户文件。
3. 缺省用户文件不存在时单次模式行为不变。
4. `iwrt shell` 与 `--interactive` 均可进入上下文模式。
5. Shell 连续执行至少三个远程命令而不退出。
6. `set`、`$name`、`${name}`、`unset`、`vars` 行为正确。
7. 同一命令非敏感参数可缺省复用，危险和敏感参数不复用。
8. `pwd/cd/cd ../ls/get/root` 在固定节点和 reflection 长路径上正确。
9. 原有规范命令在 Shell 内外生成相同 Runtime Frame。
10. 用户 alias 和 composite 在缺省用户配置及显式用户配置下均可用。
11. 单次模式仍保持一个 JSON 输出和原退出码。
12. Shell 中单个命令失败后可继续执行下一命令。
13. `exit`、`quit` 和 EOF 不产生管道请求，被控制宿主继续运行；只有显式 `lifecycle.shutdown` 才停止宿主。
14. Debug/Release 构建、CLI 功能场景、SampleHost 真实往返和安全退出通过。
15. 文档、三份 JSON 配置/样例、技能和全量安装包同步更新。
