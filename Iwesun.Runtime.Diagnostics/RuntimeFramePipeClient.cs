using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeFramePipeClient
{
	private const int MaxFrameBytes = 16 * 1024 * 1024;
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public async Task<RuntimeDiagnosticFrame> ExecuteAsync(
		string pipeName,
		RuntimeDiagnosticFrame request,
		TimeSpan timeout,
		CancellationToken cancellationToken = default)
	{
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(timeout);
		await using var pipe = await ConnectAsync(pipeName, timeoutCts.Token).ConfigureAwait(false);
		await WriteAsync(pipe, request, timeoutCts.Token).ConfigureAwait(false);
		return await ReadAsync(pipe, timeoutCts.Token).ConfigureAwait(false);
	}

	public async IAsyncEnumerable<RuntimeDiagnosticFrame> StreamAsync(
		string pipeName,
		RuntimeDiagnosticFrame request,
		TimeSpan connectTimeout,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		connectCts.CancelAfter(connectTimeout);
		await using var pipe = await ConnectAsync(pipeName, connectCts.Token).ConfigureAwait(false);
		await WriteAsync(pipe, request, cancellationToken).ConfigureAwait(false);
		while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
			yield return await ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
	}

	private static async Task<NamedPipeClientStream> ConnectAsync(string pipeName, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
		var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
		try
		{
			await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
			return pipe;
		}
		catch
		{
			await pipe.DisposeAsync().ConfigureAwait(false);
			throw;
		}
	}

	private static async Task WriteAsync(Stream stream, RuntimeDiagnosticFrame frame, CancellationToken cancellationToken)
	{
		var payload = JsonSerializer.SerializeToUtf8Bytes(frame, JsonOptions);
		if (payload.Length == 0 || payload.Length > MaxFrameBytes)
			throw new InvalidOperationException($"Runtime frame length is invalid: {payload.Length}.");
		var header = new byte[sizeof(int)];
		BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
		await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
		await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
		await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	private static async Task<RuntimeDiagnosticFrame> ReadAsync(Stream stream, CancellationToken cancellationToken)
	{
		var header = new byte[sizeof(int)];
		await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
		var length = BinaryPrimitives.ReadInt32LittleEndian(header);
		if (length <= 0 || length > MaxFrameBytes)
			throw new InvalidDataException($"Runtime frame length is invalid: {length}.");
		var payload = new byte[length];
		await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false);
		return JsonSerializer.Deserialize<RuntimeDiagnosticFrame>(payload, JsonOptions)
			?? throw new InvalidDataException("Runtime frame payload was null.");
	}

	private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
	{
		var offset = 0;
		while (offset < buffer.Length)
		{
			var read = await stream.ReadAsync(buffer.AsMemory(offset), cancellationToken).ConfigureAwait(false);
			if (read == 0)
				throw new EndOfStreamException("Runtime frame stream closed before the complete payload was received.");
			offset += read;
		}
	}
}
