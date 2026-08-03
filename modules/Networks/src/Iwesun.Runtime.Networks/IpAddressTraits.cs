namespace Iwesun.Runtime.Networks;

/// <summary>Orthogonal protocol and scope traits derived from an IP address value.</summary>
[Flags]
public enum IpAddressTraits : ulong
{
	None = 0,
	IPv4 = 1UL << 0,
	IPv6 = 1UL << 1,
	Unspecified = 1UL << 2,
	Loopback = 1UL << 3,
	Private = 1UL << 4,
	LinkLocal = 1UL << 5,
	Multicast = 1UL << 6,
	Broadcast = 1UL << 7,
	Shared = 1UL << 8,
	Documentation = 1UL << 9,
	Benchmark = 1UL << 10,
	Reserved = 1UL << 11,
	GlobalUnicast = 1UL << 12,
	Transition = 1UL << 13,
	IPv4Mapped = 1UL << 14,
	UniqueLocal = 1UL << 15,
	SiteLocal = 1UL << 16
}
