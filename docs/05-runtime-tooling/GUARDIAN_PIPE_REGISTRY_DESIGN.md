# 进程守护代理与管道注册中心设计

> 状态：DRAFT  
> 最后更新：2026-07-10  
> 适用范围：`Iwesun.Runtime.Diagnostics`（进程内对象监视 + 主控汇聚）

> 说明：本文件作为“总览与实施计划”保留，不删除。  
> 方案拆分文档：
> - 静态方案（旧方案保留）：`INJECTOR_STATIC_SCHEME.md`
> - 动态方案（对象级缓冲池）：`INJECTOR_DYNAMIC_POOL_SCHEME.md`

## 1. 背景与问题

当前监视/断点注入已经具备能力，但在书写与生命周期管理上存在高出错风险：

1. 注册/反注册分散在多个位置，维护时容易漏改。
2. 同一对象实例（`this`）会重复执行代码段，要求幂等注册。
3. 同一 `this` 可能挂多个监视器。
4. 对象销户后如果无清理，注册表会残留脏数据。
5. 跨进程场景下，`this` 语义不能直接共享。

## 2. 设计目标

1. 监视器按对象实例语义管理：`(this, staticId)`。
2. 注入器从“静态单槽”改为“缓冲池式登记”。
2. 进程内单点治理：由进程守护代理（Guardian）统一管理注册生命周期。
3. 主控只做汇聚，不直接管理对象语义。
4. 提供主控管道注册中心：分支进程可申请专有管道。
5. 清理闭环：显式反注册 + GC 扫描兜底。

## 3. 核心原则

1. **对象语义仅在进程内有效**：`this` 不跨进程传递。
2. **静态定义仅提供唯一键**：使用 `[Attribute]` 或常量给出 `staticId`。
3. **注入器实例化由池统一分配**：不再以静态字段持有单实例。
4. **注册池不等于对象生命周期**：对象可常驻，监视器可解绑/重绑。
4. **主从分层**：
   - 本进程 Guardian：主责任（注册、解绑、清理、上报）
   - 主控监控：次责任（管道管理、数据汇聚）

## 4. 架构总览

```text
业务对象代码段
  -> Build(this, staticId)
  -> 进程内 Guardian 注册池 (this + staticId)
  -> 进程专有上报管道
  -> 主控监控(汇聚/显示/控制)
```

## 5. 数据模型

## 5.1 进程内对象监视槽（Guardian 内）

- 键：`ObjectRef + StaticId`
- 值：`MonitorRegistration`
  - `RegistrationId`
  - `StaticId`
  - `UnitType`
  - `CreatedAt`
  - `UpdatedAt`
  - `Active`

说明：
- ObjectRef 使用弱引用语义（`WeakReference<object>` 或 `ConditionalWeakTable` 配套状态）。
- 同一对象可对应多个 `StaticId`。

## 5.2 注入器缓冲池登记表（进程内）

- 键：`ObjectRef + StaticId`
- 值：`InjectorSlot`
  - `RegistrationId`
  - `InjectorKind`（Monitor/Breakpoint/Watcher/Hook）
  - `Active`
  - `LastUsedAt`
  - `RefCount`（可选）

说明：
- 由池负责“分配/复用/回收”。
- 静态定义只提供 `StaticId` 与元数据，不再直接承载实例。

## 5.3 主控管道注册表

- 键：`BranchId`（分支进程标识）
- 值：`PipeLease`
  - `PipeName`
  - `OwnerProcessId`
  - `CreatedAt`
  - `ExpiresAt`
  - `State`（Active/Released/Expired）

说明：
- 每个分支可申请专有管道。
- 主控负责分配、续期、回收。

## 6. 关键流程

## 6.1 注册（幂等）

`Build(this, staticId)`：
1. 查 `ObjectRef + StaticId` 是否已存在且 `Active=true`。
2. 存在则复用，不重复注册。
3. 不存在则创建新 `RegistrationId` 并登记。
4. 同步在注入器缓冲池中建立或复用 `InjectorSlot`。

## 6.2 反注册（显式）

`Unregister(this, staticId)`：
1. 仅解绑该对象该静态监视器槽位。
2. 不销毁对象，不影响同对象其他监视器。
3. 对应 `InjectorSlot` 标记为可回收（或减引用计数）。

`UnregisterAll(this)`：
1. 解绑该对象全部监视器槽位。

## 6.3 GC 兜底清理

Guardian 定期扫描弱引用：
1. `TryGetTarget == false` 视为对象销户。
2. 清理其全部 `MonitorRegistration`。
3. 回收其全部 `InjectorSlot`（释放池容量）。

## 6.4 管道申请与释放

1. 分支进程向主控请求 `AcquirePipe(branchId)`。
2. 主控返回专有 `PipeName`（租约）。
3. 分支通过该管道上报。
4. 分支主动 `ReleasePipe` 或租约超时后主控回收。

