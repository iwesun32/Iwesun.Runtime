using System.Security.Principal;
using Iwesun.Runtime.RemoteConsole.Protocol;

namespace Iwesun.Runtime.RemoteConsole;

public sealed class RemoteConsoleOptions
{
	public const string DefaultPipeName = "Iwesun.Runtime.RemoteConsole";

	public string PipeName { get; init; } = DefaultPipeName;

	public IReadOnlyList<string> SubmitterPrincipals { get; init; } = [];

	public IReadOnlyList<string> ApproverPrincipals { get; init; } = [];

	public RemoteConsoleApprovalMode ApprovalMode { get; init; } = RemoteConsoleApprovalMode.Manual;

	public IReadOnlyList<string> AutoApprovePatterns { get; init; } = [];

	public IReadOnlyList<string> DenyPatterns { get; init; } = [];

	public string WorkspaceRoot { get; init; } = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
		"Iwesun",
		"RemoteConsole",
		"workspaces");

	public int MaxRequestBytes { get; init; } = 16 * 1024 * 1024;

	public string PowerShellPath { get; init; } = "pwsh.exe";

	public string CommandPromptPath { get; init; } = "cmd.exe";

	public int MaxOutputBytesPerJob { get; init; } = 4 * 1024 * 1024;

	public long MaxFileBytes { get; init; } = 64 * 1024 * 1024;

	public long MaxWorkspaceBytes { get; init; } = 256 * 1024 * 1024;

	public long MaxTotalWorkspaceBytes { get; init; } = 1024L * 1024 * 1024;

	public TimeSpan WorkspaceRetention { get; init; } = TimeSpan.FromHours(24);
}

public sealed class RemoteConsoleConfigurationException : InvalidOperationException
{
	public RemoteConsoleConfigurationException(string message) : base(message)
	{
	}

	public RemoteConsoleConfigurationException(string message, Exception innerException) : base(message, innerException)
	{
	}
}

internal sealed record RemoteConsoleResolvedPrincipals(
	IReadOnlyList<SecurityIdentifier> Submitters,
	IReadOnlyList<SecurityIdentifier> Approvers)
{
	public IReadOnlyList<string> SubmitterSids { get; } = Submitters.Select(static sid => sid.Value).ToArray();

	public IReadOnlyList<string> ApproverSids { get; } = Approvers.Select(static sid => sid.Value).ToArray();
}

internal static class RemoteConsolePrincipalResolver
{
	public static RemoteConsoleResolvedPrincipals Resolve(RemoteConsoleOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentException.ThrowIfNullOrWhiteSpace(options.PipeName);
		if (options.MaxRequestBytes <= 0)
			throw new RemoteConsoleConfigurationException("MaxRequestBytes must be positive.");
		if (!OperatingSystem.IsWindows())
			throw new PlatformNotSupportedException("Iwesun Runtime RemoteConsole requires Windows.");

		var submitters = ResolveList(options.SubmitterPrincipals, nameof(options.SubmitterPrincipals));
		var approvers = ResolveList(options.ApproverPrincipals, nameof(options.ApproverPrincipals));
		if (submitters.Count == 0)
			throw new RemoteConsoleConfigurationException("At least one submitter principal is required.");
		if (approvers.Count == 0)
			throw new RemoteConsoleConfigurationException("At least one approver principal is required.");
		return new RemoteConsoleResolvedPrincipals(submitters, approvers);
	}

	private static IReadOnlyList<SecurityIdentifier> ResolveList(
		IEnumerable<string> values,
		string optionName)
	{
		ArgumentNullException.ThrowIfNull(values);
		var resolved = new Dictionary<string, SecurityIdentifier>(StringComparer.OrdinalIgnoreCase);
		foreach (var value in values.Where(static item => !string.IsNullOrWhiteSpace(item)))
		{
			var principal = value.Trim();
			try
			{
				var sid = principal.StartsWith("S-", StringComparison.OrdinalIgnoreCase)
					? new SecurityIdentifier(principal)
					: (SecurityIdentifier)new NTAccount(principal).Translate(typeof(SecurityIdentifier));
				resolved[sid.Value] = sid;
			}
			catch (Exception ex) when (ex is IdentityNotMappedException or ArgumentException)
			{
				throw new RemoteConsoleConfigurationException(
					$"RemoteConsole principal '{principal}' in {optionName} could not be resolved.",
					ex);
			}
		}
		return resolved.Values.ToArray();
	}
}
