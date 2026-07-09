# Runtime 交接说明（注入器标准化）

> **交接时间**: 2026-07-09
> **范围**: Runtime.Diagnostics 注入器标准化、样板宿主、状态与线程任务基础能力

本文用于交接当前工作进度，说明已经完成什么、尚未完成什么、下一步重点是什么，并给出可执行建议。

## 一、当前结论（总览）

本轮工作已经把“注入器标准化”做成可运行原型，核心是两层模型：

1. **固定宿主模板层**：启动和退出统一走标准模板。
2. **业务标注化注入层**：输出、断点、数据、线程和任务在业务源码中按标准门面注入。

该模型已经在 `Iwesun.Runtime.SampleHost` 跑通，且 `Iwesun.Runtime.slnx` 已编译通过。

## 二、已完成项

### 1. 六类能力的标准入口已落地

- 启动模板：`RuntimeHostTemplate.Start(...)`
- 激活模板：`RuntimeHostTemplate.Activate(...)`
- 退出模板：`RuntimeHostTemplate.Stop(...)`
- 输出注入：`RuntimeInjector.Output(...)`
- 观察点注入：`RuntimeInjector.Watch(...)`
- 断点注入：`RuntimeInjector.Break(...)`
- 数据注入：`RuntimeInjector.Data(...)`
- 线程注入：`RuntimeInjector.Thread(...)`
- 任务注入：`RuntimeInjector.Task(...)`

涉及源码：

- `Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`

### 2. 状态分类基础能力已完成

- `RuntimeState` 值对象
- `RuntimeStateCatalog` 状态目录
- `RuntimeStateManager` 在线状态切换
- `RuntimeStateContracts` 接口分层
- `RuntimeStateKey` 枚举键

涉及源码：

- `Iwesun.Runtime.Diagnostics/RuntimeState.cs`
- `Iwesun.Runtime.Diagnostics/RuntimeStateCatalog.cs`
- `Iwesun.Runtime.Diagnostics/RuntimeStateManager.cs`
- `Iwesun.Runtime.Diagnostics/RuntimeStateContracts.cs`
- `Iwesun.Runtime.Diagnostics/RuntimeStateKey.cs`

### 3. 线程/任务执行目录基础能力已完成

- 静态/动态线程表
- 静态/动态任务表
- 线程和任务注册、心跳、状态推进、快照
- `runtime.execution` 反射目标暴露

涉及源码：

- `Iwesun.Runtime.Diagnostics/RuntimeExecutionManagement.cs`
- `Iwesun.Runtime.Diagnostics/RuntimeDiagnosticsServiceCollectionExtensions.cs`

### 4. 独立样板宿主已跑通

- 不依赖 DDNS Snap
- 静态数据类 + 动态数据类
- 多循环并行（协调/工作/监视）
- 动态任务登记
- 输出监视点 + 条件断点

涉及源码：

- `Iwesun.Runtime.SampleHost/Program.cs`
- `Iwesun.Runtime.SampleHost/SampleHostProfile.cs`
- `Iwesun.Runtime.SampleHost/SampleHostState.cs`
- `Iwesun.Runtime.SampleHost/SampleHostWorker.cs`

### 5. 文档体系已同步

已新增或更新：

- `docs/05-runtime-tooling/INJECTOR_STANDARDIZATION.md`
- `docs/05-runtime-tooling/INJECTOR_STANDARDIZATION_PLAN.md`
- `docs/05-runtime-tooling/INJECTOR_STANDARDIZATION_TASKS.md`
- `docs/05-runtime-tooling/SAMPLE_HOST.md`
- `docs/05-runtime-tooling/THREAD_TASK_MANAGEMENT.md`
- `docs/RUNTIME_DIAGNOSTICS.md`
- `docs/README.md`

## 三、未完成项（缺项）

### 1. 命名收口仍未最终冻结

当前模板命名已经可用，但对外长期命名（例如最终是否保留 `RuntimeInjector`）尚未冻结成版本约束。

### 2. 自动化测试覆盖不足

当前主要通过编译和样板宿主行为验证，尚未补齐完整自动化测试（尤其是退出路径、异常路径、断点行为路径）。

### 3. 退出模板仍是基础版

`RuntimeHostTemplate.Stop(...)` 已可用，但超时兜底、分阶段收尾策略、统一异常映射仍有细化空间。

### 4. 标注化规范缺少“操作手册”

目前有设计文档和方案文档，但还缺“可直接复制的最小模板手册”和“脚本化验收步骤”。

## 四、语义核对结果

本次重点核对了语义是否正确，结论如下：

1. 启动/退出与业务逻辑分离：**已满足**。
2. 业务注入只在边界出现：**已满足**。
3. 静态/动态数据类并存：**已满足**。
4. 线程/任务静态表和动态表：**已满足基础版**。
5. 输出监视点和断点：**已满足**。
6. 样板宿主不依赖业务项目：**已满足**。

## 五、验证记录

已完成验证：

- `dotnet build d:\Git Space\Runtime\Iwesun.Runtime.slnx -c Release --nologo`：成功。

备注：

- 终端有 CRLF/LF 提示，不影响编译结果和功能语义。

## 六、下一步重点（建议执行顺序）

### P1（优先）

1. 冻结标准模板与门面命名（形成稳定 API 约束）。
2. 补一份“最小接入模板文档”（新项目可直接复制）。
3. 补退出模板增强（超时、异常、兜底路径）。

### P2（次优先）

1. 为样板宿主补自动化验收脚本。
2. 为注入门面补单元测试和集成测试。
3. 增加跨线程上下文一致性验证。

### P3（后续）

1. 将样板宿主整理为正式发布模板包。
2. 提供“迁移指引”：旧宿主如何切到标准模板。

## 七、交接建议

接手方建议从以下顺序开始：

1. 先阅读本交接文档。
2. 阅读注入器三件套文档（设计、方案、任务书）。
3. 阅读样板宿主并跑一次本地 build。
4. 进入 P1 清单执行。

## 八、相关文档入口

- 注入器标准化设计：`docs/05-runtime-tooling/INJECTOR_STANDARDIZATION.md`
- 注入器标准化方案：`docs/05-runtime-tooling/INJECTOR_STANDARDIZATION_PLAN.md`
- 注入器标准化任务：`docs/05-runtime-tooling/INJECTOR_STANDARDIZATION_TASKS.md`
- 样板宿主：`docs/05-runtime-tooling/SAMPLE_HOST.md`
- 线程与任务管理：`docs/05-runtime-tooling/THREAD_TASK_MANAGEMENT.md`