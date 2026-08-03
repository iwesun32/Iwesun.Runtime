using System.Buffers.Binary;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkDnsReverseLookupEndpointTests
{
	[Fact]
	public async Task PtrUdpSeparatesQueryObjectFromResolverPath()
	{
		using var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
		var port = checked((ushort)((IPEndPoint)udp.Client.LocalEndPoint!).Port);
		var environment = CreateEnvironment(IPAddress.Loopback);
		using var endpoint = new NetworkDnsReverseLookupEndpoint<string>(environment.Context, maxAttemptCount: 1);
		var server = Task.Run(async () =>
		{
			var received = await udp.ReceiveAsync();
			var response = BuildPtrResponse(received.Buffer, truncated: false);
			await udp.SendAsync(response, response.Length, received.RemoteEndPoint);
		});
		var request = new NetworkDnsReverseLookupRequest<string>(
			Guid.NewGuid(), "ptr", IpAddressValue.Parse("192.0.2.25"),
			NetworkDnsResolverSelection.Exact(IpAddressValue.FromIPAddress(IPAddress.Loopback), port),
			environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(), 1500, false);

		Assert.True(endpoint.TrySend(request));
		await server.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));

		Assert.Equal("host.example", completion.Response.HostName);
		Assert.Equal(request.QueryAddress, completion.Response.QueryAddress);
		Assert.Equal(IpAddressValue.FromIPAddress(IPAddress.Loopback), completion.Response.ResolverAddress);
		Assert.Single(completion.Response.Stages);
		Assert.Equal(NetworkDnsTransportKind.Udp, completion.Response.Stages[0].Transport);
		Assert.Equal(completion.Response.Identity.BranchId, completion.Response.Stages[0].Execution.Identity.BranchId);
	}

	[Fact]
	public async Task PtrTruncatedUdpFallsBackToTcpWithinTheSameBranch()
	{
		var servers = CreateDualProtocolServers();
		var tcp = servers.Tcp;
		using var udp = servers.Udp;
		var port = servers.Port;
		var backend = new RecordingWfpBackend();
		var environment = CreateEnvironment(IPAddress.Loopback, exactNextHop: true, backend);
		using var endpoint = new NetworkDnsReverseLookupEndpoint<int>(environment.Context, maxAttemptCount: 1);
		var udpServer = Task.Run(async () =>
		{
			var received = await udp.ReceiveAsync();
			var response = BuildPtrResponse(received.Buffer, truncated: true);
			await udp.SendAsync(response, response.Length, received.RemoteEndPoint);
		});
		var tcpServer = Task.Run(async () =>
		{
			using var socket = await tcp.AcceptSocketAsync();
			var prefix = new byte[2];
			await ReceiveExactlyAsync(socket, prefix);
			var length = BinaryPrimitives.ReadUInt16BigEndian(prefix);
			var query = new byte[length];
			await ReceiveExactlyAsync(socket, query);
			var response = BuildPtrResponse(query, truncated: false);
			var framed = new byte[response.Length + 2];
			BinaryPrimitives.WriteUInt16BigEndian(framed, checked((ushort)response.Length));
			response.CopyTo(framed, 2);
			await socket.SendAsync(framed);
		});
		var request = new NetworkDnsReverseLookupRequest<int>(
			Guid.NewGuid(), 5, IpAddressValue.Parse("2001:db8::25"),
			NetworkDnsResolverSelection.Exact(IpAddressValue.FromIPAddress(IPAddress.Loopback), port),
			environment.Plan, NetworkAccessSecurityBoundary.NotApplicable(), 1500, false);

		Assert.True(endpoint.TrySend(request));
		await Task.WhenAll(udpServer, tcpServer).WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1);
		Assert.True(endpoint.TryReadReceived(out var completion));
		tcp.Stop();

		Assert.True(completion.Response.UsedTcp);
		Assert.Equal("host.example", completion.Response.HostName);
		Assert.Equal(2, completion.Response.Stages.Length);
		Assert.Equal(NetworkDnsTransportKind.Udp, completion.Response.Stages[0].Transport);
		Assert.Equal(NetworkDnsTransportKind.Tcp, completion.Response.Stages[1].Transport);
		Assert.All(completion.Response.Stages,
			stage => Assert.Equal(ProofKind.PolicyEnforced, stage.Execution.Evidence.NextHop.Proof));
		Assert.All(completion.Response.Stages,
			stage => Assert.Equal(completion.Response.Identity.BranchId, stage.Execution.Identity.BranchId));
		Assert.Equal(new[] { ProtocolType.Udp, ProtocolType.Tcp }, backend.Acquired.Select(item => item.Protocol));
		Assert.Equal(2, backend.DisposeCount);
	}

	private static (TcpListener Tcp, UdpClient Udp, ushort Port) CreateDualProtocolServers()
	{
		SocketException? lastError = null;
		for (var candidate = 15000; candidate <= 60000; candidate++)
		{
			var port = checked((ushort)candidate);
			var tcp = new TcpListener(IPAddress.Loopback, port);
			UdpClient? udp = null;
			try
			{
				tcp.Start();
				udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
				return (tcp, udp, port);
			}
			catch (SocketException ex)
			{
				lastError = ex;
				tcp.Stop();
				udp?.Dispose();
			}
		}

		throw new InvalidOperationException("dual-protocol-test-port-unavailable", lastError);
	}

	private static TestEnvironment CreateEnvironment(
		IPAddress resolver,
		bool exactNextHop = false,
		INetworkWfpConnectionPolicyBackend? wfpBackend = null)
	{
		var binary = IpAddressValue.FromIPAddress(resolver);
		var index = FindInterfaceIndex(resolver);
		var identity = new NetworkInterfaceIdentity(Guid.NewGuid(), index, "dns-test", index);
		var interfaces = new NetworkInterfaceSnapshotProvider();
		Assert.True(interfaces.TryReplace([new NetworkInterfaceSnapshot(identity, default, true)], out _, out _));
		var routes = new NetworkRouteSnapshotProvider();
		Assert.True(routes.TryReplace(binary, binary, identity, exactNextHop ? binary : default, 0, out _, out _));
		var context = new NetworkAccessExecutionContext(
			interfaces, routes, refreshWindowsSnapshots: false, wfpBackend);
		var plan = new RequestedAccessPlan(
			exactNextHop ? NetworkPathProvider.Direct : NetworkPathProvider.Automatic,
			NetworkDestinationSelection.SystemSelected(),
			NetworkSourceSelection.SystemSelected(),
			NetworkInterfaceSelection.SystemSelected(),
			exactNextHop ? NetworkNextHopSelection.Exact(binary) : NetworkNextHopSelection.SystemSelected(),
			NetworkLocalEndpointSelection.Ephemeral(),
			NetworkRouteScopeSelection.Current(),
			NetworkIpPacketPolicy.SystemDefault(),
			NetworkRouteAdapterIdentity.NotApplicable());
		return new TestEnvironment(context, plan);
	}

	private static byte[] BuildPtrResponse(byte[] query, bool truncated)
	{
		var headerAndQuestion = query.ToArray();
		BinaryPrimitives.WriteUInt16BigEndian(headerAndQuestion.AsSpan(2),
			truncated ? (ushort)0x8380 : (ushort)0x8180);
		BinaryPrimitives.WriteUInt16BigEndian(headerAndQuestion.AsSpan(6), truncated ? (ushort)0 : (ushort)1);
		if (truncated) return headerAndQuestion;
		var response = new List<byte>(headerAndQuestion);
		response.AddRange([0xC0, 0x0C, 0x00, 0x0C, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C]);
		var name = new byte[] { 4, (byte)'h', (byte)'o', (byte)'s', (byte)'t', 7, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e', 0 };
		response.Add(0);
		response.Add((byte)name.Length);
		response.AddRange(name);
		return [.. response];
	}

	private static async Task ReceiveExactlyAsync(Socket socket, Memory<byte> buffer)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var count = await socket.ReceiveAsync(buffer[offset..]);
			if (count == 0) throw new EndOfStreamException();
			offset += count;
		}
	}

	private static uint FindInterfaceIndex(IPAddress address)
	{
		foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
		{
			var properties = item.GetIPProperties();
			if (!properties.UnicastAddresses.Any(entry => entry.Address.Equals(address))) continue;
			var index = properties.GetIPv4Properties()?.Index;
			if (index is > 0) return checked((uint)index.Value);
		}
		throw new InvalidOperationException("resolver-interface-not-found");
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
