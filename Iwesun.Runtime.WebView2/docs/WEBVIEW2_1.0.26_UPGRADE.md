# Iwesun.Runtime.WebView2 1.0.26 升级说明

## 1. 适用范围

1.0.26 是 1.0.25 之后的补丁升级。它保留现有 WebRuntime Frame、脚本会话、虚拟输入和 DOM 真快照接口，新增正式的数据流监视记录器。升级不要求业务项目重建 DOM，也不改变既有 WebView2 会话选择器和登录状态机。

## 2. 新增内容

- `IDataStreamRecorderManager` 与 `DataStreamRecorderManager`；
- HTTP、Fetch、XHR、WebSocket、SSE、WebMessage 和 Download 的公共记录模型；
- 通用复合条件、分类器、命名委托和直接委托；
- 请求/响应交换上下文、请求正文 sidecar、JSONL manifest 和有界输出；
- `data.recorder.*` 管理动作；
- `web.data-recorder.*` 八条 CLI v3 命令；
- SampleHost 中可编译的 `DataStreamRecorderSample.cs`。

## 3. 从 1.0.25 升级

1. 停止使用 Runtime DLL 的业务宿主和 CLI。
2. 安装 1.0.26，或把 `Iwesun.Runtime.WebView2.dll`、CLI 文件和两份系统 JSON 配置作为同一版本整体替换。
3. 清理业务宿主自己的 `bin`、`obj`、`publish` 和安装 staging；安装器不会替换宿主已经复制出去的私有 DLL。
4. 重新构建宿主，确认加载的 `Iwesun.Runtime.WebView2.dll` FileVersion 为 `1.0.26.0`。
5. 宿主注册业务 `MatcherId` 对应委托，并把 WebView2 网络事件转换为 `DataStreamRecord`。
6. 运行 `iwrt web.data-recorder.create --help`，再完成 create、start、status、stop、delete 闭环。

## 4. 配置兼容性

- CLI 配置 schema 仍为 `iwesun.runtime.cli/3.0`。
- Frame schema 仍为 `rtdiag/2.0`；组合命令继续使用 `rtdiag/3.0`。
- 1.0.25 用户配置可以继续增量扩展，但不得用旧系统配置覆盖 1.0.26 的 `RuntimeCliSystemConfig.json` 和 `RuntimeCliSystemMetadata.json`。
- 监视器定义不是全局持久化状态。业务宿主负责在启动时重新创建并注册需要的命名委托。

## 5. 安全变化

- 正文由宿主直接写入指定目录，不经过 CLI 管道。
- Cookie、Authorization、Set-Cookie、Token、Secret 和 API Key 等敏感头默认不进入记录、事件或 manifest。
- `WriteRequestBody` 默认关闭；启用前应确认请求正文是否包含账号、提示词或业务秘密。
- `DeleteOutputFiles` 默认关闭，删除监视器不会隐式删除业务数据。
- 输出根目录必须由可信业务代码或受控管理配置提供。

## 6. 回退到 1.0.25

1. 停止全部监视器并退出宿主。
2. 保留或人工归档记录目录；回退不会自动删除 1.0.26 生成的数据。
3. 整体恢复 1.0.25 的 WebView2 DLL、CLI 和系统 JSON，禁止混用版本。
4. 移除业务代码对数据记录器新 API 的调用后重新构建宿主。

记录文件是普通正文、请求 sidecar 和 JSONL manifest，回退 Runtime 不会使这些文件失效。

## 7. 发布验收

- Runtime 全解决方案 Debug/Release 均成功；
- CLI 系统路由和帮助元数据一一对应；
- 八条命令都能解析 `--help`；
- create/start/observe/status/stop/delete 和停止后不再写入通过；
- staging 包含 DLL、配置、权威手册、升级说明和可编译样例；
- staging 自检确认版本一致且不存在 v2 配置。
