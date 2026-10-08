using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Iwesun.Runtime.Networks;
using Iwesun.Runtime.Diagnostics;

#if DEBUG
[assembly: DiagnosticBreakpoint("wfp.validation.stage.break", "wfp-validation",
	"Inspect the live policy lease and socket boundaries.", "Program.cs", Enabled = false)]
if (args.FirstOrDefault() == "serve-debug")
	return await WfpValidationDebugHost.RunAsync().ConfigureAwait(false);
#endif

return args.FirstOrDefault() == "serve-local"
    ? await LocalStreamValidationHost.RunAsync(args.Skip(1).ToArray()).ConfigureAwait(false)
    : await WfpValidationProgram.RunAsync(args).ConfigureAwait(false);

internal static class WfpValidationProgram
{
	private const string Confirmation = "--confirm-system-mutation";

	public static async Task<int> RunAsync(string[] args)
	{
		try { return await RunCoreAsync(args).ConfigureAwait(false); }
		catch (Exception error) when (error is OperationCanceledException or SocketException or
			HttpRequestException or IOException or System.Security.Authentication.AuthenticationException)
		{
			Write(new { mode = "failure", reason = error is OperationCanceledException ? "timeout" : "network-error",
				errorType = error.GetType().Name, message = error.Message, cleanupVerification = "run check --policy-id independently" });
			return 8;
		}
	}

