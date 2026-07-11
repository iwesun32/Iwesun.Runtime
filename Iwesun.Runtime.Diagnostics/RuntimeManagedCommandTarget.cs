using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeManagedCommandTarget : RuntimeDiagnosticTargetBase
{
    private readonly RuntimeManagedRegistry _registry;
    private readonly RuntimeDiagnosticHub _hub;

    public RuntimeManagedCommandTarget(RuntimeManagedRegistry registry, RuntimeDiagnosticHub hub) : base("runtime.managed")
    {
        _registry = registry;
        _hub = hub;
    }

    public override object Snapshot()
    {
        _registry.ImportSharedStates();
        return _registry.Snapshot();
    }

    public override Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct)
    {
        _registry.ImportSharedStates();
        switch (command.Action.ToLowerInvariant())
        {
            case "snapshot":
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, _registry.Snapshot(ReadInt(command, "count") ?? 100)));
            case "registrations":
            case "list":
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, _registry.SnapshotRegistrations()));
            case "processes":
            case "listprocesses":
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, SnapshotByUnitType("process", ReadInt(command, "count") ?? 100)));
            case "threads":
            case "listthreads":
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, SnapshotByUnitType("thread", ReadInt(command, "count") ?? 100)));
            case "tasks":
            case "listtasks":
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, SnapshotByUnitType("task", ReadInt(command, "count") ?? 100)));
            case "globalstate":
            case "globalstateget":
            case "global.state.get":
            case "lifecycle.get":
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new
                {
                    state = _registry.GlobalLifecycleState,
                    history = _registry.SnapshotGlobalLifecycleHistory(ReadInt(command, "count") ?? 32),
                    exitDeadlineUtc = _registry.GlobalExitDeadlineUtc
                }));
            case "globalstateset":
            case "global.state.set":
            case "lifecycle.set":
                {
                    var name = ReadString(command, "name") ?? ReadString(command, "state");
                    if (string.IsNullOrWhiteSpace(name))
                        return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "name is required."));
                    var countdownMs = ReadInt(command, "countdownMs");
                    DateTimeOffset? deadline = null;
                    if (countdownMs.HasValue && countdownMs.Value > 0)
                    {
                        deadline = DateTimeOffset.UtcNow.AddMilliseconds(countdownMs.Value);
                    }

                    _registry.EnqueueControllerCommand(RuntimeManagedCommandKind.Initialize, $"state={name};deadline={deadline:O}");
                    var snapshot = _registry.SetGlobalLifecycleState(name, deadline);
                    return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, snapshot));
                }
            case "shutdownstatus":
            case "shutdown.status":
            case "lifecycle.status":
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, _registry.EvaluateShutdownStatus()));
            case "reflectionrefresh":
            case "registryrefresh":
            case "reflection.refresh":
                {
                    var host = _hub.RescanHost();
                    return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new
                    {
                        host,
                        targetIds = _hub.TargetIds
                    }));
                }
            case "reflectionlist":
            case "reflection.list":
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, _hub.TargetIds));
            case "globalcommandbroadcast":
            case "lifecycle.broadcast":
                {
                    if (!TryReadCommandKind(command, out var kind))
                        return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "kind is required and must be a valid RuntimeManagedCommandKind."));
                    var payload = ReadString(command, "payload");
                    _registry.EnqueueControllerCommand(kind, payload);
                    var count = _registry.BroadcastCommand(kind, payload);
                    return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { count, kind = kind.ToString(), payload }));
                }
            case "shutdownrequest":
            case "shutdown.request":
            case "lifecycle.shutdown":
                {
                    var countdownMs = ReadInt(command, "countdownMs") ?? 5000;
                    var payload = ReadString(command, "payload");
                    var status = _registry.RequestShutdown(countdownMs, payload);
                    return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new
                    {
                        status
                    }));
                }
            case "unitstateget":
            case "unitstate":
                {
                    var unitId = ReadString(command, "unitId") ?? ReadString(command, "targetUnitId");
                    if (string.IsNullOrWhiteSpace(unitId))
                        return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "unitId is required."));
                    if (!_registry.TryGetUnitState(unitId, out var snapshot) || snapshot == null)
                        return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { found = false }));
                    return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { found = true, state = snapshot }));
                }
            case "unitstatehistory":
                {
                    var unitId = ReadString(command, "unitId") ?? ReadString(command, "targetUnitId");
                    if (string.IsNullOrWhiteSpace(unitId))
                        return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "unitId is required."));
                    var history = _registry.SnapshotUnitStateHistory(unitId, ReadInt(command, "count") ?? 32);
                    return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, history));
                }
            case "unitstatetransition":
            case "unit.state.transition":
                return ExecuteUnitStateMutationAsync(command, mode: "transition", strict: true);
            case "unitstatetrytransition":
            case "unit.state.trytransition":
                return ExecuteUnitStateMutationAsync(command, mode: "transition", strict: false);
            case "unitstatesubtaskappend":
            case "unit.state.subtask.append":
                return ExecuteUnitStateMutationAsync(command, mode: "subtask", strict: true);
            case "unitstatesubtasktryappend":
            case "unit.state.subtask.tryappend":
                return ExecuteUnitStateMutationAsync(command, mode: "subtask", strict: false);
            case "processreflectget":
            case "process.mem.get":
                return ExecuteProcessReflectionAsync(command, "get");
            case "processreflectset":
            case "process.mem.set":
                return ExecuteProcessReflectionAsync(command, "set");
            case "processreflectnav":
            case "process.mem.nav":
                return ExecuteProcessReflectionAsync(command, "navigate");
            case "events":
            case "drain":
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, _registry.DrainEvents(ReadInt(command, "count") ?? 100)));
            case "enqueue":
                {
                    var targetUnitId = ReadString(command, "targetUnitId") ?? ReadString(command, "unitId");
                    if (string.IsNullOrWhiteSpace(targetUnitId))
                        return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "targetUnitId is required."));

                    if (!TryReadCommandKind(command, out var kind))
                        return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "kind is required and must be a valid RuntimeManagedCommandKind."));

                    var payload = ReadString(command, "payload");
                    var queued = _registry.EnqueueCommand(targetUnitId, kind, payload);
                    return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, queued));
                }
            case "dequeue":
                {
                    var targetUnitId = ReadString(command, "targetUnitId") ?? ReadString(command, "unitId");
                    if (string.IsNullOrWhiteSpace(targetUnitId))
                        return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "targetUnitId is required."));

                    if (_registry.TryDequeueCommand(targetUnitId, out var dequeued))
                        return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { found = true, command = dequeued }));

                    return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { found = false }));
                }
            default:
                return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Unsupported action: {command.Action}"));
        }
    }

    private static bool TryReadCommandKind(RuntimeDiagnosticAction command, out RuntimeManagedCommandKind kind)
    {
        kind = default;
        if (command.Args == null || !command.Args.TryGetValue("kind", out var value))
            return false;

        if (value.ValueKind == JsonValueKind.String)
            return Enum.TryParse(value.GetString(), true, out kind);

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var kindValue))
        {
            if (Enum.IsDefined(typeof(RuntimeManagedCommandKind), kindValue))
            {
                kind = (RuntimeManagedCommandKind)kindValue;
                return true;
            }
        }

        return false;
    }

    private static string? ReadString(RuntimeDiagnosticAction command, string key)
    {
        if (command.Args != null
            && command.Args.TryGetValue(key, out var value)
            && value.ValueKind == JsonValueKind.String)
            return value.GetString();
        return null;
    }

    private static int? ReadInt(RuntimeDiagnosticAction command, string key)
    {
        if (command.Args != null
            && command.Args.TryGetValue(key, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number))
            return number;
        return null;
    }

    private IReadOnlyList<RuntimeManagedRegistration> SnapshotByUnitType(string unitType, int count)
    {
        return _registry.SnapshotRegistrations()
            .Where(x => x.UnitType.Equals(unitType, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(count, 1, 4096))
            .ToArray();
    }

    private Task<RuntimeDiagnosticActionResult> ExecuteProcessReflectionAsync(RuntimeDiagnosticAction command, string reflectionAction)
    {
        var unitId = ReadString(command, "unitId") ?? ReadString(command, "targetUnitId");
        if (string.IsNullOrWhiteSpace(unitId))
        {
            return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "unitId is required."));
        }

        var member = ReadString(command, "member");
        if (string.IsNullOrWhiteSpace(member))
        {
            return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "member is required."));
        }

        var reflectionCommand = new RuntimeDiagnosticAction
        {
            TargetId = $"runtime.process.{unitId}",
            Action = reflectionAction,
            Member = member,
            Value = command.Value,
            Args = command.Args
        };

        return _hub.ExecuteAsync(reflectionCommand);
    }

    private Task<RuntimeDiagnosticActionResult> ExecuteUnitStateMutationAsync(RuntimeDiagnosticAction command, string mode, bool strict)
    {
        var unitId = ReadString(command, "unitId") ?? ReadString(command, "targetUnitId");
        var stateName = ReadString(command, "stateName") ?? ReadString(command, "name");
        if (string.IsNullOrWhiteSpace(unitId))
        {
            return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "unitId is required."));
        }

        if (string.IsNullOrWhiteSpace(stateName))
        {
            return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "stateName is required."));
        }

        var registration = _registry.SnapshotRegistrations().FirstOrDefault(x => x.UnitId.Equals(unitId, StringComparison.OrdinalIgnoreCase));
        if (registration == null)
        {
            return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"unit not found: {unitId}"));
        }

        if (mode.Equals("subtask", StringComparison.OrdinalIgnoreCase))
        {
            var stateCatalog = registration.State.State.CurrentState.Catalog ?? RuntimeStateCatalog.CreateOnlineDefaults();
            if (strict)
            {
                var next = registration.State with
                {
                    SubTaskStates = (registration.State.SubTaskStates ?? Array.Empty<RuntimeState>()).Concat([stateCatalog.RequireByName(stateName)]).ToArray()
                };
                _registry.UpdateState(unitId, next);
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, next));
            }

            if (!stateCatalog.TryGetByName(stateName, out var state))
            {
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { changed = false }));
            }

            var nextTry = registration.State with
            {
                SubTaskStates = (registration.State.SubTaskStates ?? Array.Empty<RuntimeState>()).Concat([state]).ToArray()
            };
            _registry.UpdateState(unitId, nextTry);
            return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { changed = true, snapshot = nextTry }));
        }

        if (mode.Equals("transition", StringComparison.OrdinalIgnoreCase))
        {
            var stateSnapshot = registration.State.State;
            var stateCatalog = stateSnapshot.CurrentState.Catalog ?? RuntimeStateCatalog.CreateOnlineDefaults();
            if (strict)
            {
                var target = stateCatalog.RequireByName(stateName);
                var transitioned = registration.State with
                {
                    State = stateSnapshot with
                    {
                        CurrentState = target,
                        CurrentPath = target.Name,
                        UpdatedAt = DateTimeOffset.UtcNow
                    }
                };
                _registry.UpdateState(unitId, transitioned);
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, transitioned));
            }

            if (!stateCatalog.TryGetByName(stateName, out var tryTarget))
            {
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { changed = false }));
            }

            var tryTransitioned = registration.State with
            {
                State = stateSnapshot with
                {
                    CurrentState = tryTarget,
                    CurrentPath = tryTarget.Name,
                    UpdatedAt = DateTimeOffset.UtcNow
                }
            };
            _registry.UpdateState(unitId, tryTransitioned);
            return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new { changed = true, snapshot = tryTransitioned }));
        }

        return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"unsupported mutation mode: {mode}"));
    }
}
