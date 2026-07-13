namespace Iwesun.Runtime.Cli;

internal sealed class CliTargetContext
{
    private readonly Dictionary<string, CliTarget> _targets;
    public CliTargetContext(IReadOnlyDictionary<string, CliTarget> targets) =>
        _targets = new Dictionary<string, CliTarget>(targets, StringComparer.OrdinalIgnoreCase);

    public string? CurrentName { get; private set; }
    public IReadOnlyDictionary<string, CliTarget> Targets => _targets;

    public void Add(string name, string pipeName)
    {
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(pipeName))
            throw new CliException("CLI_TARGET_PIPE", "Target pipe name is required.", 2);
        _targets[name] = new CliTarget { PipeName = pipeName.Trim() };
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

    public (string Name, CliTarget Target)? Current() =>
        CurrentName is null ? null : (CurrentName, Resolve(CurrentName));

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(ch => !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
            throw new CliException("CLI_TARGET_NAME", $"Invalid target name '{name}'.", 2);
    }
}
