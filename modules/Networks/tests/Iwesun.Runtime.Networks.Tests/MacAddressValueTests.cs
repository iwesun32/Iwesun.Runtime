using System.Data.SqlTypes;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Xunit;
namespace Iwesun.Runtime.Networks.Tests;

public sealed class MacAddressValueTests
{
	[Fact]
	public void Layout_ShouldUseExactlySevenBytes()
	{
		Assert.Equal(7, MacAddressValue.BinarySize);
		Assert.Equal(7, Unsafe.SizeOf<MacAddressValue>());
		Assert.Equal(7, Marshal.SizeOf<MacAddressValue>());
		Assert.Equal(
			["_high", "_low", "_state"],
			typeof(MacAddressValue)
				.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
				.Select(static field => field.Name)
				.ToArray());
	}

	[Fact]
	public void Null_ShouldRemainDistinctFromAllZeroAddress()
	{
		var nullValue = default(MacAddressValue);
		var zero = MacAddressValue.Parse("00:00:00:00:00:00", null);

		Assert.True(nullValue.IsNull);
		Assert.Equal(string.Empty, nullValue.ToString());
		Assert.Throws<SqlNullValueException>(() => nullValue.ToPhysicalAddress());
		Assert.False(zero.IsNull);
		Assert.True(zero.IsUnspecified);
		Assert.False(zero.IsValidIdentity);
		Assert.NotEqual(nullValue, zero);
	}

	[Theory]
	[InlineData("FC:34:97:E3:A9:20")]
	[InlineData("fc-34-97-e3-a9-20")]
	[InlineData("FC3497E3A920")]
	[InlineData("FC34.97E3.A920")]
	public void Parse_ShouldAcceptStandardFormsAndProduceCanonicalText(string text)
	{
		var value = MacAddressValue.Parse(text, null);

		Assert.Equal("FC:34:97:E3:A9:20", value.ToString());
		Assert.Equal("FC3497E3A920", value.ToString("N", null));
		Assert.Equal("FC-34-97-E3-A9-20", value.ToString("D", null));
		Assert.Equal("FC34.97E3.A920", value.ToString("C", null));
	}

	[Theory]
	[InlineData("FC:34:97:E3:A9:20", "G")]
	[InlineData("FC3497E3A920", "N")]
	[InlineData("FC-34-97-E3-A9-20", "D")]
	[InlineData("FC34.97E3.A920", "C")]
	public void Utf8GenericContracts_ShouldRoundTripEveryFormat(string expected, string format)
	{
		var value = ParseUtf8<MacAddressValue>("fc-34-97-e3-a9-20"u8);
		Span<byte> destination = stackalloc byte[17];

		Assert.True(value.TryFormat(destination, out var written, format, null));
		Assert.Equal(expected, Encoding.UTF8.GetString(destination[..written]));
		Assert.Equal(value, MacAddressValue.Parse(destination[..written], null));
		Assert.IsAssignableFrom<IUtf8SpanFormattable>(value);
	}