	private static async Task<int> RunCoreAsync(string[] args)
	{
		if (args.Length == 0) return Usage();
		var mode = args[0].Trim().ToLowerInvariant();
		var options = ParseOptions(args.AsSpan(1));
		if (options.TryGetValue("https-host", out var httpsHost) &&
			(Uri.CheckHostName(httpsHost) != UriHostNameType.Dns ||
			(options.TryGetValue("protocol", out var requestedProtocol) &&
			 !StringComparer.OrdinalIgnoreCase.Equals(requestedProtocol, "tcp")))) return Usage();

		if (mode == "recovery-capability")
		{
			if (!TryCreateRecoveryInterface(options, out var networkInterface)) return Usage();
			var recovery = new WindowsNetworkRecoveryPrimitives();
			Write(new
			{
				mode,
				networkInterface,
				capabilities = Enum.GetValues<NetworkRecoveryActionKind>()
					.Where(static action => action != NetworkRecoveryActionKind.Unspecified)
					.Select(recovery.GetCapability)
					.ToArray(),
			});
			return 0;
		}

		if (mode == "recovery")
		{
			if (!options.ContainsKey(Confirmation[2..]) ||
				!TryCreateRecoveryRequest(options, out var recoveryRequest)) return Usage();
			var recovery = new WindowsNetworkRecoveryPrimitives();
			using var timeout = new CancellationTokenSource(GetInt(options, "timeout-ms", 30000));
			var result = await recovery.ExecuteAsync(recoveryRequest, cancellationToken: timeout.Token).ConfigureAwait(false);
			Write(new { mode, result });
			return result.RequestAccepted && result.PlatformActionSucceeded ? 0 : 7;
		}

		var backend = new WindowsNetworkWfpConnectionPolicyBackend();

		if (mode == "capability")
		{
			var capability = backend.QueryCapability();
			Write(new
			{
				mode,
				capability.Supported,
				capability.MissingPermission,
				capability.ReasonCode,
				capability.PlatformError,
			});
			return capability.Supported ? 0 : 2;
		}

		if (mode == "check")
		{
			if (!TryGuid(options, "policy-id", out var policyId)) return Usage();
			var queried = backend.TryPolicyExists(policyId, out var exists, out var error);
			Write(new { mode, policyId, queried, exists, error });
			return queried && !exists ? 0 : queried ? 3 : 4;
		}

		if (mode is not ("run" or "crash") || !options.ContainsKey(Confirmation[2..]))
			return Usage();
		if (mode == "crash" && (options.ContainsKey("diagnostic-app-only") ||
			options.ContainsKey("diagnostic-target-port-only") ||
			options.ContainsKey("diagnostic-target-only")))
			return Usage();
		if (options.ContainsKey("diagnostic-max-weight") &&
			(mode != "run" || !options.ContainsKey("diagnostic-app-only") &&
			 !options.ContainsKey("diagnostic-target-port-only") &&
			 !options.ContainsKey("diagnostic-target-only"))) return Usage();
		var diagnosticPolicyBeforeBind = options.ContainsKey("diagnostic-policy-before-bind");
		if (diagnosticPolicyBeforeBind &&
			(mode != "run" || !options.ContainsKey("diagnostic-app-only"))) return Usage();
		var diagnosticTargetPortOnly = options.ContainsKey("diagnostic-target-port-only");
		var diagnosticTargetOnly = options.ContainsKey("diagnostic-target-only");
		var diagnosticUnboundSocket = options.ContainsKey("diagnostic-unbound-socket");
		var diagnosticNextHopOnly = options.ContainsKey("diagnostic-next-hop-setting-only");
		if (diagnosticNextHopOnly && (mode != "run" || !diagnosticTargetOnly)) return Usage();
		if (diagnosticUnboundSocket && (mode != "run" || !diagnosticTargetOnly ||
			diagnosticPolicyBeforeBind)) return Usage();
		if (diagnosticTargetPortOnly &&
			(options.ContainsKey("diagnostic-app-only") || diagnosticPolicyBeforeBind || diagnosticTargetOnly)) return Usage();
		if (diagnosticTargetOnly &&
			(options.ContainsKey("diagnostic-app-only") || diagnosticPolicyBeforeBind)) return Usage();
		if (!TryCreateRequest(options, diagnosticPolicyBeforeBind, diagnosticUnboundSocket,
			out var request, out var socket, out var target))
			return Usage();

		using (socket)
		{
			var capability = backend.QueryCapability();
			if (!capability.Supported)
			{
				Write(new { mode, capability.Supported, capability.ReasonCode, capability.PlatformError });
				return 2;
			}

			var diagnosticApplicationOnly = options.ContainsKey("diagnostic-app-only");
			var diagnosticMaximumWeight = options.ContainsKey("diagnostic-max-weight");
			var acquired = backend.Acquire(request, diagnosticApplicationOnly, diagnosticMaximumWeight,
				diagnosticTargetPortOnly, diagnosticTargetOnly, diagnosticNextHopOnly);
			if (!acquired.Succeeded)
			{
				Write(new { mode, acquired.ReasonCode, acquired.PlatformError });
				return 5;
			}
			using (acquired.Lease)
			{
#if DEBUG
			await WfpValidationDebugHost.StageAsync("acquired", new { request, ProcessId = Environment.ProcessId,
				ApplicationPath = Environment.ProcessPath, LocalEndpoint = socket.LocalEndPoint?.ToString() });
#endif
			var readbackQueried = backend.TryPolicyReadback(
				request.PolicyId, out var policyReadback, out var readbackError);
			var filtersQueried = backend.TryPolicyFilterReadback(
				request.PolicyId, out var filterReadback, out var filterReadbackError);
			var calloutReadback = default(WindowsNetworkWfpConnectionPolicyBackend.NetworkWfpCalloutReadback);
			var calloutReadbackError = default(NetworkPlatformError);
			var calloutQueried = filterReadback.Matching is { Length: > 0 } &&
				backend.TryCalloutReadback(filterReadback.Matching[0].ActionKey,
					out calloutReadback, out calloutReadbackError);
			var policyConfigured = readbackQueried && filtersQueried &&
				filterReadback.Matching is { Length: > 0 } && calloutQueried && calloutReadback.Registered;
#if DEBUG
			await WfpValidationDebugHost.StageAsync("readback", new { request.PolicyId, policyConfigured,
				policyReadback, filterReadback, calloutReadback, readbackError, filterReadbackError, calloutReadbackError });
#endif

			if (mode == "crash")
			{
				Write(new { mode, request.PolicyId, acquired = true, processId = Environment.ProcessId });
				Console.Out.Flush();
				Environment.FailFast("Intentional WFP dynamic-session crash-cleanup validation.");
			}

			{
#if !DEBUG
				using var timeout = new CancellationTokenSource(GetInt(options, "timeout-ms", 5000));
#endif
				if (diagnosticPolicyBeforeBind)
					socket.Bind(new IPEndPoint(request.Source.ToIPAddress(),
						options.TryGetValue("local-port", out var requestedLocalPort) &&
						ushort.TryParse(requestedLocalPort, out var bindPort) ? bindPort : 0));
#if DEBUG
				await WfpValidationDebugHost.StageAsync("connect-before", new { request.PolicyId,
					LocalEndpoint = socket.LocalEndPoint?.ToString(), Target = target.ToString(), ApplicationPath = Environment.ProcessPath });
				using (var connectTimeout = new CancellationTokenSource(GetInt(options, "timeout-ms", 5000)))
					await socket.ConnectAsync(target, connectTimeout.Token).ConfigureAwait(false);
				await WfpValidationDebugHost.StageAsync("connect-after", new { request.PolicyId,
					LocalEndpoint = socket.LocalEndPoint?.ToString(), RemoteEndpoint = socket.RemoteEndPoint?.ToString(), socket.Connected });
				using var timeout = new CancellationTokenSource(GetInt(options, "timeout-ms", 5000));
#else
				await socket.ConnectAsync(target, timeout.Token).ConfigureAwait(false);
#endif
				object? httpsResult = null;
				if (httpsHost is not null)
					httpsResult = await WfpHttpsProbe.ExecuteAsync(socket, httpsHost, target.Port, timeout.Token).ConfigureAwait(false);
				if (request.Protocol == ProtocolType.Udp)
					await socket.SendAsync(new byte[] { 0 }, SocketFlags.None, timeout.Token).ConfigureAwait(false);
				var holdMs = GetInt(options, "hold-ms", 0);
				if (holdMs > 0) await Task.Delay(holdMs, timeout.Token).ConfigureAwait(false);
				Write(new
				{
					mode,
					request.PolicyId,
					request.Identity,
					request.Source,
					request.Interface,
					request.NextHop,
					request.Destination,
					request.LocalPort,
					request.RemotePort,
					request.Protocol,
					readbackQueried,
					policyReadback,
					readbackError,
					filtersQueried,
					filterReadback,
					filterReadbackError,
					calloutQueried,
					calloutReadback,
					calloutReadbackError,
					policyConfigured,
					routeObserved = false,
					matchMode = diagnosticApplicationOnly ? "application-only-diagnostic" :
						diagnosticTargetPortOnly ? "target-and-port-diagnostic" :
						diagnosticTargetOnly ? "target-address-only-diagnostic" : "per-connection",
					policyWeight = diagnosticMaximumWeight ? "maximum-diagnostic" : "default",
					bindOrder = diagnosticPolicyBeforeBind ? "policy-before-bind-diagnostic" : "bind-before-policy",
					binding = diagnosticUnboundSocket ? "unbound-socket-diagnostic" : "explicit-source-bind",
					settingMode = diagnosticNextHopOnly ? "next-hop-only-diagnostic" : "source-interface-next-hop",
					localEndpoint = socket.LocalEndPoint?.ToString(),
					remoteEndpoint = socket.RemoteEndPoint?.ToString(),
					proof = ProofKind.Inferred,
					https = httpsResult,
				});
			}
			}

			var cleanupQueried = backend.TryPolicyExists(
				request.PolicyId, out var remains, out var cleanupError);
#if DEBUG
			await WfpValidationDebugHost.StageAsync("cleanup", new { request.PolicyId, cleanupQueried, remains, cleanupError });
#endif
			Write(new { mode = "cleanup", request.PolicyId, cleanupQueried, remains, cleanupError });
			return cleanupQueried && !remains ? 0 : 6;
		}
	}

