namespace Iwesun.Runtime.Cli;

internal sealed class CliTargetContext
{
    private readonly Dictionary<string, CliTarget> _targets;
    private readonly Dictionary<string, CliNode> _nodes;
    public CliTargetContext(IReadOnlyDictionary<string, CliNode> nodes, IReadOnlyDictionary<string, CliTarget> targets)
    {
        _nodes = new Dictionary<string, CliNode>(nodes, StringComparer.OrdinalIgnoreCase);
        _nodes.TryAdd("local", new CliNode());
        _targets = new Dictionary<string, CliTarget>(targets, StringComparer.OrdinalIgnoreCase);
    }

    public string? CurrentName { get; private set; }
    public IReadOnlyDictionary<string, CliTarget> Targets => _targets;
    public IReadOnlyDictionary<string, CliNode> Nodes => _nodes;

    public void Add(string name, string pipeName, string node = "local", string endpoint = "diagnostics")
    {
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(pipeName))
            throw new CliException("CLI_TARGET_PIPE", "Target pipe name is required.", 2);
        ResolveNode(node);
        _targets[name] = new CliTarget { Node = node, Endpoint = endpoint, PipeName = pipeName.Trim() };
    }

    public void Use(string name)
    {
        Resolve(name);
        CurrentName = name;
    }

    public void Remove(string name)
    {
        if (!_targets.Remove(name))
            throw new CliException("CLI_TARGET_NOT_FOUND", $"Target '{name}' is not defined.", 2);
        if (name.Equals(CurrentName, StringComparison.OrdinalIgnoreCase))
            CurrentName = null;
    }

    public CliTarget Resolve(string name) => _targets.TryGetValue(name, out var target)
        ? target
        : throw new CliException("CLI_TARGET_NOT_FOUND", $"Target '{name}' is not defined.", 2);

    public CliNode ResolveNode(string name) => _nodes.TryGetValue(name, out var node)
        ? node
        : throw new CliException("CLI_NODE_NOT_FOUND", $"Node '{name}' is not defined.", 2);

    public void AddNode(string name, string serverName)
    {
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(serverName))
            throw new CliException("CLI_NODE_SERVER", "Node server name is required.", 2);
        _nodes[name] = new CliNode { ServerName = serverName.Trim() };
    }

    public void RemoveNode(string name)
    {
        var references = _targets.Where(x => x.Value.Node.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).Order().ToArray();
        if (references.Length != 0)
            throw new CliException("CLI_NODE_IN_USE", $"Node '{name}' is referenced by: {string.Join(", ", references)}.", 2);
        if (!_nodes.Remove(name))
            throw new CliException("CLI_NODE_NOT_FOUND", $"Node '{name}' is not defined.", 2);
    }

    public (string Name, CliTarget Target)? Current() =>
        CurrentName is null ? null : (CurrentName, Resolve(CurrentName));

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
            throw new CliException("CLI_TARGET_NAME", $"Invalid target name '{name}'.", 2);
    }
}
