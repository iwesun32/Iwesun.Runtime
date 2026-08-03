namespace Iwesun.Runtime.Networks;

public enum NetworkPathProvider : byte
{
	Unspecified = 0,
	Automatic = 1,
	Direct = 2,
	RouteAdapter = 3,
	SystemProxy = 4,
}

public enum NetworkSelectorKind : byte
{
	Unspecified = 0,
	SystemSelected = 1,
	Exact = 2,
	AllowedSet = 3,
	PreferredSet = 4,
	ExcludedSet = 5,
	Derived = 6,
	NotApplicable = 7,
}

public enum NetworkSelectionDimension : byte
{
	Unspecified = 0,
	Destination = 1,
	Source = 2,
	Interface = 3,
	NextHop = 4,
	LocalEndpoint = 5,
	RouteScope = 6,
}

/// <summary>An immutable, de-duplicated, stable-ordered address set.</summary>
public readonly struct NetworkAddressSet : IEquatable<NetworkAddressSet>
{
	private readonly IpAddressValue[]? _items;

	private NetworkAddressSet(IpAddressValue[] items) => _items = items;

	public int Count => _items?.Length ?? 0;
	public bool IsValid => Count > 0;
	public IpAddressValue this[int index] => _items![index];

	public static bool TryCreate(
		ReadOnlySpan<IpAddressValue> values,
		out NetworkAddressSet result,
		out string reason)
	{
		if (values.IsEmpty)
		{
			result = default;
			reason = NetworkAccessFailureCodes.SelectorSetEmpty;
			return false;
		}

		var items = values.ToArray();
		if (Array.Exists(items, static value => value.Family is not (4 or 6)))
		{
			result = default;
			reason = NetworkAccessFailureCodes.SelectorValueInvalid;
			return false;
		}

		Array.Sort(items, IpAddressValueComparer.Instance);
		var uniqueCount = 1;
		for (var index = 1; index < items.Length; index++)
		{
			if (items[index] != items[uniqueCount - 1])
			{
				items[uniqueCount++] = items[index];
			}
		}

		if (uniqueCount != items.Length)
		{
			Array.Resize(ref items, uniqueCount);
		}

		result = new NetworkAddressSet(items);
		reason = string.Empty;
		return true;
	}

	public bool Equals(NetworkAddressSet other)
	{
		if (Count != other.Count)
		{
			return false;
		}

		for (var index = 0; index < Count; index++)
		{
			if (this[index] != other[index])
			{
				return false;
			}
		}

		return true;
	}

	public override bool Equals(object? obj) => obj is NetworkAddressSet other && Equals(other);

	public override int GetHashCode()
	{
		var hash = new HashCode();
		for (var index = 0; index < Count; index++)
		{
			hash.Add(this[index]);
		}

		return hash.ToHashCode();
	}

	public static bool operator ==(NetworkAddressSet left, NetworkAddressSet right) => left.Equals(right);
	public static bool operator !=(NetworkAddressSet left, NetworkAddressSet right) => !left.Equals(right);

	private sealed class IpAddressValueComparer : IComparer<IpAddressValue>
	{
		public static IpAddressValueComparer Instance { get; } = new();

		public int Compare(IpAddressValue x, IpAddressValue y)
		{
			var family = x.Family.CompareTo(y.Family);
			if (family != 0) return family;
			var high = x.High.CompareTo(y.High);
			return high != 0 ? high : x.Low.CompareTo(y.Low);
		}
	}
}

/// <summary>A stable operating-system interface identity.</summary>
public readonly record struct NetworkInterfaceIdentity(
	Guid InterfaceGuid,
	ulong Luid,
	string Alias,
	uint InterfaceIndex = 0)
{
	public bool IsValid =>
		InterfaceGuid != Guid.Empty || Luid != 0 || !string.IsNullOrWhiteSpace(Alias) || InterfaceIndex != 0;
	public bool HasStableIdentity =>
		InterfaceGuid != Guid.Empty || Luid != 0 || !string.IsNullOrWhiteSpace(Alias);
}

