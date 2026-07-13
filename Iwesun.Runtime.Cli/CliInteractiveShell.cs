using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;

namespace Iwesun.Runtime.Cli;

internal sealed class CliInteractiveShell
{
    private static readonly Regex VariablePattern = new(@"\$(?:\{(?<braced>[A-Za-z_][A-Za-z0-9_.-]*)\}|(?<plain>[A-Za-z_][A-Za-z0-9_.-]*))", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string[]> _commandMemory = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _history = [];
    private readonly Func<string[], CancellationToken, Task<int>> _execute;
    private readonly CliTargetContext _targets;
    private readonly HashSet<string> _destructiveCommands;
    private readonly bool _hasStartupPipe;
    private string _currentPath = "/";

    public CliInteractiveShell(
        Func<string[], CancellationToken, Task<int>> execute,
        IReadOnlyDictionary<string, CliNode>? nodes = null,
        IReadOnlyDictionary<string, CliTarget>? targets = null,
        IEnumerable<string>? destructiveCommands = null,
        bool hasStartupPipe = false)
    {
        _execute = execute;
        _targets = new CliTargetContext(nodes ?? new Dictionary<string, CliNode>(), targets ?? new Dictionary<string, CliTarget>());
        _destructiveCommands = new HashSet<string>(destructiveCommands ?? [], StringComparer.OrdinalIgnoreCase);
        _hasStartupPipe = hasStartupPipe;
    }

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!Console.IsInputRedirected)
                Console.Write($"iwrt:{_currentPath}> ");
            var line = await Console.In.ReadLineAsync(cancellationToken);
            if (line == null)
                return 0;
            line = line.Trim();
            if (line.Length == 0)
                continue;
            if (!line.StartsWith("node auth ", StringComparison.OrdinalIgnoreCase))
                _history.Add(line);

            string[] tokens;
            try { tokens = CliTokenizer.Tokenize(line); }
            catch (CliException ex) { CliApplication.RenderShellFailure(ex); continue; }
            if (tokens.Length == 0)
                continue;

