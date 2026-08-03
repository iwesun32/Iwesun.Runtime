using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace Iwesun.Runtime.Diagnostics;

internal static class RuntimeNamedPipeServerFactory
{
	public static NamedPipeServerStream CreateDiagnosticsServer(
		string pipeName,
		RuntimeNamedPipeAccessPolicy accessPolicy)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
		ArgumentNullException.ThrowIfNull(accessPolicy);

		if (!OperatingSystem.IsWindows())
		{
			return new NamedPipeServerStream(
				pipeName,
				PipeDirection.InOut,
				NamedPipeServerStream.MaxAllowedServerInstances,
				PipeTransmissionMode.Byte,
				PipeOptions.Asynchronous);
		}

		var security = new PipeSecurity();
		security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
		AddRule(security, WellKnownSidType.LocalSystemSid, PipeAccessRights.FullControl);
		AddRule(security, WellKnownSidType.NetworkServiceSid, PipeAccessRights.FullControl);
		AddRule(security, WellKnownSidType.BuiltinAdministratorsSid, PipeAccessRights.FullControl);
		if (accessPolicy.AllowLocalInteractiveUsers)
		{
			AddRule(
				security,
				WellKnownSidType.InteractiveSid,
				PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance);
		}
		foreach (var principal in accessPolicy.AllowedWindowsPrincipals)
			AddRule(security, principal, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance);
		if (accessPolicy.AllowAuthenticatedUsers)
		{
			AddRule(
				security,
				WellKnownSidType.AuthenticatedUserSid,
				PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance);
		}

		return NamedPipeServerStreamAcl.Create(
			pipeName,
			PipeDirection.InOut,
			NamedPipeServerStream.MaxAllowedServerInstances,
			PipeTransmissionMode.Byte,
			PipeOptions.Asynchronous,
			inBufferSize: 0,
			outBufferSize: 0,
			security);
	}

	[SupportedOSPlatform("windows")]
	private static void AddRule(PipeSecurity security, WellKnownSidType sidType, PipeAccessRights rights)
	{
		security.AddAccessRule(new PipeAccessRule(
			new SecurityIdentifier(sidType, domainSid: null),
			rights,
			AccessControlType.Allow));
	}

	[SupportedOSPlatform("windows")]
	private static void AddRule(PipeSecurity security, SecurityIdentifier principal, PipeAccessRights rights)
	{
		security.AddAccessRule(new PipeAccessRule(principal, rights, AccessControlType.Allow));
	}
}
