namespace Iwesun.Runtime.Networks;

/// <summary>Classification of an IP address scope or protocol type.</summary>
public enum IpAddressType
{
	/// <summary>Address classification was not executed, so whether a type exists is unknown.</summary>
	Null = 0,
	/// <summary>Classification was executed and confirmed that no applicable type can be assigned.</summary>
	Unset = 1,
	/// <summary>Publicly routable address.</summary>
	Public = 2,
	/// <summary>Private address.</summary>
	Private = 3,
	/// <summary>Link-local address.</summary>
	LinkLocal = 4,
	/// <summary>Loopback address.</summary>
	Loopback = 5,
	/// <summary>Address that does not fit a more specific supported type.</summary>
	Temp = 6,
	/// <summary>Multicast address.</summary>
	Multicast = 7,
	/// <summary>Protocol-transition address.</summary>
	Transition = 8
}
