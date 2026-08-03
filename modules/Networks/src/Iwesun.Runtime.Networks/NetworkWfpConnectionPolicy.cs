using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Iwesun.Runtime.Networks;

/// <summary>
/// Fully qualified connection identity used by the Windows Filtering Platform backend.
/// The bound local port is deliberately part of the key so concurrent requests in the
/// same process cannot match each other's route policy.
/// </summary>
internal readonly record struct NetworkWfpConnectionPolicyRequest(
	Guid PolicyId,
	NetworkExecutionIdentity Identity,
	IpAddressValue Source,
	NetworkInterfaceIdentity Interface,
	IpAddressValue NextHop,
	ushort LocalPort,
	IpAddressValue Destination,
	ushort RemotePort,
	ProtocolType Protocol,
	string ApplicationPath)
{
	public bool IsValid =>
		PolicyId != Guid.Empty && Identity.HasBranch &&
		Source.Family is 4 or 6 && Destination.Family is 4 or 6 && NextHop.Family is 4 or 6 &&
		Source.Family == Destination.Family && NextHop.Family == Destination.Family &&
		Interface.Luid != 0 && LocalPort > 0 && RemotePort > 0 &&
		Protocol is ProtocolType.Tcp or ProtocolType.Udp &&
		!string.IsNullOrWhiteSpace(ApplicationPath);
}

internal readonly record struct NetworkWfpPolicyCapability(
	bool Supported,
	bool MissingPermission,
	string ReasonCode,
	NetworkPlatformError PlatformError);

internal readonly record struct NetworkWfpPolicyAcquireResult(
	INetworkWfpConnectionPolicyLease? Lease,
	string ReasonCode,
	NetworkPlatformError PlatformError)
{
	public bool Succeeded => Lease is not null;
}

internal interface INetworkWfpConnectionPolicyLease : IDisposable
{
	Guid PolicyId { get; }
	NetworkWfpConnectionPolicyRequest Request { get; }
}

internal interface INetworkWfpConnectionPolicyBackend
{
	NetworkWfpPolicyCapability QueryCapability();
	NetworkWfpPolicyAcquireResult Acquire(in NetworkWfpConnectionPolicyRequest request);
}

/// <summary>
/// Prevents duplicate connection match keys inside this process before the native policy
/// is installed. Native dynamic sessions provide the second cleanup boundary on process exit.
/// </summary>
internal sealed class IsolatedNetworkWfpConnectionPolicyBackend(INetworkWfpConnectionPolicyBackend inner)
	: INetworkWfpConnectionPolicyBackend
{
	private readonly ConcurrentDictionary<NetworkWfpConnectionMatchKey, Guid> _active = new();

	internal int ActiveLeaseCount => _active.Count;

	public NetworkWfpPolicyCapability QueryCapability() => inner.QueryCapability();

	public NetworkWfpPolicyAcquireResult Acquire(in NetworkWfpConnectionPolicyRequest request)
	{
		if (!request.IsValid)
			return new(null, NetworkAccessFailureCodes.WfpPolicyRequestInvalid, default);

		var key = NetworkWfpConnectionMatchKey.From(request);
		if (!_active.TryAdd(key, request.PolicyId))
			return new(null, NetworkAccessFailureCodes.WfpPolicyIsolationCollision, default);

		NetworkWfpPolicyAcquireResult acquired;
		try
		{
			acquired = inner.Acquire(request);
		}
		catch (Exception ex)
		{
			_active.TryRemove(new KeyValuePair<NetworkWfpConnectionMatchKey, Guid>(key, request.PolicyId));
			return new(
				null,
				NetworkAccessFailureCodes.WfpPolicyAddFailed,
				new NetworkPlatformError("wfp-backend", 0, ex.HResult, "wfp-policy-acquire"));
		}
		if (!acquired.Succeeded)
		{
			_active.TryRemove(new KeyValuePair<NetworkWfpConnectionMatchKey, Guid>(key, request.PolicyId));
			return acquired;
		}

		return new(new IsolatedLease(acquired.Lease!, key, request.PolicyId, _active), string.Empty, default);
	}

	private readonly record struct NetworkWfpConnectionMatchKey(
		IpAddressValue Source,
		ushort LocalPort,
		IpAddressValue Destination,
		ushort RemotePort,
		ProtocolType Protocol,
		string ApplicationPath)
	{
		public static NetworkWfpConnectionMatchKey From(in NetworkWfpConnectionPolicyRequest request) => new(
			request.Source,
			request.LocalPort,
			request.Destination,
			request.RemotePort,
			request.Protocol,
			request.ApplicationPath.ToUpperInvariant());
	}

	private sealed class IsolatedLease(
		INetworkWfpConnectionPolicyLease inner,
		NetworkWfpConnectionMatchKey key,
		Guid policyId,
		ConcurrentDictionary<NetworkWfpConnectionMatchKey, Guid> active)
		: INetworkWfpConnectionPolicyLease
	{
		private int _disposed;

		public Guid PolicyId => inner.PolicyId;
		public NetworkWfpConnectionPolicyRequest Request => inner.Request;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
			try
			{
				inner.Dispose();
			}
			finally
			{
				active.TryRemove(new KeyValuePair<NetworkWfpConnectionMatchKey, Guid>(key, policyId));
			}
		}
	}
}

internal sealed class NetworkWfpPolicyCleanupException(NetworkPlatformError platformError)
	: Exception(NetworkAccessFailureCodes.WfpPolicyCleanupFailed)
{
	public NetworkPlatformError PlatformError { get; } = platformError;
}

internal sealed class UnsupportedNetworkWfpConnectionPolicyBackend : INetworkWfpConnectionPolicyBackend
{
	public NetworkWfpPolicyCapability QueryCapability() => new(
		false,
		false,
		NetworkAccessFailureCodes.ExactNextHopBackendUnavailable,
		default);

	public NetworkWfpPolicyAcquireResult Acquire(in NetworkWfpConnectionPolicyRequest request) => new(
		null,
		NetworkAccessFailureCodes.ExactNextHopBackendUnavailable,
		default);
}
