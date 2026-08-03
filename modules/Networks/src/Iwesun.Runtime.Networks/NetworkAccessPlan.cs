using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Iwesun.Runtime.Networks;

public enum NetworkIpPacketPolicyKind : byte
{
	Unspecified = 0,
	SystemDefault = 1,
	Explicit = 2,
	NotApplicable = 3,
}

public readonly record struct NetworkOptionalByte(bool HasValue, byte Value)
{
	public static NetworkOptionalByte None => default;
	public static NetworkOptionalByte Some(byte value) => new(true, value);
}

public readonly record struct NetworkIpPacketPolicy(
	NetworkIpPacketPolicyKind Kind,
	NetworkOptionalByte TimeToLiveOrHopLimit,
	bool DontFragment,
	bool AllowBroadcast,
	bool AllowMulticastLoopback)
{
	public static NetworkIpPacketPolicy SystemDefault() => new(NetworkIpPacketPolicyKind.SystemDefault, default, false, false, false);
	public static NetworkIpPacketPolicy Explicit(
		NetworkOptionalByte timeToLiveOrHopLimit,
		bool dontFragment,
		bool allowBroadcast,
		bool allowMulticastLoopback) =>
		new(NetworkIpPacketPolicyKind.Explicit, timeToLiveOrHopLimit, dontFragment, allowBroadcast, allowMulticastLoopback);
	public static NetworkIpPacketPolicy NotApplicable() => new(NetworkIpPacketPolicyKind.NotApplicable, default, false, false, false);
}

public enum NetworkRouteAdapterIdentityKind : byte
{
	Unspecified = 0,
	NotApplicable = 1,
	Exact = 2,
}

public readonly record struct NetworkRouteAdapterIdentity(
	NetworkRouteAdapterIdentityKind Kind,
	string AdapterId,
	int CapabilityVersion)
{
	public bool IsValid => Kind switch
	{
		NetworkRouteAdapterIdentityKind.NotApplicable => true,
		NetworkRouteAdapterIdentityKind.Exact => !string.IsNullOrWhiteSpace(AdapterId) && CapabilityVersion > 0,
		_ => false,
	};

	public static NetworkRouteAdapterIdentity NotApplicable() =>
		new(NetworkRouteAdapterIdentityKind.NotApplicable, string.Empty, 0);

	public static NetworkRouteAdapterIdentity Exact(string adapterId, int capabilityVersion) =>
		new(NetworkRouteAdapterIdentityKind.Exact, adapterId, capabilityVersion);
}

