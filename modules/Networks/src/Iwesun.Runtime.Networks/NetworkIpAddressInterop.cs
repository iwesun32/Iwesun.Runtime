using System.Net;

namespace Iwesun.Runtime.Networks;

internal static class NetworkIpAddressInterop
{
	public static IpAddressValue FromSystemAddress(IPAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		return address.AddressFamily switch
		{
			System.Net.Sockets.AddressFamily.InterNetwork =>
				IpAddressValue.FromIPv4Bytes(address.GetAddressBytes()),
			System.Net.Sockets.AddressFamily.InterNetworkV6 =>
				IpAddressValue.FromIPv6Bytes(address.GetAddressBytes()),
			_ => throw new ArgumentException("Only IPv4 and IPv6 addresses are supported.", nameof(address)),
		};
	}

	public static IPAddress ToSystemAddress(IpAddressValue address, uint interfaceIndex) =>
		address.RequiresInterfaceScope
			? address.ToIPAddress(interfaceIndex)
			: address.ToIPAddress();
}
