using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeDiagnosticHub
{
	private readonly ConcurrentDictionary<string, IRuntimeDiagnosticTarget> _targets = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentQueue<RuntimeDiagnosticEvent> _events = new();
	private readonly RuntimeHostScanOptions _hostScanOptions;
	private RuntimeHostSnapshot _hostSnapshot;
	#if DEBUG
	private RuntimeDiagnosticBreakpoints? _breakpoints;
	#endif
	private RuntimeDiagnosticHooks? _hooks;
	private RegistrySnapshot? _registrySnapshot;

	public RuntimeDiagnosticHub(RuntimeHostScanOptions? hostScanOptions = null)
	{
		_hostScanOptions = hostScanOptions ?? new RuntimeHostScanOptions();
		_hostSnapshot = RuntimeHostScanner.Scan(TargetIds, _hostScanOptions);
	}

	#if DEBUG
	/// <summary>Wire the debug-only breakpoints singleton for direct breakpoint command handling.</summary>
	public void SetBreakpoints(RuntimeDiagnosticBreakpoints breakpoints)
	{
		_breakpoints = breakpoints ?? throw new ArgumentNullException(nameof(breakpoints));
	}
	#endif

	/// <summary>Wire the hooks singleton for direct hook command handling.</summary>
	public void SetHooks(RuntimeDiagnosticHooks hooks)
	{
		_hooks = hooks ?? throw new ArgumentNullException(nameof(hooks));
	}

	/// <summary>Store the registry snapshot for querying.</summary>
	public void SetRegistrySnapshot(RegistrySnapshot snapshot)
	{
		_registrySnapshot = snapshot;
	}

	public bool Register(IRuntimeDiagnosticTarget target)
	{
		var added = _targets.TryAdd(target.TargetId, target);
		if (added)
			RefreshHostRegisteredTargets();
		return added;
	}

	public bool RegisterObject(string targetId, object instance, RuntimeDiagnosticObjectAccess? access = null)
	{
		_hooks?.RegisterInstance(instance);
		return Register(new ReflectionRuntimeDiagnosticTarget(targetId, instance, access));
	}

	public bool Unregister(string targetId)
	{
		var removed = _targets.TryRemove(targetId, out _);
		if (removed)
			RefreshHostRegisteredTargets();
		return removed;
	}

	public IReadOnlyList<string> TargetIds =>
		_targets.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();

	public RuntimeHostSnapshot HostSnapshot => _hostSnapshot;
	public RegistrySnapshot RegistrySnapshot => _registrySnapshot ?? new RegistrySnapshot(
		Array.Empty<WatchPointEntry>(),
		Array.Empty<BreakpointEntry>(),
		Array.Empty<HookEntry>());

	public RuntimeHostSnapshot RescanHost()
	{
		_hostSnapshot = RuntimeHostScanner.Scan(TargetIds, _hostScanOptions);
		Publish(new RuntimeDiagnosticEvent
		{
			TargetId = "runtime.host",
			Kind = "host-scan",
			Message = "Host runtime snapshot refreshed.",
			Payload = new
			{
				assemblyCount = _hostSnapshot.Assemblies.Count,
				targetCount = _hostSnapshot.RegisteredTargets.Count
			}
		});
		return _hostSnapshot;
	}

	public object Snapshot(string? targetId = null)
	{
		if (!string.IsNullOrWhiteSpace(targetId))
		{
			if (!_targets.TryGetValue(targetId, out var target))
				return new { targetId, found = false };

			return new { targetId = target.TargetId, found = true, snapshot = target.Snapshot() };
		}

		return _targets.Values
			.OrderBy(t => t.TargetId, StringComparer.OrdinalIgnoreCase)
			.Select(t => new { targetId = t.TargetId, snapshot = t.Snapshot() })
			.ToArray();
	}

	public async Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct = default)
	{
		#if DEBUG
		// ── Debug-only breakpoint actions ─────────────────────────────
		if (command.TargetId.Equals("diagnostics.breakpoints", StringComparison.OrdinalIgnoreCase))
		{
			return await ExecuteBreakpointActionAsync(command);
		}
		#endif

		// ── Hook actions ──────────────────────────────────────────────
		if (command.TargetId.Equals("diagnostics.hooks", StringComparison.OrdinalIgnoreCase))
		{
			return ExecuteHookAction(command);
		}

		// ── Registry actions ──────────────────────────────────────────
		if (command.TargetId.Equals("diagnostics.registry", StringComparison.OrdinalIgnoreCase))
		{
			return ExecuteRegistryAction(command);
		}

		if (string.IsNullOrWhiteSpace(command.TargetId))
		{
			if (command.Action.Equals("list", StringComparison.OrdinalIgnoreCase))
				return RuntimeDiagnosticActionResult.Ok("", command.Action, TargetIds);
			if (command.Action.Equals("host", StringComparison.OrdinalIgnoreCase))
				return RuntimeDiagnosticActionResult.Ok("", command.Action, HostSnapshot);
			if (command.Action.Equals("rescanHost", StringComparison.OrdinalIgnoreCase))
				return RuntimeDiagnosticActionResult.Ok("", command.Action, RescanHost());
			if (command.Action.Equals("events", StringComparison.OrdinalIgnoreCase))
				return RuntimeDiagnosticActionResult.Ok("", command.Action, DrainEvents(ReadCount(command), ReadEventFilter(command)));

			return RuntimeDiagnosticActionResult.Fail("", command.Action, "TargetId is required.");
		}

		if (!_targets.TryGetValue(command.TargetId, out var target))
			return RuntimeDiagnosticActionResult.Fail(command.TargetId, command.Action, $"Target not found: {command.TargetId}");

		var result = await target.ExecuteAsync(command, ct);
		Publish(new RuntimeDiagnosticEvent
		{
			TargetId = command.TargetId,
			Kind = "command",
			Message = command.Action,
			Payload = new { result.Success, result.Error }
		});
		return result;
	}

	public async Task<RuntimeDiagnosticFrame> ExecuteFrameAsync(RuntimeDiagnosticFrame request, CancellationToken ct = default)
	{
		if (request.Command == null)
			return BuildFrameResponse(request, RuntimeDiagnosticActionResult.Fail("", request.Header.Operation, "command is required."));

		var action = !string.IsNullOrWhiteSpace(request.Command.Action)
			? request.Command.Action
			: request.Header.Operation;

		var command = new RuntimeDiagnosticAction
		{
			TargetId = request.Command.Target,
			Action = action,
			Member = request.Command.Member,
			Args = request.Command.Args
		};

		if (command.Args != null && command.Args.TryGetValue("value", out var valueArg))
		{
			command = new RuntimeDiagnosticAction
			{
				TargetId = command.TargetId,
				Action = command.Action,
				Member = command.Member,
				Value = valueArg.Clone(),
				Args = command.Args.Where(x => !x.Key.Equals("value", StringComparison.OrdinalIgnoreCase))
					.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)
			};
		}

		var result = await ExecuteAsync(command, ct);
		return BuildFrameResponse(request, result);
	}

	public void Publish(RuntimeDiagnosticEvent diagnosticEvent)
	{
		_events.Enqueue(diagnosticEvent);
		while (_events.Count > 2048 && _events.TryDequeue(out _)) { }
	}

	public IReadOnlyList<RuntimeDiagnosticEvent> DrainEvents(int count = 100, RuntimeDiagnosticEventFilter? filter = null)
	{
		count = Math.Clamp(count, 1, 2048);
		var items = new List<RuntimeDiagnosticEvent>(count);
		var scanned = 0;
		while (items.Count < count && scanned < 2048 && _events.TryDequeue(out var item))
		{
			scanned++;
			if (filter == null || filter.Matches(item))
				items.Add(item);
		}
		return items;
	}

	public IReadOnlyList<RuntimeDiagnosticEvent> SnapshotEvents(int count = 100, RuntimeDiagnosticEventFilter? filter = null)
	{
		count = Math.Clamp(count, 1, 2048);
		var items = _events.ToArray();
		IEnumerable<RuntimeDiagnosticEvent> query = items;
		if (filter != null)
		{
			query = query.Where(filter.Matches);
		}

		var materialized = query.ToArray();
		if (materialized.Length <= count)
		{
			return materialized;
		}

		return materialized[^count..];
	}

	public string ExecuteJson(string commandJson, JsonSerializerOptions? options = null, CancellationToken ct = default)
	{
		options ??= new JsonSerializerOptions(JsonSerializerDefaults.Web);
		var command = JsonSerializer.Deserialize<RuntimeDiagnosticAction>(commandJson, options)
			?? new RuntimeDiagnosticAction();
		var result = ExecuteAsync(command, ct).GetAwaiter().GetResult();
		return JsonSerializer.Serialize(result, options);
	}

	private static RuntimeDiagnosticFrame BuildFrameResponse(RuntimeDiagnosticFrame request, RuntimeDiagnosticActionResult result)
	{
		JsonElement? data = null;
		if (result.Value is not null)
			data = JsonSerializer.SerializeToElement(result.Value);

		return new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V2Schema,
				FrameType = "response",
				Category = request.Header.Category,
				Operation = request.Header.Operation,
				RequestId = request.Header.RequestId,
				CorrelationId = request.Header.RequestId ?? request.Header.CorrelationId,
				Timestamp = DateTimeOffset.UtcNow,
				Source = "runtime",
				Destination = request.Header.Source
			},
			Status = new RuntimeDiagnosticFrameStatus
			{
				Ok = result.Success,
				Code = result.Success ? "OK" : "ERROR",
				Message = result.Success ? "success" : (result.Error ?? "failed"),
				Retryable = false
			},
			ExtStatus = result.Success
				? null
				: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
				{
					["module"] = JsonSerializer.SerializeToElement("diagnostics"),
					["target"] = JsonSerializer.SerializeToElement(result.TargetId),
					["action"] = JsonSerializer.SerializeToElement(result.Action)
				},
			Data = data
		};
	}

	private void RefreshHostRegisteredTargets()
	{
		_hostSnapshot = new RuntimeHostSnapshot
		{
			ScannedAt = _hostSnapshot.ScannedAt,
			ProcessName = _hostSnapshot.ProcessName,
			ProcessId = _hostSnapshot.ProcessId,
			RuntimeVersion = _hostSnapshot.RuntimeVersion,
			BaseDirectory = _hostSnapshot.BaseDirectory,
			Assemblies = _hostSnapshot.Assemblies,
			RegisteredTargets = TargetIds
		};
	}

	private static int ReadCount(RuntimeDiagnosticAction command)
	{
		if (command.Args != null
			&& command.Args.TryGetValue("count", out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetInt32(out var count))
		{
			return count;
		}

		return 100;
	}

	private static RuntimeDiagnosticEventFilter? ReadEventFilter(RuntimeDiagnosticAction command)
	{
		if (command.Args == null)
			return null;

		return new RuntimeDiagnosticEventFilter(
			ReadString(command, "targetId"),
			ReadString(command, "kind"),
			ReadString(command, "section"),
			ReadString(command, "outputPointId") ?? ReadString(command, "point"),
			ReadString(command, "messageContains") ?? ReadString(command, "text"),
			ReadStringList(command, "excludeKinds"));
	}

	private static string? ReadString(RuntimeDiagnosticAction command, string key)
	{
		if (command.Args != null
			&& command.Args.TryGetValue(key, out var value)
			&& value.ValueKind == JsonValueKind.String)
			return value.GetString();
		return null;
	}

	private static double? ReadDouble(RuntimeDiagnosticAction command, string key)
	{
		if (command.Args != null
			&& command.Args.TryGetValue(key, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetDouble(out var number))
			return number;
		return null;
	}

	private static IReadOnlySet<string> ReadStringList(RuntimeDiagnosticAction command, string key)
	{
		if (command.Args == null || !command.Args.TryGetValue(key, out var value))
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		if (value.ValueKind == JsonValueKind.String)
		{
			var text = value.GetString();
			return string.IsNullOrWhiteSpace(text)
				? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
				: text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
		}

		if (value.ValueKind == JsonValueKind.Array)
		{
			return value.EnumerateArray()
				.Where(x => x.ValueKind == JsonValueKind.String)
				.Select(x => x.GetString())
				.Where(x => !string.IsNullOrWhiteSpace(x))
				.Select(x => x!)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
		}

		return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
	}

	#if DEBUG
	// ── Debug-only breakpoint command handling ───────────────────────
	private async Task<RuntimeDiagnosticActionResult> ExecuteBreakpointActionAsync(RuntimeDiagnosticAction command)
	{
		if (_breakpoints == null)
			return RuntimeDiagnosticActionResult.Fail("diagnostics.breakpoints", command.Action, "Breakpoints not initialized.");

		var action = command.Action.ToLowerInvariant();

		switch (action)
		{
			case "list":
			case "snapshot":
				return RuntimeDiagnosticActionResult.Ok("diagnostics.breakpoints", action, _breakpoints.Snapshot());

			case "enable":
				var enableId = ReadString(command, "id") ?? ReadString(command, "breakpointId");
				if (string.IsNullOrWhiteSpace(enableId))
					return RuntimeDiagnosticActionResult.Fail("diagnostics.breakpoints", action, "id or breakpointId is required.");
				return RuntimeDiagnosticActionResult.Ok("diagnostics.breakpoints", action,
					_breakpoints.Enable(enableId));

			case "disable":
				var disableId = ReadString(command, "id") ?? ReadString(command, "breakpointId");
				if (string.IsNullOrWhiteSpace(disableId))
					return RuntimeDiagnosticActionResult.Fail("diagnostics.breakpoints", action, "id or breakpointId is required.");
				return RuntimeDiagnosticActionResult.Ok("diagnostics.breakpoints", action,
					_breakpoints.Disable(disableId));

			case "resume":
				var resumeId = ReadString(command, "id") ?? ReadString(command, "breakpointId");
				if (string.IsNullOrWhiteSpace(resumeId))
					return RuntimeDiagnosticActionResult.Fail("diagnostics.breakpoints", action, "id or breakpointId is required.");
				return RuntimeDiagnosticActionResult.Ok("diagnostics.breakpoints", action,
					_breakpoints.Resume(resumeId));

			case "resumeall":
				_breakpoints.ResumeAll();
				return RuntimeDiagnosticActionResult.Ok("diagnostics.breakpoints", action, _breakpoints.Snapshot());

			case "setnumeric":
			case "bindnumeric":
				var bindId = ReadString(command, "id") ?? ReadString(command, "breakpointId");
				var predicateId = ReadString(command, "predicateId") ?? ReadString(command, "predicate");
				if (string.IsNullOrWhiteSpace(bindId) || string.IsNullOrWhiteSpace(predicateId))
					return RuntimeDiagnosticActionResult.Fail("diagnostics.breakpoints", action, "id/breakpointId and predicateId are required.");
				return RuntimeDiagnosticActionResult.Ok("diagnostics.breakpoints", action, new
				{
					updated = _breakpoints.SetNumericBinding(bindId, predicateId),
					numericBindings = _breakpoints.SnapshotNumericBindings()
				});

			case "setnumericthreshold":
			case "bindnumericthreshold":
				var thresholdId = ReadString(command, "id") ?? ReadString(command, "breakpointId");
				var op = ReadString(command, "operator") ?? ReadString(command, "predicateId") ?? ReadString(command, "predicate");
				if (string.IsNullOrWhiteSpace(thresholdId) || string.IsNullOrWhiteSpace(op))
					return RuntimeDiagnosticActionResult.Fail("diagnostics.breakpoints", action, "id/breakpointId and operator are required.");
				var threshold1 = ReadDouble(command, "threshold1") ?? ReadDouble(command, "threshold") ?? 0;
				var threshold2 = ReadDouble(command, "threshold2") ?? 0;
				return RuntimeDiagnosticActionResult.Ok("diagnostics.breakpoints", action, new
				{
					updated = _breakpoints.SetNumericThresholdBinding(thresholdId, op, threshold1, threshold2),
					numericBindings = _breakpoints.SnapshotNumericBindings()
				});

			case "clearnumeric":
			case "unbindnumeric":
				var unbindId = ReadString(command, "id") ?? ReadString(command, "breakpointId");
				if (string.IsNullOrWhiteSpace(unbindId))
					return RuntimeDiagnosticActionResult.Fail("diagnostics.breakpoints", action, "id/breakpointId is required.");
				return RuntimeDiagnosticActionResult.Ok("diagnostics.breakpoints", action, new
				{
					removed = _breakpoints.RemoveNumericBinding(unbindId),
					numericBindings = _breakpoints.SnapshotNumericBindings()
				});

			case "listnumeric":
			case "numeric":
				return RuntimeDiagnosticActionResult.Ok("diagnostics.breakpoints", action, new
				{
					bindings = _breakpoints.SnapshotNumericBindings(),
					supportedPredicates = RuntimeNumericPredicateCatalog.SupportedPredicateIds,
					supportedThresholdOperators = RuntimeNumericThresholdOperators.SupportedOperators
				});

			default:
				return RuntimeDiagnosticActionResult.Fail("diagnostics.breakpoints", action,
					$"Unknown action: {action}. Supported: list, enable, disable, resume, resumeAll, setNumeric, setNumericThreshold, clearNumeric, listNumeric");
		}
	}
	#endif

	// ── Hook command handling ────────────────────────────────────────

	private RuntimeDiagnosticActionResult ExecuteHookAction(RuntimeDiagnosticAction command)
	{
		if (_hooks == null)
			return RuntimeDiagnosticActionResult.Fail("diagnostics.hooks", command.Action, "Hooks not initialized.");

		var action = command.Action.ToLowerInvariant();

		switch (action)
		{
			case "list":
			case "available":
				return RuntimeDiagnosticActionResult.Ok("diagnostics.hooks", action, new
				{
					available = _hooks.AvailableHooks().Select(hook => new
					{
						hook.Id,
						hook.EventName,
						TargetTypeName = hook.TargetType.FullName ?? hook.TargetType.Name
					}),
					active = _hooks.ActiveHooks()
				});

			case "attach":
				var attachId = ReadString(command, "id") ?? ReadString(command, "hookId");
				if (string.IsNullOrWhiteSpace(attachId))
					return RuntimeDiagnosticActionResult.Fail("diagnostics.hooks", action, "id or hookId is required.");
				var attached = _hooks.Attach(attachId);
				return RuntimeDiagnosticActionResult.Ok("diagnostics.hooks", action, attached);

			case "detach":
				var detachId = ReadString(command, "id") ?? ReadString(command, "hookId");
				if (string.IsNullOrWhiteSpace(detachId))
					return RuntimeDiagnosticActionResult.Fail("diagnostics.hooks", action, "id or hookId is required.");
				var detached = _hooks.Detach(detachId);
				return RuntimeDiagnosticActionResult.Ok("diagnostics.hooks", action, detached);

			default:
				return RuntimeDiagnosticActionResult.Fail("diagnostics.hooks", action,
					$"Unknown action: {action}. Supported: list, attach, detach");
		}
	}

	// ── Registry command handling ────────────────────────────────────

	private RuntimeDiagnosticActionResult ExecuteRegistryAction(RuntimeDiagnosticAction command)
	{
		var action = command.Action.ToLowerInvariant();
		var kind = ReadString(command, "kind") ?? "all";

		var snapshot = _registrySnapshot ?? new RegistrySnapshot(
			Array.Empty<WatchPointEntry>(),
			Array.Empty<BreakpointEntry>(),
			Array.Empty<HookEntry>());

		object result = kind.ToLowerInvariant() switch
		{
			"watchpoints" => snapshot.WatchPoints,
			"breakpoints" => snapshot.Breakpoints,
			"hooks" => snapshot.Hooks,
			"all" => snapshot,
			_ => new { error = $"Unknown registry kind: {kind}. Supported: all, watchpoints, breakpoints, hooks" }
		};

		return RuntimeDiagnosticActionResult.Ok("diagnostics.registry", action, result);
	}
}

public sealed record RuntimeDiagnosticEventFilter(
	string? TargetId = null,
	string? Kind = null,
	string? Section = null,
	string? OutputPointId = null,
	string? MessageContains = null,
	IReadOnlySet<string>? ExcludeKinds = null)
{
	public bool Matches(RuntimeDiagnosticEvent item)
	{
		if (!string.IsNullOrWhiteSpace(TargetId)
			&& !item.TargetId.Equals(TargetId, StringComparison.OrdinalIgnoreCase))
			return false;

		if (!string.IsNullOrWhiteSpace(Kind)
			&& !item.Kind.Equals(Kind, StringComparison.OrdinalIgnoreCase))
			return false;

		if (ExcludeKinds != null && ExcludeKinds.Contains(item.Kind))
			return false;

		if (!string.IsNullOrWhiteSpace(MessageContains)
			&& !item.Message.Contains(MessageContains, StringComparison.OrdinalIgnoreCase))
			return false;

		if (!string.IsNullOrWhiteSpace(Section) || !string.IsNullOrWhiteSpace(OutputPointId))
		{
			var payload = ToPayloadElement(item.Payload);
			if (!string.IsNullOrWhiteSpace(Section)
				&& !PayloadStringEquals(payload, "section", Section))
				return false;
			if (!string.IsNullOrWhiteSpace(OutputPointId)
				&& !PayloadStringEquals(payload, "outputPointId", OutputPointId))
				return false;
		}

		return true;
	}

	private static JsonElement? ToPayloadElement(object? payload)
	{
		if (payload == null)
			return null;
		if (payload is JsonElement element)
			return element;
		return JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
	}

	private static bool PayloadStringEquals(JsonElement? payload, string propertyName, string expected)
	{
		if (payload == null || payload.Value.ValueKind != JsonValueKind.Object)
			return false;
		if (!payload.Value.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
			return false;
		return value.GetString()?.Equals(expected, StringComparison.OrdinalIgnoreCase) == true;
	}
}
