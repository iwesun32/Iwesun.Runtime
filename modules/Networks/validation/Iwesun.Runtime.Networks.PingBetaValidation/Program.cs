using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Iwesun.Runtime.Networks;

var requestedIndex = ParseInterfaceIndex(args);
var targetAddress = ParseTarget(args);
var adapter = SelectAdapter(requestedIndex);
var properties = adapter.GetIPProperties();
var ipv6 = properties.GetIPv6Properties() ?? throw new InvalidOperationException("ipv6-properties-unavailable");
var interfaceIndex = checked((uint)ipv6.Index);
var source = properties.UnicastAddresses
	.Select(item => item.Address)
	.FirstOrDefault(item => item.AddressFamily == AddressFamily.InterNetworkV6 && !item.IsIPv6Multicast) ??
	throw new InvalidOperationException("ipv6-source-unavailable");
var interfaceGuid = Guid.TryParse(adapter.Id.Trim('{', '}'), out var parsedGuid) ? parsedGuid : Guid.NewGuid();
var identity = new NetworkInterfaceIdentity(interfaceGuid, 0, adapter.Name, interfaceIndex);
var destination = IpAddressValue.FromIPAddress(targetAddress);
var sourceBinary = IpAddressValue.FromIPAddress(source);
if (!NetworkAddressSet.TryCreate([sourceBinary], out var unicastAddresses, out var addressReason))
	throw new InvalidOperationException(addressReason);
var interfaces = new NetworkInterfaceSnapshotProvider();
if (!interfaces.TryReplace([new NetworkInterfaceSnapshot(identity, unicastAddresses, true)], out _, out var interfaceReason))
	throw new InvalidOperationException(interfaceReason);
var routes = new NetworkRouteSnapshotProvider();
if (!routes.TryReplace(destination, sourceBinary, identity, default, 0, out _, out var routeReason))
	throw new InvalidOperationException(routeReason);
var context = new NetworkAccessExecutionContext(interfaces, routes, refreshWindowsSnapshots: false);
var plan = new RequestedAccessPlan(
	NetworkPathProvider.Direct,
	NetworkDestinationSelection.Exact(destination),
	NetworkSourceSelection.Exact(sourceBinary),
	NetworkInterfaceSelection.Exact(identity),
	NetworkNextHopSelection.SystemSelected(),
	NetworkLocalEndpointSelection.NotApplicable(),
	NetworkRouteScopeSelection.Current(),
	NetworkIpPacketPolicy.SystemDefault(),
	NetworkRouteAdapterIdentity.NotApplicable());
using var endpoint = new NetworkPingEndpoint<string>(context, maxAttemptCount: 1, defaultTimeoutMs: 2000);
var requestId = Guid.NewGuid();
var responses = new List<NetworkPingExecutionResult>();
var completion = new TaskCompletionSource<NetworkRequestTerminal>(TaskCreationOptions.RunContinuationsAsynchronously);
var failureCompletion = new TaskCompletionSource<NetworkFailure>(TaskCreationOptions.RunContinuationsAsynchronously);
endpoint.ResponseObserved += (_, args) => responses.Add(args.Response.Result);
endpoint.RequestFailed += (_, args) => failureCompletion.TrySetResult(args.Failure.Failure);
endpoint.RequestTerminated += (_, args) => completion.TrySetResult(args.Terminal);
var request = new NetworkPingRequest<string>(
	requestId,
	targetAddress.ToString(),
	plan,
	NetworkAccessSecurityBoundary.NotApplicable(),
	[1, 2, 3, 4],
	2000,
	false,
	targetAddress.IsIPv6Multicast
		? TrackedResponseCollectionPolicy.CollectUntilWindowEnds(1500, 64)
		: TrackedResponseCollectionPolicy.FirstValidResponse());
if (!endpoint.TrySend(request)) throw new InvalidOperationException("ping-request-not-accepted");
var terminal = await completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
var failure = terminal.State == NetworkExecutionTerminalState.Succeeded
	? NetworkFailure.None
	: await failureCompletion.Task.WaitAsync(TimeSpan.FromSeconds(1));
var report = new
{
	RequestId = requestId,
	Interface = new { adapter.Name, adapter.Id, InterfaceIndex = interfaceIndex, Source = source.ToString() },
	Target = targetAddress.IsIPv6Multicast ? $"{targetAddress}%{interfaceIndex}" : targetAddress.ToString(),
	terminal.State,
	terminal.ProtocolOutcome,
	terminal.AccessCompliance,
	Failure = failure,
	ResponseCount = responses.Count,
	Responses = responses.Select(item => new
	{
		item.Outcome,
		item.Compliance,
		Destination = item.Evidence.Destination.Value.ToIPAddress().ToString(),
		DestinationProof = item.Evidence.Destination.Proof,
		InterfaceProof = item.Evidence.Interface.Proof,
		Responder = item.ResponderAddress == default ? "" : item.ResponderAddress.ToIPAddress().ToString(),
	}),
};
var output = Console.OpenStandardOutput();
await JsonSerializer.SerializeAsync(output, report, new JsonSerializerOptions { WriteIndented = true });
await output.WriteAsync("\n"u8.ToArray());

static uint ParseInterfaceIndex(string[] arguments)
{
	foreach (var argument in arguments)
		if (argument.StartsWith("--interface-index=", StringComparison.OrdinalIgnoreCase) &&
			uint.TryParse(argument[18..], out var value)) return value;
	return 0;
}

static IPAddress ParseTarget(string[] arguments)
{
	foreach (var argument in arguments)
		if (argument.StartsWith("--target=", StringComparison.OrdinalIgnoreCase) &&
			IPAddress.TryParse(argument[9..], out var value) &&
			value.AddressFamily == AddressFamily.InterNetworkV6) return value;
	return IPAddress.Parse("ff02::1");
}

static NetworkInterface SelectAdapter(uint requestedIndex)
{
	foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
	{
		if (adapter.OperationalStatus != OperationalStatus.Up || !adapter.SupportsMulticast) continue;
		try
		{
			var properties = adapter.GetIPProperties();
			var ipv6 = properties.GetIPv6Properties();
			if (ipv6 is null || (requestedIndex != 0 && ipv6.Index != requestedIndex)) continue;
			if (properties.UnicastAddresses.Any(item => item.Address.AddressFamily == AddressFamily.InterNetworkV6))
				return adapter;
		}
		catch (NetworkInformationException)
		{
		}
	}
	throw new InvalidOperationException("eligible-ipv6-interface-not-found");
}