/// <summary>The normalized, immutable cross-protocol access request.</summary>
public readonly record struct RequestedAccessPlan(
	NetworkPathProvider PathProvider,
	NetworkDestinationSelection Destination,
	NetworkSourceSelection Source,
	NetworkInterfaceSelection Interface,
	NetworkNextHopSelection NextHop,
	NetworkLocalEndpointSelection LocalEndpoint,
	NetworkRouteScopeSelection RouteScope,
	NetworkIpPacketPolicy IpPacketPolicy,
	NetworkRouteAdapterIdentity RouteAdapter)
{
	public bool TryValidate(out string reason)
	{
		if (PathProvider == NetworkPathProvider.Unspecified)
		{
			reason = NetworkAccessFailureCodes.PathProviderUnspecified;
			return false;
		}

		if (!ValidateAddressSelector(Destination.Kind, Destination.ExactValue, Destination.Set, false, out reason) ||
			!ValidateAddressSelector(Source.Kind, Source.ExactValue, Source.Set, true, out reason) ||
			!ValidateInterfaceSelector(Interface, out reason) ||
			!ValidateNextHopSelector(NextHop, out reason))
		{
			return false;
		}

		if (!ValidateLocalEndpoint(LocalEndpoint, out reason) || !ValidateRouteScope(RouteScope, out reason))
		{
			return false;
		}

		if (IpPacketPolicy.Kind == NetworkIpPacketPolicyKind.Unspecified)
		{
			reason = NetworkAccessFailureCodes.PacketPolicyUnspecified;
			return false;
		}

		if (!RouteAdapter.IsValid ||
			(PathProvider == NetworkPathProvider.RouteAdapter && RouteAdapter.Kind != NetworkRouteAdapterIdentityKind.Exact) ||
			(PathProvider != NetworkPathProvider.RouteAdapter && RouteAdapter.Kind != NetworkRouteAdapterIdentityKind.NotApplicable))
		{
			reason = NetworkAccessFailureCodes.RouteAdapterIdentityMissing;
			return false;
		}

		if (!ValidateDerivedSources())
		{
			reason = NetworkAccessFailureCodes.SelectorDerivedSourceInvalid;
			return false;
		}

		if (PathProvider == NetworkPathProvider.Automatic &&
			(Source.Kind != NetworkSelectorKind.SystemSelected ||
			 Interface.Kind != NetworkSelectorKind.SystemSelected ||
			 NextHop.Kind != NetworkSelectorKind.SystemSelected))
		{
			reason = NetworkAccessFailureCodes.AutomaticExactConstraintUnsupported;
			return false;
		}

		if (HasDerivedCycle())
		{
			reason = NetworkAccessFailureCodes.SelectorDerivedCycle;
			return false;
		}

		if (!HasOneAddressFamily())
		{
			reason = NetworkAccessFailureCodes.SelectorAddressFamilyMismatch;
			return false;
		}

		reason = string.Empty;
		return true;
	}

	private bool HasOneAddressFamily()
	{
		byte family = 0;
		return AddFamily(Destination.Kind, Destination.ExactValue, Destination.Set, ref family) &&
			AddFamily(Source.Kind, Source.ExactValue, Source.Set, ref family) &&
			AddFamily(NextHop.Kind, NextHop.ExactValue, NextHop.Set, ref family);
	}

	private bool HasDerivedCycle()
	{
		Span<NetworkSelectionDimension> sources = stackalloc NetworkSelectionDimension[7];
		Span<bool> seen = stackalloc bool[7];
		if (Source.Kind == NetworkSelectorKind.Derived) sources[(int)NetworkSelectionDimension.Source] = Source.DerivedFrom;
		if (Interface.Kind == NetworkSelectorKind.Derived) sources[(int)NetworkSelectionDimension.Interface] = Interface.DerivedFrom;
		if (NextHop.Kind == NetworkSelectorKind.Derived) sources[(int)NetworkSelectionDimension.NextHop] = NextHop.DerivedFrom;

		for (var origin = NetworkSelectionDimension.Source; origin <= NetworkSelectionDimension.NextHop; origin++)
		{
			seen.Clear();
			var current = origin;
			while (current != NetworkSelectionDimension.Unspecified && (int)current < sources.Length)
			{
				if (seen[(int)current]) return true;
				seen[(int)current] = true;
				current = sources[(int)current];
			}
		}

		return false;
	}

	private bool ValidateDerivedSources() =>
		(Source.Kind != NetworkSelectorKind.Derived ||
		 Source.DerivedFrom is NetworkSelectionDimension.Destination or NetworkSelectionDimension.Interface or NetworkSelectionDimension.NextHop) &&
		(Interface.Kind != NetworkSelectorKind.Derived ||
		 Interface.DerivedFrom is NetworkSelectionDimension.Source or NetworkSelectionDimension.NextHop) &&
		(NextHop.Kind != NetworkSelectorKind.Derived ||
		 NextHop.DerivedFrom is NetworkSelectionDimension.Destination or NetworkSelectionDimension.Source or NetworkSelectionDimension.Interface);

	private static bool ValidateAddressSelector(
		NetworkSelectorKind kind,
		IpAddressValue exact,
		NetworkAddressSet set,
		bool allowDerived,
		out string reason)
	{
		var valid = kind switch
		{
			NetworkSelectorKind.SystemSelected or NetworkSelectorKind.NotApplicable => true,
			NetworkSelectorKind.Exact => exact.Family is 4 or 6,
			NetworkSelectorKind.AllowedSet or NetworkSelectorKind.PreferredSet or NetworkSelectorKind.ExcludedSet => set.IsValid,
			NetworkSelectorKind.Derived => allowDerived,
			_ => false,
		};
		reason = valid ? string.Empty : NetworkAccessFailureCodes.SelectorUnspecified;
		return valid;
	}

	private static bool ValidateInterfaceSelector(NetworkInterfaceSelection selector, out string reason)
	{
		var valid = selector.Kind switch
		{
			NetworkSelectorKind.SystemSelected or NetworkSelectorKind.NotApplicable => true,
			NetworkSelectorKind.Exact => selector.ExactValue.IsValid,
			NetworkSelectorKind.AllowedSet or NetworkSelectorKind.PreferredSet or NetworkSelectorKind.ExcludedSet => selector.Set.IsValid,
			NetworkSelectorKind.Derived => IsDerivedSource(selector.DerivedFrom),
			_ => false,
		};
		reason = valid ? string.Empty : NetworkAccessFailureCodes.SelectorUnspecified;
		return valid;
	}

	private static bool ValidateNextHopSelector(NetworkNextHopSelection selector, out string reason)
	{
		var valid = selector.Kind switch
		{
			NetworkSelectorKind.SystemSelected or NetworkSelectorKind.NotApplicable => true,
			NetworkSelectorKind.Exact => selector.IsOnLink || selector.ExactValue.Family is 4 or 6,
			NetworkSelectorKind.AllowedSet or NetworkSelectorKind.ExcludedSet => selector.Set.IsValid,
			NetworkSelectorKind.Derived => IsDerivedSource(selector.DerivedFrom),
			_ => false,
		};
		reason = valid ? string.Empty : NetworkAccessFailureCodes.SelectorUnspecified;
		return valid;
	}

	private static bool ValidateLocalEndpoint(NetworkLocalEndpointSelection value, out string reason)
	{
		var valid = value.Kind switch
		{
			NetworkLocalEndpointSelectionKind.SystemSelected or
			NetworkLocalEndpointSelectionKind.Ephemeral or
			NetworkLocalEndpointSelectionKind.NotApplicable => true,
			NetworkLocalEndpointSelectionKind.ExactPort => value.FirstPort > 0 && value.FirstPort == value.LastPort,
			NetworkLocalEndpointSelectionKind.AllowedRange => value.FirstPort > 0 && value.FirstPort <= value.LastPort,
			NetworkLocalEndpointSelectionKind.ReservedLease => value.LeaseId != Guid.Empty,
			_ => false,
		};
		reason = valid ? string.Empty : NetworkAccessFailureCodes.LocalEndpointInvalid;
		return valid;
	}

	private static bool ValidateRouteScope(NetworkRouteScopeSelection value, out string reason)
	{
		var valid = value.Kind switch
		{
			NetworkRouteScopeSelectionKind.CurrentScope or NetworkRouteScopeSelectionKind.NotApplicable => true,
			NetworkRouteScopeSelectionKind.ExactCompartment => value.CompartmentId > 0,
			_ => false,
		};
		reason = valid ? string.Empty : NetworkAccessFailureCodes.RouteScopeInvalid;
		return valid;
	}

	private static bool IsDerivedSource(NetworkSelectionDimension source) =>
		source is >= NetworkSelectionDimension.Destination and <= NetworkSelectionDimension.RouteScope;

	private static bool AddFamily(
		NetworkSelectorKind kind,
		IpAddressValue exact,
		NetworkAddressSet set,
		ref byte family)
	{
		if (kind == NetworkSelectorKind.Exact && exact.Family is 4 or 6 && !MergeFamily(exact.Family, ref family))
		{
			return false;
		}

		if (kind is NetworkSelectorKind.AllowedSet or NetworkSelectorKind.PreferredSet or NetworkSelectorKind.ExcludedSet)
		{
			for (var index = 0; index < set.Count; index++)
			{
				if (!MergeFamily(set[index].Family, ref family)) return false;
			}
		}

		return true;
	}

	private static bool MergeFamily(byte candidate, ref byte family)
	{
		if (family == 0) family = candidate;
		return family == candidate;
	}
}

