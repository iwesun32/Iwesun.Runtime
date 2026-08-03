using System.Buffers.Binary;
using System.Net;

namespace Iwesun.Runtime.Networks;

/// <summary>
/// DNS wire-format (RFC 1035/8484) message builder and parser.
/// Uses value types and span-based parsing for zero-heap-allocation hot paths.
/// </summary>
public static class DnsWireCodec
{
	/// <summary>Maximum number of A/AAAA records in a single response we can parse.</summary>
	public const int MaxAnswers = 32;

	/// <summary>
	/// Builds a DNS wire-format query message for a domain.
	/// </summary>
	public static byte[] BuildQuery(string domain, ushort queryType = 1)
		=> BuildQuery(domain, queryType, (ushort)Random.Shared.Next(1, 65536));

	/// <summary>Builds a DNS query with an explicit protocol transaction identifier.</summary>
	public static byte[] BuildQuery(string domain, ushort queryType, ushort transactionId)
	{
		var labels = domain.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries);
		var nameBytes = new List<byte>(64);
		foreach (var label in labels)
		{
			var labelBytes = System.Text.Encoding.ASCII.GetBytes(label);
			if (labelBytes.Length > 63)
				throw new ArgumentException($"DNS label too long: {label}", nameof(domain));
			nameBytes.Add((byte)labelBytes.Length);
			nameBytes.AddRange(labelBytes);
		}
		nameBytes.Add(0);

		var message = new byte[12 + nameBytes.Count + 4];
		var span = message.AsSpan();

		BinaryPrimitives.WriteUInt16BigEndian(span[0..2], transactionId);
		BinaryPrimitives.WriteUInt16BigEndian(span[2..4], 0x0100); // RD=1
		BinaryPrimitives.WriteUInt16BigEndian(span[4..6], 1);      // QDCOUNT=1

		var offset = 12;
		nameBytes.CopyTo(message, offset);
		offset += nameBytes.Count;
		BinaryPrimitives.WriteUInt16BigEndian(span[offset..], queryType);
		offset += 2;
		BinaryPrimitives.WriteUInt16BigEndian(span[offset..], 1); // QCLASS=IN