/// <summary>An immutable, de-duplicated, stable-ordered interface set.</summary>
public readonly struct NetworkInterfaceSet : IEquatable<NetworkInterfaceSet>
{
	private readonly NetworkInterfaceIdentity[]? _items;

	private NetworkInterfaceSet(NetworkInterfaceIdentity[] items) => _items = items;

	public int Count => _items?.Length ?? 0;
	public bool IsValid => Count > 0;
	public NetworkInterfaceIdentity this[int index] => _items![index];

	public static bool TryCreate(
		ReadOnlySpan<NetworkInterfaceIdentity> values,
		out NetworkInterfaceSet result,
		out string reason)
	{
		if (values.IsEmpty)
		{
			result = default;
			reason = NetworkAccessFailureCodes.SelectorSetEmpty;
			return false;
		}

		var items = values.ToArray();
		if (Array.Exists(items, static value => !value.IsValid))
		{
			result = default;
			reason = NetworkAccessFailureCodes.SelectorValueInvalid;
			return false;
		}

		Array.Sort(items, static (left, right) =>
		{
			var guid = left.InterfaceGuid.CompareTo(right.InterfaceGuid);
			if (guid != 0) return guid;
			var luid = left.Luid.CompareTo(right.Luid);
			if (luid != 0) return luid;
			var alias = StringComparer.OrdinalIgnoreCase.Compare(left.Alias, right.Alias);
			return alias != 0 ? alias : left.InterfaceIndex.CompareTo(right.InterfaceIndex);
		});
		var uniqueCount = 1;
		for (var index = 1; index < items.Length; index++)
		{
			if (!SameIdentity(items[index], items[uniqueCount - 1]))
			{
				items[uniqueCount++] = items[index];
			}
		}

		if (uniqueCount != items.Length)
		{
			Array.Resize(ref items, uniqueCount);
		}

		result = new NetworkInterfaceSet(items);
		reason = string.Empty;
		return true;
	}

	public bool Equals(NetworkInterfaceSet other)
	{
		if (Count != other.Count) return false;
		for (var index = 0; index < Count; index++)
		{
			if (!SameIdentity(this[index], other[index])) return false;
		}

		return true;
	}

	public override bool Equals(object? obj) => obj is NetworkInterfaceSet other && Equals(other);
	public override int GetHashCode()
	{
		var hash = new HashCode();
		for (var index = 0; index < Count; index++)
		{
			hash.Add(this[index].InterfaceGuid);
			hash.Add(this[index].Luid);
			hash.Add(this[index].Alias, StringComparer.OrdinalIgnoreCase);
			hash.Add(this[index].InterfaceIndex);
		}

		return hash.ToHashCode();
	}

	public static bool operator ==(NetworkInterfaceSet left, NetworkInterfaceSet right) => left.Equals(right);
	public static bool operator !=(NetworkInterfaceSet left, NetworkInterfaceSet right) => !left.Equals(right);

	private static bool SameIdentity(NetworkInterfaceIdentity left, NetworkInterfaceIdentity right) =>
		left.InterfaceGuid == right.InterfaceGuid &&
		left.Luid == right.Luid &&
		StringComparer.OrdinalIgnoreCase.Equals(left.Alias, right.Alias) &&
		left.InterfaceIndex == right.InterfaceIndex;
}

public readonly record struct NetworkDestinationSelection(
	NetworkSelectorKind Kind,
	IpAddressValue ExactValue,
	NetworkAddressSet Set,
	NetworkSelectionDimension DerivedFrom)
{
	public static NetworkDestinationSelection SystemSelected() => new(NetworkSelectorKind.SystemSelected, default, default, default);
	public static NetworkDestinationSelection Exact(IpAddressValue value) => new(NetworkSelectorKind.Exact, value, default, default);
	public static NetworkDestinationSelection Allowed(NetworkAddressSet set) => new(NetworkSelectorKind.AllowedSet, default, set, default);
	public static NetworkDestinationSelection Preferred(NetworkAddressSet set) => new(NetworkSelectorKind.PreferredSet, default, set, default);
	public static NetworkDestinationSelection NotApplicable() => new(NetworkSelectorKind.NotApplicable, default, default, default);
}

public readonly record struct NetworkSourceSelection(
	NetworkSelectorKind Kind,
	IpAddressValue ExactValue,
	NetworkAddressSet Set,
	NetworkSelectionDimension DerivedFrom)
{
	public static NetworkSourceSelection SystemSelected() => new(NetworkSelectorKind.SystemSelected, default, default, default);
	public static NetworkSourceSelection Exact(IpAddressValue value) => new(NetworkSelectorKind.Exact, value, default, default);
	public static NetworkSourceSelection Allowed(NetworkAddressSet set) => new(NetworkSelectorKind.AllowedSet, default, set, default);
	public static NetworkSourceSelection Preferred(NetworkAddressSet set) => new(NetworkSelectorKind.PreferredSet, default, set, default);
	public static NetworkSourceSelection Derived(NetworkSelectionDimension source) => new(NetworkSelectorKind.Derived, default, default, source);
	public static NetworkSourceSelection NotApplicable() => new(NetworkSelectorKind.NotApplicable, default, default, default);
}

