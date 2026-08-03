using System.Buffers;
using System.Buffers.Binary;
using System.Data.SqlTypes;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Iwesun.Runtime.Networks;

/// <summary>
/// Immutable numeric IP address. The default value is Null; IPv4 and IPv6
/// addresses are stored in network byte order without retaining source text.
/// </summary>
[JsonConverter(typeof(IpAddressValueJsonConverter))]
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = BinarySize)]
public readonly struct IpAddressValue :
	INullable,
	IEquatable<IpAddressValue>,
	IComparable,
	IComparable<IpAddressValue>,
	ISpanFormattable,
	IParsable<IpAddressValue>,
	ISpanParsable<IpAddressValue>,
	IUtf8SpanFormattable,
	IUtf8SpanParsable<IpAddressValue>
{
	private const byte NullFamily = 0;
	private const byte IPv4Family = 4;
	private const byte IPv6Family = 6;
	public const int BinarySize = 17;

	private readonly ulong _high;
	private readonly ulong _low;
	private readonly byte _family;

	/// <summary>
	/// Creates an IPv4 value from its 32-bit network-byte-order numeric value.
	/// </summary>
	public IpAddressValue(uint ipv4NetworkOrder)
		: this(IPv4Family, 0, ipv4NetworkOrder)
	{
	}

	/// <summary>
	/// Creates an IPv6 value from its high and low 64-bit network-byte-order parts.
	/// </summary>
	public IpAddressValue(ulong ipv6High, ulong ipv6Low)
		: this(IPv6Family, ipv6High, ipv6Low)
	{
	}

	private IpAddressValue(byte family, ulong high, ulong low)
	{
		if (family == NullFamily && (high != 0 || low != 0))
			throw new ArgumentException("A Null IP address must contain only zero address bits.");
		if (family == IPv4Family && (high != 0 || low > uint.MaxValue))
			throw new ArgumentException("An IPv4 address requires High=0 and a 32-bit Low value.");
		if (family is not (NullFamily or IPv4Family or IPv6Family))
			throw new ArgumentOutOfRangeException(nameof(family));

		_family = family;
		_high = high;
		_low = low;
	}

	public IpAddressValue(IPAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		var bytes = address.GetAddressBytes();
		switch (address.AddressFamily)
		{
			case AddressFamily.InterNetwork:
				if (bytes.Length != 4)
					throw new ArgumentException("An IPv4 address must contain exactly 4 bytes.", nameof(address));
				_family = IPv4Family;
				_high = 0;
				_low = BinaryPrimitives.ReadUInt32BigEndian(bytes);
				break;
			case AddressFamily.InterNetworkV6:
				if (bytes.Length != 16)
					throw new ArgumentException("An IPv6 address must contain exactly 16 bytes.", nameof(address));
				if (address.ScopeId != 0)
					throw new ArgumentException(
						"An IP address value cannot contain an IPv6 ScopeId; store the interface constraint separately.",
						nameof(address));
				_family = IPv6Family;
				_high = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(0, 8));
				_low = BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8, 8));
				break;
			default:
				throw new ArgumentException("Only IPv4 and IPv6 addresses are supported.", nameof(address));
		}
	}

	public static IpAddressValue Null => default;
	public byte Family => _family;
	public ulong High => _high;
	public ulong Low => _low;
	public bool IsNull => _family == NullFamily;
	public bool IsIPv4 => _family == IPv4Family;
	public bool IsIPv6 => _family == IPv6Family;
	public int AddressByteCount => IsIPv4 ? 4 : IsIPv6 ? 16 : 0;
	public AddressFamily AddressFamily => IsIPv4
		? AddressFamily.InterNetwork
		: IsIPv6
			? AddressFamily.InterNetworkV6
			: AddressFamily.Unspecified;

	public IpAddressTraits Traits => Classify();
	public IpAddressType AddressType => ClassifyAddressType(Traits);
	public bool IsUnspecified => HasTrait(IpAddressTraits.Unspecified);
	public bool IsLoopback => HasTrait(IpAddressTraits.Loopback);
	public bool IsPrivate => HasTrait(IpAddressTraits.Private);
	public bool IsLinkLocal => HasTrait(IpAddressTraits.LinkLocal);
	public bool IsMulticast => HasTrait(IpAddressTraits.Multicast);
	public bool IsBroadcast => HasTrait(IpAddressTraits.Broadcast);
	public bool IsShared => HasTrait(IpAddressTraits.Shared);
	public bool IsDocumentation => HasTrait(IpAddressTraits.Documentation);
	public bool IsBenchmark => HasTrait(IpAddressTraits.Benchmark);
	public bool IsReserved => HasTrait(IpAddressTraits.Reserved);
	public bool IsGlobalUnicast => HasTrait(IpAddressTraits.GlobalUnicast);
	public bool IsTransition => HasTrait(IpAddressTraits.Transition);
	public bool IsIPv4MappedToIPv6 => HasTrait(IpAddressTraits.IPv4Mapped);
	public bool IsUniqueLocal => HasTrait(IpAddressTraits.UniqueLocal);
	public bool IsSiteLocal => HasTrait(IpAddressTraits.SiteLocal);
	public bool IsLocalNetwork => IsPrivate || IsLinkLocal;
	public bool IsPublic => IsGlobalUnicast;
	public bool IsUnicast => !IsNull
		&& !IsUnspecified
		&& !IsMulticast
		&& !IsBroadcast;
	public bool RequiresInterfaceScope =>
		IsIPv6 && (Traits & (IpAddressTraits.LinkLocal | IpAddressTraits.Multicast)) != 0;

	public static IpAddressValue FromIPv4(uint networkOrder) =>
		new(networkOrder);

	public static IpAddressValue FromIPv6(ulong high, ulong low) =>
		new(high, low);

	public static IpAddressValue FromIPAddress(IPAddress address) => new(address);

	public IPAddress ToIPAddress()
	{
		if (IsNull)
			throw new SqlNullValueException("A Null IP address has no System.Net.IPAddress value.");
		Span<byte> bytes = stackalloc byte[16];
		if (IsIPv4)
		{
			BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)_low));
			return new IPAddress(bytes[..4]);
		}
		BinaryPrimitives.WriteUInt64BigEndian(bytes, _high);
		BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], _low);
		return new IPAddress(bytes);
	}

	public IPAddress ToIPAddress(uint scopeId)
	{
		if (scopeId == 0)
			return ToIPAddress();
		if (!RequiresInterfaceScope)
			throw new ArgumentOutOfRangeException(
				nameof(scopeId),
				"Only IPv6 link-local and multicast addresses can use an interface ScopeId.");
		Span<byte> bytes = stackalloc byte[16];
		BinaryPrimitives.WriteUInt64BigEndian(bytes, _high);
		BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], _low);
		return new IPAddress(bytes, scopeId);
	}

	public static IpAddressValue FromIPv4Bytes(ReadOnlySpan<byte> addressBytes)
	{
		if (addressBytes.Length != 4)
			throw new ArgumentException("An IPv4 address must contain exactly 4 bytes.", nameof(addressBytes));
		return new IpAddressValue(BinaryPrimitives.ReadUInt32BigEndian(addressBytes));
	}

	public static IpAddressValue FromIPv6Bytes(ReadOnlySpan<byte> addressBytes)
	{
		if (addressBytes.Length != 16)
			throw new ArgumentException("An IPv6 address must contain exactly 16 bytes.", nameof(addressBytes));
		return new IpAddressValue(
			BinaryPrimitives.ReadUInt64BigEndian(addressBytes[..8]),
			BinaryPrimitives.ReadUInt64BigEndian(addressBytes[8..]));
	}

	public bool TryWriteAddressBytes(Span<byte> destination, out int bytesWritten)
	{
		bytesWritten = 0;
		if (IsNull || destination.Length < AddressByteCount)
			return false;
		if (IsIPv4)
		{
			BinaryPrimitives.WriteUInt32BigEndian(destination, checked((uint)_low));
			bytesWritten = 4;
			return true;
		}
		BinaryPrimitives.WriteUInt64BigEndian(destination, _high);
		BinaryPrimitives.WriteUInt64BigEndian(destination[8..], _low);
		bytesWritten = 16;
		return true;
	}

	public bool TryWriteBinary(Span<byte> destination)
	{
		if (destination.Length < BinarySize)
			return false;
		destination[..BinarySize].Clear();
		destination[0] = _family;
		BinaryPrimitives.WriteUInt64BigEndian(destination[1..9], _high);
		BinaryPrimitives.WriteUInt64BigEndian(destination[9..17], _low);
		return true;
	}

	public static bool TryReadBinary(ReadOnlySpan<byte> source, out IpAddressValue value)
	{
		value = Null;
		if (source.Length < BinarySize)
			return false;
		var family = source[0];
		var high = BinaryPrimitives.ReadUInt64BigEndian(source[1..9]);
		var low = BinaryPrimitives.ReadUInt64BigEndian(source[9..17]);
		if (family == NullFamily)
			return high == 0 && low == 0;
		if (family == IPv4Family)
		{
			if (high != 0 || low > uint.MaxValue)
				return false;
			value = new IpAddressValue(family, 0, low);
			return true;
		}
		if (family != IPv6Family)
			return false;
		value = new IpAddressValue(family, high, low);
		return true;
	}

	public static IpAddressValue Parse(string s, IFormatProvider? provider)
	{
		ArgumentNullException.ThrowIfNull(s);
		return Parse(s.AsSpan(), provider);
	}

	public static IpAddressValue Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
	{
		if (!TryParse(s, provider, out var result))
			throw new FormatException($"'{s.ToString()}' is not a valid IPv4 or IPv6 address.");
		return result;
	}

	public static IpAddressValue Parse(string s) =>
		Parse(s, CultureInfo.InvariantCulture);

	public static IpAddressValue Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
	{
		if (!TryParse(utf8Text, provider, out var result))
			throw new FormatException("The UTF-8 text is not a valid IPv4 or IPv6 address.");
		return result;
	}

	public static bool TryParse(string? s, IFormatProvider? provider, out IpAddressValue result)
	{
		result = Null;
		return s is not null && TryParse(s.AsSpan(), provider, out result);
	}

	public static bool TryParse(
		ReadOnlySpan<char> s,
		IFormatProvider? provider,
		out IpAddressValue result)
	{
		_ = provider;
		result = Null;
		s = s.Trim();
		if (s.IsEmpty || !IPAddress.TryParse(s, out var parsed))
			return false;
		if (parsed.AddressFamily == AddressFamily.InterNetworkV6 && parsed.ScopeId != 0)
			return false;
		result = new IpAddressValue(parsed);
		return true;
	}

	public static bool TryParse(
		ReadOnlySpan<byte> utf8Text,
		IFormatProvider? provider,
		out IpAddressValue result)
	{
		result = Null;
		var charCount = Encoding.UTF8.GetCharCount(utf8Text);
		char[]? rented = null;
		Span<char> characters = charCount <= 128
			? stackalloc char[128]
			: rented = ArrayPool<char>.Shared.Rent(charCount);
		try
		{
			if (!Encoding.UTF8.TryGetChars(utf8Text, characters, out var charsWritten))
				return false;
			return TryParse(characters[..charsWritten], provider, out result);
		}
		finally
		{
			if (rented is not null) ArrayPool<char>.Shared.Return(rented);
		}
	}

	public override string ToString() => ToString("G", CultureInfo.InvariantCulture);
	public string ToString(string? format, IFormatProvider? formatProvider)
	{
		if (!string.IsNullOrEmpty(format) && !string.Equals(format, "G", StringComparison.OrdinalIgnoreCase))
			throw new FormatException($"The '{format}' IP address format is not supported.");
		Span<char> buffer = stackalloc char[45];
		if (!TryFormat(buffer, out var written, format, formatProvider))
			throw new InvalidOperationException("The IP address could not be formatted.");
		return buffer[..written].ToString();
	}

	public bool TryFormat(
		Span<char> destination,
		out int charsWritten,
		ReadOnlySpan<char> format,
		IFormatProvider? provider)
	{
		charsWritten = 0;
		if (!format.IsEmpty && !format.Equals("G", StringComparison.OrdinalIgnoreCase))
			return false;
		_ = provider;
		Span<char> buffer = stackalloc char[45];
		var length = IsNull
			? 0
			: IsIPv4
				? FormatIPv4(buffer)
				: FormatIPv6(buffer);
		if (length > destination.Length)
			return false;
		buffer[..length].CopyTo(destination);
		charsWritten = length;
		return true;
	}

	public bool TryFormat(
		Span<byte> utf8Destination,
		out int bytesWritten,
		ReadOnlySpan<char> format,
		IFormatProvider? provider)
	{
		bytesWritten = 0;
		Span<char> characters = stackalloc char[45];
		if (!TryFormat(characters, out var charsWritten, format, provider))
			return false;
		return Encoding.UTF8.TryGetBytes(
			characters[..charsWritten],
			utf8Destination,
			out bytesWritten);
	}

	private int FormatIPv4(Span<char> destination)
		=> FormatIPv4Value(destination, checked((uint)_low));

	private static int FormatIPv4Value(Span<char> destination, uint value)
	{
		var position = 0;
		for (var shift = 24; shift >= 0; shift -= 8)
		{
			if (position != 0)
				destination[position++] = '.';
			var octet = checked((byte)((value >> shift) & byte.MaxValue));
			if (!octet.TryFormat(destination[position..], out var written, provider: CultureInfo.InvariantCulture))
				throw new InvalidOperationException("The IPv4 octet could not be formatted.");
			position += written;
		}
		return position;
	}

	private int FormatIPv6(Span<char> destination)
	{
		var embeddedPrefix = _high == 0 ? checked((uint)(_low >> 32)) : uint.MaxValue;
		var embeddedAddress = checked((uint)(_low & uint.MaxValue));
		if (embeddedPrefix == ushort.MaxValue)
		{
			"::ffff:".AsSpan().CopyTo(destination);
			return 7 + FormatIPv4Value(destination[7..], embeddedAddress);
		}
		if (embeddedPrefix == 0 && embeddedAddress > 1)
		{
			"::".AsSpan().CopyTo(destination);
			return 2 + FormatIPv4Value(destination[2..], embeddedAddress);
		}
		Span<ushort> groups = stackalloc ushort[8];
		for (var index = 0; index < 4; index++)
		{
			groups[index] = checked((ushort)((_high >> (48 - (index * 16))) & ushort.MaxValue));
			groups[index + 4] = checked((ushort)((_low >> (48 - (index * 16))) & ushort.MaxValue));
		}
		var bestStart = -1;
		var bestLength = 1;
		for (var index = 0; index < groups.Length;)
		{
			if (groups[index] != 0)
			{
				index++;
				continue;
			}
			var start = index;
			while (index < groups.Length && groups[index] == 0) index++;
			var length = index - start;
			if (length > bestLength)
			{
				bestStart = start;
				bestLength = length;
			}
		}
		var position = 0;
		for (var index = 0; index < groups.Length;)
		{
			if (index == bestStart)
			{
				destination[position++] = ':';
				destination[position++] = ':';
				index += bestLength;
				continue;
			}
			if (position != 0 && destination[position - 1] != ':')
				destination[position++] = ':';
			if (!groups[index].TryFormat(
				destination[position..],
				out var written,
				"x",
				CultureInfo.InvariantCulture))
				throw new InvalidOperationException("The IPv6 group could not be formatted.");
			position += written;
			index++;
		}
		return position;
	}

	public bool Equals(IpAddressValue other) =>
		_family == other._family
		&& _high == other._high
		&& _low == other._low;

	public override bool Equals(object? obj) => obj is IpAddressValue other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(_family, _high, _low);
	public int CompareTo(IpAddressValue other)
	{
		if (IsNull)
			return other.IsNull ? 0 : 1;
		if (other.IsNull)
			return -1;
		var result = _family.CompareTo(other._family);
		if (result != 0) return result;
		result = _high.CompareTo(other._high);
		if (result != 0) return result;
		return _low.CompareTo(other._low);
	}

	public int CompareTo(object? obj)
	{
		if (obj is null)
			return 1;
		if (obj is IpAddressValue other)
			return CompareTo(other);
		throw new ArgumentException(
			$"Object must be of type {nameof(IpAddressValue)}.",
			nameof(obj));
	}

	public static bool operator ==(IpAddressValue left, IpAddressValue right) => left.Equals(right);
	public static bool operator !=(IpAddressValue left, IpAddressValue right) => !left.Equals(right);
	public static bool operator <(IpAddressValue left, IpAddressValue right) => left.CompareTo(right) < 0;
	public static bool operator >(IpAddressValue left, IpAddressValue right) => left.CompareTo(right) > 0;
	public static implicit operator IpAddressValue(IPAddress value) => new(value);
	public static implicit operator IpAddressValue(string? value) =>
		string.IsNullOrWhiteSpace(value)
			? Null
			: Parse(value, CultureInfo.InvariantCulture);
	public static implicit operator string(IpAddressValue value) => value.ToString();
	public static explicit operator IPAddress(IpAddressValue value) => value.ToIPAddress();

	private bool HasTrait(IpAddressTraits trait) => (Traits & trait) != 0;

	private IpAddressTraits Classify()
	{
		if (IsNull)
			return IpAddressTraits.None;
		return IsIPv4 ? ClassifyIPv4() : ClassifyIPv6();
	}

	private IpAddressTraits ClassifyIPv4()
	{
		var value = checked((uint)_low);
		var traits = IpAddressTraits.IPv4;
		if (value == 0) return traits | IpAddressTraits.Unspecified | IpAddressTraits.Reserved;
		if (InIPv4Prefix(value, 0x7F000000, 8))
			return traits | IpAddressTraits.Loopback | IpAddressTraits.Reserved;
		if (InIPv4Prefix(value, 0x0A000000, 8)
			|| InIPv4Prefix(value, 0xAC100000, 12)
			|| InIPv4Prefix(value, 0xC0A80000, 16))
			return traits | IpAddressTraits.Private;
		if (InIPv4Prefix(value, 0xA9FE0000, 16))
			return traits | IpAddressTraits.LinkLocal;
		if (InIPv4Prefix(value, 0x64400000, 10))
			return traits | IpAddressTraits.Shared;
		if (InIPv4Prefix(value, 0xC0000200, 24)
			|| InIPv4Prefix(value, 0xC6336400, 24)
			|| InIPv4Prefix(value, 0xCB007100, 24))
			return traits | IpAddressTraits.Documentation | IpAddressTraits.Reserved;
		if (InIPv4Prefix(value, 0xC6120000, 15))
			return traits | IpAddressTraits.Benchmark | IpAddressTraits.Reserved;
		if (value == uint.MaxValue)
			return traits | IpAddressTraits.Broadcast | IpAddressTraits.Reserved;
		if (InIPv4Prefix(value, 0xE0000000, 4))
			return traits | IpAddressTraits.Multicast;
		if (InIPv4Prefix(value, 0, 8)
			|| InIPv4Prefix(value, 0xC0000000, 24)
			|| InIPv4Prefix(value, 0xF0000000, 4))
			return traits | IpAddressTraits.Reserved;
		return traits | IpAddressTraits.GlobalUnicast;
	}

	private IpAddressTraits ClassifyIPv6()
	{
		var traits = IpAddressTraits.IPv6;
		if (_high == 0 && _low == 0)
			return traits | IpAddressTraits.Unspecified | IpAddressTraits.Reserved;
		if (_high == 0 && _low == 1)
			return traits | IpAddressTraits.Loopback | IpAddressTraits.Reserved;
		if ((_high & 0xFFC0000000000000UL) == 0xFE80000000000000UL)
			return traits | IpAddressTraits.LinkLocal;
		if ((_high & 0xFE00000000000000UL) == 0xFC00000000000000UL)
			return traits | IpAddressTraits.Private | IpAddressTraits.UniqueLocal;
		if ((_high & 0xFFC0000000000000UL) == 0xFEC0000000000000UL)
			return traits | IpAddressTraits.Private | IpAddressTraits.SiteLocal | IpAddressTraits.Reserved;
		if ((_high & 0xFF00000000000000UL) == 0xFF00000000000000UL)
			return traits | IpAddressTraits.Multicast;
		if ((_high & 0xFFFFFFFF00000000UL) == 0x20010DB800000000UL)
			return traits | IpAddressTraits.Documentation | IpAddressTraits.Reserved;
		if ((_high & 0xFFFFFFFFFFFF0000UL) == 0x2001000200000000UL)
			return traits | IpAddressTraits.Benchmark | IpAddressTraits.Reserved;
		if (_high == 0 && (_low >> 32) == 0x0000FFFFUL)
			return traits | IpAddressTraits.IPv4Mapped | IpAddressTraits.Transition;
		if ((_high & 0xFFFFFFFF00000000UL) == 0x2001000000000000UL
			|| (_high & 0xFFFF000000000000UL) == 0x2002000000000000UL)
			return traits | IpAddressTraits.Transition;
		if ((_high & 0xE000000000000000UL) == 0x2000000000000000UL)
			return traits | IpAddressTraits.GlobalUnicast;
		return traits | IpAddressTraits.Reserved;
	}

	private static IpAddressType ClassifyAddressType(IpAddressTraits traits)
	{
		if (traits == IpAddressTraits.None) return IpAddressType.Null;
		if ((traits & IpAddressTraits.Loopback) != 0) return IpAddressType.Loopback;
		if ((traits & IpAddressTraits.LinkLocal) != 0) return IpAddressType.LinkLocal;
		if ((traits & IpAddressTraits.Multicast) != 0) return IpAddressType.Multicast;
		if ((traits & IpAddressTraits.Private) != 0) return IpAddressType.Private;
		if ((traits & (IpAddressTraits.Transition | IpAddressTraits.IPv4Mapped)) != 0)
			return IpAddressType.Transition;
		if ((traits & IpAddressTraits.GlobalUnicast) != 0) return IpAddressType.Public;
		return IpAddressType.Unset;
	}

	private static bool InIPv4Prefix(uint value, uint network, int prefixLength)
	{
		var mask = prefixLength == 0 ? 0U : uint.MaxValue << (32 - prefixLength);
		return (value & mask) == (network & mask);
	}
}

public sealed class IpAddressValueJsonConverter : JsonConverter<IpAddressValue>
{
	public override IpAddressValue Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
			return IpAddressValue.Null;
		if (reader.TokenType != JsonTokenType.String)
			throw new JsonException("An IP address must be represented as a JSON string or null.");
		var text = reader.GetString();
		if (string.IsNullOrWhiteSpace(text))
			return IpAddressValue.Null;
		if (!IpAddressValue.TryParse(text, CultureInfo.InvariantCulture, out var value))
			throw new JsonException($"'{text}' is not a valid IPv4 or IPv6 address.");
		return value;
	}

	public override void Write(
		Utf8JsonWriter writer,
		IpAddressValue value,
		JsonSerializerOptions options)
	{
		if (value.IsNull)
		{
			writer.WriteNullValue();
			return;
		}
		writer.WriteStringValue(value.ToString());
	}
}
