namespace Iwesun.Runtime.Cli;

internal sealed class CliTargetContext
{
    private readonly Dictionary<string, CliTarget> _targets;
    private readonly Dictionary<string, CliNode> _nodes;
    private readonly Dictionary<string, string> _currentNames = new(StringComparer.OrdinalIgnoreCase);
    public CliTargetContext(IReadOnlyDictionary<string, CliNode> nodes, IReadOnlyDictionary<string, CliTarget> targets)
    {
        _nodes = new Dictionary<string, CliNode>(nodes, StringComparer.OrdinalIgnoreCase);
        _nodes.TryAdd("local", new CliNode());
        _targets = new Dictionary<string, CliTarget>(targets, StringComparer.OrdinalIgnoreCase);
    }

    public string? CurrentName => CurrentNameFor("diagnostics");
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
		var target = Resolve(name);
		_currentNames[target.Endpoint] = name;
    }

	public void Use(string name, string endpoint)
	{
		var target = Resolve(name);
		if (!target.Endpoint.Equals(endpoint, StringComparison.OrdinalIgnoreCase))
			throw new CliException("CLI_TARGET_ENDPOINT", $"Target '{name}' is endpoint '{target.Endpoint}', not '{endpoint}'.", 2);
		_currentNames[endpoint] = name;
	}

	public string? CurrentNameFor(string endpoint) => _currentNames.GetValueOrDefault(endpoint);

    public void Remove(string name)
    {
        if (!_targets.Remove(name))
            throw new CliException("CLI_TARGET_NOT_FOUND", $"Target '{name}' is not defined.", 2);
		foreach (var endpoint in _currentNames.Where(item => item.Value.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(static item => item.Key).ToArray())
			_currentNames.Remove(endpoint);
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

	public (string Name, CliTarget Target)? Current(string endpoint)
	{
		var name = CurrentNameFor(endpoint);
		return name is null ? null : (name, Resolve(name));
	}

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
            throw new CliException("CLI_TARGET_NAME", $"Invalid target name '{name}'.", 2);
    }
}
