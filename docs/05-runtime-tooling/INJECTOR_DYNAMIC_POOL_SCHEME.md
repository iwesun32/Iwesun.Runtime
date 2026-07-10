# 注入器动态缓冲池方案（对象级登记与回收）

> 状态：DRAFT（目标方案）  
> 最后更新：2026-07-10  
> 适用范围：`Iwesun.Runtime.Diagnostics` 对象级注入生命周期治理

## 1. 方案定位

该方案将注入器从静态单槽改为对象级缓冲池登记：

1. 以 `(this, staticId)` 作为监视器槽位键。
2. 支持同一 `this` 挂多个监视器。
3. 显式反注册 + GC 扫描兜底回收。
4. 每个进程由 Guardian 统一管理本进程对象池，并向主控上报。

## 2. 核心模型

## 2.1 进程内对象池

- 键：`ObjectRef + StaticId`
- 值：`InjectorSlot / Registration`
- 能力：幂等 Build、单槽解绑、整对象解绑、统计与审计

## 2.2 主从职责

1. 分支进程 Guardian：管理本进程对象池与上报。
2. 主控：分配/回收分支专有管道并汇聚数据。
3. 主控不直接管理 `this` 语义。

## 3. 生命周期

1. Build：
   - 已存在活跃槽位则复用
   - 不存在则创建并登记
2. Unregister：
   - 支持 `Unregister(this, staticId)` 与 `UnregisterAll(this)`
3. 回收：
   - 显式解绑优先
   - 弱引用扫描失效对象后兜底清理

## 4. 约束

1. 静态定义仅用于唯一标识（`staticId`），不直接承载对象实例状态。
2. 不使用对象指针作为跨进程标识。
3. 池清理与解绑必须幂等。
4. 分支专有管道名必须由主控分配。

## 5. 迁移策略

1. 先建立动态池与兼容 facade 并行。
2. 业务代码逐步收敛到 `Build(this, staticId).Output(...)`。
3. 稳定后将静态入口降级为转发层，最终默认走动态池。

## 6. 关联文档

1. 总体设计与阶段计划：`GUARDIAN_PIPE_REGISTRY_DESIGN.md`
2. 静态旧方案保留说明：`INJECTOR_STATIC_SCHEME.md`

