using System.Security.Principal;

namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Defines the Windows identities that may connect to the Runtime diagnostics pipe.
/// Credentials are deliberately not part of this model.
/// </summary>
public sealed class RuntimeNamedPipeAccessOptions
{
	/// <summary>
	/// Gets or sets whether local interactive Windows users are granted pipe access.
	/// This keeps local CLI operation independent from remote account authorization.
	/// </summary>
	public bool AllowLocalInteractiveUsers { get; init; } = true;

	/// <summary>
	/// Gets or sets whether every authenticated Windows identity is granted pipe access.
	/// Disable this for strict account or group based remote access.
	/// </summary>
	public bool AllowAuthenticatedUsers { get; init; } = true;

	/// <summary>
	/// Gets or sets the Windows account or group names granted pipe access.
	/// Examples: <c>SERVER\IwesunAiDiag</c> or <c>DOMAIN\RuntimeOperators</c>.
	/// </summary>
	public IReadOnlyList<string> AllowedWindowsPrincipals { get; init; } = [];

	internal RuntimeNamedPipeAccessOptions Normalize()
	{
		var principals = AllowedWindowsPrincipals
			.Where(static value => !string.IsNullOrWhiteSpace(value))
			.Select(static value => value.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		return new RuntimeNamedPipeAccessOptions
		{
			AllowLocalInteractiveUsers = AllowLocalInteractiveUsers,
			AllowAuthenticatedUsers = AllowAuthenticatedUsers,
			AllowedWindowsPrincipals = principals
		};
	}
}

internal sealed class RuntimeNamedPipeAccessPolicy
{
	public RuntimeNamedPipeAccessPolicy(RuntimeNamedPipeAccessOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		AllowLocalInteractiveUsers = options.AllowLocalInteractiveUsers;
		AllowAuthenticatedUsers = options.AllowAuthenticatedUsers;
		if (!OperatingSystem.IsWindows())
		{
			AllowedWindowsPrincipals = [];
			return;
		}

		var principals = new List<SecurityIdentifier>();
		foreach (var principalName in options.AllowedWindowsPrincipals)
		{
			try
			{
				var account = new NTAccount(principalName);
				principals.Add((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier)));
			}
			catch (IdentityNotMappedException ex)
			{
				throw new RuntimeHostConfigurationException(
					$"Runtime pipe principal '{principalName}' could not be resolved on this host.",
					ex);
			}
		}

		AllowedWindowsPrincipals = principals;
	}

	public bool AllowAuthenticatedUsers { get; }

	public bool AllowLocalInteractiveUsers { get; }

	public IReadOnlyList<SecurityIdentifier> AllowedWindowsPrincipals { get; }
}
