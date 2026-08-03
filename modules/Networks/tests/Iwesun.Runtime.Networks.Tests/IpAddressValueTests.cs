using System.Data.SqlTypes;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Xunit;
namespace Iwesun.Runtime.Networks.Tests;

public sealed class IpAddressValueTests
{
	[Fact]
	public void Null_ShouldRemainDistinctFromUnspecifiedAddresses()
	{
		var nullValue = default(IpAddressValue);
		var ipv4Any = IpAddressValue.Parse("0.0.0.0", null);
		var ipv6Any = IpAddressValue.Parse("::", null);

		Assert.True(nullValue.IsNull);
		Assert.Equal(string.Empty, nullValue.ToString());
		Assert.Throws<SqlNullValueException>(() => nullValue.ToIPAddress());
		Assert.False(ipv4Any.IsNull);
		Assert.True(ipv4Any.IsUnspecified);
		Assert.Equal(IpAddressType.Unset, ipv4Any.AddressType);
		Assert.False(ipv6Any.IsNull);
		Assert.True(ipv6Any.IsUnspecified);
		Assert.Equal(IpAddressType.Unset, ipv6Any.AddressType);
	}

	[Fact]
	public void Layout_ShouldUseExactlySeventeenBytes()
	{
		Assert.Equal(17, IpAddressValue.BinarySize);
		Assert.Equal(17, Unsafe.SizeOf<IpAddressValue>());
		Assert.Equal(17, Marshal.SizeOf<IpAddressValue>());
		Assert.Equal(
			["_high", "_low", "_family"],
			typeof(IpAddressValue)
				.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
				.Select(static field => field.Name)
				.ToArray());
	}

	[Fact]
	public void Parse_ShouldCanonicalizePureAddressAndRejectIpv6Scope()
	{
		var ipv4 = IpAddressValue.Parse("192.168.32.16", null);
		var ipv6 = IpAddressValue.Parse("FE80:0:0:0:0:0:0:1", null);

		Assert.Equal("192.168.32.16", ipv4.ToString());
		Assert.True(ipv4.IsIPv4);
		Assert.Equal("fe80::1", ipv6.ToString());
		Assert.True(ipv6.IsIPv6);
		Assert.Equal(IPAddress.Parse("fe80::1"), ipv6.ToIPAddress());
		Assert.False(IpAddressValue.TryParse("fe80::1%17", null, out _));
		Assert.Throws<FormatException>(() => IpAddressValue.Parse("fe80::1%17", null));
		Assert.Throws<ArgumentException>(() => new IpAddressValue(IPAddress.Parse("fe80::1%17")));
	}

	[Theory]
	[InlineData("192.168.32.16")]
	[InlineData("240e:36d:1234:d2f0::1")]
	[InlineData("::ffff:192.168.32.16")]
	[InlineData("::192.0.2.1")]
	public void Utf8GenericContracts_ShouldRoundTripCanonicalText(string text)
	{
		var value = ParseUtf8<IpAddressValue>(Encoding.UTF8.GetBytes(text));
		Span<byte> destination = stackalloc byte[45];

		Assert.True(value.TryFormat(destination, out var written, default, null));
		Assert.Equal(value.ToString(), Encoding.UTF8.GetString(destination[..written]));
		Assert.Equal(value, IpAddressValue.Parse(destination[..written], null));
		Assert.IsAssignableFrom<IUtf8SpanFormattable>(value);
	}

	[Fact]
	public void SystemAddressConversion_ShouldApplyInterfaceScopeOnlyAtBoundary()
	{
		var linkLocal = IpAddressValue.Parse("fe80::1");
		var multicast = IpAddressValue.Parse("ff02::1");
		var global = IpAddressValue.Parse("2001:db8::1");
		var ipv4 = IpAddressValue.Parse("192.0.2.1");

		Assert.Equal(17, linkLocal.ToIPAddress(17).ScopeId);
		Assert.Equal(17, multicast.ToIPAddress(17).ScopeId);
		Assert.Equal(0, linkLocal.ToIPAddress().ScopeId);
		Assert.Throws<ArgumentOutOfRangeException>(() => global.ToIPAddress(17));
		Assert.Throws<ArgumentOutOfRangeException>(() => ipv4.ToIPAddress(17));
	}

