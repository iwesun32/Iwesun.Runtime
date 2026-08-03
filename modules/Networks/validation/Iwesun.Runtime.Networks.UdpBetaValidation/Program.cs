using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Iwesun.Runtime.Networks;

var settings = ValidationSettings.Parse(args);
var address = settings.UseIpv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
using var server = new UdpClient(new IPEndPoint(address, 0));
var remotePort = checked((ushort)((IPEndPoint)server.Client.LocalEndPoint!).Port);
var environment = CreateEnvironment(address);
using var dataPlane = new SystemSocketDatagramDataPlane(
	environment.Executor,
	TimeSpan.FromMilliseconds(settings.QuarantineMs),
	settings.MaxSocketsPerPool,
	maxPoolCount: 1);
var latencies = new ConcurrentBag<double>();
var succeeded = 0L;
var rejected = 0L;
var timedOut = 0L;
var cancelledAtEnd = 0L;
var failed = 0L;
using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(settings.DurationSeconds));
var serverTask = RunEchoServerAsync(server, settings.ResponseDelayMs, lifetime.Token);
var allocatedBefore = GC.GetTotalAllocatedBytes(false);
var started = Stopwatch.GetTimestamp();

var workers = Enumerable.Range(0, settings.Concurrency).Select(async worker =>
{
	while (!lifetime.IsCancellationRequested)
	{
		var identity = NetworkExecutionIdentity.ForRequest(Guid.NewGuid())
			.StartAttempt(Guid.NewGuid())
			.StartBranch(Guid.NewGuid());
		var request = new NetworkSocketExecutionRequest(
			identity,
			environment.Plan,
			environment.Resolved,
			NetworkAccessSecurityBoundary.NotApplicable(),
			NetworkSocketOperation.UdpExchange,
			remotePort,
			[checked((byte)(worker & 0xff))],
			64);
		using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
		timeout.CancelAfter(settings.RequestTimeoutMs);
		var requestStarted = Stopwatch.GetTimestamp();
		var result = await dataPlane.ExecuteDatagramAsync(request, timeout.Token).ConfigureAwait(false);
		var elapsedMs = Stopwatch.GetElapsedTime(requestStarted).TotalMilliseconds;
		switch (result.Outcome)
		{
			case ProtocolOutcome.Succeeded:
				Interlocked.Increment(ref succeeded);
				latencies.Add(elapsedMs);
				break;
			case ProtocolOutcome.Rejected:
				Interlocked.Increment(ref rejected);
				await Task.Delay(1, CancellationToken.None).ConfigureAwait(false);
				break;
			case ProtocolOutcome.TimedOut:
				Interlocked.Increment(ref timedOut);
				break;
			case ProtocolOutcome.Cancelled:
				Interlocked.Increment(ref cancelledAtEnd);
				break;
			default:
				Interlocked.Increment(ref failed);
				break;
		}
	}
}).ToArray();

await Task.WhenAll(workers).ConfigureAwait(false);
server.Dispose();
try { await serverTask.ConfigureAwait(false); }
catch (OperationCanceledException) { }
catch (ObjectDisposedException) { }
catch (SocketException) when (lifetime.IsCancellationRequested) { }

var elapsed = Stopwatch.GetElapsedTime(started);
var counters = dataPlane.Counters;
var samples = latencies.Order().ToArray();
var total = succeeded + rejected + timedOut + cancelledAtEnd + failed;
var suggested = counters.CapacityRejections == 0
	? Math.Max(1, (int)Math.Ceiling(counters.PeakSlotsPerPool * 1.25))
	: Math.Max(settings.MaxSocketsPerPool + 1, settings.MaxSocketsPerPool * 2);
var report = new ValidationReport(
	DateTimeOffset.UtcNow,
	settings,
	elapsed.TotalSeconds,
	total,
	succeeded,
	rejected,
	timedOut,
	cancelledAtEnd,
	failed,
	succeeded / Math.Max(0.001, elapsed.TotalSeconds),
	Percentile(samples, 0.50),
	Percentile(samples, 0.95),
	Percentile(samples, 0.99),
	GC.GetTotalAllocatedBytes(false) - allocatedBefore,
	counters,
	suggested,
	counters.CapacityRejections == 0 ? "capacity-observed-without-rejection" : "increase-capacity-and-rerun");
var output = Console.OpenStandardOutput();
await JsonSerializer.SerializeAsync(output, report, new JsonSerializerOptions { WriteIndented = true });
await output.WriteAsync("\n"u8.ToArray());

