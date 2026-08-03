using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Iwesun.Runtime.Networks;

/// <summary>
/// Versioned HTTP CONNECT byte-stream adapter. The client leg is executed by Networks;
/// the proxy egress is reported only for the CONNECT target and is never inferred from the client socket.
/// </summary>
public sealed class HttpConnectNetworkRouteAdapter : INetworkRouteAdapter
{
	private const int MaxHeaderBytes = 16 * 1024;
	private readonly Uri _proxyEndpoint;
	private readonly RequestedAccessPlan _clientAccessPlan;
	private readonly NetworkAccessExecutionContext _context;
	private readonly NetworkCredential? _credential;

	public HttpConnectNetworkRouteAdapter(
		string adapterId,
		int capabilityVersion,
		Uri proxyEndpoint,
		RequestedAccessPlan clientAccessPlan,
		NetworkAccessExecutionContext? context = null,
		NetworkCredential? credential = null)
	{
		ArgumentNullException.ThrowIfNull(proxyEndpoint);
		if (string.IsNullOrWhiteSpace(adapterId)) throw new ArgumentException("Adapter ID is required.", nameof(adapterId));
		if (capabilityVersion <= 0) throw new ArgumentOutOfRangeException(nameof(capabilityVersion));
		if (!proxyEndpoint.IsAbsoluteUri || proxyEndpoint.Scheme != Uri.UriSchemeHttp)
			throw new ArgumentException("The HTTP CONNECT proxy endpoint must be an absolute HTTP URI.", nameof(proxyEndpoint));
		if (!clientAccessPlan.TryValidate(out var reason) ||
			clientAccessPlan.PathProvider is not (NetworkPathProvider.Automatic or NetworkPathProvider.Direct) ||
			clientAccessPlan.Destination.Kind != NetworkSelectorKind.Exact)
			throw new ArgumentException($"The proxy client access plan is invalid: {reason}", nameof(clientAccessPlan));

		Capabilities = new NetworkRouteAdapterCapabilities(
			adapterId.Trim(), capabilityVersion, NetworkRouteAdapterTransportCapabilities.ByteStream, true, true);
		_proxyEndpoint = proxyEndpoint;
		_clientAccessPlan = clientAccessPlan;
		_context = context ?? NetworkAccessExecutionContext.CreatePlatformDefault();
		_credential = credential;
	}

	public NetworkRouteAdapterCapabilities Capabilities { get; }
	public Uri ProxyEndpoint => _proxyEndpoint;

	public async ValueTask<NetworkRouteAdapterConnection> ConnectAsync(
		NetworkRouteAdapterConnectRequest request,
		CancellationToken cancellationToken)
	{
		if (!request.IsValid) throw new ArgumentException("Route adapter request is invalid.", nameof(request));
		if (!SupportsEgressPlan(request.Requested))
			throw new InvalidOperationException(NetworkAccessFailureCodes.RouteAdapterEgressConstraintUnsupported);

		var proxyPort = checked((ushort)(_proxyEndpoint.IsDefaultPort ? 80 : _proxyEndpoint.Port));
		var security = new NetworkAccessSecurityBoundary(
			NetworkAccessLeg.ClientLeg, _proxyEndpoint.IdnHost, string.Empty, string.Empty, string.Empty);
		if (!_context.TryResolve(_clientAccessPlan, security, out var resolved, out var failure))
			throw new InvalidOperationException(failure.ReasonCode);

		var socket = new Socket(
			resolved.Destination.IsIPv4 ? AddressFamily.InterNetwork : AddressFamily.InterNetworkV6,
			SocketType.Stream,
			ProtocolType.Tcp);
		INetworkWfpConnectionPolicyLease? policy = null;
		try
		{
			var executionRequest = new NetworkSocketExecutionRequest(
				request.Identity, _clientAccessPlan, resolved, security,
				NetworkSocketOperation.TcpConnect, proxyPort, [], 0);
			NetworkSocketExecutor.ApplyPlan(socket, executionRequest);
			policy = _context.SocketExecutor.AcquireWfpPolicyIfRequired(
				socket, executionRequest, ProtocolType.Tcp, out var policyFailure);
			if (!string.IsNullOrEmpty(policyFailure.ReasonCode))
				throw new InvalidOperationException(policyFailure.ReasonCode);
			await socket.ConnectAsync(
				new IPEndPoint(
					NetworkIpAddressInterop.ToSystemAddress(
						resolved.Destination,
						resolved.Interface.InterfaceIndex),
					proxyPort),
				cancellationToken).ConfigureAwait(false);
			var clientResult = NetworkSocketExecutor.CreateSuccessResult(
				executionRequest, socket, [], ProofKind.Observed, policyEnforced: policy is not null);
			Stream stream = new NetworkStream(socket, ownsSocket: true);
			if (policy is not null)
			{
				stream = new NetworkWfpPolicyOwnedStream(stream, policy);
				policy = null;
			}
			try
			{
				await EstablishTunnelAsync(stream, request.Resolved.Destination, request.Protocol.Port, cancellationToken)
					.ConfigureAwait(false);
				var clientLeg = new NetworkRouteLegEvidence(
					NetworkAccessLeg.ClientLeg, clientResult.Evidence, Capabilities.AdapterId);
				var egressLeg = CreateEgressEvidence(request);
				return new NetworkRouteAdapterConnection(
					stream, clientLeg, egressLeg, clientResult.Compliance);
			}
			catch
			{
				stream.Dispose();
				throw;
			}
		}
		catch
		{
			try
			{
				if (socket.SafeHandle is { IsClosed: false }) socket.Dispose();
			}
			finally
			{
				policy?.Dispose();
			}
			throw;
		}
	}

