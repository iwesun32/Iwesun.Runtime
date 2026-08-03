namespace Iwesun.Runtime.Networks;

/// <summary>Orthogonal traits derived from an EUI-48 MAC address value.</summary>
[Flags]
public enum MacAddressTraits : ushort
{
	None = 0,
	Eui48 = 1 << 0,
	Unspecified = 1 << 1,
	Broadcast = 1 << 2,
	Unicast = 1 << 3,
	Multicast = 1 << 4,
	Individual = 1 << 5,
	Group = 1 << 6,
	UniversallyAdministered = 1 << 7,
	LocallyAdministered = 1 << 8
}