public enum NetworkAccessLeg : byte
{
	Unspecified = 0,
	Direct = 1,
	ClientLeg = 2,
	EgressLeg = 3,
	NotApplicable = 4,
}

/// <summary>Stable protocol and security isolation data used by connection pools.</summary>
public readonly record struct NetworkAccessSecurityBoundary(
	NetworkAccessLeg Leg,
	string LogicalHost,
	string ServerNameIndication,
	string CertificateValidationName,
	string ResolverIdentity)
{
	public bool IsValid => Leg != NetworkAccessLeg.Unspecified;

	public static NetworkAccessSecurityBoundary NotApplicable() =>
		new(NetworkAccessLeg.NotApplicable, string.Empty, string.Empty, string.Empty, string.Empty);
}

/// <summary>A deterministic digest of stable path and security semantics.</summary>
public readonly record struct NetworkAccessStableKey(ulong Part1, ulong Part2, ulong Part3, ulong Part4)
{
	public bool IsValid => Part1 != 0 || Part2 != 0 || Part3 != 0 || Part4 != 0;

	public static bool TryCreate(
		in RequestedAccessPlan plan,
		in NetworkAccessSecurityBoundary security,
		out NetworkAccessStableKey key,
		out string reason)
	{
		if (!plan.TryValidate(out reason) || !security.IsValid)
		{
			key = default;
			if (string.IsNullOrEmpty(reason)) reason = NetworkAccessFailureCodes.SelectorValueInvalid;
			return false;
		}

		var canonical = NetworkAccessStableKeyBuilder.Build(plan, security);
		var digest = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
		key = new NetworkAccessStableKey(
			BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(0, 8)),
			BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(8, 8)),
			BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(16, 8)),
			BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(24, 8)));
		reason = string.Empty;
		return true;
	}

	public override string ToString() => string.Concat(
		Part1.ToString("x16", CultureInfo.InvariantCulture),
		Part2.ToString("x16", CultureInfo.InvariantCulture),
		Part3.ToString("x16", CultureInfo.InvariantCulture),
		Part4.ToString("x16", CultureInfo.InvariantCulture));
}

