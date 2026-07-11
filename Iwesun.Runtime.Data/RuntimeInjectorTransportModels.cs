using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace Iwesun.Runtime.Data;

public static class RuntimeInjectorTransportDefaults
{
	public const int MaxPayloadBytes = 4096;
}
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct RuntimeInjectorFifoHeader(
	int processId,
	int managedThreadId,
	ulong injectorCompileIdHash,
	long timestampUtcTicks,
	int payloadLength)
{
	public int ProcessId { get; } = processId;
	public int ManagedThreadId { get; } = managedThreadId;
	public ulong InjectorCompileIdHash { get; } = injectorCompileIdHash;
	public long TimestampUtcTicks { get; } = timestampUtcTicks;
	public int PayloadLength { get; } = payloadLength;
}

public static class RuntimeInjectorTransportCodec
{
	public static RuntimeInjectorFifoHeader CreateHeader(string injectorCompileId, int payloadLength, int processId, int managedThreadId, DateTimeOffset timestamp)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(injectorCompileId);
		payloadLength = Math.Clamp(payloadLength, 0, RuntimeInjectorTransportDefaults.MaxPayloadBytes);
		return new RuntimeInjectorFifoHeader(
			processId,
			managedThreadId,
			ComputeStableHash(injectorCompileId),
			timestamp.UtcTicks,
			payloadLength);
	}

	public static byte[] EncodePayloadUtf8(string text, int maxPayloadBytes = RuntimeInjectorTransportDefaults.MaxPayloadBytes)
	{
		text ??= string.Empty;
		maxPayloadBytes = Math.Clamp(maxPayloadBytes, 1, RuntimeInjectorTransportDefaults.MaxPayloadBytes);
		var bytes = Encoding.UTF8.GetBytes(text);
		if (bytes.Length <= maxPayloadBytes)
		{
			return bytes;
		}

		var slice = new byte[maxPayloadBytes];
		Buffer.BlockCopy(bytes, 0, slice, 0, maxPayloadBytes);
		return slice;
	}

	public static string DecodePayloadUtf8(ReadOnlySpan<byte> payload)
	{
		if (payload.IsEmpty)
		{
			return string.Empty;
		}

		return Encoding.UTF8.GetString(payload);
	}

	public static ulong ComputeStableHash(string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(value);
		const ulong fnvOffset = 14695981039346656037UL;
		const ulong fnvPrime = 1099511628211UL;
		ulong hash = fnvOffset;
		foreach (var ch in value)
		{
			hash ^= ch;
			hash *= fnvPrime;
		}

		return hash;
	}

	public static int ComputeStableHash32(string value)
	{
		var hash64 = ComputeStableHash(value);
		return unchecked((int)(hash64 ^ (hash64 >> 32)));
	}
}