	private static bool TryCreateRequest(
		IReadOnlyDictionary<string, string> options,
		bool diagnosticPolicyBeforeBind,
		bool diagnosticUnboundSocket,
		out NetworkWfpConnectionPolicyRequest request,
		out Socket socket,
		out IPEndPoint target)
	{
		request = default;
		socket = null!;
		target = null!;
		if (!TryAddress(options, "source", out var source) ||
			!TryAddress(options, "next-hop", out var nextHop) ||
			!TryAddress(options, "target", out var destination) ||
			!TryInterfaceLuid(options, out var interfaceLuid) ||
			!TryUshort(options, "target-port", out var remotePort) ||
			source.AddressFamily != destination.AddressFamily ||
			nextHop.AddressFamily != destination.AddressFamily)
		{
			return false;
		}

		var protocol = options.TryGetValue("protocol", out var protocolText) &&
			StringComparer.OrdinalIgnoreCase.Equals(protocolText, "udp")
			? ProtocolType.Udp : ProtocolType.Tcp;
		var socketType = protocol == ProtocolType.Udp ? SocketType.Dgram : SocketType.Stream;
		socket = new Socket(destination.AddressFamily, socketType, protocol);
		var localPort = options.TryGetValue("local-port", out var localPortText) &&
			ushort.TryParse(localPortText, out var parsedLocalPort) ? parsedLocalPort : (ushort)0;
		if (!diagnosticPolicyBeforeBind && !diagnosticUnboundSocket)
			socket.Bind(new IPEndPoint(source, localPort));
		var actualLocalPort = diagnosticPolicyBeforeBind || diagnosticUnboundSocket
			? (ushort)(localPort == 0 ? ushort.MaxValue : localPort)
			: checked((ushort)((IPEndPoint)socket.LocalEndPoint!).Port);
		target = new IPEndPoint(destination, remotePort);
		var identity = NetworkExecutionIdentity.ForRequest(Guid.NewGuid())
			.StartAttempt(Guid.NewGuid()).StartBranch(Guid.NewGuid());
		request = new NetworkWfpConnectionPolicyRequest(
			TryGuid(options, "policy-id", out var requestedPolicyId) ? requestedPolicyId : Guid.NewGuid(),
			identity,
			IpAddressValue.FromIPAddress(source),
			new NetworkInterfaceIdentity(Guid.Empty, interfaceLuid, "validation"),
			IpAddressValue.FromIPAddress(nextHop),
			actualLocalPort,
			IpAddressValue.FromIPAddress(destination),
			remotePort,
			protocol,
			Environment.ProcessPath ?? string.Empty);
		return request.IsValid;
	}