		return message;
	}

	/// <summary>Parses one PTR response and validates its protocol transaction identifier.</summary>
	public static bool TryParsePtrResponse(
		ReadOnlySpan<byte> data,
		ushort expectedTransactionId,
		out string hostName,
		out uint ttl,
		out bool truncated,
		out byte responseCode)
	{
		hostName = string.Empty;
		ttl = 0;
		truncated = false;
		responseCode = 0;
		if (data.Length < 12 || BinaryPrimitives.ReadUInt16BigEndian(data) != expectedTransactionId)
			return false;

		var flags = BinaryPrimitives.ReadUInt16BigEndian(data[2..]);
		truncated = (flags & 0x0200) != 0;
		responseCode = (byte)(flags & 0x000F);
		if ((flags & 0x8000) == 0 || responseCode != 0) return false;
		var qdCount = BinaryPrimitives.ReadUInt16BigEndian(data[4..]);
		var anCount = BinaryPrimitives.ReadUInt16BigEndian(data[6..]);
		var offset = 12;
		for (var i = 0; i < qdCount; i++)
		{
			offset = SkipName(data, offset);
			if (offset + 4 > data.Length) return false;
			offset += 4;
		}

		for (var i = 0; i < anCount; i++)
		{
			offset = SkipName(data, offset);
			if (offset + 10 > data.Length) return false;
			var type = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
			var recordTtl = BinaryPrimitives.ReadUInt32BigEndian(data[(offset + 4)..]);
			var length = BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 8)..]);
			offset += 10;
			if (offset + length > data.Length) return false;
			if (type == 12 && TryReadName(data, offset, out hostName))
			{
				ttl = recordTtl;
				return true;
			}
			offset += length;
		}
		return false;
	}

	/// <summary>
	/// Builds a DNS wire-format query and returns base64url encoding (no padding) for RFC 8484 GET.
	/// </summary>
	public static string BuildQueryBase64Url(string domain, ushort queryType = 1)
	{
		var bytes = BuildQuery(domain, queryType);
		return Base64UrlEncode(bytes);
	}

	/// <summary>
	/// Parses a DNS wire-format response into value-type results.
	/// Extracts A/AAAA records into <see cref="IpAddressValue"/> values.
	/// </summary>
	/// <param name="data">Wire-format DNS response bytes.</param>
	/// <param name="ips">Buffer to fill with parsed IP addresses (max <see cref="MaxAnswers"/>).</param>
	/// <param name="ttl">Minimum TTL across all matching records (clamped to 60).</param>
	/// <returns>Number of IP addresses written to <paramref name="ips"/>.</returns>
	public static int ParseResponse(ReadOnlySpan<byte> data, Span<IpAddressValue> ips, out uint ttl)
	{
		ttl = 60;
		if (data.Length < 12 || ips.Length == 0)
			return 0;

		var qdCount = BinaryPrimitives.ReadUInt16BigEndian(data[4..6]);
		var anCount = BinaryPrimitives.ReadUInt16BigEndian(data[6..8]);

		// Skip question section
		var offset = 12;
		for (int i = 0; i < qdCount && offset < data.Length; i++)
		{
			offset = SkipName(data, offset);
			offset += 4; // QTYPE + QCLASS
		}

		// Parse answer section
		int count = 0;
		uint minTtl = uint.MaxValue;

		for (int i = 0; i < anCount && offset < data.Length && count < ips.Length; i++)
		{
			offset = SkipName(data, offset);
			if (offset + 10 > data.Length)
				break;

			var rtype = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
			offset += 2;
			offset += 2; // RCLASS
			var recordTtl = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
			offset += 4;
			var rdLength = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
			offset += 2;

			if (offset + rdLength > data.Length)
				break;

			if (rtype == 1 && rdLength == 4) // A record
			{
				var ipValue = BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
				ips[count++] = IpAddressValue.FromIPv4(ipValue);
				if (recordTtl < minTtl) minTtl = recordTtl;
			}
			else if (rtype == 28 && rdLength == 16) // AAAA record
			{
				var high = BinaryPrimitives.ReadUInt64BigEndian(data[offset..]);
				var low = BinaryPrimitives.ReadUInt64BigEndian(data[(offset + 8)..]);
				ips[count++] = IpAddressValue.FromIPv6(high, low);
				if (recordTtl < minTtl) minTtl = recordTtl;
			}

			offset += rdLength;
		}

		ttl = count > 0 ? Math.Max(minTtl, 60) : 60;
		return count;
	}

	/// <summary>
	/// Parses a DoH JSON response body (Google/Cloudflare format) into value-type results.
	/// </summary>
	public static int ParseJsonResponse(ReadOnlySpan<byte> data, Span<IpAddressValue> ips, out uint ttl)
	{
		ttl = 60;
		if (data.Length == 0 || ips.Length == 0)
			return 0;

		try
		{
			var json = System.Text.Json.JsonDocument.Parse(data.ToArray()).RootElement;
			int count = 0;
			uint minTtl = uint.MaxValue;

			if (json.TryGetProperty("Answer", out var answerArr))
			{
				foreach (var ans in answerArr.EnumerateArray())
				{
					if (count >= ips.Length) break;
					if (!ans.TryGetProperty("data", out var dataEl) ||
						!ans.TryGetProperty("TTL", out var ttlEl))
						continue;

					var dataStr = dataEl.GetString();
					if (string.IsNullOrEmpty(dataStr)) continue;
					if (!IPAddress.TryParse(dataStr, out var ip)) continue;

					var ttlVal = ttlEl.GetUInt32();
					ips[count++] = NetworkIpAddressInterop.FromSystemAddress(ip);
					if (ttlVal < minTtl) minTtl = ttlVal;
				}
			}

			ttl = count > 0 ? Math.Max(minTtl, 60) : 60;
			return count;
		}
		catch
		{
			return 0;
		}
	}

	private static int SkipName(ReadOnlySpan<byte> data, int offset)
	{
		while (offset < data.Length)
		{
			var len = data[offset];
			if (len == 0) { offset++; break; }
			if ((len & 0xC0) == 0xC0) { offset += 2; break; }
			offset += 1 + len;
		}
		return offset;
	}

	private static bool TryReadName(ReadOnlySpan<byte> data, int offset, out string name)
	{
		name = string.Empty;
		var labels = new List<string>(8);
		var hops = 0;
		while (offset < data.Length && hops++ < 128)
		{
			var length = data[offset++];
			if (length == 0)
			{
				name = string.Join('.', labels);
				return labels.Count > 0;
			}
			if ((length & 0xC0) == 0xC0)
			{
				if (offset >= data.Length) return false;
				offset = ((length & 0x3F) << 8) | data[offset];
				continue;
			}
			if (length > 63 || offset + length > data.Length) return false;
			labels.Add(System.Text.Encoding.ASCII.GetString(data.Slice(offset, length)));
			offset += length;
		}
		return false;
	}

	private static string Base64UrlEncode(byte[] bytes)
	{
		var base64 = Convert.ToBase64String(bytes);
		var sb = new System.Text.StringBuilder(base64.Length);
		foreach (var c in base64)
		{
			sb.Append(c switch { '+' => '-', '/' => '_', '=' => '\0', _ => c });
		}
		return sb.ToString().TrimEnd('\0');
	}
}
