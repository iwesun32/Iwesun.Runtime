namespace Iwesun.Runtime.Networks;

/// <summary>Keeps a request-scoped WFP dynamic session alive for exactly one HTTP connection.</summary>
internal sealed class NetworkWfpPolicyOwnedStream(
	Stream inner,
	INetworkWfpConnectionPolicyLease policy) : Stream
{
	private int _disposed;

	public override bool CanRead => inner.CanRead;
	public override bool CanSeek => inner.CanSeek;
	public override bool CanWrite => inner.CanWrite;
	public override long Length => inner.Length;
	public override long Position { get => inner.Position; set => inner.Position = value; }
	public override void Flush() => inner.Flush();
	public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
	public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
	public override int Read(Span<byte> buffer) => inner.Read(buffer);
	public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
		inner.ReadAsync(buffer, cancellationToken);
	public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
	public override void SetLength(long value) => inner.SetLength(value);
	public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
	public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
	public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
		inner.WriteAsync(buffer, cancellationToken);

	protected override void Dispose(bool disposing)
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		try
		{
			if (disposing) inner.Dispose();
		}
		finally
		{
			policy.Dispose();
			base.Dispose(disposing);
		}
	}

	public override async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
		try
		{
			await inner.DisposeAsync().ConfigureAwait(false);
		}
		finally
		{
			policy.Dispose();
			GC.SuppressFinalize(this);
		}
	}
}