static async Task RunEchoServerAsync(UdpClient server, int delayMs, CancellationToken cancellationToken)
{
	while (!cancellationToken.IsCancellationRequested)
	{
		var received = await server.ReceiveAsync(cancellationToken).ConfigureAwait(false);
		if (delayMs > 0) await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
		await server.SendAsync(received.Buffer, received.Buffer.Length, received.RemoteEndPoint).ConfigureAwait(false);
	}
}

static ValidationEnvironment CreateEnvironment(IPAddress address)
{
	var binary = IpAddressValue.FromIPAddress(address);
	var interfaceIndex = FindInterfaceIndex(address);
	var identity = new NetworkInterfaceIdentity(Guid.NewGuid(), interfaceIndex, "udp-beta-loopback", interfaceIndex);
	var interfaces = new NetworkInterfaceSnapshotProvider();
	if (!interfaces.TryReplace([new NetworkInterfaceSnapshot(identity, default, true)], out _, out var interfaceReason))
		throw new InvalidOperationException(interfaceReason);
	var routes = new NetworkRouteSnapshotProvider();
	if (!routes.TryReplace(binary, binary, identity, default, 0, out _, out var routeReason))
		throw new InvalidOperationException(routeReason);
	var plan = new RequestedAccessPlan(
		NetworkPathProvider.Automatic,
		NetworkDestinationSelection.Exact(binary),
		NetworkSourceSelection.SystemSelected(),
		NetworkInterfaceSelection.SystemSelected(),
		NetworkNextHopSelection.SystemSelected(),
		NetworkLocalEndpointSelection.Ephemeral(),
		NetworkRouteScopeSelection.Current(),
		NetworkIpPacketPolicy.SystemDefault(),
		NetworkRouteAdapterIdentity.NotApplicable());
	var resolver = new NetworkAccessConstraintResolver(
		new NetworkAccessPlanNormalizer(new NetworkInterfaceIdentityResolver()), interfaces, routes);
	if (!resolver.TryResolve(plan, NetworkAccessSecurityBoundary.NotApplicable(), out var resolved, out var reason))
		throw new InvalidOperationException(reason);
	return new ValidationEnvironment(plan, resolved, new NetworkSocketExecutor(resolver));
}

static uint FindInterfaceIndex(IPAddress address)
{
	foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
	{
		var properties = item.GetIPProperties();
		if (!properties.UnicastAddresses.Any(candidate => candidate.Address.Equals(address))) continue;
		var index = address.AddressFamily == AddressFamily.InterNetwork
			? properties.GetIPv4Properties()?.Index
			: properties.GetIPv6Properties()?.Index;
		if (index is > 0) return checked((uint)index.Value);
	}
	throw new InvalidOperationException("loopback-interface-index-not-found");
}

static double Percentile(double[] sorted, double percentile) => sorted.Length == 0
	? 0
	: sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * percentile) - 1, 0, sorted.Length - 1)];

internal sealed record ValidationSettings(
	int DurationSeconds,
	int Concurrency,
	int ResponseDelayMs,
	int QuarantineMs,
	int MaxSocketsPerPool,
	int RequestTimeoutMs,
	bool UseIpv6)
{
	public static ValidationSettings Parse(string[] args)
	{
		var values = args.Select(item => item.Split('=', 2))
			.Where(item => item.Length == 2)
			.ToDictionary(item => item[0].TrimStart('-'), item => item[1], StringComparer.OrdinalIgnoreCase);
		int Read(string key, int fallback, int minimum) => values.TryGetValue(key, out var value) &&
			int.TryParse(value, out var parsed) && parsed >= minimum ? parsed : fallback;
		return new ValidationSettings(
			Read("duration", 10, 1), Read("concurrency", 16, 1), Read("delay", 5, 0),
			Read("quarantine", 1000, 0), Read("max-slots", 64, 1), Read("timeout", 3000, 1),
			values.TryGetValue("ipv6", out var ipv6) && bool.TryParse(ipv6, out var enabled) && enabled);
	}
}

internal sealed record ValidationEnvironment(
	RequestedAccessPlan Plan,
	ResolvedAccessPlan Resolved,
	NetworkSocketExecutor Executor);

internal sealed record ValidationReport(
	DateTimeOffset CompletedAt,
	ValidationSettings Settings,
	double ElapsedSeconds,
	long TotalRequests,
	long Succeeded,
	long Rejected,
	long TimedOut,
	long CancelledAtEnd,
	long Failed,
	double SuccessfulRequestsPerSecond,
	double LatencyP50Ms,
	double LatencyP95Ms,
	double LatencyP99Ms,
	long AllocatedBytes,
	NetworkDatagramDataPlaneCounters Counters,
	int SuggestedNextCapacity,
	string Recommendation);