            try
            {
                tokens = ExpandVariables(tokens);
                var local = tokens[0].ToLowerInvariant();
                if (local is "exit" or "quit")
                    return 0;
                if (local == "help" && tokens.Length > 1)
                {
                    await _execute([tokens[1], "--help"], cancellationToken);
                    continue;
                }
                if (local == "get")
                {
                    var path = tokens.Length == 1 ? _currentPath : RuntimeVirtualPathRouter.Resolve(_currentPath, tokens[1]);
                    await ExecuteRemoteAsync(RuntimeVirtualPathRouter.MapGet(path), _targets.CurrentName, cancellationToken);
                    continue;
                }
                if (local == "ls")
                {
                    var path = tokens.Length == 1 ? _currentPath : RuntimeVirtualPathRouter.Resolve(_currentPath, tokens[1]);
                    var children = RuntimeVirtualPathRouter.List(path);
                    if (children.Count == 0 || path.StartsWith("/reflection", StringComparison.OrdinalIgnoreCase))
                        await ExecuteRemoteAsync(RuntimeVirtualPathRouter.MapGet(path), _targets.CurrentName, cancellationToken);
                    else
                        foreach (var child in children) Console.WriteLine(child);
                    continue;
                }
                if (local == "target")
                {
                    await HandleTargetAsync(tokens, cancellationToken);
                    continue;
                }
                if (local == "node")
                {
                    await HandleNodeAsync(tokens, cancellationToken);
                    continue;
                }
                if (HandleLocal(tokens))
                    continue;
                string? targetAlias = null;
                if (tokens[0].StartsWith('@'))
                {
                    targetAlias = tokens[0][1..];
                    if (tokens.Length == 1)
                        throw new CliException("CLI_TARGET_COMMAND", "A command is required after @target.", 2);
                    tokens = tokens[1..];
                }
                else
                {
                    targetAlias = _targets.CurrentName;
                }
                if (targetAlias is null && !_hasStartupPipe && _targets.Targets.Count > 1 && _destructiveCommands.Contains(tokens[0]))
                    throw new CliException("CLI_TARGET_REQUIRED", $"Command '{tokens[0]}' requires an explicit target in a multi-target shell.", 2);
                if (tokens.Length == 1 && _commandMemory.TryGetValue(tokens[0], out var remembered))
                    tokens = [tokens[0], .. remembered];
                var rememberedCommand = tokens;
                var exitCode = await ExecuteRemoteAsync(tokens, targetAlias, cancellationToken);
                if (exitCode == 0 && rememberedCommand.Length > 1 && !IsUnsafeToRemember(rememberedCommand[0], rememberedCommand.Skip(1)))
                    _commandMemory[rememberedCommand[0]] = rememberedCommand.Skip(1).ToArray();
            }
            catch (CliException ex)
            {
                CliApplication.RenderShellFailure(ex);
            }
        }
        return 0;
    }

    private Task<int> ExecuteRemoteAsync(string[] tokens, string? targetAlias, CancellationToken cancellationToken)
    {
        if (targetAlias is null)
            return _execute(tokens, cancellationToken);
        var target = _targets.Resolve(targetAlias);
        var node = _targets.ResolveNode(target.Node);
        return _execute([$"--server={node.ServerName}", $"--pipe={target.PipeName}", $"--target-alias={targetAlias}", .. tokens], cancellationToken);
    }

    private bool HandleLocal(string[] tokens)
    {
        switch (tokens[0].ToLowerInvariant())
        {
            case "set":
                if (tokens.Length < 3) throw new CliException("CLI_CONTEXT_SET", "Usage: set <name> <value>.", 2);
                ValidateVariableName(tokens[1]);
                _variables[tokens[1]] = string.Join(' ', tokens.Skip(2));
                return true;
            case "unset":
                if (tokens.Length != 2) throw new CliException("CLI_CONTEXT_UNSET", "Usage: unset <name>.", 2);
                _variables.Remove(tokens[1]);
                return true;
            case "vars":
                foreach (var item in _variables.OrderBy(x => x.Key)) Console.WriteLine($"{item.Key}={item.Value}");
                return true;
            case "history":
                for (var i = 0; i < _history.Count; i++) Console.WriteLine($"{i + 1}: {_history[i]}");
                return true;
            case "clear":
                _variables.Clear(); _commandMemory.Clear(); _history.Clear(); _currentPath = "/";
                return true;
            case "pwd": Console.WriteLine(_currentPath); return true;
            case "root": _currentPath = "/"; return true;
            case "cd":
                if (tokens.Length != 2) throw new CliException("CLI_CONTEXT_PATH", "Usage: cd <path>.", 2);
                _currentPath = RuntimeVirtualPathRouter.Resolve(_currentPath, tokens[1]);
                return true;
            case "help":
                Console.WriteLine("Local: help exit quit history vars set unset clear pwd cd ls get root target; use @name <command> for one command.");
                return true;
            default: return false;
        }
    }

    private async Task HandleTargetAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (tokens.Length < 2)
            throw new CliException("CLI_TARGET_USAGE", "Usage: target add|list|use|current|remove.", 2);
        switch (tokens[1].ToLowerInvariant())
        {
            case "add" when tokens.Length == 4:
                _targets.Add(tokens[2], tokens[3]);
                return;
            case "add" when tokens.Length == 7 && tokens[5].Equals("--node", StringComparison.OrdinalIgnoreCase):
                _targets.Add(tokens[2], tokens[4], tokens[6], tokens[3]);
                return;
            case "list" when tokens.Length == 2:
                foreach (var target in _targets.Targets.OrderBy(x => x.Key))
                {
                    var value = target.Value.Node == "local" && target.Value.Endpoint == "diagnostics"
                        ? target.Value.PipeName
                        : $"{target.Value.Node}/{target.Value.Endpoint}/{target.Value.PipeName}";
                    Console.WriteLine($"{target.Key}{(target.Key.Equals(_targets.CurrentName, StringComparison.OrdinalIgnoreCase) ? "*" : "")}={value}");
                }
                return;
            case "use" when tokens.Length == 3:
                _targets.Use(tokens[2]);
                return;
            case "current" when tokens.Length == 2:
                var current = _targets.Current();
                Console.WriteLine(current is null ? "(none)" : $"{current.Value.Name}={current.Value.Target.PipeName}");
                return;
            case "remove" when tokens.Length == 3:
                _targets.Remove(tokens[2]);
                return;
            case "test" when tokens.Length == 3:
                await ExecuteRemoteAsync(["host.summary"], tokens[2], cancellationToken);
                return;
            default:
                throw new CliException("CLI_TARGET_USAGE", "Usage: target add <name> <pipe> | target list | target use <name> | target current | target remove <name>.", 2);
        }
    }

    private async Task HandleNodeAsync(string[] tokens, CancellationToken cancellationToken)
    {
        if (tokens.Length < 2)
            throw new CliException("CLI_NODE_USAGE", "Usage: node add|list|show|remove.", 2);
        switch (tokens[1].ToLowerInvariant())
        {
            case "add" when tokens.Length == 4:
                _targets.AddNode(tokens[2], tokens[3]);
                return;
            case "list" when tokens.Length == 2:
                foreach (var item in _targets.Nodes.OrderBy(x => x.Key)) Console.WriteLine($"{item.Key}={item.Value.ServerName}");
                return;
            case "show" when tokens.Length == 3:
                var selectedNode = _targets.ResolveNode(tokens[2]);
                Console.WriteLine($"{tokens[2]}={selectedNode.ServerName}");
                return;
            case "remove" when tokens.Length == 3:
                _targets.RemoveNode(tokens[2]);
                return;
            case "auth" when tokens.Length == 5 && tokens[3].Equals("--user", StringComparison.OrdinalIgnoreCase):
                var authNode = _targets.ResolveNode(tokens[2]);
                CliWindowsNodeSession.Authenticate(authNode.ServerName, tokens[4]);
                Console.WriteLine($"{tokens[2]}: authenticated Windows IPC session established");
                return;
            case "logout" when tokens.Length == 4 && tokens[3].Equals("--confirm", StringComparison.OrdinalIgnoreCase):
                var logoutNode = _targets.ResolveNode(tokens[2]);
                CliWindowsNodeSession.Logout(logoutNode.ServerName);
                Console.WriteLine($"{tokens[2]}: Windows IPC session removed");
                return;
            case "test" when tokens.Length == 3:
                var testedNode = _targets.ResolveNode(tokens[2]);
                if (testedNode.ServerName == ".")
                {
                    Console.WriteLine($"{tokens[2]}: local ready");
                    return;
                }
                var addresses = await Dns.GetHostAddressesAsync(testedNode.ServerName, cancellationToken);
                using (var client = new TcpClient())
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(testedNode.ConnectTimeoutMs ?? 5000);
                    await client.ConnectAsync(testedNode.ServerName, 445, timeout.Token);
                }
                Console.WriteLine($"{tokens[2]}: resolved={string.Join(',', addresses.Select(x => x.ToString()))}; smb445=ready");
                return;
            default:
                throw new CliException("CLI_NODE_USAGE", "Usage: node add <name> <server> | node list | node show <name> | node remove <name>.", 2);
        }
    }

    private string[] ExpandVariables(string[] tokens) => tokens.Select(token => VariablePattern.Replace(token, match =>
    {
        var name = match.Groups["braced"].Success ? match.Groups["braced"].Value : match.Groups["plain"].Value;
        return _variables.TryGetValue(name, out var value) ? value : throw new CliException("CLI_CONTEXT_VARIABLE_NOT_FOUND", $"Context variable '{name}' is not defined.", 2);
    })).ToArray();

    private static void ValidateVariableName(string name)
    {
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_.-]*$", RegexOptions.CultureInvariant))
            throw new CliException("CLI_CONTEXT_VARIABLE_NAME", $"Invalid context variable name '{name}'.", 2);
    }

    private static bool IsUnsafeToRemember(string command, IEnumerable<string> arguments)
    {
        if (command.Contains("shutdown", StringComparison.OrdinalIgnoreCase) || command.Contains("delete", StringComparison.OrdinalIgnoreCase) || command.Contains("clear", StringComparison.OrdinalIgnoreCase))
            return true;
        return arguments.Any(value => value.Contains("password", StringComparison.OrdinalIgnoreCase) || value.Contains("secret", StringComparison.OrdinalIgnoreCase) || value.Contains("token", StringComparison.OrdinalIgnoreCase));
    }
}

internal static class CliTokenizer
{
    public static string[] Tokenize(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        char quote = '\0';
        var escape = false;
        foreach (var ch in line)
        {
            if (escape) { current.Append(ch); escape = false; continue; }
            if (ch == '\\') { escape = true; continue; }
            if (quote != '\0') { if (ch == quote) quote = '\0'; else current.Append(ch); continue; }
            if (ch is '\'' or '"') { quote = ch; continue; }
            if (char.IsWhiteSpace(ch)) { if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); } continue; }
            current.Append(ch);
        }
        if (escape || quote != '\0') throw new CliException("CLI_SHELL_SYNTAX", "Unterminated escape or quote.", 2);
        if (current.Length > 0) result.Add(current.ToString());
        return result.ToArray();
    }
}
