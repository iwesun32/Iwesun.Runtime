namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Exposes the host scan snapshot through a stable runtime target.
/// </summary>
public sealed class RuntimeHostCommandTarget : RuntimeDiagnosticTargetBase
{
    private readonly RuntimeDiagnosticHub _hub;

    public RuntimeHostCommandTarget(RuntimeDiagnosticHub hub) : base("runtime.host")
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;
    }

    public override object Snapshot() => _hub.HostSnapshot;

    private RuntimeHostSummary Summary()
    {
        var host = _hub.HostSnapshot;
        return new RuntimeHostSummary(
            host.InstanceId,
            DateTimeOffset.UtcNow,
            host.ProcessName,
            host.ProcessId,
            host.ProcessStartTimeUtc,
            host.RuntimeVersion,
            DiagnosticSwitchboard.RuntimeDiagnosticsPipeName,
            host.Assemblies.Select(x => x.Name).ToArray(),
            host.Assemblies.Count,
            host.Assemblies.Sum(x => x.TypeCount),
            host.RegisteredTargets.Count);
    }

    public override Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct)
    {
        return command.Action.ToLowerInvariant() switch
        {
            "snapshot" or "info" => Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Snapshot())),
            "summary" => Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, Summary())),
            "rescan" or "refresh" => Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, _hub.RescanHost())),
            _ => Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, $"Unsupported action: {command.Action}"))
        };
    }
}
