using System.Security.Cryptography;

namespace Iwesun.Runtime.RemoteConsole;

internal sealed class RemoteConsoleUploadSession : IDisposable
{
	public required string UploadId { get; init; }
	public required string WorkspaceId { get; init; }
	public required string RelativePath { get; init; }
	public required string TempPath { get; init; }
	public required string FinalPath { get; init; }
	public required long ExpectedLength { get; init; }
	public required string ExpectedSha256 { get; init; }
	public required FileStream Stream { get; init; }
	public SemaphoreSlim Sync { get; } = new(1, 1);
	public long ReceivedLength { get; set; }
	public int NextSequence { get; set; }

	public async Task<string> ComputeSha256Async(CancellationToken cancellationToken)
	{
		Stream.Position = 0;
		var hash = await SHA256.HashDataAsync(Stream, cancellationToken).ConfigureAwait(false);
		return Convert.ToHexString(hash);
	}

	public void Dispose()
	{
		Stream.Dispose();
		Sync.Dispose();
	}
}
