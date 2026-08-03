using System.Security.Principal;

namespace Iwesun.Runtime.Cli;

internal static class CliRemoteErrorClassifier
{
    public static CliException Classify(Exception exception, ResolvedRuntimeTarget target, string phase)
    {
        var windowsError = exception.HResult & 0xFFFF;
        var remote = target.ServerName != ".";
        var code = windowsError switch
        {
            5 => remote ? "CLI_REMOTE_ACCESS_DENIED" : "CLI_ACCESS_DENIED",
            1219 => "CLI_REMOTE_CREDENTIAL_CONFLICT",
            2 or 3 or 231 => remote ? "CLI_REMOTE_PIPE_NOT_FOUND" : "CLI_PIPE_NOT_FOUND",
            53 or 64 or 67 or 1231 => "CLI_REMOTE_NODE_UNREACHABLE",
            _ => remote ? "CLI_REMOTE_PROTOCOL_ERROR" : "CLI_TRANSPORT_FAILURE"
        };
        var retryable = windowsError is 2 or 3 or 53 or 64 or 67 or 231 or 1231;
        return new CliException(code, exception.Message, 4, data: new
        {
            transport = "namedPipe",
            targetAlias = target.TargetAlias,
            nodeAlias = target.NodeAlias,
            serverName = target.ServerName,
            endpoint = target.EndpointName,
            pipeName = target.PipeName,
            phase,
            windowsError,
            currentUser = OperatingSystem.IsWindows() ? WindowsIdentity.GetCurrent().Name : Environment.UserName,
            retryable
        });
    }
}