	private static bool TryCreateRecoveryRequest(
		IReadOnlyDictionary<string, string> options,
		out NetworkRecoveryActionRequest request)
	{
		request = default;
		if (!TryCreateRecoveryInterface(options, out var networkInterface) ||
			!options.TryGetValue("action", out var actionText)) return false;
		var action = actionText.Trim().ToLowerInvariant() switch
		{
			"rs" or "router-solicitation" => NetworkRecoveryActionKind.RouterSolicitation,
			"release6" or "dhcpv6-release" => NetworkRecoveryActionKind.DhcpV6Release,
			"wait" => NetworkRecoveryActionKind.Wait,
			"renew6" or "dhcpv6-renew" => NetworkRecoveryActionKind.DhcpV6Renew,
			"restart" or "restart-interface" => NetworkRecoveryActionKind.RestartInterface,
			"snapshot" => NetworkRecoveryActionKind.CaptureAddressSnapshot,
			_ => NetworkRecoveryActionKind.Unspecified,
		};
		var waitMs = action == NetworkRecoveryActionKind.Wait ? GetInt(options, "wait-ms", 1000) : 0;
		request = new NetworkRecoveryActionRequest(Guid.NewGuid(), action, networkInterface, waitMs);
		return request.IsValid;
	}

	private static bool TryCreateRecoveryInterface(
		IReadOnlyDictionary<string, string> options,
		out NetworkInterfaceIdentity networkInterface)
	{
		networkInterface = default;
		if (!options.TryGetValue("interface-index", out var indexText) ||
			!uint.TryParse(indexText, out var index) || index == 0 ||
			!options.TryGetValue("interface-alias", out var alias) || string.IsNullOrWhiteSpace(alias) ||
			!TryInterfaceLuid(options, out var luid)) return false;
		networkInterface = new NetworkInterfaceIdentity(Guid.Empty, luid, alias.Trim(), index);
		return networkInterface.IsValid;
	}

