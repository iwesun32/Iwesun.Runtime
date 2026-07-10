using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeManagedCommandTarget : RuntimeDiagnosticTargetBase
{
    private readonly RuntimeManagedRegistry _registry;

    public RuntimeManagedCommandTarget(RuntimeManagedRegistry registry) : base("runtime.managed")
    {
        _registry = registry;
    }

    public override object Snapshot() => _registry.Snapshot();

    public override Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct)
    {
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
}