## 7. 安全与稳定性约束

1. 主控只接收已登记分支的管道请求。
2. 每个分支默认单专有管道（可配置上限）。
3. 管道名必须由主控生成，分支不可自定义覆盖。
4. 失联分支由主控按租约超时回收。
5. 清理任务必须幂等，重复执行不应抛错。

## 8. 书写规范（避免分散修改）

业务代码只写一类调用：

```csharp
var monitor = RuntimeObjectLifecycle.Build(this, staticId);
monitor.Output(...);
```

生命周期控制统一写在 Guardian 与 RuntimeObjectLifecycle 内部，不要求业务点重复写注册细节。

## 8.1 注入器迁移约束

1. 禁止新增 `static XxxInjector _instance` 这类单槽持有写法。
2. 原静态注入器入口保留为兼容 facade，但内部必须转发到缓冲池。
3. 池内槽位必须可审计：可列出 active/inactive、分配数、回收数。
4. 回收必须同时支持：
   - 显式反注册触发回收
   - GC 扫描失效对象触发回收

## 9. 分阶段落地建议

1. Phase 1：实现进程内 `(this, staticId)` 注册池与幂等注册。
2. Phase 2：将注入器从静态单槽迁移到缓冲池登记，并提供兼容 facade。
3. Phase 3：实现显式反注册 + GC 扫描兜底 + 槽位回收。
4. Phase 4：实现主控管道注册中心与分支专有管道租约。
5. Phase 5：将现有监视/断点输出入口统一收敛到 Guardian 代理通道。

## 10. 最小实现任务清单（可直接执行）

## 10.1 M1：对象级注册池（先不改主控）

目标：先把静态单槽替换为 `(this, staticId)` 幂等登记池。

任务：
1. 新增 `RuntimeObjectLifecycle`（进程内）：
   - `Build(object owner, string staticId, string unitType)`  
   - `Unregister(object owner, string staticId)`  
   - `UnregisterAll(object owner)`
2. 内部维护对象弱引用与槽位映射（支持同一 `this` 多监视器）。
3. 保留兼容 facade：原静态入口转发到 `RuntimeObjectLifecycle`。

验收：
1. 同一对象重复 `Build(this, sameStaticId)` 不重复登记。
2. 同一对象 `Build(this, differentStaticId)` 可得到多个槽位。
3. 不再新增任何 `static _instance` 注入器持有模式。

## 10.2 M2：显式反注册与回收

目标：不依赖对象销毁即可解绑，且能自动清残留。

任务：
1. 业务侧可调用 `Unregister(this, staticId)` 解绑单槽。
2. 增加后台清理：扫描弱引用失效对象并回收其全部槽位。
3. 增加池统计：`allocated / active / reclaimed / gcReclaimed`。

验收：
1. 显式 `Unregister` 后输出调用不再命中该槽位。
2. 对象无强引用后，GC + 扫描可回收残留槽位。
3. 清理任务重复执行不报错（幂等）。

## 10.3 M3：Guardian 管道代理（进程内统一出口）

目标：每个进程只经 Guardian 上报，不让业务点直接拼管道行为。

任务：
1. 新增 `RuntimeProcessGuardian`：
   - 持有本进程生命周期池引用
   - 提供统一 `Report(...)` 出口
2. 业务输出入口统一转发到 Guardian（先保留兼容路径）。
3. 增加 guardian 状态快照目标（供 CLI 查询）。

验收：
1. 业务代码仅保留 `Build(...).Output(...)` 写法。
2. 进程内所有上报路径都能从 guardian 快照看到。

## 10.4 M4：主控管道注册中心 + 分支专有管道

目标：主控分配专有管道，分支按租约上报。

任务：
1. 主控新增 `AcquirePipe(branchId)`、`ReleasePipe(branchId)`、`RenewPipe(branchId)`。
2. 分支启动时申请专有管道，退出时释放。
3. 主控维护租约超时回收逻辑。

验收：
1. 至少两个分支可同时申请并获得不同管道名。
2. 失联分支在租约过期后自动回收。
3. 主控可查询当前分支管道租约表。

## 10.5 M5：测试闭环（功能测试）

目标：避免只改结构不改验证。

任务：
1. FunctionalTests 增加对象级池场景：
   - 同一 `this` 多 staticId
   - 重复 Build 幂等
   - 显式 Unregister 生效
2. 增加 GC 回收场景（弱引用失效后清理）。
3. 增加分支管道申请/释放/过期回收场景。

验收：
1. `dotnet build Iwesun.Runtime.slnx -c Release` 通过。
2. `dotnet test Iwesun.Runtime.FunctionalTests ...` 通过。
3. 新增场景全部绿色并可重复运行。
