using System.Buffers.Binary;

namespace Iwesun.Runtime.Diagnostics;

public static class RuntimeFramePipeCodec
{
	private const int MaxWritePayloadBytes = 16 * 1024 * 1024;

	public static async Task WriteAsync(
		Stream stream,
		RuntimeDiagnosticFrame frame,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(stream);
		ArgumentNullException.ThrowIfNull(frame);

		var payload = RuntimeDiagnosticJson.SerializeToUtf8Bytes(frame);
		if (payload.Length == 0 || payload.Length > MaxWritePayloadBytes)
			throw new InvalidDataException($"Runtime frame length is invalid: {payload.Length}.");

		var header = new byte[sizeof(int)];
		BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
		await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
		await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
		await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	public static async Task<RuntimeDiagnosticFrame> ReadAsync(
		Stream stream,
		int maxPayloadBytes,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(stream);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPayloadBytes);

		var header = new byte[sizeof(int)];
		await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
		var length = BinaryPrimitives.ReadInt32LittleEndian(header);
		if (length <= 0 || length > maxPayloadBytes)
			throw new InvalidDataException($"Runtime frame length is invalid: {length}.");

		var payload = new byte[length];
		await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
		return RuntimeDiagnosticJson.Deserialize<RuntimeDiagnosticFrame>(payload)
			?? throw new InvalidDataException("Runtime frame payload was null.");
	}

	private static async Task ReadExactAsync(
		Stream stream,
		Memory<byte> buffer,
		CancellationToken cancellationToken)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
			if (read == 0)
				throw new EndOfStreamException("Runtime frame stream closed before the complete payload was received.");
			offset += read;
		}
	}
}
