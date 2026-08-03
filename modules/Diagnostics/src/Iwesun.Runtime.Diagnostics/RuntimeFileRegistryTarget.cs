namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeFileRegistryTarget : RuntimeDiagnosticTargetBase
{
    public RuntimeFileRegistryTarget() : base("diagnostics.files")
    {
    }

    public override object Snapshot() => RuntimeFileRegistry.Snapshot(includeInactive: true);

    public override Task<RuntimeDiagnosticActionResult> ExecuteAsync(RuntimeDiagnosticAction command, CancellationToken ct)
    {
        var action = command.Action.ToLowerInvariant();
        switch (action)
        {
            case "snapshot":
            case "list":
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(
                    TargetId,
                    command.Action,
                    RuntimePagedResult<RuntimeFileLeaseSnapshot>.Create(
                        RuntimeFileRegistry.Snapshot(ReadBool(command, "includeInactive") ?? true),
                        ReadInt(command, "offset") ?? 0,
                        ReadInt(command, "limit") ?? 100)));

            case "register":
                var regName = ReadString(command, "name") ?? ReadString(command, "id");
                var regTemplate = ReadString(command, "templatePath") ?? ReadString(command, "template") ?? ReadString(command, "path");
                var regResolved = ReadString(command, "resolvedPath") ?? ReadString(command, "resolved") ?? regTemplate;
                if (string.IsNullOrWhiteSpace(regName) || string.IsNullOrWhiteSpace(regTemplate))
                    return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "name and templatePath/path are required."));
                var regModeStr = ReadString(command, "writeMode") ?? ReadString(command, "mode") ?? "Append";
                if (!Enum.TryParse<FileWriteMode>(regModeStr, ignoreCase: true, out var regMode))
                    regMode = FileWriteMode.Append;
                var regSnapshot = RuntimeFileRegistry.Register(regName, regTemplate, regResolved!, regMode);
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, regSnapshot));

            case "release":
            case "unregister":
                var relToken = ReadString(command, "name") ?? ReadString(command, "id");
                if (string.IsNullOrWhiteSpace(relToken))
                    return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "name/id is required."));
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new
                {
                    released = RuntimeFileRegistry.Release(relToken),
                    registration = RuntimeFileRegistry.GetRegistration(relToken)
                }));

            case "resolve":
                var resToken = ReadString(command, "name") ?? ReadString(command, "id");
                if (string.IsNullOrWhiteSpace(resToken))
                    return Task.FromResult(RuntimeDiagnosticActionResult.Fail(TargetId, command.Action, "name/id is required."));
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(TargetId, command.Action, new
                {
                    resolvedPath = RuntimeFileRegistry.GetResolvedPath(resToken),
                    registration = RuntimeFileRegistry.GetRegistration(resToken)
                }));

            case "purge":
                return Task.FromResult(RuntimeDiagnosticActionResult.Ok(
                    TargetId,
                    command.Action,
                    new { purged = RuntimeFileRegistry.PurgeInactive() }));

            default:
                return Task.FromResult(RuntimeDiagnosticActionResult.Fail(
                    TargetId,
                    command.Action,
                    $"Unknown action '{command.Action}'. Supported: snapshot, list, register, release, unregister, resolve, purge."));
        }
    }

    private static int? ReadInt(RuntimeDiagnosticAction command, string key)
    {
        if (command.Args != null && command.Args.TryGetValue(key, out var value) &&
            value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt32(out var number))
            return number;
        return null;
    }

    private static string? ReadString(RuntimeDiagnosticAction command, string key)
    {
        if (command.Args != null
            && command.Args.TryGetValue(key, out var value)
            && value.ValueKind == System.Text.Json.JsonValueKind.String)
            return value.GetString();
        return null;
    }

    private static bool? ReadBool(RuntimeDiagnosticAction command, string key)
    {
        if (command.Args != null
            && command.Args.TryGetValue(key, out var value)
            && (value.ValueKind == System.Text.Json.JsonValueKind.True || value.ValueKind == System.Text.Json.JsonValueKind.False))
            return value.GetBoolean();
        return null;
    }
}
