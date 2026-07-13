using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace Iwesun.Runtime.Diagnostics;

internal static class RuntimeNamedPipeServerFactory
{
	public static NamedPipeServerStream CreateDiagnosticsServer(string pipeName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);

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
		AddRuntimeOperatorsRule(security);
		AddRule(
			security,
			WellKnownSidType.AuthenticatedUserSid,
			PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance);

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
	private static void AddRuntimeOperatorsRule(PipeSecurity security)
	{
		try
		{
			var account = new NTAccount(Environment.MachineName, "Iwesun Runtime Operators");
			var sid = (SecurityIdentifier)account.Translate(typeof(SecurityIdentifier));
			security.AddAccessRule(new PipeAccessRule(
				sid,
				PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance,
				AccessControlType.Allow));
		}
		catch (IdentityNotMappedException)
		{
			// Installation of the optional operators group is an administrator deployment task.
		}
	}

	[SupportedOSPlatform("windows")]
	private static void AddRule(PipeSecurity security, WellKnownSidType sidType, PipeAccessRights rights)
	{
		security.AddAccessRule(new PipeAccessRule(
			new SecurityIdentifier(sidType, domainSid: null),
			rights,
			AccessControlType.Allow));
	}
}