	[Theory]
	[InlineData("127.0.0.1", IpAddressType.Loopback, IpAddressTraits.Loopback)]
	[InlineData("192.168.32.16", IpAddressType.Private, IpAddressTraits.Private)]
	[InlineData("169.254.1.1", IpAddressType.LinkLocal, IpAddressTraits.LinkLocal)]
	[InlineData("224.0.0.1", IpAddressType.Multicast, IpAddressTraits.Multicast)]
	[InlineData("255.255.255.255", IpAddressType.Unset, IpAddressTraits.Broadcast)]
	[InlineData("100.64.0.1", IpAddressType.Unset, IpAddressTraits.Shared)]
	[InlineData("192.0.2.1", IpAddressType.Unset, IpAddressTraits.Documentation)]
	[InlineData("198.18.0.1", IpAddressType.Unset, IpAddressTraits.Benchmark)]
	[InlineData("8.8.8.8", IpAddressType.Public, IpAddressTraits.GlobalUnicast)]
	[InlineData("::1", IpAddressType.Loopback, IpAddressTraits.Loopback)]
	[InlineData("fe80::1", IpAddressType.LinkLocal, IpAddressTraits.LinkLocal)]
	[InlineData("fd00::1", IpAddressType.Private, IpAddressTraits.UniqueLocal)]
	[InlineData("ff02::1", IpAddressType.Multicast, IpAddressTraits.Multicast)]
	[InlineData("2001:db8::1", IpAddressType.Unset, IpAddressTraits.Documentation)]
	[InlineData("2002::1", IpAddressType.Transition, IpAddressTraits.Transition)]
	[InlineData("::ffff:192.168.32.16", IpAddressType.Transition, IpAddressTraits.IPv4Mapped)]
	[InlineData("240e:36d:1234:d2f0::1", IpAddressType.Public, IpAddressTraits.GlobalUnicast)]
	public void Classification_ShouldExposeExpectedTypeAndTrait(
		string text,
		IpAddressType expectedType,
		IpAddressTraits expectedTrait)
	{
		var value = IpAddressValue.Parse(text, null);

		Assert.Equal(expectedType, value.AddressType);
		Assert.True(value.Traits.HasFlag(expectedTrait));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("192.168.32.16")]
	[InlineData("240e:36d:1234:d2f0::1")]
	[InlineData("fe80::1")]
	public void FixedBinaryFormat_ShouldRoundTrip(string? text)
	{
		var expected = text is null
			? IpAddressValue.Null
			: IpAddressValue.Parse(text, null);
		Span<byte> buffer = stackalloc byte[IpAddressValue.BinarySize];

		Assert.True(expected.TryWriteBinary(buffer));
		Assert.True(IpAddressValue.TryReadBinary(buffer, out var actual));
		Assert.Equal(expected, actual);
	}

	[Fact]
	public void ExplicitAddressByteFactories_ShouldUseNetworkByteOrder()
	{
		var ipv4 = IpAddressValue.Parse("192.168.32.16", null);
		var ipv6 = IpAddressValue.Parse("240e:36d:1234:d2f0::1", null);
		Span<byte> ipv4Bytes = stackalloc byte[4];
		Span<byte> ipv6Bytes = stackalloc byte[16];

		Assert.True(ipv4.TryWriteAddressBytes(ipv4Bytes, out var ipv4BytesWritten));
		Assert.True(ipv6.TryWriteAddressBytes(ipv6Bytes, out var ipv6BytesWritten));
		Assert.Equal(4, ipv4BytesWritten);
		Assert.Equal(16, ipv6BytesWritten);
		Assert.Equal(new byte[] { 192, 168, 32, 16 }, ipv4Bytes.ToArray());
		Assert.Equal(ipv4, IpAddressValue.FromIPv4Bytes(ipv4Bytes));
		Assert.Equal(ipv6, IpAddressValue.FromIPv6Bytes(ipv6Bytes));
		Assert.Throws<ArgumentException>(() => IpAddressValue.FromIPv4Bytes(new byte[16]));
		Assert.Throws<ArgumentException>(() => IpAddressValue.FromIPv6Bytes(new byte[4]));
	}

	[Fact]
	public void NumericConstructors_ShouldDeclareAddressFamily()
	{
		var ipv4 = new IpAddressValue(0xC0A82010U);
		var ipv6 = new IpAddressValue(0x240E036D1234D2F0UL, 1UL);

		Assert.True(ipv4.IsIPv4);
		Assert.Equal("192.168.32.16", ipv4.ToString());
		Assert.True(ipv6.IsIPv6);
		Assert.Equal("240e:36d:1234:d2f0::1", ipv6.ToString());
	}

	[Fact]
	public void Json_ShouldUseCanonicalStringAndNull()
	{
		var value = IpAddressValue.Parse("240E:36D:1234:D2F0:0:0:0:1", null);

		var json = JsonSerializer.Serialize(value);
		var restored = JsonSerializer.Deserialize<IpAddressValue>(json);

		Assert.Equal("\"240e:36d:1234:d2f0::1\"", json);
		Assert.Equal(value, restored);
		Assert.Equal("null", JsonSerializer.Serialize(IpAddressValue.Null));
		Assert.True(JsonSerializer.Deserialize<IpAddressValue>("null").IsNull);
		Assert.Throws<JsonException>(() =>
			JsonSerializer.Deserialize<IpAddressValue>("\"not-an-ip\""));
	}

	[Fact]
	public void EquivalentText_ShouldProduceEqualNumericValues()
	{
		var expanded = IpAddressValue.Parse("240e:36d:1234:d2f0:0:0:0:1", null);
		var compressed = IpAddressValue.Parse("240e:36d:1234:d2f0::1", null);

		Assert.Equal(expanded, compressed);
		Assert.Equal(expanded.GetHashCode(), compressed.GetHashCode());
		Assert.Equal(0, expanded.CompareTo(compressed));
	}

	[Fact]
	public void EqualityAndOrdering_ShouldTreatNullAsOneValueAndSortItLast()
	{
		var firstNull = IpAddressValue.Null;
		var secondNull = default(IpAddressValue);
		var ipv4 = IpAddressValue.Parse("192.168.32.16", null);
		var ipv6 = IpAddressValue.Parse("240e:36d:1234:d2f0::1", null);
		var values = new[] { firstNull, ipv6, ipv4 };

		Array.Sort(values);

		Assert.Equal(firstNull, secondNull);
		Assert.Equal(firstNull.GetHashCode(), secondNull.GetHashCode());
		Assert.Equal(0, firstNull.CompareTo(secondNull));
		Assert.True(firstNull.CompareTo(ipv4) > 0);
		Assert.True(ipv4.CompareTo(firstNull) < 0);
		Assert.True(((IComparable)firstNull).CompareTo(ipv4) > 0);
		Assert.True(values[^1].IsNull);
	}

	private static T ParseUtf8<T>(ReadOnlySpan<byte> text)
		where T : IUtf8SpanParsable<T> => T.Parse(text, null);
}
