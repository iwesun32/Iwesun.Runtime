using System.Buffers.Binary;
using Iwesun.Runtime.Diagnostics;

internal static class RemoteConsoleScenario
{
	public static async Task<FunctionalScenarioResult> RunFrameCodecAsync()
	{
		var checks = new List<string>();
		var failures = new List<string>();
		var frame = new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V2Schema,
				FrameType = "request",
				RequestId = "codec-fragmented"
			}
		};

		await using (var stream = new FragmentingDuplexStream([1, 2, 3, 5]))
		{
			await RuntimeFramePipeCodec.WriteAsync(stream, frame, CancellationToken.None);
			stream.Rewind();
			var decoded = await RuntimeFramePipeCodec.ReadAsync(stream, 1024 * 1024, CancellationToken.None);
			if (decoded.Header.RequestId == "codec-fragmented")
				checks.Add("fragmented-frame-round-trip");
			else
				failures.Add("Fragmented frame requestId mismatch.");
		}

		await using (var oversized = new FragmentingDuplexStream([1]))
		{
			var header = new byte[sizeof(int)];
			BinaryPrimitives.WriteInt32LittleEndian(header, 1025);
			await oversized.WriteAsync(header);
			oversized.Rewind();
			try
			{
				await RuntimeFramePipeCodec.ReadAsync(oversized, 1024, CancellationToken.None);
				failures.Add("Oversized frame was accepted.");
			}
			catch (InvalidDataException)
			{
				checks.Add("oversized-frame-rejected");
			}
		}

		return failures.Count == 0
			? FunctionalScenarioResult.Pass("remote-console-frame-codec", checks.ToArray())
			: FunctionalScenarioResult.Fail("remote-console-frame-codec", checks, failures);
	}

	private sealed class FragmentingDuplexStream(IReadOnlyList<int> fragmentSizes) : Stream
	{
		private readonly MemoryStream _inner = new();
		private int _fragmentIndex;

		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => true;
		public override long Length => _inner.Length;
		public override long Position { get => _inner.Position; set => _inner.Position = value; }

		public void Rewind()
		{
			_inner.Position = 0;
			_fragmentIndex = 0;
		}

		public override int Read(byte[] buffer, int offset, int count) =>
			_inner.Read(buffer, offset, Math.Min(count, NextFragmentSize()));

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
			_inner.ReadAsync(buffer[..Math.Min(buffer.Length, NextFragmentSize())], cancellationToken);

		public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

		public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
			_inner.WriteAsync(buffer, cancellationToken);

		public override void Flush() => _inner.Flush();
		public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);
		public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
		public override void SetLength(long value) => _inner.SetLength(value);

		protected override void Dispose(bool disposing)
		{
			if (disposing)
				_inner.Dispose();
			base.Dispose(disposing);
		}

		public override async ValueTask DisposeAsync()
		{
			await _inner.DisposeAsync();
			GC.SuppressFinalize(this);
		}

		private int NextFragmentSize()
		{
			if (fragmentSizes.Count == 0)
				return 1;
			var size = fragmentSizes[_fragmentIndex % fragmentSizes.Count];
			_fragmentIndex++;
			return Math.Max(1, size);
		}
	}
}