	private async Task EstablishTunnelAsync(
		Stream stream,
		IpAddressValue targetAddress,
		ushort targetPort,
		CancellationToken cancellationToken)
	{
		var target = targetAddress.ToIPAddress();
		var authority = target.AddressFamily == AddressFamily.InterNetworkV6
			? $"[{target}]:{targetPort}"
			: $"{target}:{targetPort}";
		var authorization = _credential is null
			? string.Empty
			: $"Proxy-Authorization: Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_credential.UserName}:{_credential.Password}"))}\r\n";
		var bytes = Encoding.ASCII.GetBytes(
			$"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n{authorization}Proxy-Connection: Keep-Alive\r\n\r\n");
		await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
		await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
		var header = await ReadHeaderAsync(stream, cancellationToken).ConfigureAwait(false);
		var lineEnd = header.IndexOf("\r\n", StringComparison.Ordinal);
		var statusLine = lineEnd >= 0 ? header[..lineEnd] : header;
		if (!statusLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) ||
			statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries) is not [_, "200", ..])
			throw new HttpRequestException($"HTTP CONNECT failed: {statusLine}");
	}

	private NetworkRouteLegEvidence CreateEgressEvidence(NetworkRouteAdapterConnectRequest request)
	{
		var notApplicableAddress = new NetworkDimensionEvidence<IpAddressValue>(
			default, ProofKind.Unavailable, AccessCompliance.NotApplicable);
		var evidence = new ActualAccessEvidence(
			request.Identity,
			new(NetworkPathProvider.RouteAdapter, ProofKind.AdapterReported, AccessCompliance.Satisfied),
			new(request.Resolved.Destination, ProofKind.AdapterReported, AccessCompliance.Satisfied),
			notApplicableAddress,
			new(default, ProofKind.Unavailable, AccessCompliance.NotApplicable),
			notApplicableAddress,
			new((ushort)0, ProofKind.Unavailable, AccessCompliance.NotApplicable),
			new(0U, ProofKind.Unavailable, AccessCompliance.NotApplicable),
			new(NetworkIpPacketPolicy.NotApplicable(), ProofKind.Unavailable, AccessCompliance.NotApplicable),
			new(request.Requested.RouteAdapter, ProofKind.PolicyEnforced, AccessCompliance.Satisfied));
		return new NetworkRouteLegEvidence(NetworkAccessLeg.EgressLeg, evidence, Capabilities.AdapterId);
	}

	private static bool SupportsEgressPlan(RequestedAccessPlan plan) =>
		plan.Destination.Kind == NetworkSelectorKind.Exact &&
		plan.Source.Kind == NetworkSelectorKind.SystemSelected &&
		plan.Interface.Kind == NetworkSelectorKind.SystemSelected &&
		plan.NextHop.Kind == NetworkSelectorKind.SystemSelected &&
		plan.LocalEndpoint.Kind == NetworkLocalEndpointSelectionKind.Ephemeral &&
		plan.RouteScope.Kind == NetworkRouteScopeSelectionKind.CurrentScope &&
		plan.IpPacketPolicy.Kind == NetworkIpPacketPolicyKind.SystemDefault;

	private static async Task<string> ReadHeaderAsync(Stream stream, CancellationToken cancellationToken)
	{
		var buffer = new byte[MaxHeaderBytes];
		var length = 0;
		while (length < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer.AsMemory(length, 1), cancellationToken).ConfigureAwait(false);
			if (read == 0) throw new EndOfStreamException("Proxy closed before completing the CONNECT response.");
			length += read;
			if (length >= 4 && buffer.AsSpan(length - 4, 4).SequenceEqual("\r\n\r\n"u8))
				return Encoding.ASCII.GetString(buffer, 0, length);
		}
		throw new HttpRequestException("HTTP CONNECT response header exceeded the configured limit.");
	}
}
