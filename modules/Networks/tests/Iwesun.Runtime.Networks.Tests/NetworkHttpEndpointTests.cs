using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkHttpEndpointTests
{
	[Fact]
	public async Task ExactNextHopNoReuseOwnsPolicyForTheHttpConnection()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port);
		var backend = new RecordingWfpBackend();
		var environment = CreateEnvironment(exactNextHop: true, backend);
		using var endpoint = new NetworkHttpGetEndpoint<string>(environment.Context, maxAttemptCount: 1);
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri($"http://logical.test:{port}/probe"), null, null, null, 1,
			NetworkHttpConnectionReusePolicy.NoReuseRequestPolicy, out var protocol, out var reason), reason);
		var hosts = new List<string>();
		var server = ServeRequestsAsync(listener, hosts, 1);
		var request = new NetworkHttpGetRequest<string>(
			Guid.NewGuid(), "exact-next-hop", $"http://logical.test:{port}/probe", protocol,
			environment.Plan, "manual-resolver", "text/plain", null, 1500, false);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		await server.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => backend.DisposeCount == 1);

		Assert.True(completion.Response.Result.IsSuccess, completion.Response.Result.ReasonCode);
		Assert.Equal(ProofKind.PolicyEnforced,
			completion.Response.Result.PathEvidence.ClientLeg.Evidence.NextHop.Proof);
		Assert.Equal(AccessCompliance.Satisfied, completion.Response.Result.Compliance);
		Assert.Single(backend.Acquired);
		Assert.Equal(0, endpoint.ConnectionPoolCount);
	}

	[Fact]
	public async Task ExactNextHopRejectsReusableHttpPolicyBeforeConnecting()
	{
		var backend = new RecordingWfpBackend();
		var environment = CreateEnvironment(exactNextHop: true, backend);
		using var endpoint = new NetworkHttpGetEndpoint<string>(environment.Context, maxAttemptCount: 1);
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri("http://logical.test:8080/probe"), null, null, null, 1,
			NetworkHttpConnectionReusePolicy.Reusable, out var protocol, out var reason), reason);
		var failed = new TaskCompletionSource<TrackedRequestFailure<NetworkHttpGetRequest<string>, string>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);
		var request = new NetworkHttpGetRequest<string>(
			Guid.NewGuid(), "reuse-rejected", "http://logical.test:8080/probe", protocol,
			environment.Plan, "manual-resolver", "text/plain", null, 1000, false);

		Assert.True(endpoint.TrySend(request));
		var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(NetworkAccessFailureCodes.WfpPolicyConnectionReuseUnsupported, failure.Failure.Reason);
		Assert.Empty(backend.Acquired);
	}

	[Fact]
	public async Task FixedTransportIpPreservesLogicalHostAndReusesStablePool()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port);
		var environment = CreateEnvironment();
		using var endpoint = new NetworkHttpGetEndpoint<string>(environment.Context, maxAttemptCount: 1);
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri($"http://logical.test:{port}/probe"), null, null, null, 1,
			NetworkHttpConnectionReusePolicy.Reusable, out var protocol, out var reason), reason);
		var hosts = new List<string>();
		var server = ServeRequestsAsync(listener, hosts, 2);

		for (var index = 0; index < 2; index++)
		{
			var request = new NetworkHttpGetRequest<string>(
				Guid.NewGuid(), $"key-{index}", $"http://logical.test:{port}/probe", protocol,
				environment.Plan, "manual-resolver", "text/plain", null, 1500, false);
			Assert.True(endpoint.TrySend(request));
			await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
			Assert.True(endpoint.TryReadReceived(out var completion));
			Assert.Equal("ok", Encoding.ASCII.GetString(completion.Response.Result.Body));
			Assert.Equal(AccessCompliance.Satisfied, completion.Response.Result.Compliance);
			Assert.Equal(ProofKind.Observed,
				completion.Response.Result.PathEvidence.ClientLeg.Evidence.Destination.Proof);
		}
		await server.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(2, hosts.Count);
		Assert.All(hosts, host => Assert.Equal($"logical.test:{port}", host));
		Assert.Equal(1, endpoint.ConnectionPoolCount);
	}

	[Fact]
	public async Task IdleConnectionPoolEntryIsRetiredWithoutFurtherTraffic()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port);
		var environment = CreateEnvironment();
		using var endpoint = new NetworkHttpGetEndpoint<string>(
			environment.Context,
			maxAttemptCount: 1,
			idleConnectionRetention: TimeSpan.FromMilliseconds(50),
			connectionPoolSweepInterval: 1);
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri($"http://logical.test:{port}/probe"), null, null, null, 1,
			NetworkHttpConnectionReusePolicy.Reusable, out var protocol, out var reason), reason);
		var hosts = new List<string>();
		var server = ServeRequestsAsync(listener, hosts, 1);
		var request = new NetworkHttpGetRequest<string>(
			Guid.NewGuid(), "idle-retirement", $"http://logical.test:{port}/probe", protocol,
			environment.Plan, "manual-resolver", "text/plain", null, 1500, false);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		Assert.True(completion.Response.Result.IsSuccess, completion.Response.Result.ReasonCode);
		await server.WaitAsync(TimeSpan.FromSeconds(3));
		Assert.Equal(1, endpoint.ConnectionPoolCount);
		await WaitUntilAsync(() => endpoint.ConnectionPoolCount == 0);
	}

	[Fact]
	public void ConnectionPoolConfigurationRejectsInvalidBounds()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new NetworkHttpGetEndpoint<string>(idleConnectionRetention: TimeSpan.Zero));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new NetworkHttpGetEndpoint<string>(maxConnectionPoolCount: 0));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new NetworkDohEndpoint<string>(connectionPoolSweepInterval: 0));
	}

	[Fact]
	public async Task HttpConnectKeepsClientAndEgressEvidenceSeparate()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var proxyPort = checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port);
		var backend = new RecordingWfpBackend();
		var environment = CreateEnvironment(exactNextHop: true, backend);
		var registry = new NetworkRouteAdapterRegistry();
		var adapter = new HttpConnectNetworkRouteAdapter(
			"proxy-a", 1, new Uri($"http://proxy.test:{proxyPort}"),
			environment.Plan, environment.Context);
		Assert.True(registry.TryRegister(adapter));
		using var endpoint = new NetworkHttpGetEndpoint<string>(
			environment.Context, registry, maxAttemptCount: 1);
		const ushort targetPort = 8080;
		var target = IpAddressValue.Parse("203.0.113.20");
		var routePlan = new RequestedAccessPlan(
			NetworkPathProvider.RouteAdapter,
			NetworkDestinationSelection.Exact(target),
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.SystemSelected(),
			NetworkLocalEndpointSelection.Ephemeral(),
			NetworkRouteScopeSelection.Current(),
			NetworkIpPacketPolicy.SystemDefault(),
			NetworkRouteAdapterIdentity.Exact("proxy-a", 1));
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri($"http://logical.test:{targetPort}/probe"), null, null, null, 1,
			NetworkHttpConnectionReusePolicy.NoReuseRequestPolicy, out var protocol, out var reason), reason);
		var observation = new ProxyObservation();
		var server = ServeProxyTunnelAsync(listener, observation);
		var request = new NetworkHttpGetRequest<string>(
			Guid.NewGuid(), "proxy", $"http://logical.test:{targetPort}/probe", protocol,
			routePlan, "manual-resolver", "text/plain", null, 2000, false);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		await server.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => backend.DisposeCount == 1);

		Assert.True(completion.Response.Result.IsSuccess, completion.Response.Result.ReasonCode);
		Assert.Equal($"203.0.113.20:{targetPort}", observation.ConnectAuthority);
		Assert.Equal($"logical.test:{targetPort}", observation.HostHeader);
		var evidence = completion.Response.Result.PathEvidence;
		Assert.Equal(NetworkAccessLeg.ClientLeg, evidence.ClientLeg.Leg);
		Assert.Equal(IpAddressValue.FromIPAddress(IPAddress.Loopback), evidence.ClientLeg.Evidence.Destination.Value);
		Assert.Equal(ProofKind.Observed, evidence.ClientLeg.Evidence.Destination.Proof);
		Assert.Equal(ProofKind.PolicyEnforced, evidence.ClientLeg.Evidence.NextHop.Proof);
		Assert.Equal(NetworkAccessLeg.EgressLeg, evidence.EgressLeg.Leg);
		Assert.Equal(target, evidence.EgressLeg.Evidence.Destination.Value);
		Assert.Equal(ProofKind.AdapterReported, evidence.EgressLeg.Evidence.Destination.Proof);
	}

	[Fact]
	public async Task SystemProxyReportsOnlyObservedClientLeg()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var proxyPort = checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port);
		var environment = CreateEnvironment();
		var proxy = new WebProxy(new Uri($"http://127.0.0.1:{proxyPort}"), false);
		using var endpoint = new NetworkHttpGetEndpoint<string>(
			environment.Context, systemProxy: proxy, maxAttemptCount: 1);
		const ushort targetPort = 8080;
		var target = IpAddressValue.Parse("203.0.113.20");
		var plan = new RequestedAccessPlan(
			NetworkPathProvider.SystemProxy,
			NetworkDestinationSelection.Exact(target),
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.SystemSelected(),
			NetworkNextHopSelection.SystemSelected(),
			NetworkLocalEndpointSelection.Ephemeral(),
			NetworkRouteScopeSelection.Current(),
			NetworkIpPacketPolicy.SystemDefault(),
			NetworkRouteAdapterIdentity.NotApplicable());
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri($"http://logical.test:{targetPort}/probe"), null, null, null, 1,
			NetworkHttpConnectionReusePolicy.Reusable, out var protocol, out var reason), reason);
		string? requestLine = null;
		var server = Task.Run(async () =>
		{
			using var socket = await listener.AcceptSocketAsync();
			var header = await ReadHeaderAsync(socket);
			requestLine = header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[0];
			await socket.SendAsync(Encoding.ASCII.GetBytes(
				"HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"));
		});
		var request = new NetworkHttpGetRequest<string>(
			Guid.NewGuid(), "system-proxy", $"http://logical.test:{targetPort}/probe", protocol,
			plan, "manual-resolver", "text/plain", null, 2000, false);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		await server.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(completion.Response.Result.IsSuccess, completion.Response.Result.ReasonCode);
		Assert.StartsWith($"GET http://logical.test:{targetPort}/probe ", requestLine);
		var evidence = completion.Response.Result.PathEvidence;
		Assert.Equal(NetworkAccessLeg.ClientLeg, evidence.ClientLeg.Leg);
		Assert.Equal(IpAddressValue.FromIPAddress(IPAddress.Loopback), evidence.ClientLeg.Evidence.Destination.Value);
		Assert.Equal(ProofKind.Observed, evidence.ClientLeg.Evidence.Destination.Proof);
		Assert.Equal(NetworkAccessLeg.EgressLeg, evidence.EgressLeg.Leg);
		Assert.Equal(ProofKind.Unavailable, evidence.EgressLeg.Evidence.Destination.Proof);
		Assert.Equal(AccessCompliance.NotApplicable, evidence.EgressLeg.Evidence.Destination.Compliance);
	}

	[Fact]
	public async Task InvalidTlsPeerPreservesTlsFailureReason()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port);
		var environment = CreateEnvironment();
		using var endpoint = new NetworkHttpGetEndpoint<string>(environment.Context, maxAttemptCount: 1);
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri($"https://logical.test:{port}/probe"), null, null, null, 1,
			NetworkHttpConnectionReusePolicy.NoReuseRequestPolicy, out var protocol, out var reason), reason);
		var failed = new TaskCompletionSource<TrackedRequestFailure<NetworkHttpGetRequest<string>, string>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);
		var server = Task.Run(async () =>
		{
			using var socket = await listener.AcceptSocketAsync();
			var buffer = new byte[256];
			_ = await socket.ReceiveAsync(buffer);
			await socket.SendAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\n\r\n"u8.ToArray());
		});
		var request = new NetworkHttpGetRequest<string>(
			Guid.NewGuid(), "invalid-tls", $"https://logical.test:{port}/probe", protocol,
			environment.Plan, "manual-resolver", "application/dns-message", null, 1500, false);

		Assert.True(endpoint.TrySend(request));
		var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
		await server.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(NetworkFailureKind.Transport, failure.Failure.Kind);
		Assert.Equal("http-tls-failed", failure.Failure.Reason);
	}

	private static TestEnvironment CreateEnvironment(
		bool exactNextHop = false,
		INetworkWfpConnectionPolicyBackend? wfpBackend = null)
	{
		var address = IpAddressValue.FromIPAddress(IPAddress.Loopback);
		var index = FindInterfaceIndex();
		var identity = new NetworkInterfaceIdentity(Guid.NewGuid(), index, "http-test", index);
		var interfaces = new NetworkInterfaceSnapshotProvider();
		Assert.True(interfaces.TryReplace([new NetworkInterfaceSnapshot(identity, default, true)], out _, out _));
		var routes = new NetworkRouteSnapshotProvider();
		Assert.True(routes.TryReplace(address, address, identity, exactNextHop ? address : default, 0, out _, out _));
		var context = new NetworkAccessExecutionContext(
			interfaces, routes, refreshWindowsSnapshots: false, wfpBackend);
		var plan = new RequestedAccessPlan(
			NetworkPathProvider.Direct,
			NetworkDestinationSelection.Exact(address),
			NetworkSourceSelection.Exact(address),
			NetworkInterfaceSelection.SystemSelected(),
			exactNextHop ? NetworkNextHopSelection.Exact(address) : NetworkNextHopSelection.SystemSelected(),
			NetworkLocalEndpointSelection.Ephemeral(),
			NetworkRouteScopeSelection.Current(),
			NetworkIpPacketPolicy.SystemDefault(),
			NetworkRouteAdapterIdentity.NotApplicable());
		return new TestEnvironment(context, plan);
	}

	private static async Task ServeRequestsAsync(TcpListener listener, List<string> hosts, int count)
	{
		for (var index = 0; index < count; index++)
		{
			using var socket = await listener.AcceptSocketAsync();
			var request = await ReadHeaderAsync(socket);
			var host = request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
				.First(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))[5..].Trim();
			hosts.Add(host);
			var response = Encoding.ASCII.GetBytes(
				"HTTP/1.1 200 OK\r\nContent-Length: 2\r\nContent-Type: text/plain\r\nConnection: close\r\n\r\nok");
			await socket.SendAsync(response);
		}
	}

	private static async Task ServeProxyTunnelAsync(TcpListener listener, ProxyObservation observation)
	{
		using var socket = await listener.AcceptSocketAsync();
		var connect = await ReadHeaderAsync(socket);
		observation.ConnectAuthority = connect.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[0]
			.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
		await socket.SendAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray());
		var request = await ReadHeaderAsync(socket);
		observation.HostHeader = request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
			.First(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))[5..].Trim();
		var response = Encoding.ASCII.GetBytes(
			"HTTP/1.1 200 OK\r\nContent-Length: 2\r\nContent-Type: text/plain\r\nConnection: close\r\n\r\nok");
		await socket.SendAsync(response);
	}

	private static async Task<string> ReadHeaderAsync(Socket socket)
	{
		var buffer = new byte[8192];
		var length = 0;
		while (length < buffer.Length)
		{
			var count = await socket.ReceiveAsync(buffer.AsMemory(length, 1));
			if (count == 0) throw new EndOfStreamException();
			length += count;
			if (length >= 4 && buffer.AsSpan(length - 4, 4).SequenceEqual("\r\n\r\n"u8))
				return Encoding.ASCII.GetString(buffer, 0, length);
		}
		throw new InvalidDataException("header-too-long");
	}

	private static uint FindInterfaceIndex()
	{
		foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
		{
			var properties = item.GetIPProperties();
			if (!properties.UnicastAddresses.Any(entry => entry.Address.Equals(IPAddress.Loopback))) continue;
			var index = properties.GetIPv4Properties()?.Index;
			if (index is > 0) return checked((uint)index.Value);
		}
		throw new InvalidOperationException("loopback-interface-not-found");
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(3);
		while (!condition())
		{
			if (DateTime.UtcNow >= deadline) throw new TimeoutException("test-condition-timeout");
			await Task.Delay(10);
		}
	}

	private readonly record struct TestEnvironment(NetworkAccessExecutionContext Context, RequestedAccessPlan Plan);

	private sealed class ProxyObservation
	{
		public string? ConnectAuthority { get; set; }
		public string? HostHeader { get; set; }
	}

	private sealed class RecordingWfpBackend : INetworkWfpConnectionPolicyBackend
	{
		public List<NetworkWfpConnectionPolicyRequest> Acquired { get; } = [];
		public int DisposeCount { get; private set; }
		public NetworkWfpPolicyCapability QueryCapability() => new(true, false, string.Empty, default);
		public NetworkWfpPolicyAcquireResult Acquire(in NetworkWfpConnectionPolicyRequest request)
		{
			Acquired.Add(request);
			return new(new Lease(request, () => DisposeCount++), string.Empty, default);
		}

		private sealed class Lease(NetworkWfpConnectionPolicyRequest request, Action disposed)
			: INetworkWfpConnectionPolicyLease
		{
			private int _disposed;
			public Guid PolicyId => request.PolicyId;
			public NetworkWfpConnectionPolicyRequest Request => request;
			public void Dispose()
			{
				if (Interlocked.Exchange(ref _disposed, 1) == 0) disposed();
			}
		}
	}
}
