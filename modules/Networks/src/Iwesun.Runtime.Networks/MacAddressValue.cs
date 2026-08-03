using System.Buffers;
using System.Data.SqlTypes;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Iwesun.Runtime.Networks;

/// <summary>
/// Immutable numeric EUI-48 MAC address. The default value is Null; an actual
/// all-zero address remains a distinct, non-null value.
/// </summary>
[JsonConverter(typeof(MacAddressValueJsonConverter))]
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = BinarySize)]
public readonly struct MacAddressValue :
	INullable,
	IEquatable<MacAddressValue>,
	IComparable,
	IComparable<MacAddressValue>,
	ISpanFormattable,
	IParsable<MacAddressValue>,
	ISpanParsable<MacAddressValue>,
	IUtf8SpanFormattable,
	IUtf8SpanParsable<MacAddressValue>
{
	private const ulong ValueMask = 0x0000FFFFFFFFFFFFUL;
	private const byte ValueState = 1;
	public const int AddressByteCount = 6;
	public const int BinarySize = 7;

	private readonly uint _high;
	private readonly ushort _low;
	private readonly byte _state;

	private MacAddressValue(ulong value)
	{
		if ((value & ~ValueMask) != 0)
			throw new ArgumentOutOfRangeException(nameof(value), "A MAC address contains exactly 48 bits.");
		_high = checked((uint)(value >> 16));
		_low = checked((ushort)(value & ushort.MaxValue));
		_state = ValueState;
	}

	public MacAddressValue(PhysicalAddress address)
	{
		ArgumentNullException.ThrowIfNull(address);
		var bytes = address.GetAddressBytes();
		if (bytes.Length != AddressByteCount)
			throw new ArgumentException("Only 48-bit physical addresses are supported.", nameof(address));
		var value = ReadValue(bytes);
		_high = checked((uint)(value >> 16));
		_low = checked((ushort)(value & ushort.MaxValue));
		_state = ValueState;
	}

	private ulong Value => ((ulong)_high << 16) | _low;
	public static MacAddressValue Null => default;
	public bool IsNull => _state == 0;
	public ulong NumericValue => IsNull
		? throw new SqlNullValueException("A Null MAC address has no numeric value.")
		: Value;
	public uint OrganizationallyUniqueIdentifier => IsNull
		? throw new SqlNullValueException("A Null MAC address has no OUI.")
		: checked((uint)(Value >> 24));
	public uint NetworkInterfaceControllerIdentifier => IsNull
		? throw new SqlNullValueException("A Null MAC address has no interface identifier.")
		: checked((uint)(Value & 0xFFFFFFUL));

	public MacAddressTraits Traits => Classify();
	public bool IsUnspecified => HasTrait(MacAddressTraits.Unspecified);
	public bool IsBroadcast => HasTrait(MacAddressTraits.Broadcast);
	public bool IsUnicast => HasTrait(MacAddressTraits.Unicast);
	public bool IsMulticast => HasTrait(MacAddressTraits.Multicast);
	public bool IsIndividual => HasTrait(MacAddressTraits.Individual);
	public bool IsGroup => HasTrait(MacAddressTraits.Group);
	public bool IsUniversallyAdministered => HasTrait(MacAddressTraits.UniversallyAdministered);
	public bool IsLocallyAdministered => HasTrait(MacAddressTraits.LocallyAdministered);
	public bool IsValidIdentity => !IsNull && !IsUnspecified && !IsBroadcast && IsUnicast;

	public static MacAddressValue FromAddressBytes(ReadOnlySpan<byte> addressBytes)
	{
		if (addressBytes.Length != AddressByteCount)
			throw new ArgumentException("A MAC address must contain exactly 6 bytes.", nameof(addressBytes));
		return new MacAddressValue(ReadValue(addressBytes));
	}

	public bool TryWriteAddressBytes(Span<byte> destination)
	{
		if (IsNull || destination.Length < AddressByteCount)
			return false;
		WriteValue(destination, Value);
		return true;
	}

	public PhysicalAddress ToPhysicalAddress()
	{
		if (IsNull)
			throw new SqlNullValueException("A Null MAC address has no PhysicalAddress value.");
		var bytes = new byte[AddressByteCount];
		WriteValue(bytes, Value);
		return new PhysicalAddress(bytes);
	}

	public bool TryWriteBinary(Span<byte> destination)
	{
		if (destination.Length < BinarySize)
			return false;
		destination[..BinarySize].Clear();
		if (IsNull)
			return true;
		destination[0] = ValueState;
		WriteValue(destination[1..], Value);
		return true;
	}

	public static bool TryReadBinary(ReadOnlySpan<byte> source, out MacAddressValue value)
	{
		value = Null;
		if (source.Length < BinarySize)
			return false;
		if (source[0] == 0)
			return source[1..BinarySize].IndexOfAnyExcept((byte)0) < 0;
		if (source[0] != ValueState)
			return false;
		value = new MacAddressValue(ReadValue(source[1..BinarySize]));
		return true;
	}

	public static MacAddressValue Parse(string s, IFormatProvider? provider)
	{
		ArgumentNullException.ThrowIfNull(s);
		return Parse(s.AsSpan(), provider);
	}

	public static MacAddressValue Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
	{
		if (!TryParse(s, provider, out var result))
			throw new FormatException($"'{s.ToString()}' is not a valid EUI-48 MAC address.");
		return result;
	}

	public static MacAddressValue Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider)
	{
		if (!TryParse(utf8Text, provider, out var result))
			throw new FormatException("The UTF-8 text is not a valid EUI-48 MAC address.");
		return result;
	}

	public static bool TryParse(string? s, IFormatProvider? provider, out MacAddressValue result)
	{
		result = Null;
		return s is not null && TryParse(s.AsSpan(), provider, out result);
	}

	public static bool TryParse(
		ReadOnlySpan<char> s,
		IFormatProvider? provider,
		out MacAddressValue result)
	{
		_ = provider;
		result = Null;
		s = s.Trim();
		if (s.IsEmpty)
			return true;
		Span<char> compact = stackalloc char[12];
		if (!TryCompact(s, compact))
			return false;
		ulong value = 0;
		for (var index = 0; index < compact.Length; index++)
		{
			var digit = HexValue(compact[index]);
			if (digit < 0)
				return false;
			value = (value << 4) | checked((uint)digit);
		}
		result = new MacAddressValue(value);
		return true;
	}

	public static bool TryParse(
		ReadOnlySpan<byte> utf8Text,
		IFormatProvider? provider,
		out MacAddressValue result)
	{
		result = Null;
		var charCount = Encoding.UTF8.GetCharCount(utf8Text);
		char[]? rented = null;
		Span<char> characters = charCount <= 64
			? stackalloc char[64]
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
		_ = formatProvider;
		var normalizedFormat = string.IsNullOrEmpty(format) ? 'G' : char.ToUpperInvariant(format[0]);
		if (format?.Length > 1 || normalizedFormat is not ('G' or 'N' or 'D' or 'C'))
			throw new FormatException($"The '{format}' MAC address format is not supported.");
		if (IsNull)
			return string.Empty;
		Span<char> buffer = stackalloc char[17];
		if (!TryFormat(buffer, out var written, normalizedFormat.ToString(), CultureInfo.InvariantCulture))
			throw new InvalidOperationException("The MAC address could not be formatted.");
		return buffer[..written].ToString();
	}

	public bool TryFormat(
		Span<char> destination,
		out int charsWritten,
		ReadOnlySpan<char> format,
		IFormatProvider? provider)
	{
		_ = provider;
		charsWritten = 0;
		var normalizedFormat = format.IsEmpty ? 'G' : char.ToUpperInvariant(format[0]);
		if (format.Length > 1 || normalizedFormat is not ('G' or 'N' or 'D' or 'C'))
			return false;
		if (IsNull)
			return true;

		var requiredLength = normalizedFormat switch
		{
			'N' => 12,
			'C' => 14,
			_ => 17
		};
		if (destination.Length < requiredLength)
			return false;

		Span<char> compact = stackalloc char[12];
		var value = Value;
		for (var index = compact.Length - 1; index >= 0; index--)
		{
			compact[index] = HexCharacter(checked((int)(value & 0xFUL)));
			value >>= 4;
		}

		if (normalizedFormat == 'N')
		{
			compact.CopyTo(destination);
			charsWritten = compact.Length;
			return true;
		}

		var separator = normalizedFormat == 'D' ? '-' : ':';
		if (normalizedFormat == 'C')
		{
			compact[..4].CopyTo(destination);
			destination[4] = '.';
			compact[4..8].CopyTo(destination[5..]);
			destination[9] = '.';
			compact[8..].CopyTo(destination[10..]);
			charsWritten = requiredLength;
			return true;
		}

		var target = 0;
		for (var index = 0; index < compact.Length; index += 2)
		{
			if (target != 0)
				destination[target++] = separator;
			destination[target++] = compact[index];
			destination[target++] = compact[index + 1];
		}
		charsWritten = requiredLength;
		return true;
	}

	public bool TryFormat(
		Span<byte> utf8Destination,
		out int bytesWritten,
		ReadOnlySpan<char> format,
		IFormatProvider? provider)
	{
		bytesWritten = 0;
		Span<char> characters = stackalloc char[17];
		if (!TryFormat(characters, out var charsWritten, format, provider))
			return false;
		return Encoding.UTF8.TryGetBytes(
			characters[..charsWritten],
			utf8Destination,
			out bytesWritten);
	}

	public bool Equals(MacAddressValue other) =>
		_state == other._state && _high == other._high && _low == other._low;
	public override bool Equals(object? obj) => obj is MacAddressValue other && Equals(other);
	public override int GetHashCode() => HashCode.Combine(_state, _high, _low);
	public int CompareTo(MacAddressValue other)
	{
		if (IsNull)
			return other.IsNull ? 0 : 1;
		if (other.IsNull)
			return -1;
		var result = _state.CompareTo(other._state);
		return result != 0 ? result : Value.CompareTo(other.Value);
	}

	public int CompareTo(object? obj)
	{
		if (obj is null)
			return 1;
		if (obj is MacAddressValue other)
			return CompareTo(other);
		throw new ArgumentException(
			$"Object must be of type {nameof(MacAddressValue)}.",
			nameof(obj));
	}

	public static bool operator ==(MacAddressValue left, MacAddressValue right) => left.Equals(right);
	public static bool operator !=(MacAddressValue left, MacAddressValue right) => !left.Equals(right);
	public static bool operator <(MacAddressValue left, MacAddressValue right) => left.CompareTo(right) < 0;
	public static bool operator >(MacAddressValue left, MacAddressValue right) => left.CompareTo(right) > 0;
	public static implicit operator MacAddressValue(PhysicalAddress value) => new(value);
	public static implicit operator MacAddressValue(string? value) =>
		string.IsNullOrWhiteSpace(value)
			? Null
			: Parse(value, CultureInfo.InvariantCulture);
	public static implicit operator string(MacAddressValue value) => value.ToString();
	public static explicit operator PhysicalAddress(MacAddressValue value) => value.ToPhysicalAddress();

	private bool HasTrait(MacAddressTraits trait) => (Traits & trait) != 0;

	private MacAddressTraits Classify()
	{
		if (IsNull)
			return MacAddressTraits.None;
		var value = Value;
		var traits = MacAddressTraits.Eui48;
		if (value == 0)
			return traits | MacAddressTraits.Unspecified;
		if (value == ValueMask)
			return traits | MacAddressTraits.Broadcast | MacAddressTraits.Group;

		var firstOctet = checked((byte)(value >> 40));
		if ((firstOctet & 0x01) == 0)
			traits |= MacAddressTraits.Unicast | MacAddressTraits.Individual;
		else
			traits |= MacAddressTraits.Multicast | MacAddressTraits.Group;
		traits |= (firstOctet & 0x02) == 0
			? MacAddressTraits.UniversallyAdministered
			: MacAddressTraits.LocallyAdministered;
		return traits;
	}

	private static bool TryCompact(ReadOnlySpan<char> source, Span<char> destination)
	{
		if (source.Length == 12)
		{
			source.CopyTo(destination);
			return true;
		}
		if (source.Length == 17 && (source[2] == ':' || source[2] == '-'))
		{
			var separator = source[2];
			for (var group = 0; group < 6; group++)
			{
				var sourceIndex = group * 3;
				if (group > 0 && source[sourceIndex - 1] != separator)
					return false;
				destination[group * 2] = source[sourceIndex];
				destination[(group * 2) + 1] = source[sourceIndex + 1];
			}
			return true;
		}
		if (source.Length == 14 && source[4] == '.' && source[9] == '.')
		{
			source[..4].CopyTo(destination);
			source[5..9].CopyTo(destination[4..]);
			source[10..14].CopyTo(destination[8..]);
			return true;
		}
		return false;
	}

	private static ulong ReadValue(ReadOnlySpan<byte> source)
	{
		ulong value = 0;
		for (var index = 0; index < AddressByteCount; index++)
			value = (value << 8) | source[index];
		return value;
	}

	private static void WriteValue(Span<byte> destination, ulong value)
	{
		for (var index = AddressByteCount - 1; index >= 0; index--)
		{
			destination[index] = checked((byte)(value & 0xFFUL));
			value >>= 8;
		}
	}

	private static int HexValue(char character) => character switch
	{
		>= '0' and <= '9' => character - '0',
		>= 'A' and <= 'F' => character - 'A' + 10,
		>= 'a' and <= 'f' => character - 'a' + 10,
		_ => -1
	};

	private static char HexCharacter(int value) =>
		(char)(value < 10 ? '0' + value : 'A' + value - 10);
}

public sealed class MacAddressValueJsonConverter : JsonConverter<MacAddressValue>
{
	public override MacAddressValue Read(
		ref Utf8JsonReader reader,
		Type typeToConvert,
		JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.Null)
			return MacAddressValue.Null;
		if (reader.TokenType != JsonTokenType.String)
			throw new JsonException("A MAC address must be represented as a JSON string or null.");
		var text = reader.GetString();
		if (!MacAddressValue.TryParse(text, CultureInfo.InvariantCulture, out var value))
			throw new JsonException($"'{text}' is not a valid EUI-48 MAC address.");
		return value;
	}

	public override void Write(
		Utf8JsonWriter writer,
		MacAddressValue value,
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