	private static Dictionary<string, string> ParseOptions(ReadOnlySpan<string> args)
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		for (var index = 0; index < args.Length; index++)
		{
			var item = args[index];
			if (!item.StartsWith("--", StringComparison.Ordinal)) continue;
			var key = item[2..];
			var value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
				? args[++index] : "true";
			result[key] = value;
		}
		return result;
	}

	private static bool TryAddress(IReadOnlyDictionary<string, string> values, string key, out IPAddress address)
	{
		address = IPAddress.None;
		if (!values.TryGetValue(key, out var text) || !IPAddress.TryParse(text, out var parsed) || parsed is null)
			return false;
		address = parsed;
		return true;
	}
	private static bool TryGuid(IReadOnlyDictionary<string, string> values, string key, out Guid value)
	{
		value = Guid.Empty;
		return values.TryGetValue(key, out var text) && Guid.TryParse(text, out value);
	}
	private static bool TryUlong(IReadOnlyDictionary<string, string> values, string key, out ulong value)
	{
		value = 0;
		return values.TryGetValue(key, out var text) && ulong.TryParse(text, out value) && value > 0;
	}
	private static bool TryUshort(IReadOnlyDictionary<string, string> values, string key, out ushort value)
	{
		value = 0;
		return values.TryGetValue(key, out var text) && ushort.TryParse(text, out value) && value > 0;
	}
	private static bool TryInterfaceLuid(IReadOnlyDictionary<string, string> values, out ulong luid)
	{
		if (TryUlong(values, "interface-luid", out luid)) return true;
		luid = 0;
		if (!values.TryGetValue("interface-index", out var text) ||
			!uint.TryParse(text, out var index) || index == 0 || !OperatingSystem.IsWindows())
			return false;
		return ConvertInterfaceIndexToLuid(index, out luid) == 0 && luid != 0;
	}
	private static int GetInt(IReadOnlyDictionary<string, string> values, string key, int fallback) =>
		values.TryGetValue(key, out var text) && int.TryParse(text, out var value) && value > 0 ? value : fallback;

	private static void Write<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value));

	private static int Usage()
	{
		Console.Error.WriteLine("Usage: capability | check --policy-id <guid> | run|crash --confirm-system-mutation --source <ip> (--interface-index <uint32>|--interface-luid <uint64>) --next-hop <ip> --target <ip> --target-port <port> [--local-port <port>] [--protocol tcp|udp] [--policy-id <guid>] [--timeout-ms <ms>] [--hold-ms <ms>] [--diagnostic-app-only [--diagnostic-max-weight] [--diagnostic-policy-before-bind]] | recovery-capability --interface-index <uint32> --interface-alias <name> | recovery --confirm-system-mutation --action <rs|release6|wait|renew6|restart|snapshot> --interface-index <uint32> --interface-alias <name> [--wait-ms <ms>] [--timeout-ms <ms>]");
		return 64;
	}

	[DllImport("iphlpapi.dll", ExactSpelling = true)]
	private static extern uint ConvertInterfaceIndexToLuid(uint interfaceIndex, out ulong interfaceLuid);
}
