using System.Text.Json;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeRootContainerTarget : RuntimeDiagnosticTargetBase
{
	private enum RefreshGroup
	{
		OutputMonitor,
		Registry,
		#if DEBUG
		Breakpoints,
		#endif
		Hooks,
		RuntimeState,
		Execution,
		Managed,
		DiagnosticHub,
		Protocol
	}

	private static readonly RefreshGroup[] AllRefreshGroups =
	[
		RefreshGroup.OutputMonitor,
		RefreshGroup.Registry,
		#if DEBUG
		RefreshGroup.Breakpoints,
		#endif
		RefreshGroup.Hooks,
		RefreshGroup.RuntimeState,
		RefreshGroup.Execution,
		RefreshGroup.Managed,
		RefreshGroup.DiagnosticHub,
		RefreshGroup.Protocol
	];

	private static readonly IReadOnlyDictionary<RefreshGroup, TimeSpan> RefreshGroupTtls = new Dictionary<RefreshGroup, TimeSpan>
	{
		[RefreshGroup.OutputMonitor] = TimeSpan.FromMilliseconds(500),
		[RefreshGroup.Registry] = TimeSpan.FromMilliseconds(500),
		#if DEBUG
		[RefreshGroup.Breakpoints] = TimeSpan.FromMilliseconds(500),
		#endif
		[RefreshGroup.Hooks] = TimeSpan.FromMilliseconds(500),
		[RefreshGroup.RuntimeState] = TimeSpan.FromMilliseconds(500),
		[RefreshGroup.Execution] = TimeSpan.FromMilliseconds(300),
		[RefreshGroup.Managed] = TimeSpan.FromMilliseconds(300),
		[RefreshGroup.DiagnosticHub] = TimeSpan.FromMilliseconds(300),
		[RefreshGroup.Protocol] = TimeSpan.FromSeconds(2)
	};

	private const int ReflectionTypePageSize = 256;
	private const int ReflectionTypePageCountLimit = 8;
	private const int ReflectionTypeTotalLimit = ReflectionTypePageSize * ReflectionTypePageCountLimit;
	private readonly object _refreshGate = new();
	private readonly RuntimeRootContainer _root;
	private readonly RuntimeExecutionManager _execution;
	private readonly RuntimeManagedRegistry _managed;
	private readonly RuntimeStateManager _state;
	private readonly RuntimeDiagnosticHub _hub;
	#if DEBUG
	private readonly RuntimeDiagnosticBreakpoints _breakpoints;
	#endif
	private readonly RuntimeDiagnosticHooks _hooks;
	private readonly Dictionary<RefreshGroup, DateTimeOffset> _groupRefreshAt = new();
	private DateTimeOffset _lastRefreshAt = DateTimeOffset.MinValue;

	public RuntimeRootContainerTarget(
		RuntimeRootContainer root,
		RuntimeExecutionManager execution,
		RuntimeManagedRegistry managed,
		RuntimeStateManager state,
		RuntimeDiagnosticHub hub,
		#if DEBUG
		RuntimeDiagnosticBreakpoints breakpoints,
		#endif
		RuntimeDiagnosticHooks hooks) : base("runtime.root")
	{
		_root = root ?? throw new ArgumentNullException(nameof(root));
		_execution = execution ?? throw new ArgumentNullException(nameof(execution));
		_managed = managed ?? throw new ArgumentNullException(nameof(managed));
		_state = state ?? throw new ArgumentNullException(nameof(state));
		_hub = hub ?? throw new ArgumentNullException(nameof(hub));
		#if DEBUG
		_breakpoints = breakpoints ?? throw new ArgumentNullException(nameof(breakpoints));
		#endif
		_hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
	}

	public override object Snapshot()
	{
		EnsureRootFresh();
		return _root.Snapshot();
	}

	public override Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct)
	{
		switch (command.Action.ToLowerInvariant())
		{
			case "snapshot":
				EnsureRootFresh();
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, _root.Snapshot()));
				case "paths":
				case "filepaths":
					EnsureRootFresh();
					return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, _root.GetFilePathDescriptorsSnapshot()));
			case "refresh":
				RefreshGroups(force: true, requestedGroups: null);
				return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, _root.Snapshot()));
			case "table":
				{
					var table = ReadString(command, "table");
					if (string.IsNullOrWhiteSpace(table))
						return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "table is required."));
					EnsureTableFresh(table);

					var snapshot = _root.Snapshot();
					var matched = snapshot.Tables.FirstOrDefault(x => x.TableName.Equals(table, StringComparison.OrdinalIgnoreCase));
					if (matched == null)
						return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Table not found: {table}"));
					return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, matched));
				}
			case "get":
				{
					var table = ReadString(command, "table");
					var id = ReadString(command, "id");
					if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(id))
						return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "table and id are required."));
					EnsureTableFresh(table);

					if (_root.TryGet(table, id, out var entry))
						return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, entry));
					return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { found = false }));
				}
			case "query":
				{
					var table = ReadString(command, "table");
					var key = ReadString(command, "key");
					var value = ReadString(command, "value");
					if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
						return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "table, key and value are required."));
					EnsureTableFresh(table);

					var result = _root.QueryBySecondary(table, key, value);
					return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, result));
				}
			case "prefix":
				{
					var table = ReadString(command, "table");
					var prefix = ReadString(command, "prefix");
					var take = ReadInt(command, "take") ?? 100;
					if (string.IsNullOrWhiteSpace(table) || string.IsNullOrWhiteSpace(prefix))
						return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "table and prefix are required."));
					EnsureTableFresh(table);

					var result = _root.QueryByPrimaryPrefix(table, prefix, take);
					return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, result));
				}
			case "indexes":
				{
					var table = ReadString(command, "table");
					if (string.IsNullOrWhiteSpace(table))
						return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "table is required."));
					EnsureTableFresh(table);

					var result = _root.ListAuxIndexes(table);
					return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, result));
				}
			default:
				return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Unsupported action: {command.Action}"));
		}
	}

	public void RefreshFromRuntime() => RefreshFromRuntime(force: true);

	private void EnsureRootFresh() => RefreshGroups(force: false, requestedGroups: null);

	private void EnsureTableFresh(string tableName)
	{
		if (TryResolveRefreshGroup(tableName, out var group))
		{
			RefreshGroups(force: false, requestedGroups: [group]);
			return;
		}

		EnsureRootFresh();
	}

	private void RefreshFromRuntime(bool force) => RefreshGroups(force, requestedGroups: null);

	private void RefreshGroups(bool force, IReadOnlyList<RefreshGroup>? requestedGroups)
	{
		lock (_refreshGate)
		{
			var now = DateTimeOffset.UtcNow;
			var groups = requestedGroups ?? AllRefreshGroups;
			var refreshedAny = false;
			foreach (var group in groups)
			{
				if (!force
					&& _groupRefreshAt.TryGetValue(group, out var refreshedAt)
					&& now - refreshedAt < RefreshGroupTtls[group])
				{
					continue;
				}

				RefreshGroupCore(group);
				_groupRefreshAt[group] = DateTimeOffset.UtcNow;
				refreshedAny = true;
			}

			if (refreshedAny)
			{
				_lastRefreshAt = DateTimeOffset.UtcNow;
			}
		}
	}

	private void RefreshGroupCore(RefreshGroup group)
	{
		switch (group)
		{
			case RefreshGroup.OutputMonitor:
				RefreshOutputMonitorTables();
				break;
			case RefreshGroup.Registry:
				RefreshRegistryTables();
				break;
			#if DEBUG
			case RefreshGroup.Breakpoints:
				RefreshBreakpointTables();
				break;
			#endif
			case RefreshGroup.Hooks:
				RefreshHookTables();
				break;
			case RefreshGroup.RuntimeState:
				RefreshRuntimeStateTables();
				break;
			case RefreshGroup.Execution:
				RefreshExecutionTables();
				break;
			case RefreshGroup.Managed:
				RefreshManagedTables();
				break;
			case RefreshGroup.DiagnosticHub:
				RefreshDiagnosticHubTables();
				break;
			case RefreshGroup.Protocol:
				RefreshProtocolTables();
				break;
		}
	}

	private void RefreshOutputMonitorTables()
	{
		var output = DiagnosticSwitchboard.Snapshot();
		var filePathDescriptors = new List<RuntimeFilePathDescriptor>();
		if (!string.IsNullOrWhiteSpace(output.FilePath))
		{
			filePathDescriptors.Add(new RuntimeFilePathDescriptor(
				output.FilePath,
				description: "Resolved diagnostics file output path.",
				purpose: "diagnostic-file-output",
				source: "resolved(startup-args > persisted-json > startup-default)",
				isPrimaryRecord: true,
				annotations: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
				{
					["channel"] = "file",
					["fileOutputEnabled"] = output.FileOutputEnabled.ToString(),
					["globalEnabled"] = output.GlobalEnabled.ToString(),
					["pipeName"] = output.RuntimeDiagnosticsPipeName
				}));
		}

		_root.SetFilePathDescriptors(filePathDescriptors);
		_root.SetTableEntries("T01.OutputMonitor.Meta",
		[
			Entry("t01.output.meta", "output.meta", output, Secondary(
				("table", "T01"),
				("globalEnabled", output.GlobalEnabled.ToString()),
				("pipeOutputEnabled", output.PipeOutputEnabled.ToString()),
				("fileOutputEnabled", output.FileOutputEnabled.ToString())))
		]);
		_root.SetTableEntries("T01.OutputMonitor.Sections",
			output.Sections.Select(section => Entry(
				$"section.{section.Section}",
				section.Section,
				section,
				Secondary(("table", "T01"), ("section", section.Section)))));
		_root.SetTableEntries("T01.OutputMonitor.Statements",
			output.Statements.Select(statement => Entry(
				$"statement.{statement.Id}",
				statement.Id,
				statement,
				Secondary(
					("table", "T01"),
					("section", statement.Section),
					("kind", statement.Kind),
					("outputPointId", statement.OutputPointId ?? "")))));
		_root.SetTableEntries("T01.OutputMonitor.OutputPoints",
			output.OutputPoints.Select(point => Entry(
				$"outputpoint.{point.Id}",
				point.Id,
				point,
				Secondary(
					("table", "T01"),
					("section", point.Section),
					("category", point.Category)))));
	}

	private void RefreshRegistryTables()
	{
		var registry = _hub.RegistrySnapshot;
		_root.SetTableEntries("T02.Registry.WatchPoints",
			registry.WatchPoints.Select(point => Entry(
				$"watch.{point.Id}",
				point.Id,
				point,
				Secondary(("table", "T02"), ("section", point.Section), ("kind", point.Kind)))));
		#if DEBUG
		_root.SetTableEntries("T02.Registry.Breakpoints",
			registry.Breakpoints.Select(point => Entry(
				$"breakpoint.registry.{point.Id}",
				point.Id,
				point,
				Secondary(("table", "T02"), ("section", point.Section)))));
		#endif
		_root.SetTableEntries("T02.Registry.Hooks",
			registry.Hooks.Select(hook => Entry(
				$"hook.registry.{hook.HookId}",
				hook.HookId,
				hook,
				Secondary(("table", "T02"), ("targetType", hook.TargetTypeName)))));
	}

	#if DEBUG
	private void RefreshBreakpointTables()
	{
		var breakpointSnapshot = _breakpoints.Snapshot();
		_root.SetTableEntries("T03.BreakpointTable",
			breakpointSnapshot.Select(point => Entry(
				$"breakpoint.{point.Id}",
				point.Id,
				point,
				Secondary(
					("table", "T03"),
					("section", point.Section),
					("enabled", point.Enabled.ToString()),
					("isWaiting", point.IsWaiting.ToString())))));
	}
	#endif

	private void RefreshHookTables()
	{
		_root.SetTableEntries("T04.HookTable.Available",
			_hooks.AvailableHooks().Select(hook => Entry(
				$"hook.available.{hook.Id}",
				hook.Id,
				hook,
				Secondary(("table", "T04"), ("eventName", hook.EventName), ("targetType", hook.TargetType.FullName ?? "")))));
		_root.SetTableEntries("T04.HookTable.Active",
			_hooks.ActiveHooks().Select(hook => Entry(
				$"hook.active.{hook.HookId}",
				hook.HookId,
				hook,
				Secondary(("table", "T04"), ("eventName", hook.EventName), ("targetType", hook.TargetTypeName)))));
	}

	private void RefreshRuntimeStateTables()
	{
		var stateSnapshot = _state.Snapshot();
		_root.SetTableEntries("T05.RuntimeState.Current",
		[
			Entry(
				"runtime.state.current",
				stateSnapshot.CurrentState.Code.ToString(),
				stateSnapshot,
				Secondary(
					("table", "T05"),
					("code", stateSnapshot.CurrentState.Code.ToString()),
					("name", stateSnapshot.CurrentState.Name)))
		]);
		_root.SetTableEntries("T05.RuntimeState.Catalog",
			stateSnapshot.States.Select(state => Entry(
				$"runtime.state.{state.Code}",
				state.Code.ToString(),
				state,
				Secondary(("table", "T05"), ("name", state.Name), ("group", state.Group.ToString())))));
	}

	private void RefreshExecutionTables()
	{
		var executionSnapshot = _execution.Snapshot();
		var allTasks = executionSnapshot.StaticTasks.Concat(executionSnapshot.DynamicTasks).ToArray();
		_root.SetTableEntries("T06.ThreadTable",
			executionSnapshot.StaticThreads.Concat(executionSnapshot.DynamicThreads).Select(thread => Entry(
				$"thread.{thread.Id}",
				thread.Id,
				thread,
				Secondary(
					("table", "T06"),
					("state", thread.State.ToString()),
					("lifetime", thread.Lifetime.ToString()),
					("kind", thread.Kind.ToString()),
					("owner", thread.Owner)))));
		_root.SetTableEntries("T07.TaskTable",
			allTasks.Select(task => Entry(
				$"task.{task.Id}",
				task.Id,
				task,
				Secondary(
					("table", "T07"),
					("state", task.State.ToString()),
					("lifetime", task.Lifetime.ToString()),
					("category", task.Category),
					("threadId", task.ThreadId)))));
		_root.SetTableEntries("T06.ProcessTable",
			allTasks
				.Where(task => task.Category.Equals("process", StringComparison.OrdinalIgnoreCase))
				.Select(task => Entry(
					$"process.task.{task.Id}",
					task.Id,
					task,
					Secondary(
						("table", "T06"),
						("kind", "process"),
						("state", task.State.ToString()),
						("lifetime", task.Lifetime.ToString()),
						("threadId", task.ThreadId)))));
	}

	private void RefreshManagedTables()
	{
		var managedRegistrations = _managed.SnapshotRegistrations();
		var managedCommandQueues = _managed.SnapshotCommandQueues();
		var managedEvents = _managed.SnapshotEvents(256);
		var globalLifecycle = _managed.GlobalLifecycleState;
		var globalLifecycleHistory = _managed.SnapshotGlobalLifecycleHistory(64);
		var unitStateHistories = _managed.SnapshotAllUnitStateHistories(64);
		_root.SetTableEntries("T08.ManagedTable.Registrations",
			managedRegistrations.Select(registration => Entry(
				$"managed.registration.{registration.UnitId}",
				registration.UnitId,
				registration,
				Secondary(("table", "T08"), ("unitType", registration.UnitType), ("ownership", registration.Ownership)))));
		_root.SetTableEntries("T08.ManagedTable.ProcessRegistrations",
			managedRegistrations
				.Where(registration => registration.UnitType.Equals("process", StringComparison.OrdinalIgnoreCase))
				.Select(registration => Entry(
					$"managed.process.registration.{registration.UnitId}",
					registration.UnitId,
					registration,
					Secondary(("table", "T08"), ("unitType", registration.UnitType), ("ownership", registration.Ownership)))));
		_root.SetTableEntries("T08.ManagedTable.Commands",
			managedCommandQueues.SelectMany(queue =>
				queue.Commands.Select(command => Entry(
					$"managed.command.{queue.UnitId}.{command.Sequence}",
					command.Sequence.ToString(),
					command,
					Secondary(
						("table", "T08"),
						("unitId", queue.UnitId),
						("kind", command.Kind.ToString()),
						("targetUnitId", command.TargetUnitId))))));
		_root.SetTableEntries("T08.ManagedTable.Events",
			managedEvents.Select(evt => Entry(
				$"managed.event.{evt.Sequence}",
				evt.Sequence.ToString(),
				evt,
				Secondary(
					("table", "T08"),
					("unitId", evt.UnitId),
					("kind", evt.Kind)))));
		_root.SetTableEntries("T08.ManagedTable.GlobalLifecycle",
		[
			Entry(
				"managed.global.lifecycle.current",
				globalLifecycle.CurrentState.Code.ToString(),
				new
				{
					globalLifecycle,
					exitDeadlineUtc = _managed.GlobalExitDeadlineUtc
				},
				Secondary(("table", "T08"), ("kind", "global-lifecycle"), ("name", globalLifecycle.CurrentState.Name)))
		]);
		_root.SetTableEntries("T08.ManagedTable.GlobalLifecycleHistory",
			globalLifecycleHistory.Select((state, index) => Entry(
				$"managed.global.lifecycle.history.{index}.{state.Code}",
				$"{index:000}-{state.Code}",
				state,
				Secondary(("table", "T08"), ("kind", "global-lifecycle-history"), ("name", state.Name)))));
		_root.SetTableEntries("T08.ManagedTable.UnitStateHistory",
			unitStateHistories.SelectMany(pair =>
				pair.Value.Select((state, index) => Entry(
					$"managed.unit.history.{pair.Key}.{index}.{state.Code}",
					$"{pair.Key}:{index:000}",
					new
					{
						unitId = pair.Key,
						state
					},
					Secondary(("table", "T08"), ("kind", "unit-state-history"), ("unitId", pair.Key), ("name", state.Name))))));
		_root.SetTableEntries("T08.ManagedTable.Meta",
		[
			Entry(
				"managed.meta",
				"managed.meta",
				new
				{
					registrationCount = managedRegistrations.Count,
					commandQueueCount = managedCommandQueues.Count,
					pendingCommandCount = _managed.PendingCommandCount,
					droppedCommandCount = _managed.DroppedCommandCount,
					eventCount = managedEvents.Count,
					globalLifecycleState = globalLifecycle.CurrentState.Name,
					globalLifecycleHistoryCount = globalLifecycleHistory.Count,
					unitStateHistoryCount = unitStateHistories.Count
				},
				Secondary(("table", "T08")))
		]);
	}

	private void RefreshDiagnosticHubTables()
	{
		var hostSnapshot = _hub.HostSnapshot;
		var hubRegistrySnapshot = _hub.RegistrySnapshot;
		var hubEvents = _hub.SnapshotEvents(256);
		_root.SetTableEntries("T09.DiagnosticHubTable.Host",
		[
			Entry(
				"hub.host",
				hostSnapshot.ProcessId.ToString(),
				hostSnapshot,
				Secondary(("table", "T09"), ("processName", hostSnapshot.ProcessName)))
		]);
		_root.SetTableEntries("T09.DiagnosticHubTable.Targets",
			_hub.TargetIds.Select(targetId => Entry(
				$"hub.target.{targetId}",
				targetId,
				new { targetId },
				Secondary(("table", "T09"), ("targetId", targetId)))));
		_root.SetTableEntries("T09.DiagnosticHubTable.Events",
			hubEvents.Select((evt, index) => Entry(
				$"hub.event.{index}.{evt.Timestamp.ToUnixTimeMilliseconds()}",
				$"{evt.Timestamp.ToUnixTimeMilliseconds()}-{index}",
				evt,
				Secondary(
					("table", "T09"),
					("targetId", evt.TargetId),
					("kind", evt.Kind)))));
		_root.SetTableEntries("T09.DiagnosticHubTable.Registry.WatchPoints",
			hubRegistrySnapshot.WatchPoints.Select(point => Entry(
				$"hub.registry.watch.{point.Id}",
				point.Id,
				point,
				Secondary(("table", "T09"), ("kind", "watchpoint"), ("section", point.Section)))));
		#if DEBUG
		_root.SetTableEntries("T09.DiagnosticHubTable.Registry.Breakpoints",
			hubRegistrySnapshot.Breakpoints.Select(point => Entry(
				$"hub.registry.breakpoint.{point.Id}",
				point.Id,
				point,
				Secondary(("table", "T09"), ("kind", "breakpoint"), ("section", point.Section)))));
		#endif
		_root.SetTableEntries("T09.DiagnosticHubTable.Registry.Hooks",
			hubRegistrySnapshot.Hooks.Select(hook => Entry(
				$"hub.registry.hook.{hook.HookId}",
				hook.HookId,
				hook,
				Secondary(("table", "T09"), ("kind", "hook"), ("targetType", hook.TargetTypeName)))));
		_root.SetTableEntries("T09.DiagnosticHubTable.Meta",
		[
			Entry(
				"hub.meta",
				"hub.meta",
				new
				{
					targetCount = _hub.TargetIds.Count,
					eventCount = hubEvents.Count,
					watchPointCount = hubRegistrySnapshot.WatchPoints.Count,
					#if DEBUG
					breakpointCount = hubRegistrySnapshot.Breakpoints.Count,
					#endif
					hookCount = hubRegistrySnapshot.Hooks.Count
				},
				Secondary(("table", "T09")))
		]);
		_root.SetTableEntries("T09.DiagnosticHubTable.Reflection.Assemblies",
			hostSnapshot.Assemblies.Select(assembly => Entry(
				$"hub.reflection.assembly.{assembly.Name}",
				assembly.FullName,
				assembly,
				Secondary(
					("table", "T09"),
					("kind", "reflection-assembly"),
					("assemblyName", assembly.Name),
					("hasError", (!string.IsNullOrWhiteSpace(assembly.Error)).ToString())))));
		var reflectionTypes = new List<(string AssemblyName, string AssemblyFullName, RuntimeTypeSnapshot Type)>(ReflectionTypeTotalLimit);
		var totalTypeCount = 0;
		foreach (var assembly in hostSnapshot.Assemblies)
		{
			foreach (var type in assembly.Types)
			{
				totalTypeCount++;
				if (reflectionTypes.Count >= ReflectionTypeTotalLimit)
				{
					continue;
				}

				reflectionTypes.Add((assembly.Name, assembly.FullName, type));
			}
		}

		var reflectionPageCount = (reflectionTypes.Count + ReflectionTypePageSize - 1) / ReflectionTypePageSize;
		var reflectionTypeCatalog = Enumerable.Range(1, reflectionPageCount)
			.Select(page => Entry(
				$"hub.reflection.types.catalog.{page:D4}",
				$"{page:D4}",
				new
				{
					page,
					pageSize = ReflectionTypePageSize,
					tableName = $"T09.DiagnosticHubTable.Reflection.Types.Page.{page:D4}"
				},
				Secondary(
					("table", "T09"),
					("kind", "reflection-types-catalog"))))
			.ToArray();
		_root.SetTableEntries("T09.DiagnosticHubTable.Reflection.Types.Catalog", reflectionTypeCatalog);
		foreach (var page in Enumerable.Range(1, reflectionPageCount))
		{
			var slice = reflectionTypes
				.Skip((page - 1) * ReflectionTypePageSize)
				.Take(ReflectionTypePageSize)
				.ToArray();
			_root.SetTableEntries(
				$"T09.DiagnosticHubTable.Reflection.Types.Page.{page:D4}",
				slice.Select((item, index) => Entry(
					$"hub.reflection.type.{page:D4}.{index:D4}",
					$"{item.AssemblyFullName}|{item.Type.FullName}",
					new
					{
						item.AssemblyName,
						item.AssemblyFullName,
						item.Type.Name,
						item.Type.FullName,
						item.Type.Namespace,
						item.Type.Kind,
						item.Type.IsPublic,
						item.Type.IsAbstract,
						item.Type.IsGeneric,
						item.Type.PublicPropertyCount,
						item.Type.PublicFieldCount,
						item.Type.PublicMethodCount,
						item.Type.PublicEventCount,
						item.Type.Interfaces
					},
					Secondary(
						("table", "T09"),
						("kind", "reflection-type"),
						("assemblyName", item.AssemblyName),
						("namespace", item.Type.Namespace),
						("typeKind", item.Type.Kind),
						("isPublic", item.Type.IsPublic.ToString())))));
		}
		_root.SetTableEntries("T09.DiagnosticHubTable.Reflection.Meta",
		[
			Entry(
				"hub.reflection.meta",
				"hub.reflection.meta",
				new
				{
					assemblyCount = hostSnapshot.Assemblies.Count,
					totalTypeCount,
					erroredAssemblyCount = hostSnapshot.Assemblies.Count(x => !string.IsNullOrWhiteSpace(x.Error)),
					pagedTypeCount = reflectionTypes.Count,
					pageSize = ReflectionTypePageSize,
					pageCount = reflectionPageCount,
					pageCountLimit = ReflectionTypePageCountLimit,
					totalTypeLimit = ReflectionTypeTotalLimit,
					isTypeListTruncated = totalTypeCount > reflectionTypes.Count
				},
				Secondary(("table", "T09"), ("kind", "reflection-meta")))
		]);
	}

	private void RefreshProtocolTables()
	{
		_root.SetTableEntries("T10.ProtocolTable",
		[
			Entry("protocol.runtime.action", "RuntimeDiagnosticAction", typeof(RuntimeDiagnosticAction), Secondary(("table", "T10"), ("kind", "action"))),
			Entry("protocol.runtime.actionresult", "RuntimeDiagnosticActionResult", typeof(RuntimeDiagnosticActionResult), Secondary(("table", "T10"), ("kind", "result"))),
			Entry("protocol.runtime.frame", "RuntimeDiagnosticFrame", typeof(RuntimeDiagnosticFrame), Secondary(("table", "T10"), ("kind", "frame"))),
			Entry("protocol.runtime.header", "RuntimeDiagnosticFrameHeader", typeof(RuntimeDiagnosticFrameHeader), Secondary(("table", "T10"), ("kind", "frame-header"))),
			Entry("protocol.runtime.command", "RuntimeDiagnosticFrameCommand", typeof(RuntimeDiagnosticFrameCommand), Secondary(("table", "T10"), ("kind", "frame-command"))),
			Entry("protocol.runtime.status", "RuntimeDiagnosticFrameStatus", typeof(RuntimeDiagnosticFrameStatus), Secondary(("table", "T10"), ("kind", "frame-status"))),
			Entry("protocol.runtime.meta", "RuntimeDiagnosticFrameMeta", typeof(RuntimeDiagnosticFrameMeta), Secondary(("table", "T10"), ("kind", "frame-meta")))
		]);
	}

	private static bool TryResolveRefreshGroup(string tableName, out RefreshGroup group)
	{
		if (tableName.StartsWith("T01.", StringComparison.OrdinalIgnoreCase))
		{
			group = RefreshGroup.OutputMonitor;
			return true;
		}

		if (tableName.StartsWith("T02.", StringComparison.OrdinalIgnoreCase))
		{
			group = RefreshGroup.Registry;
			return true;
		}

		#if DEBUG
		if (tableName.StartsWith("T03.", StringComparison.OrdinalIgnoreCase))
		{
			group = RefreshGroup.Breakpoints;
			return true;
		}
		#endif

		if (tableName.StartsWith("T04.", StringComparison.OrdinalIgnoreCase))
		{
			group = RefreshGroup.Hooks;
			return true;
		}

		if (tableName.StartsWith("T05.", StringComparison.OrdinalIgnoreCase))
		{
			group = RefreshGroup.RuntimeState;
			return true;
		}

		if (tableName.StartsWith("T06.", StringComparison.OrdinalIgnoreCase)
			|| tableName.StartsWith("T07.", StringComparison.OrdinalIgnoreCase))
		{
			group = RefreshGroup.Execution;
			return true;
		}

		if (tableName.StartsWith("T08.", StringComparison.OrdinalIgnoreCase))
		{
			group = RefreshGroup.Managed;
			return true;
		}

		if (tableName.StartsWith("T09.", StringComparison.OrdinalIgnoreCase))
		{
			group = RefreshGroup.DiagnosticHub;
			return true;
		}

		if (tableName.StartsWith("T10.", StringComparison.OrdinalIgnoreCase))
		{
			group = RefreshGroup.Protocol;
			return true;
		}

		group = default;
		return false;
	}

	private static RuntimeRootEntryEnvelope Entry(
		string id,
		string primaryKey,
		object payload,
		IReadOnlyDictionary<string, string>? secondaryKeys = null) =>
		new(id, primaryKey, secondaryKeys, payload);

	private static IReadOnlyDictionary<string, string> Secondary(params (string Key, string Value)[] keys)
	{
		var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var pair in keys)
		{
			if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
			{
				continue;
			}

			map[pair.Key] = pair.Value;
		}

		return map;
	}

	private static string? ReadString(RuntimeDiagnosticAction command, string key)
	{
		if (command.Args != null
			&& command.Args.TryGetValue(key, out var value)
			&& value.ValueKind == JsonValueKind.String)
		{
			return value.GetString();
		}

		return null;
	}

	private static int? ReadInt(RuntimeDiagnosticAction command, string key)
	{
		if (command.Args != null
			&& command.Args.TryGetValue(key, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetInt32(out var number))
		{
			return number;
		}

		return null;
	}
}
