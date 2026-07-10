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

public static class RuntimeInjectorFrameCodec
{
	private const string CommandPrefix = "rtf1.cmd:";
	private const string StatePrefix = "rtf1.sta:";
	private const int CommandFrameBytes = 52;
	private const int StateFrameBytes = 40;

	public static string EncodeCommandFrame(RuntimeCommandFrame frame)
	{
		Span<byte> bytes = stackalloc byte[CommandFrameBytes];
		WriteInt32(bytes, 0, frame.ProcessId);
		WriteInt32(bytes, 4, frame.ManagedThreadId);
		WriteInt64(bytes, 8, frame.Sequence);
		WriteInt32(bytes, 16, frame.TargetIdHash);
		WriteInt32(bytes, 20, frame.CommandKind);
		WriteInt64(bytes, 24, frame.TimestampUtcTicks);
		WriteInt64(bytes, 32, frame.Arg0);
		WriteInt64(bytes, 40, frame.Arg1);
		WriteInt32(bytes, 48, 1); // protocol version
		return $"{CommandPrefix}{Convert.ToBase64String(bytes)}";
	}

	public static string EncodeStateFrame(RuntimeStateFrame frame)
	{
		Span<byte> bytes = stackalloc byte[StateFrameBytes];
		WriteInt32(bytes, 0, frame.ProcessId);
		WriteInt32(bytes, 4, frame.ManagedThreadId);
		WriteInt64(bytes, 8, frame.Sequence);
		WriteInt32(bytes, 16, frame.EntityKind);
		WriteInt32(bytes, 20, frame.EntityIdHash);
		WriteInt32(bytes, 24, frame.StateKind);
		WriteInt64(bytes, 28, frame.TimestampUtcTicks);
		WriteInt32(bytes, 36, 1); // protocol version
		return $"{StatePrefix}{Convert.ToBase64String(bytes)}";
	}

	public static bool TryDecodeCommandFrame(string? encoded, out RuntimeCommandFrame frame)
	{
		frame = default;
		if (string.IsNullOrWhiteSpace(encoded) || !encoded.StartsWith(CommandPrefix, StringComparison.Ordinal))
		{
			return false;
		}

		if (!TryParsePayload(encoded[CommandPrefix.Length..], CommandFrameBytes, out var payload))
		{
			return false;
		}

		frame = new RuntimeCommandFrame(
			processId: ReadInt32(payload, 0),
			managedThreadId: ReadInt32(payload, 4),
			sequence: ReadInt64(payload, 8),
			targetIdHash: ReadInt32(payload, 16),
			commandKind: ReadInt32(payload, 20),
			timestampUtcTicks: ReadInt64(payload, 24),
			arg0: ReadInt64(payload, 32),
			arg1: ReadInt64(payload, 40));
		return true;
	}

	public static bool TryDecodeStateFrame(string? encoded, out RuntimeStateFrame frame)
	{
		frame = default;
		if (string.IsNullOrWhiteSpace(encoded) || !encoded.StartsWith(StatePrefix, StringComparison.Ordinal))
		{
			return false;
		}

		if (!TryParsePayload(encoded[StatePrefix.Length..], StateFrameBytes, out var payload))
		{
			return false;
		}

		frame = new RuntimeStateFrame(
			processId: ReadInt32(payload, 0),
			managedThreadId: ReadInt32(payload, 4),
			sequence: ReadInt64(payload, 8),
			entityKind: ReadInt32(payload, 16),
			entityIdHash: ReadInt32(payload, 20),
			stateKind: ReadInt32(payload, 24),
			timestampUtcTicks: ReadInt64(payload, 28));
		return true;
	}

	private static bool TryParsePayload(string payloadBase64, int expectedBytes, out byte[] payload)
	{
		payload = [];
		try
		{
			payload = Convert.FromBase64String(payloadBase64);
			if (payload.Length != expectedBytes)
			{
				payload = [];
				return false;
			}

			var version = ReadInt32(payload, expectedBytes - 4);
			if (version != 1)
			{
				payload = [];
				return false;
			}

			return true;
		}
		catch (FormatException)
		{
			return false;
		}
	}

	private static void WriteInt32(Span<byte> buffer, int offset, int value) => BinaryPrimitives.WriteInt32LittleEndian(buffer[offset..], value);
	private static void WriteInt64(Span<byte> buffer, int offset, long value) => BinaryPrimitives.WriteInt64LittleEndian(buffer[offset..], value);
	private static int ReadInt32(ReadOnlySpan<byte> buffer, int offset) => BinaryPrimitives.ReadInt32LittleEndian(buffer[offset..]);
	private static long ReadInt64(ReadOnlySpan<byte> buffer, int offset) => BinaryPrimitives.ReadInt64LittleEndian(buffer[offset..]);
}