public static class NetworkAccessStableKeyBuilder
{
	public static bool TryBuild(
		in RequestedAccessPlan plan,
		in NetworkAccessSecurityBoundary security,
		out NetworkAccessStableKey key,
		out string reason) => NetworkAccessStableKey.TryCreate(plan, security, out key, out reason);

	internal static string Build(in RequestedAccessPlan plan, in NetworkAccessSecurityBoundary security)
	{
		var builder = new StringBuilder(512);
		Add(builder, (byte)plan.PathProvider);
		Add(builder, plan.Destination.Kind, plan.Destination.ExactValue, plan.Destination.Set, plan.Destination.DerivedFrom);
		Add(builder, plan.Source.Kind, plan.Source.ExactValue, plan.Source.Set, plan.Source.DerivedFrom);
		Add(builder, plan.Interface);
		Add(builder, plan.NextHop.Kind, plan.NextHop.ExactValue, plan.NextHop.Set, plan.NextHop.DerivedFrom);
		Add(builder, plan.NextHop.IsOnLink);
		Add(builder, (byte)plan.LocalEndpoint.Kind);
		Add(builder, plan.LocalEndpoint.FirstPort);
		Add(builder, plan.LocalEndpoint.LastPort);
		Add(builder, plan.LocalEndpoint.LeaseId);
		Add(builder, (byte)plan.RouteScope.Kind);
		Add(builder, plan.RouteScope.CompartmentId);
		Add(builder, (byte)plan.IpPacketPolicy.Kind);
		Add(builder, plan.IpPacketPolicy.TimeToLiveOrHopLimit.HasValue);
		Add(builder, plan.IpPacketPolicy.TimeToLiveOrHopLimit.Value);
		Add(builder, plan.IpPacketPolicy.DontFragment);
		Add(builder, plan.IpPacketPolicy.AllowBroadcast);
		Add(builder, plan.IpPacketPolicy.AllowMulticastLoopback);
		Add(builder, (byte)plan.RouteAdapter.Kind);
		Add(builder, plan.RouteAdapter.AdapterId);
		Add(builder, plan.RouteAdapter.CapabilityVersion);
		Add(builder, (byte)security.Leg);
		Add(builder, NormalizeName(security.LogicalHost));
		Add(builder, NormalizeName(security.ServerNameIndication));
		Add(builder, NormalizeName(security.CertificateValidationName));
		Add(builder, NormalizeName(security.ResolverIdentity));
		return builder.ToString();
	}

	private static void Add(
		StringBuilder builder,
		NetworkSelectorKind kind,
		IpAddressValue exact,
		NetworkAddressSet set,
		NetworkSelectionDimension derivedFrom)
	{
		Add(builder, (byte)kind);
		Add(builder, exact);
		Add(builder, (byte)derivedFrom);
		Add(builder, set.Count);
		for (var index = 0; index < set.Count; index++) Add(builder, set[index]);
	}

	private static void Add(StringBuilder builder, NetworkInterfaceSelection selector)
	{
		Add(builder, (byte)selector.Kind);
		Add(builder, selector.ExactValue);
		Add(builder, (byte)selector.DerivedFrom);
		Add(builder, selector.Set.Count);
		for (var index = 0; index < selector.Set.Count; index++) Add(builder, selector.Set[index]);
	}

	private static void Add(StringBuilder builder, IpAddressValue value)
	{
		Add(builder, value.Family);
		Add(builder, value.High);
		Add(builder, value.Low);
	}

	private static void Add(StringBuilder builder, NetworkInterfaceIdentity value)
	{
		Add(builder, value.InterfaceGuid);
		Add(builder, value.Luid);
		Add(builder, NormalizeName(value.Alias));
		Add(builder, value.HasStableIdentity ? 0U : value.InterfaceIndex);
	}

	private static void Add(StringBuilder builder, string value) =>
		Add(builder, (object)value);

	private static void Add(StringBuilder builder, object value)
	{
		var text = value switch
		{
			bool boolean => boolean ? "1" : "0",
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString() ?? string.Empty,
		};
		builder.Append(text.Length).Append(':').Append(text).Append(';');
	}

	private static string NormalizeName(string value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
