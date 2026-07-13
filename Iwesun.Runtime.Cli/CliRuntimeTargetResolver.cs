namespace Iwesun.Runtime.Cli;

internal sealed record ResolvedRuntimeTarget(
    string? TargetAlias,
    string NodeAlias,
    string ServerName,
    string EndpointName,
    string PipeName,
    int ConnectTimeoutMs,
    int RequestTimeoutMs,
    int MaxResponseBytes);

internal static class CliRuntimeTargetResolver
{
    public static ResolvedRuntimeTarget Resolve(
        CliConfiguration configuration,
        CliEndpoint endpoint,
        string endpointName,
        string? targetAlias,
        string? serverOverride,
        string? pipeOverride,
        int? requestTimeoutOverride)
    {
        if (!string.IsNullOrWhiteSpace(targetAlias) &&
            (!string.IsNullOrWhiteSpace(serverOverride) || !string.IsNullOrWhiteSpace(pipeOverride)))
            throw new CliException("CLI_TARGET_OVERRIDE_CONFLICT", "--target cannot be combined with --server or --pipe.", 2);
        if (!string.IsNullOrWhiteSpace(serverOverride) && string.IsNullOrWhiteSpace(pipeOverride))
            throw new CliException("CLI_REMOTE_OVERRIDE_INCOMPLETE", "--server requires --pipe.", 2);

        CliTarget? target = null;
        if (!string.IsNullOrWhiteSpace(targetAlias))
            target = configuration.Targets.GetValueOrDefault(targetAlias)
                ?? throw new CliException("CLI_TARGET_NOT_FOUND", $"Target '{targetAlias}' is not defined.", 2);

        var configuredNodeAlias = target?.Node ?? "local";
        var nodeAlias = target is null && !string.IsNullOrWhiteSpace(serverOverride) ? "temporary" : configuredNodeAlias;
        var node = configuration.Nodes.GetValueOrDefault(configuredNodeAlias)
            ?? throw new CliException("CLI_TARGET_NODE_NOT_FOUND", $"Target '{targetAlias}' references unknown node '{configuredNodeAlias}'.", 3);
        var pipeName = pipeOverride ?? target?.PipeName ?? endpoint.PipeName;
        ValidatePipeName(pipeName);

        return new ResolvedRuntimeTarget(
            targetAlias,
            nodeAlias,
            serverOverride ?? node.ServerName,
            target?.Endpoint ?? endpointName,
            pipeName,
            target?.ConnectTimeoutMs ?? node.ConnectTimeoutMs ?? endpoint.ConnectTimeoutMs,
            target?.RequestTimeoutMs ?? requestTimeoutOverride ?? endpoint.RequestTimeoutMs,
            target?.MaxResponseBytes ?? endpoint.MaxResponseBytes);
    }

    private static void ValidatePipeName(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
            throw new CliException("CLI_TARGET_PIPE", "Target pipe name is required.", 2);
        if (pipeName.StartsWith(@"\\", StringComparison.Ordinal) || pipeName.Contains('/') || pipeName.Contains('\\'))
            throw new CliException("CLI_TARGET_PIPE_FORMAT", "pipeName must be a plain named-pipe name, not a UNC path.", 2);
    }
}
