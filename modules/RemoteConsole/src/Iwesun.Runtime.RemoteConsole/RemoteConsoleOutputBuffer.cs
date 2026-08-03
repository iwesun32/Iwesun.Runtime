using System.Text;
using Iwesun.Runtime.RemoteConsole.Protocol;

namespace Iwesun.Runtime.RemoteConsole;

public sealed record RemoteConsoleOutputReadResult(
	IReadOnlyList<RemoteConsoleOutputChunk> Chunks,
	long EarliestAvailableSequence,
	bool Truncated);

public sealed class RemoteConsoleOutputBuffer
{
	private readonly int _maxBytes;
	private readonly object _sync = new();
	private readonly LinkedList<(RemoteConsoleOutputChunk Chunk, int Bytes)> _chunks = new();
	private TaskCompletionSource _changed = NewSignal();
	private long _nextSequence;
	private int _currentBytes;

	public RemoteConsoleOutputBuffer(int maxBytes)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
		_maxBytes = maxBytes;
	}

	public RemoteConsoleOutputChunk Append(RemoteConsoleOutputStream stream, string text)
	{
		text ??= "";
		text = FitToBudget(text, _maxBytes);
		var bytes = Encoding.UTF8.GetByteCount(text);
		TaskCompletionSource changed;
		RemoteConsoleOutputChunk chunk;
		lock (_sync)
		{
			chunk = new RemoteConsoleOutputChunk(
				++_nextSequence,
				stream,
				DateTimeOffset.UtcNow,
				text);
			_chunks.AddLast((chunk, bytes));
			_currentBytes += bytes;
			while (_currentBytes > _maxBytes && _chunks.First is not null)
			{
				_currentBytes -= _chunks.First.Value.Bytes;
				_chunks.RemoveFirst();
			}
			changed = _changed;
			_changed = NewSignal();
		}
		changed.TrySetResult();
		return chunk;
	}

	public async Task<RemoteConsoleOutputReadResult> ReadAsync(
		long afterSequence,
		int limit,
		int waitMs,
		CancellationToken cancellationToken)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
		ArgumentOutOfRangeException.ThrowIfNegative(waitMs);

		Task waitTask;
		RemoteConsoleOutputReadResult result;
		lock (_sync)
		{
			result = ReadLocked(afterSequence, limit);
			waitTask = _changed.Task;
		}
		if (result.Chunks.Count > 0 || waitMs == 0)
			return result;

		try
		{
			await waitTask.WaitAsync(TimeSpan.FromMilliseconds(waitMs), cancellationToken).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
		}
		lock (_sync)
			return ReadLocked(afterSequence, limit);
	}

	private RemoteConsoleOutputReadResult ReadLocked(long afterSequence, int limit)
	{
		var earliest = _chunks.First?.Value.Chunk.Sequence ?? _nextSequence + 1;
		var chunks = _chunks
			.Select(static item => item.Chunk)
			.Where(chunk => chunk.Sequence > afterSequence)
			.Take(limit)
			.ToArray();
		return new RemoteConsoleOutputReadResult(chunks, earliest, afterSequence < earliest - 1);
	}

	private static string FitToBudget(string value, int maxBytes)
	{
		if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
			return value;
		var length = value.Length;
		while (length > 0 && Encoding.UTF8.GetByteCount(value.AsSpan(0, length)) > maxBytes)
			length--;
		return value[..length];
	}

	private static TaskCompletionSource NewSignal() =>
		new(TaskCreationOptions.RunContinuationsAsynchronously);
}
