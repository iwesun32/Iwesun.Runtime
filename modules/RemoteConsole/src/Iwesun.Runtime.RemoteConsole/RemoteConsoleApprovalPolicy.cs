using System.Text.RegularExpressions;
using Iwesun.Runtime.RemoteConsole.Protocol;

namespace Iwesun.Runtime.RemoteConsole;

public enum RemoteConsoleApprovalDecision
{
	AwaitApproval,
	Start,
	Reject
}

public sealed class RemoteConsoleApprovalPolicy
{
	private static readonly Regex SystemPowerCommandPattern = new(
		@"(?:^|[\s;&|])(?:Restart-Computer|Stop-Computer)\b|(?:^|[\s;&|])shutdown(?:\.exe)?\b(?=[^\r\n]*(?:/(?:r|s|g|sg|hybrid)\b|-(?:r|s)\b))",
		RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
		TimeSpan.FromMilliseconds(250));
	private readonly Regex[] _autoApprovePatterns;
	private readonly Regex[] _denyPatterns;
	private int _mode;

	public RemoteConsoleApprovalPolicy(
		RemoteConsoleApprovalMode mode,
		IEnumerable<string> autoApprovePatterns,
		IEnumerable<string> denyPatterns)
	{
		if (!Enum.IsDefined(mode))
			throw new RemoteConsoleConfigurationException($"Unknown approval mode: {mode}.");
		_autoApprovePatterns = CompilePatterns(autoApprovePatterns, nameof(autoApprovePatterns));
		_denyPatterns = CompilePatterns(denyPatterns, nameof(denyPatterns));
		_mode = (int)mode;
	}

	public RemoteConsoleApprovalMode Mode => (RemoteConsoleApprovalMode)Volatile.Read(ref _mode);

	public IReadOnlyList<string> AutoApprovePatterns => _autoApprovePatterns.Select(static pattern => pattern.ToString()).ToArray();

	public IReadOnlyList<string> DenyPatterns => _denyPatterns.Select(static pattern => pattern.ToString()).ToArray();

	public void SetMode(RemoteConsoleApprovalMode mode)
	{
		if (!Enum.IsDefined(mode))
			throw new ArgumentOutOfRangeException(nameof(mode));
		Volatile.Write(ref _mode, (int)mode);
	}

	public RemoteConsoleApprovalDecision Evaluate(RemoteConsoleSubmitRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var candidate = string.Join('\n', request.Shell, request.Command, request.WorkingDirectory);
		if (SystemPowerCommandPattern.IsMatch(request.Command))
			return RemoteConsoleApprovalDecision.Reject;
		return Mode switch
		{
			RemoteConsoleApprovalMode.Manual => RemoteConsoleApprovalDecision.AwaitApproval,
			RemoteConsoleApprovalMode.Guarded => _autoApprovePatterns.Any(pattern => pattern.IsMatch(candidate))
				? RemoteConsoleApprovalDecision.Start
				: RemoteConsoleApprovalDecision.AwaitApproval,
			RemoteConsoleApprovalMode.Automatic => _denyPatterns.Any(pattern => pattern.IsMatch(candidate))
				? RemoteConsoleApprovalDecision.Reject
				: RemoteConsoleApprovalDecision.Start,
			_ => throw new RemoteConsoleConfigurationException($"Unknown approval mode: {Mode}.")
		};
	}

	private static Regex[] CompilePatterns(IEnumerable<string> patterns, string optionName)
	{
		ArgumentNullException.ThrowIfNull(patterns);
		return patterns.Select(pattern => CompilePattern(pattern, optionName)).ToArray();
	}

	private static Regex CompilePattern(string pattern, string optionName)
	{
		if (string.IsNullOrWhiteSpace(pattern)
			|| !pattern.StartsWith('^')
			|| !pattern.EndsWith('$'))
		{
			throw new RemoteConsoleConfigurationException(
				$"Every {optionName} entry must be a non-empty anchored regular expression.");
		}
		try
		{
			return new Regex(
				pattern,
				RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.Singleline,
				TimeSpan.FromMilliseconds(250));
		}
		catch (ArgumentException ex)
		{
			throw new RemoteConsoleConfigurationException($"Invalid {optionName} pattern '{pattern}'.", ex);
		}
	}
}
