namespace Iwesun.Runtime.Cli;

internal static class RuntimeVirtualPathRouter
{
    private static readonly string[] Root = ["host", "lifecycle", "registry", "execution", "switchboard", "pipes", "files", "reflection"];

    public static string Resolve(string current, string value)
    {
        var parts = (value.StartsWith('/') ? value : current.TrimEnd('/') + "/" + value).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>();
        foreach (var part in parts)
        {
            if (part == ".") continue;
            if (part == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
            stack.Add(part);
        }
        var result = "/" + string.Join('/', stack);
        Validate(result);
        return result;
    }

    public static IReadOnlyList<string> List(string path) => path switch
    {
        "/" => Root,
        "/execution" => ["processes", "threads", "tasks"],
        _ when path.StartsWith("/reflection", StringComparison.OrdinalIgnoreCase) => ["Use get or cd with a registered target/member path."],
        _ => []
    };

    public static string[] MapGet(string path)
    {
        var fixedCommand = path.ToLowerInvariant() switch
        {
            "/host" => "host.info", "/lifecycle" => "lifecycle.status", "/registry" => "registry.list",
            "/execution/processes" => "process.list", "/execution/threads" => "thread.list", "/execution/tasks" => "task.list",
            "/switchboard" => "switchboard.get", "/pipes" => "pipe.list", "/files" => "file.list", "/reflection" => "reflection.list",
            _ => null
        };
        if (fixedCommand != null) return [fixedCommand];
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && parts[0].Equals("reflection", StringComparison.OrdinalIgnoreCase))
            return parts.Length == 2 ? ["reflection.get", parts[1]] : ["reflection.get", parts[1], string.Join('.', parts.Skip(2))];
        throw new CliException("CLI_CONTEXT_PATH_NOT_READABLE", $"Runtime path '{path}' cannot be read.", 2);
    }

    private static void Validate(string path)
    {
        if (path == "/") return;
        var first = path.Split('/', StringSplitOptions.RemoveEmptyEntries)[0];
        if (!Root.Contains(first, StringComparer.OrdinalIgnoreCase))
            throw new CliException("CLI_CONTEXT_PATH_NOT_FOUND", $"Runtime path '{path}' is not registered.", 2);
    }
}
