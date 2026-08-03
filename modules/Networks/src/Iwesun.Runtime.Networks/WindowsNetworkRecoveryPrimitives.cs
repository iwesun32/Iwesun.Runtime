using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Principal;

namespace Iwesun.Runtime.Networks;

public enum NetworkRecoveryActionKind : byte
{
	Unspecified = 0,
	RouterSolicitation = 1,
	DhcpV6Release = 2,
	Wait = 3,
	DhcpV6Renew = 4,
	RestartInterface = 5,
	CaptureAddressSnapshot = 6,
}

public enum NetworkConnectivityRecheckOutcome : byte
{
	NotPerformed = 0,
	Succeeded = 1,
	Failed = 2,
}

public readonly record struct NetworkRecoveryCapability(
	NetworkRecoveryActionKind Action,
	bool Supported,
	bool RequiresElevation,
	string ReasonCode);

public readonly record struct NetworkRecoveryAddressSnapshot(
	NetworkInterfaceIdentity Interface,
	IpAddressValue[] Addresses,
	long CapturedAtUnixMs)
{
	public bool IsValid => Interface.IsValid && Addresses is not null && CapturedAtUnixMs > 0;
}

public readonly record struct NetworkRecoveryActionRequest(
	Guid RequestId,
	NetworkRecoveryActionKind Action,
	NetworkInterfaceIdentity Interface,
	int WaitMs = 0)
{
	public bool IsValid => RequestId != Guid.Empty && Interface.IsValid && Action != NetworkRecoveryActionKind.Unspecified &&
		(Action == NetworkRecoveryActionKind.Wait ? WaitMs > 0 : WaitMs == 0);
}

public readonly record struct NetworkRecoveryPlatformResult(
	bool Succeeded,
	int ExitCode,
	BinaryNetworkError Error,
	string ReasonCode)
{
	public static NetworkRecoveryPlatformResult Success() => new(true, 0, default, string.Empty);
}

public readonly record struct NetworkRecoveryActionResult(
	Guid RequestId,
	NetworkRecoveryActionKind Action,
	NetworkInterfaceIdentity Interface,
	bool RequestAccepted,
	bool PlatformActionSucceeded,
	bool AddressChanged,
	NetworkConnectivityRecheckOutcome ConnectivityRecheck,
	NetworkRecoveryAddressSnapshot Before,
	NetworkRecoveryAddressSnapshot After,
	int ExitCode,
	BinaryNetworkError Error,
	string ReasonCode,
	long CompletedAtUnixMs);

public interface INetworkRecoveryPlatformExecutor
{
	ValueTask<NetworkRecoveryPlatformResult> SendRouterSolicitationAsync(
		NetworkInterfaceIdentity networkInterface,
		CancellationToken cancellationToken);

	ValueTask<NetworkRecoveryPlatformResult> ReleaseDhcpV6Async(
		NetworkInterfaceIdentity networkInterface,
		CancellationToken cancellationToken);

	ValueTask<NetworkRecoveryPlatformResult> RenewDhcpV6Async(
		NetworkInterfaceIdentity networkInterface,
		CancellationToken cancellationToken);

	ValueTask<NetworkRecoveryPlatformResult> RestartInterfaceAsync(
		NetworkInterfaceIdentity networkInterface,
		CancellationToken cancellationToken);
}

/// <summary>Windows recovery primitives. The caller owns recovery ordering and policy.</summary>
public sealed class WindowsNetworkRecoveryPrimitives
{
	private const string NetworkConfigurationOperatorsSid = "S-1-5-32-556";
	private readonly INetworkRecoveryPlatformExecutor _platform;
	private readonly bool _platformSupported;
	private readonly bool _customPlatform;

	public WindowsNetworkRecoveryPrimitives(INetworkRecoveryPlatformExecutor? platform = null)
	{
		_platform = platform ?? new WindowsNetworkRecoveryPlatformExecutor();
		_customPlatform = platform is not null;
		_platformSupported = platform is not null || OperatingSystem.IsWindows();
	}

	public NetworkRecoveryCapability GetCapability(NetworkRecoveryActionKind action)
	{
		var known = action is NetworkRecoveryActionKind.RouterSolicitation or
			NetworkRecoveryActionKind.DhcpV6Release or NetworkRecoveryActionKind.Wait or
			NetworkRecoveryActionKind.DhcpV6Renew or NetworkRecoveryActionKind.RestartInterface or
			NetworkRecoveryActionKind.CaptureAddressSnapshot;
		if (!known) return new(action, false, false, NetworkAccessFailureCodes.CapabilityUnsupported);
		if (!_platformSupported && action is not NetworkRecoveryActionKind.Wait)
			return new(action, false, false, NetworkAccessFailureCodes.PlatformNotSupported);
		var requiresElevation = action is NetworkRecoveryActionKind.RouterSolicitation or
			NetworkRecoveryActionKind.DhcpV6Release or NetworkRecoveryActionKind.DhcpV6Renew or
			NetworkRecoveryActionKind.RestartInterface;
		if (requiresElevation && !_customPlatform && !HasRecoveryPermission())
			return new(action, false, true, NetworkAccessFailureCodes.PlatformPermissionMissing);
		return new(
			action,
			true,
			requiresElevation,
			string.Empty);
	}

