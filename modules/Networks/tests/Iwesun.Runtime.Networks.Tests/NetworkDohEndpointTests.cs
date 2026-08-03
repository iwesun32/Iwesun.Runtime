using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Iwesun.Runtime.Networks;
using Xunit;

namespace Iwesun.Runtime.Networks.Tests;

public sealed class NetworkDohEndpointTests
{
	[Fact]
	public async Task ExactNextHopNoReuseKeepsPolicyAcrossTlsAndReleasesIt()
	{
		using var certificate = CreateCertificate();
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port);
		var backend = new RecordingWfpBackend();
		var environment = CreateEnvironment(exactNextHop: true, backend);
		using var endpoint = new NetworkDohEndpoint<string>(environment.Context, maxAttemptCount: 1);
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri($"https://logical.test:{port}/dns-query"), null, null, null, 1,
			NetworkHttpConnectionReusePolicy.NoReuseRequestPolicy, out var protocol, out var reason), reason);
		protocol = protocol with
		{
			CertificatePinSha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant(),
		};
		var observed = new TlsObservation();
		var server = ServeDohAsync(listener, certificate, observed);
		var request = new NetworkDohRequest<string>(
			Guid.NewGuid(), "exact-next-hop", $"https://logical.test:{port}/dns-query", "example.com",
			NetworkDnsQueryType.A, NetworkDohResponseFormat.Json, protocol, environment.Plan,
			"manual-resolver", 2000, false);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1, server);
		Assert.True(endpoint.TryReadReceived(out var completion));
		await server.WaitAsync(TimeSpan.FromSeconds(3));
		await WaitUntilAsync(() => backend.DisposeCount == 1, Task.CompletedTask);

		Assert.True(completion.Response.Succeeded, completion.Response.ReasonCode);
		Assert.Equal(ProofKind.PolicyEnforced,
			completion.Response.PathEvidence.ClientLeg.Evidence.NextHop.Proof);
		Assert.Equal("logical.test", observed.ServerName);
		Assert.Single(backend.Acquired);
		Assert.Equal(0, endpoint.ConnectionPoolCount);
	}

	[Fact]
	public async Task ExactNextHopRejectsReusableDohPolicyBeforeConnecting()
	{
		var backend = new RecordingWfpBackend();
		var environment = CreateEnvironment(exactNextHop: true, backend);
		using var endpoint = new NetworkDohEndpoint<string>(environment.Context, maxAttemptCount: 1);
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri("https://logical.test/dns-query"), null, null, null, 1,
			NetworkHttpConnectionReusePolicy.Reusable, out var protocol, out var reason), reason);
		var failed = new TaskCompletionSource<TrackedRequestFailure<NetworkDohRequest<string>, string>>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		endpoint.RequestFailed += (_, args) => failed.TrySetResult(args.Failure);
		var request = new NetworkDohRequest<string>(
			Guid.NewGuid(), "reuse-rejected", "https://logical.test/dns-query", "example.com",
			NetworkDnsQueryType.A, NetworkDohResponseFormat.Json, protocol, environment.Plan,
			"manual-resolver", 1000, false);

		Assert.True(endpoint.TrySend(request));
		var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.Equal(NetworkAccessFailureCodes.WfpPolicyConnectionReuseUnsupported, failure.Failure.Reason);
		Assert.Empty(backend.Acquired);
	}

	[Fact]
	public async Task FixedTransportIpPreservesLogicalHostSniAndCertificatePin()
	{
		using var certificate = CreateCertificate();
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		var port = checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port);
		var environment = CreateEnvironment();
		using var endpoint = new NetworkDohEndpoint<string>(environment.Context, maxAttemptCount: 1);
		Assert.True(NetworkHttpProtocolIdentity.TryCreate(
			new Uri($"https://logical.test:{port}/dns-query"), null, null, null, 1,
			NetworkHttpConnectionReusePolicy.Reusable, out var protocol, out var reason), reason);
		protocol = protocol with
		{
			CertificatePinSha256 = Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant(),
		};
		var observed = new TlsObservation();
		var server = ServeDohAsync(listener, certificate, observed);
		var request = new NetworkDohRequest<string>(
			Guid.NewGuid(), "lookup", $"https://logical.test:{port}/dns-query", "example.com",
			NetworkDnsQueryType.A, NetworkDohResponseFormat.Json, protocol, environment.Plan,
			"manual-resolver", 2000, false);

		Assert.True(endpoint.TrySend(request));
		await WaitUntilAsync(() => endpoint.ReceiveQueueLength == 1, server);
		Assert.True(endpoint.TryReadReceived(out var completion));
		await server.WaitAsync(TimeSpan.FromSeconds(3));

		Assert.True(completion.Response.Succeeded, completion.Response.ReasonCode);
		Assert.Equal(AccessCompliance.Satisfied, completion.Response.Compliance);
		Assert.Equal(IpAddressValue.Parse("192.0.2.1"), Assert.Single(completion.Response.Addresses));
		Assert.Equal("logical.test", observed.ServerName);
		Assert.Equal($"logical.test:{port}", observed.HostHeader);
		Assert.Equal(ProofKind.Observed,
			completion.Response.PathEvidence.ClientLeg.Evidence.Destination.Proof);
	}

	private static X509Certificate2 CreateCertificate()
	{
		using var key = RSA.Create(2048);
		var request = new CertificateRequest(
			"CN=logical.test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
		var names = new SubjectAlternativeNameBuilder();
		names.AddDnsName("logical.test");
		request.CertificateExtensions.Add(names.Build());
		request.CertificateExtensions.Add(new X509KeyUsageExtension(
			X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
		using var ephemeral = request.CreateSelfSigned(
			DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
		return X509CertificateLoader.LoadPkcs12(
			ephemeral.Export(X509ContentType.Pfx), null,
			X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable);
	}

	private static async Task ServeDohAsync(
		TcpListener listener,
		X509Certificate2 certificate,
		TlsObservation observed)
	{
		using var client = await listener.AcceptTcpClientAsync();
		await using var tls = new SslStream(client.GetStream(), false);
		await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
		{
			EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
			ServerCertificateSelectionCallback = (_, serverName) =>
			{
				observed.ServerName = serverName;
				return certificate;
			},
		});
		var header = await ReadHeaderAsync(tls);
		observed.HostHeader = header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
			.First(line => line.StartsWith("Host:", StringComparison.OrdinalIgnoreCase))[5..].Trim();
		var body = "{\"Status\":0,\"Answer\":[{\"name\":\"example.com.\",\"type\":1,\"TTL\":60,\"data\":\"192.0.2.1\"}]}";
		var bytes = Encoding.UTF8.GetBytes(body);
		var response = Encoding.ASCII.GetBytes(
			$"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nContent-Type: application/dns-json\r\nConnection: close\r\n\r\n");
		await tls.WriteAsync(response);
		await tls.WriteAsync(bytes);
		await tls.FlushAsync();
	}

	private static async Task<string> ReadHeaderAsync(Stream stream)
	{
		var buffer = new byte[8192];
		var length = 0;
		while (length < buffer.Length)
		{
			var count = await stream.ReadAsync(buffer.AsMemory(length, 1));
			if (count == 0) throw new EndOfStreamException();
			length += count;
			if (length >= 4 && buffer.AsSpan(length - 4, 4).SequenceEqual("\r\n\r\n"u8))
				return Encoding.ASCII.GetString(buffer, 0, length);
		}
		throw new InvalidDataException("header-too-long");
	}

	private static TestEnvironment CreateEnvironment(
		bool exactNextHop = false,
		INetworkWfpConnectionPolicyBackend? wfpBackend = null)
	{
		var address = IpAddressValue.FromIPAddress(IPAddress.Loopback);
		var index = FindInterfaceIndex();
		var identity = new NetworkInterfaceIdentity(Guid.NewGuid(), index, "doh-test", index);
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

	private static async Task WaitUntilAsync(Func<bool> condition, Task server)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (server.IsFaulted) await server;
			if (DateTime.UtcNow >= deadline)
				throw new TimeoutException($"test-condition-timeout; server={server.Status}");
			await Task.Delay(10);
		}
	}

	private sealed class TlsObservation
	{
		public string? ServerName { get; set; }
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

	private readonly record struct TestEnvironment(NetworkAccessExecutionContext Context, RequestedAccessPlan Plan);
}
