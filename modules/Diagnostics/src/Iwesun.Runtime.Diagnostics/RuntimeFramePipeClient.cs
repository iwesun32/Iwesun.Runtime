using System.IO.Pipes;
using System.Runtime.CompilerServices;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeFramePipeClient
{
	private const int MaxFrameBytes = 16 * 1024 * 1024;

	public async Task<RuntimeDiagnosticFrame> ExecuteAsync(
		string pipeName,
		RuntimeDiagnosticFrame request,
		TimeSpan timeout,
		CancellationToken cancellationToken = default)
	{
		using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutCts.CancelAfter(timeout);
		await using var pipe = await ConnectAsync(pipeName, timeoutCts.Token).ConfigureAwait(false);
		await RuntimeFramePipeCodec.WriteAsync(pipe, request, timeoutCts.Token).ConfigureAwait(false);
		return await RuntimeFramePipeCodec.ReadAsync(pipe, MaxFrameBytes, timeoutCts.Token).ConfigureAwait(false);
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
		await RuntimeFramePipeCodec.WriteAsync(pipe, request, cancellationToken).ConfigureAwait(false);
		while (!cancellationToken.IsCancellationRequested && pipe.IsConnected)
			yield return await RuntimeFramePipeCodec.ReadAsync(pipe, MaxFrameBytes, cancellationToken).ConfigureAwait(false);
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

}