public readonly record struct NetworkInterfaceSelection(
	NetworkSelectorKind Kind,
	NetworkInterfaceIdentity ExactValue,
	NetworkInterfaceSet Set,
	NetworkSelectionDimension DerivedFrom)
{
	public static NetworkInterfaceSelection SystemSelected() => new(NetworkSelectorKind.SystemSelected, default, default, default);
	public static NetworkInterfaceSelection Exact(NetworkInterfaceIdentity value) => new(NetworkSelectorKind.Exact, value, default, default);
	public static NetworkInterfaceSelection Allowed(NetworkInterfaceSet set) => new(NetworkSelectorKind.AllowedSet, default, set, default);
	public static NetworkInterfaceSelection Preferred(NetworkInterfaceSet set) => new(NetworkSelectorKind.PreferredSet, default, set, default);
	public static NetworkInterfaceSelection Excluded(NetworkInterfaceSet set) => new(NetworkSelectorKind.ExcludedSet, default, set, default);
	public static NetworkInterfaceSelection Derived(NetworkSelectionDimension source) => new(NetworkSelectorKind.Derived, default, default, source);
	public static NetworkInterfaceSelection NotApplicable() => new(NetworkSelectorKind.NotApplicable, default, default, default);
}

public readonly record struct NetworkNextHopSelection(
	NetworkSelectorKind Kind,
	IpAddressValue ExactValue,
	NetworkAddressSet Set,
	NetworkSelectionDimension DerivedFrom,
	bool IsOnLink)
{
	public static NetworkNextHopSelection SystemSelected() => new(NetworkSelectorKind.SystemSelected, default, default, default, false);
	public static NetworkNextHopSelection Exact(IpAddressValue value) => new(NetworkSelectorKind.Exact, value, default, default, false);
	public static NetworkNextHopSelection OnLink() => new(NetworkSelectorKind.Exact, default, default, default, true);
	public static NetworkNextHopSelection Allowed(NetworkAddressSet set) => new(NetworkSelectorKind.AllowedSet, default, set, default, false);
	public static NetworkNextHopSelection Excluded(NetworkAddressSet set) => new(NetworkSelectorKind.ExcludedSet, default, set, default, false);
	public static NetworkNextHopSelection Derived(NetworkSelectionDimension source) => new(NetworkSelectorKind.Derived, default, default, source, false);
	public static NetworkNextHopSelection NotApplicable() => new(NetworkSelectorKind.NotApplicable, default, default, default, false);
}

public enum NetworkLocalEndpointSelectionKind : byte
{
	Unspecified = 0,
	SystemSelected = 1,
	Ephemeral = 2,
	ExactPort = 3,
	AllowedRange = 4,
	ReservedLease = 5,
	NotApplicable = 6,
}

public readonly record struct NetworkLocalEndpointSelection(
	NetworkLocalEndpointSelectionKind Kind,
	ushort FirstPort,
	ushort LastPort,
	Guid LeaseId)
{
	public static NetworkLocalEndpointSelection SystemSelected() => new(NetworkLocalEndpointSelectionKind.SystemSelected, 0, 0, Guid.Empty);
	public static NetworkLocalEndpointSelection Ephemeral() => new(NetworkLocalEndpointSelectionKind.Ephemeral, 0, 0, Guid.Empty);
	public static NetworkLocalEndpointSelection Exact(ushort port) => new(NetworkLocalEndpointSelectionKind.ExactPort, port, port, Guid.Empty);
	public static NetworkLocalEndpointSelection AllowedRange(ushort first, ushort last) => new(NetworkLocalEndpointSelectionKind.AllowedRange, first, last, Guid.Empty);
	public static NetworkLocalEndpointSelection Reserved(Guid leaseId) => new(NetworkLocalEndpointSelectionKind.ReservedLease, 0, 0, leaseId);
	public static NetworkLocalEndpointSelection NotApplicable() => new(NetworkLocalEndpointSelectionKind.NotApplicable, 0, 0, Guid.Empty);
}

public enum NetworkRouteScopeSelectionKind : byte
{
	Unspecified = 0,
	CurrentScope = 1,
	ExactCompartment = 2,
	NotApplicable = 3,
}

public readonly record struct NetworkRouteScopeSelection(NetworkRouteScopeSelectionKind Kind, uint CompartmentId)
{
	public static NetworkRouteScopeSelection Current() => new(NetworkRouteScopeSelectionKind.CurrentScope, 0);
	public static NetworkRouteScopeSelection Exact(uint compartmentId) => new(NetworkRouteScopeSelectionKind.ExactCompartment, compartmentId);
	public static NetworkRouteScopeSelection NotApplicable() => new(NetworkRouteScopeSelectionKind.NotApplicable, 0);
}