	[Theory]
	[InlineData("FC:34:97:E3:A9")]
	[InlineData("FC:34-97:E3:A9:20")]
	[InlineData("GG:34:97:E3:A9:20")]
	[InlineData("FC34.97E3-A920")]
	[InlineData("0011.2233.4455.6677")]
	public void Parse_ShouldRejectMalformedOrNonEui48Values(string text)
	{
		Assert.False(MacAddressValue.TryParse(text, null, out _));
		Assert.Throws<FormatException>(() => MacAddressValue.Parse(text, null));
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t\r\n")]
	public void Parse_ShouldTreatEmptyTextAsNull(string text)
	{
		Assert.True(MacAddressValue.TryParse(text, null, out var value));
		Assert.True(value.IsNull);
		Assert.True(MacAddressValue.Parse(text, null).IsNull);
	}

	[Theory]
	[InlineData("10:B7:13:F7:92:6F", MacAddressTraits.Unicast | MacAddressTraits.Individual | MacAddressTraits.UniversallyAdministered)]
	[InlineData("12:B7:13:F7:92:6F", MacAddressTraits.Unicast | MacAddressTraits.Individual | MacAddressTraits.LocallyAdministered)]
	[InlineData("01:00:5E:00:00:01", MacAddressTraits.Multicast | MacAddressTraits.Group | MacAddressTraits.UniversallyAdministered)]
	[InlineData("03:00:5E:00:00:01", MacAddressTraits.Multicast | MacAddressTraits.Group | MacAddressTraits.LocallyAdministered)]
	public void Classification_ShouldFollowIeeeIndividualAndLocalBits(
		string text,
		MacAddressTraits expected)
	{
		var value = MacAddressValue.Parse(text, null);

		Assert.True(value.Traits.HasFlag(MacAddressTraits.Eui48));
		Assert.Equal(expected, value.Traits & expected);
	}

	[Fact]
	public void Broadcast_ShouldBeDistinctFromOrdinaryMulticast()
	{
		var value = MacAddressValue.Parse("FF:FF:FF:FF:FF:FF", null);

		Assert.True(value.IsBroadcast);
		Assert.True(value.IsGroup);
		Assert.False(value.IsMulticast);
		Assert.False(value.IsValidIdentity);
	}

	[Fact]
	public void OuiAndNicIdentifier_ShouldExposeNumericHalves()
	{
		var value = MacAddressValue.Parse("FC:34:97:E3:A9:20", null);

		Assert.Equal(0xFC3497U, value.OrganizationallyUniqueIdentifier);
		Assert.Equal(0xE3A920U, value.NetworkInterfaceControllerIdentifier);
		Assert.Equal(0xFC3497E3A920UL, value.NumericValue);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("00:00:00:00:00:00")]
	[InlineData("FC:34:97:E3:A9:20")]
	[InlineData("FF:FF:FF:FF:FF:FF")]
	public void FixedBinaryFormat_ShouldRoundTrip(string? text)
	{
		var expected = text is null
			? MacAddressValue.Null
			: MacAddressValue.Parse(text, null);
		Span<byte> buffer = stackalloc byte[MacAddressValue.BinarySize];

		Assert.True(expected.TryWriteBinary(buffer));
		Assert.True(MacAddressValue.TryReadBinary(buffer, out var actual));
		Assert.Equal(expected, actual);
	}

	[Fact]
	public void AddressBytes_ShouldUseNetworkOrderAndRoundTripPhysicalAddress()
	{
		var value = MacAddressValue.Parse("FC:34:97:E3:A9:20", null);
		Span<byte> bytes = stackalloc byte[MacAddressValue.AddressByteCount];

		Assert.True(value.TryWriteAddressBytes(bytes));
		Assert.Equal(new byte[] { 0xFC, 0x34, 0x97, 0xE3, 0xA9, 0x20 }, bytes.ToArray());
		Assert.Equal(value, MacAddressValue.FromAddressBytes(bytes));
		Assert.Equal(value, new MacAddressValue(value.ToPhysicalAddress()));
		Assert.Equal(
			PhysicalAddress.Parse("FC3497E3A920"),
			(PhysicalAddress)value);
	}

	[Fact]
	public void Json_ShouldUseCanonicalStringAndNull()
	{
		var value = MacAddressValue.Parse("fc-34-97-e3-a9-20", null);

		var json = JsonSerializer.Serialize(value);
		var restored = JsonSerializer.Deserialize<MacAddressValue>(json);

		Assert.Equal("\"FC:34:97:E3:A9:20\"", json);
		Assert.Equal(value, restored);
		Assert.Equal("null", JsonSerializer.Serialize(MacAddressValue.Null));
		Assert.True(JsonSerializer.Deserialize<MacAddressValue>("null").IsNull);
		Assert.True(JsonSerializer.Deserialize<MacAddressValue>("\"\"").IsNull);
		Assert.True(JsonSerializer.Deserialize<MacAddressValue>("\"  \"").IsNull);
		Assert.Throws<JsonException>(() =>
			JsonSerializer.Deserialize<MacAddressValue>("\"not-a-mac\""));
	}

	[Fact]
	public void EqualityAndOrdering_ShouldTreatNullAsOneValueAndSortItLast()
	{
		var firstNull = MacAddressValue.Null;
		var secondNull = default(MacAddressValue);
		var first = MacAddressValue.Parse("10:B7:13:F7:92:6F", null);
		var second = MacAddressValue.Parse("FC:34:97:E3:A9:20", null);
		var values = new[] { firstNull, second, first };

		Array.Sort(values);

		Assert.Equal(firstNull, secondNull);
		Assert.Equal(firstNull.GetHashCode(), secondNull.GetHashCode());
		Assert.Equal(0, firstNull.CompareTo(secondNull));
		Assert.True(firstNull.CompareTo(first) > 0);
		Assert.True(first.CompareTo(firstNull) < 0);
		Assert.True(((IComparable)firstNull).CompareTo(first) > 0);
		Assert.True(values[^1].IsNull);
	}

	private static T ParseUtf8<T>(ReadOnlySpan<byte> text)
		where T : IUtf8SpanParsable<T> => T.Parse(text, null);
}
