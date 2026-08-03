# Runtime 改动边界与修改记录（2026-07-11）

## 1. 目标

明确 Runtime 与 DDNS Snap 的职责边界，避免把 Runtime 实现改动误落到 DDNS Snap 仓库。

## 2. 边界原则（执行口径）

1. Runtime 功能、协议、命令、数据结构、诊断目标等实现改动，只落地到 Runtime 仓库：`D:\Git Space\Runtime`。
2. DDNS Snap 仓库仅作为使用方，优先通过引用 Runtime 能力完成接入，保持“尽量少改动”。
3. 如确需改 DDNS Snap，仅允许最小接入改动（如调用入口、配置引用），不得在 DDNS 仓复刻 Runtime 实现逻辑。
4. 每次涉及 Runtime 的变更，都要同步更新本记录或同级变更记录文件，保证可追踪。

## 3. 本次修改记录

- 日期：2026-07-11
- 背景：用户明确要求“Runtime 的修改必须落地 Runtime 自身项目，不落在 DDNS 目录；并写清文档和修改记录”。
- 处理结果：
  - 在活跃需求中新增边界要求，纳入持续执行约束。
  - 新增本记录文件，作为后续审计与协作依据。

## 4. 本次实际改动文件（Runtime 仓）

1. `docs/REQUIREMENTS_ACTIVE.md`
   - 新增需求条目 #22（Runtime 改动落地边界）。
   - 在执行后复核区新增对应完成项。
2. `docs/design/RUNTIME_CHANGE_BOUNDARY_AND_LOG_2026-07-11.md`
   - 新增边界原则与本次变更记录。

## 5. 对后续协作的约束

1. 任何 Runtime 相关实现修改，默认先在 Runtime 仓完成。
2. DDNS Snap 侧若出现 Runtime 相关实现改动请求，先评估是否应回收至 Runtime 仓。
3. 交付说明中必须注明“实现落地点”和“使用方接入点”。