	public async ValueTask<NetworkRecoveryActionResult> ExecuteAsync(
		NetworkRecoveryActionRequest request,
		Func<CancellationToken, ValueTask<bool>>? connectivityRecheck = null,
		CancellationToken cancellationToken = default)
	{
		if (!request.IsValid)
			return Rejected(request, NetworkAccessFailureCodes.SelectorValueInvalid);
		var capability = GetCapability(request.Action);
		if (!capability.Supported) return Rejected(request, capability.ReasonCode);

		var before = CaptureSnapshot(request.Interface);
		NetworkRecoveryPlatformResult platformResult;
		try
		{
			platformResult = request.Action switch
			{
				NetworkRecoveryActionKind.RouterSolicitation =>
					await _platform.SendRouterSolicitationAsync(request.Interface, cancellationToken).ConfigureAwait(false),
				NetworkRecoveryActionKind.DhcpV6Release =>
					await _platform.ReleaseDhcpV6Async(request.Interface, cancellationToken).ConfigureAwait(false),
				NetworkRecoveryActionKind.Wait => await WaitAsync(request.WaitMs, cancellationToken).ConfigureAwait(false),
				NetworkRecoveryActionKind.DhcpV6Renew =>
					await _platform.RenewDhcpV6Async(request.Interface, cancellationToken).ConfigureAwait(false),
				NetworkRecoveryActionKind.RestartInterface =>
					await _platform.RestartInterfaceAsync(request.Interface, cancellationToken).ConfigureAwait(false),
				NetworkRecoveryActionKind.CaptureAddressSnapshot => NetworkRecoveryPlatformResult.Success(),
				_ => new(false, -1, default, NetworkAccessFailureCodes.CapabilityUnsupported),
			};
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			platformResult = new(false, -1, BinaryNetworkError.FromException(null, true, false), "recovery-cancelled");
		}
		catch (Exception ex)
		{
			var permissionDenied = ex is UnauthorizedAccessException ||
				ex is SocketException { SocketErrorCode: SocketError.AccessDenied };
			platformResult = new(
				false,
				-1,
				BinaryNetworkError.FromException(ex, false, false),
				permissionDenied ? NetworkAccessFailureCodes.PlatformPermissionMissing : "recovery-platform-failed");
		}

		var after = CaptureSnapshot(request.Interface);
		var addressChanged = !AddressesEqual(before.Addresses, after.Addresses);
		var recheck = NetworkConnectivityRecheckOutcome.NotPerformed;
		if (connectivityRecheck is not null)
		{
			try
			{
				recheck = await connectivityRecheck(cancellationToken).ConfigureAwait(false)
					? NetworkConnectivityRecheckOutcome.Succeeded
					: NetworkConnectivityRecheckOutcome.Failed;
			}
			catch
			{
				recheck = NetworkConnectivityRecheckOutcome.Failed;
			}
		}

		return new NetworkRecoveryActionResult(
			request.RequestId,
			request.Action,
			request.Interface,
			true,
			platformResult.Succeeded,
			addressChanged,
			recheck,
			before,
			after,
			platformResult.ExitCode,
			platformResult.Error,
			platformResult.ReasonCode,
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
	}

	private static async ValueTask<NetworkRecoveryPlatformResult> WaitAsync(
		int waitMs,
		CancellationToken cancellationToken)
	{
		await Task.Delay(waitMs, cancellationToken).ConfigureAwait(false);
		return NetworkRecoveryPlatformResult.Success();
	}

	private static NetworkRecoveryAddressSnapshot CaptureSnapshot(NetworkInterfaceIdentity identity)
	{
		var addresses = new List<IpAddressValue>();
		foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
		{
			if (!Matches(networkInterface, identity)) continue;
			try
			{
				foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
					addresses.Add(NetworkIpAddressInterop.FromSystemAddress(address.Address));
			}
			catch (NetworkInformationException)
			{
			}
			break;
		}
		return new NetworkRecoveryAddressSnapshot(
			identity,
			[.. addresses.OrderBy(static item => item.ToString(), StringComparer.Ordinal)],
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
	}

	private static bool Matches(NetworkInterface networkInterface, NetworkInterfaceIdentity identity)
	{
		if (identity.InterfaceGuid != Guid.Empty && Guid.TryParse(networkInterface.Id, out var guid) &&
			guid == identity.InterfaceGuid) return true;
		if (!string.IsNullOrWhiteSpace(identity.Alias) &&
			StringComparer.OrdinalIgnoreCase.Equals(networkInterface.Name, identity.Alias)) return true;
		try
		{
			var properties = networkInterface.GetIPProperties();
			return properties.GetIPv4Properties()?.Index == identity.InterfaceIndex ||
				properties.GetIPv6Properties()?.Index == identity.InterfaceIndex;
		}
		catch (NetworkInformationException)
		{
			return false;
		}
	}

	private static bool AddressesEqual(IpAddressValue[] first, IpAddressValue[] second) =>
		first.AsSpan().SequenceEqual(second);

	private static bool HasRecoveryPermission()
	{
		if (!OperatingSystem.IsWindows()) return false;
		try
		{
			using var identity = WindowsIdentity.GetCurrent();
			var principal = new WindowsPrincipal(identity);
			return principal.IsInRole(WindowsBuiltInRole.Administrator) ||
				principal.IsInRole(new SecurityIdentifier(NetworkConfigurationOperatorsSid));
		}
		catch
		{
			return false;
		}
	}

	private static NetworkRecoveryActionResult Rejected(
		NetworkRecoveryActionRequest request,
		string reason) => new(
		request.RequestId,
		request.Action,
		request.Interface,
		false,
		false,
		false,
		NetworkConnectivityRecheckOutcome.NotPerformed,
		default,
		default,
		-1,
		default,
		reason,
		DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}

internal sealed class WindowsNetworkRecoveryPlatformExecutor : INetworkRecoveryPlatformExecutor
{
	public async ValueTask<NetworkRecoveryPlatformResult> SendRouterSolicitationAsync(
		NetworkInterfaceIdentity networkInterface,
		CancellationToken cancellationToken)
	{
		using var socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Raw, ProtocolType.IcmpV6);
		socket.SetSocketOption(
			SocketOptionLevel.IPv6,
			SocketOptionName.MulticastInterface,
			checked((int)networkInterface.InterfaceIndex));
		socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.HopLimit, 255);
		var routerSolicitation = new byte[8];
		routerSolicitation[0] = 133;
		await socket.SendToAsync(
			routerSolicitation,
			SocketFlags.None,
			new IPEndPoint(IPAddress.Parse("ff02::2"), 0) { Address = { ScopeId = networkInterface.InterfaceIndex } },
			cancellationToken).ConfigureAwait(false);
		return NetworkRecoveryPlatformResult.Success();
	}

	public ValueTask<NetworkRecoveryPlatformResult> ReleaseDhcpV6Async(
		NetworkInterfaceIdentity networkInterface,
		CancellationToken cancellationToken) => RunAsync(
		"ipconfig.exe", ["/release6", networkInterface.Alias], cancellationToken);

	public ValueTask<NetworkRecoveryPlatformResult> RenewDhcpV6Async(
		NetworkInterfaceIdentity networkInterface,
		CancellationToken cancellationToken) => RunAsync(
		"ipconfig.exe", ["/renew6", networkInterface.Alias], cancellationToken);

	public async ValueTask<NetworkRecoveryPlatformResult> RestartInterfaceAsync(
		NetworkInterfaceIdentity networkInterface,
		CancellationToken cancellationToken)
	{
		NetworkRecoveryPlatformResult disabled;
		try
		{
			disabled = await RunAsync(
				"netsh.exe", ["interface", "set", "interface", $"name={networkInterface.Alias}", "admin=disabled"],
				cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			_ = await RunAsync(
				"netsh.exe", ["interface", "set", "interface", $"name={networkInterface.Alias}", "admin=enabled"],
				CancellationToken.None).ConfigureAwait(false);
			throw;
		}
		if (!disabled.Succeeded) return disabled;
		var enabled = await RunAsync(
			"netsh.exe", ["interface", "set", "interface", $"name={networkInterface.Alias}", "admin=enabled"],
			CancellationToken.None).ConfigureAwait(false);
		return enabled;
	}

	private static async ValueTask<NetworkRecoveryPlatformResult> RunAsync(
		string fileName,
		IReadOnlyList<string> arguments,
		CancellationToken cancellationToken)
	{
		var startInfo = new ProcessStartInfo(fileName)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
		};
		foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
		using var process = new Process { StartInfo = startInfo };
		if (!process.Start()) return new(false, -1, default, "recovery-process-start-failed");
		var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
		var error = process.StandardError.ReadToEndAsync(cancellationToken);
		await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
		await Task.WhenAll(output, error).ConfigureAwait(false);
		return process.ExitCode == 0
			? NetworkRecoveryPlatformResult.Success()
			: new(false, process.ExitCode, default, "recovery-command-failed");
	}
}
