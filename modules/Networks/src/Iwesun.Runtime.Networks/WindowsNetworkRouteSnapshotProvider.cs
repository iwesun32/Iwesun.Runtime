using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Iwesun.Runtime.Networks;

/// <summary>Refreshes immutable interface and route snapshots from Windows GetBestRoute2.</summary>
public sealed class WindowsNetworkRouteSnapshotProvider(
	NetworkInterfaceSnapshotProvider interfaceProvider,
	NetworkRouteSnapshotProvider routeProvider)
{
	private static readonly TimeSpan InterfaceSnapshotReuseWindow = TimeSpan.FromSeconds(1);
	private readonly object _interfaceCaptureGate = new();
	private NetworkInterfaceSnapshot[] _cachedInterfaces = [];
	private long _cachedInterfacesAt;

	public bool TryRefresh(
		in RequestedAccessPlan requested,
		out NetworkRouteSnapshot snapshot,
		out NetworkPlatformError platformError,
		out string reason)
	{
		return TryRefresh(requested, out _, out snapshot, out platformError, out reason);
	}

	internal bool TryRefresh(
		in RequestedAccessPlan requested,
		out NetworkInterfaceCatalog interfaceCatalog,
		out NetworkRouteSnapshot snapshot,
		out NetworkPlatformError platformError,
		out string reason)
	{
		interfaceCatalog = default;
		snapshot = default;
		platformError = default;
		if (!OperatingSystem.IsWindows())
		{
			reason = NetworkAccessFailureCodes.PlatformNotSupported;
			platformError = new NetworkPlatformError("windows", 0, 0, "platform-not-supported");
			return false;
		}
		if (Marshal.SizeOf<SockaddrInet>() != 28 ||
			Marshal.SizeOf<IpAddressPrefix>() != 32 ||
			Marshal.SizeOf<MibIpforwardRow2>() != 104)
		{
			reason = NetworkAccessFailureCodes.PlatformNotSupported;
			platformError = new NetworkPlatformError("windows", 0, 0, "native-layout-mismatch");
			return false;
		}
		if (!requested.TryValidate(out reason) ||
			requested.Destination.Kind != NetworkSelectorKind.Exact ||
			requested.PathProvider is not NetworkPathProvider.Automatic and not NetworkPathProvider.Direct)
		{
			if (string.IsNullOrEmpty(reason)) reason = NetworkAccessFailureCodes.ConstraintUnresolved;
			return false;
		}

		var interfaceIndex = requested.Interface.Kind == NetworkSelectorKind.Exact
			? requested.Interface.ExactValue.InterfaceIndex
			: 0;
		var destination = SockaddrInet.FromBinary(requested.Destination.ExactValue, interfaceIndex);
		var sourcePointer = IntPtr.Zero;
		try
		{
			if (requested.Source.Kind == NetworkSelectorKind.Exact)
			{
				var source = SockaddrInet.FromBinary(requested.Source.ExactValue, interfaceIndex);
				sourcePointer = Marshal.AllocHGlobal(Marshal.SizeOf<SockaddrInet>());
				Marshal.StructureToPtr(source, sourcePointer, false);
			}

			var nativeResult = GetBestRoute2(
				IntPtr.Zero,
				interfaceIndex,
				sourcePointer,
				ref destination,
				0,
				out var route,
				out var bestSource);
			if (nativeResult != 0)
			{
				reason = NetworkAccessFailureCodes.RouteQueryFailed;
				platformError = new NetworkPlatformError("windows", checked((int)nativeResult), 0, "get-best-route2");
				return false;
			}

			var interfaces = CaptureInterfaces();
			var identity = ResolveInterfaceIdentity(route.InterfaceIndex, route.InterfaceLuid, interfaces);
			interfaces = ApplySelectedIdentity(interfaces, identity);
			if (!interfaceProvider.TryReplace(interfaces, out interfaceCatalog, out reason)) return false;

			var nextHop = route.NextHop.ToBinaryOrDefault();
			if (!routeProvider.TryReplace(
				requested.Destination.ExactValue,
				bestSource.ToBinary(),
				identity,
				nextHop,
				0,
				out snapshot,
				out reason))
			{
				return false;
			}

			reason = string.Empty;
			return true;
		}
		catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
		{
			reason = NetworkAccessFailureCodes.PlatformNotSupported;
			platformError = new NetworkPlatformError("windows", 0, ex.HResult, "get-best-route2-unavailable");
			return false;
		}
		catch (Exception ex)
		{
			reason = NetworkAccessFailureCodes.RouteQueryFailed;
			platformError = new NetworkPlatformError("windows", 0, ex.HResult, "route-snapshot-refresh");
			return false;
		}
		finally
		{
			if (sourcePointer != IntPtr.Zero) Marshal.FreeHGlobal(sourcePointer);
		}
	}

	private NetworkInterfaceSnapshot[] CaptureInterfaces()
	{
		lock (_interfaceCaptureGate)
		{
			if (_cachedInterfaces.Length == 0 ||
				Stopwatch.GetElapsedTime(_cachedInterfacesAt) > InterfaceSnapshotReuseWindow)
			{
				var snapshots = new List<NetworkInterfaceSnapshot>();
				foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
				{
					if (!TryGetInterfaceIndex(networkInterface, out var index)) continue;
					var identity = CreateIdentity(networkInterface, index, 0);
					snapshots.Add(new NetworkInterfaceSnapshot(
						identity,
						CaptureUnicastAddresses(networkInterface),
						networkInterface.OperationalStatus == OperationalStatus.Up));
				}
				_cachedInterfaces = [.. snapshots];
				_cachedInterfacesAt = Stopwatch.GetTimestamp();
			}

			return (NetworkInterfaceSnapshot[])_cachedInterfaces.Clone();
		}
	}

	private static NetworkInterfaceSnapshot[] ApplySelectedIdentity(
		NetworkInterfaceSnapshot[] interfaces,
		NetworkInterfaceIdentity selected)
	{
		for (var index = 0; index < interfaces.Length; index++)
		{
			if (interfaces[index].Identity.InterfaceIndex != selected.InterfaceIndex) continue;
			interfaces[index] = interfaces[index] with { Identity = selected };
			return interfaces;
		}

		Array.Resize(ref interfaces, interfaces.Length + 1);
		interfaces[^1] = new NetworkInterfaceSnapshot(selected, default, true);
		return interfaces;
	}

	private static NetworkAddressSet CaptureUnicastAddresses(NetworkInterface networkInterface)
	{
		try
		{
			var addresses = networkInterface.GetIPProperties().UnicastAddresses
				.Select(static item => NetworkIpAddressInterop.FromSystemAddress(item.Address))
				.ToArray();
			return NetworkAddressSet.TryCreate(addresses, out var result, out _) ? result : default;
		}
		catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or NotSupportedException)
		{
			return default;
		}
	}

	private static NetworkInterfaceIdentity ResolveInterfaceIdentity(
		uint interfaceIndex,
		ulong luid,
		ReadOnlySpan<NetworkInterfaceSnapshot> interfaces)
	{
		foreach (var snapshot in interfaces)
		{
			if (snapshot.Identity.InterfaceIndex != interfaceIndex) continue;
			return snapshot.Identity with { Luid = luid };
		}

		return new NetworkInterfaceIdentity(Guid.Empty, luid, $"if-index-{interfaceIndex}", interfaceIndex);
	}

	private static NetworkInterfaceIdentity CreateIdentity(
		NetworkInterface networkInterface,
		uint interfaceIndex,
		ulong luid) => new(
		Guid.TryParse(networkInterface.Id, out var guid) ? guid : Guid.Empty,
		luid,
		networkInterface.Name,
		interfaceIndex);

	private static bool TryGetInterfaceIndex(NetworkInterface networkInterface, out uint index)
	{
		index = 0;
		IPInterfaceProperties properties;
		try
		{
			properties = networkInterface.GetIPProperties();
		}
		catch (NetworkInformationException)
		{
			return false;
		}
		catch (PlatformNotSupportedException)
		{
			return false;
		}

		if (TryGetIndex(static value => value.GetIPv4Properties()?.Index, properties, out index)) return true;
		return TryGetIndex(static value => value.GetIPv6Properties()?.Index, properties, out index);
	}

	private static bool TryGetIndex(
		Func<IPInterfaceProperties, int?> selector,
		IPInterfaceProperties properties,
		out uint index)
	{
		index = 0;
		try
		{
			var value = selector(properties);
			if (value is not > 0) return false;
			index = checked((uint)value.Value);
			return true;
		}
		catch (NetworkInformationException)
		{
			return false;
		}
		catch (PlatformNotSupportedException)
		{
			return false;
		}
	}

	[DllImport("iphlpapi.dll", ExactSpelling = true)]
	private static extern uint GetBestRoute2(
		IntPtr interfaceLuid,
		uint interfaceIndex,
		IntPtr sourceAddress,
		ref SockaddrInet destinationAddress,
		uint addressSortOptions,
		out MibIpforwardRow2 bestRoute,
		out SockaddrInet bestSourceAddress);

	[StructLayout(LayoutKind.Explicit, Size = 28)]
	private struct SockaddrInet
	{
		[FieldOffset(0)] public ushort Family;
		[FieldOffset(2)] public ushort Port;
		[FieldOffset(4)] public uint FlowInfoOrIpv4;
		[FieldOffset(8)] public uint Ipv6Part1;
		[FieldOffset(12)] public uint Ipv6Part2;
		[FieldOffset(16)] public uint Ipv6Part3;
		[FieldOffset(20)] public uint Ipv6Part4;
		[FieldOffset(24)] public uint ScopeId;

		public static SockaddrInet FromBinary(IpAddressValue value, uint interfaceIndex = 0)
		{
			var result = new SockaddrInet
			{
				Family = value.IsIPv4 ? (ushort)AddressFamily.InterNetwork : (ushort)AddressFamily.InterNetworkV6,
				ScopeId = value.RequiresInterfaceScope ? interfaceIndex : 0,
			};
			var addressBytes = value.ToIPAddress().GetAddressBytes();
			var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref result, 1));
			addressBytes.CopyTo(value.IsIPv4 ? bytes[4..8] : bytes[8..24]);
			return result;
		}

		public readonly IpAddressValue ToBinary()
		{
			var copy = this;
			var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref copy, 1));
			return Family switch
			{
				(ushort)AddressFamily.InterNetwork => IpAddressValue.FromIPv4Bytes(bytes[4..8]),
				(ushort)AddressFamily.InterNetworkV6 => IpAddressValue.FromIPv6Bytes(bytes[8..24]),
				_ => throw new SocketException((int)SocketError.AddressFamilyNotSupported),
			};
		}

		public readonly IpAddressValue ToBinaryOrDefault()
		{
			var value = ToBinary();
			var address = value.ToIPAddress();
			return address.Equals(value.IsIPv4 ? IPAddress.Any : IPAddress.IPv6Any) ? default : value;
		}
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct IpAddressPrefix
	{
		public SockaddrInet Prefix;
		public byte PrefixLength;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MibIpforwardRow2
	{
		public ulong InterfaceLuid;
		public uint InterfaceIndex;
		public IpAddressPrefix DestinationPrefix;
		public SockaddrInet NextHop;
		public byte SitePrefixLength;
		public uint ValidLifetime;
		public uint PreferredLifetime;
		public uint Metric;
		public int Protocol;
		public byte Loopback;
		public byte AutoconfigureAddress;
		public byte Publish;
		public byte Immortal;
		public uint Age;
		public int Origin;
	}
}
