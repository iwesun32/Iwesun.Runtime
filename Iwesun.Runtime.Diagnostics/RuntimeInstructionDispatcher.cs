using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

internal sealed class RuntimeInstructionDispatcher : IDisposable
{
	private readonly RuntimeSharedAtomicFifo _fifo;
	private readonly Action<RuntimeValueInstruction> _dispatch;
	private readonly RegisteredWaitHandle _wait;
	private int _draining;
	private int _disposed;

	public RuntimeInstructionDispatcher(RuntimeSharedAtomicFifo fifo, Action<RuntimeValueInstruction> dispatch)
	{
		_fifo = fifo ?? throw new ArgumentNullException(nameof(fifo));
		_dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
		_wait = ThreadPool.RegisterWaitForSingleObject(fifo.WakeHandle, Drain, null, Timeout.Infinite, false);
	}

	private void Drain(object? _, bool timedOut)
	{
		if (timedOut || Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _draining, 1) == 1)
			return;

		try
		{
			while (Volatile.Read(ref _disposed) == 0)
			{
				var generation = _fifo.CommitGeneration;
				while (_fifo.TryDequeue(out var instruction))
					_dispatch(instruction);
				if (generation == _fifo.CommitGeneration)
					break;
			}
		}
		catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
		{
		}
		finally
		{
			Volatile.Write(ref _draining, 0);
			if (Volatile.Read(ref _disposed) == 0 && _fifo.PendingCount > 0)
				_fifo.Signal();
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
			return;
		_wait.Unregister(null);
	}
}
