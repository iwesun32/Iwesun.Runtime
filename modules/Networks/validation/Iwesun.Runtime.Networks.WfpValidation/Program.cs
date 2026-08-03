using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using Iwesun.Runtime.Networks;

return await WfpValidationProgram.RunAsync(args).ConfigureAwait(false);

internal static class WfpValidationProgram
{
	private const string Confirmation = "--confirm-system-mutation";

	public static async Task<int> RunAsync(string[] args)
	{
		if (args.Length == 0) return Usage();
		var mode = args[0].Trim().ToLowerInvariant();
		var options = ParseOptions(args.AsSpan(1));

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
		if (!TryCreateRequest(options, out var request, out var socket, out var target))
			return Usage();

		using (socket)
		{
			var capability = backend.QueryCapability();
			if (!capability.Supported)
			{
				Write(new { mode, capability.Supported, capability.ReasonCode, capability.PlatformError });
				return 2;
			}

			var acquired = backend.Acquire(request);
			if (!acquired.Succeeded)
			{
				Write(new { mode, acquired.ReasonCode, acquired.PlatformError });
				return 5;
			}

			if (mode == "crash")
			{
				Write(new { mode, request.PolicyId, acquired = true, processId = Environment.ProcessId });
				Console.Out.Flush();
				Environment.FailFast("Intentional WFP dynamic-session crash-cleanup validation.");
			}

			using (acquired.Lease)
			using (var timeout = new CancellationTokenSource(GetInt(options, "timeout-ms", 5000)))
			{
				await socket.ConnectAsync(target, timeout.Token).ConfigureAwait(false);
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
					localEndpoint = socket.LocalEndPoint?.ToString(),
					remoteEndpoint = socket.RemoteEndPoint?.ToString(),
					proof = ProofKind.PolicyEnforced,
				});
			}

			var cleanupQueried = backend.TryPolicyExists(
				request.PolicyId, out var remains, out var cleanupError);
			Write(new { mode = "cleanup", request.PolicyId, cleanupQueried, remains, cleanupError });
			return cleanupQueried && !remains ? 0 : 6;
		}
	}

	private static bool TryCreateRequest(
		IReadOnlyDictionary<string, string> options,
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
		socket.Bind(new IPEndPoint(source, localPort));
		var actualLocalPort = checked((ushort)((IPEndPoint)socket.LocalEndPoint!).Port);
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
		Console.Error.WriteLine("Usage: capability | check --policy-id <guid> | run|crash --confirm-system-mutation --source <ip> (--interface-index <uint32>|--interface-luid <uint64>) --next-hop <ip> --target <ip> --target-port <port> [--local-port <port>] [--protocol tcp|udp] [--policy-id <guid>] [--timeout-ms <ms>] [--hold-ms <ms>] | recovery-capability --interface-index <uint32> --interface-alias <name> | recovery --confirm-system-mutation --action <rs|release6|wait|renew6|restart|snapshot> --interface-index <uint32> --interface-alias <name> [--wait-ms <ms>] [--timeout-ms <ms>]");
		return 64;
	}

	[DllImport("iphlpapi.dll", ExactSpelling = true)]
	private static extern uint ConvertInterfaceIndexToLuid(uint interfaceIndex, out ulong interfaceLuid);
}
