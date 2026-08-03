using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

internal sealed class RuntimeUnitInbox : IDisposable
{
	private readonly RuntimeSharedAtomicFifo _fifo = new(64);
	private readonly Action<RuntimeValueInstruction> _dispatch;
	private readonly object _gate = new();
	private RuntimeInstructionDispatcher? _dispatcher;
	private bool _disposed;

	public RuntimeUnitInbox(Action<RuntimeValueInstruction> dispatch)
	{
		_dispatch = dispatch;
		_dispatcher = new RuntimeInstructionDispatcher(_fifo, dispatch);
	}

	public int Capacity => _fifo.Capacity;
	public long PendingCount => _fifo.PendingCount;
	public long DroppedCount => _fifo.DroppedCount;
	internal RuntimeSharedAtomicFifo Fifo => _fifo;
	public bool TrySend(in RuntimeValueInstruction instruction) => _fifo.TryEnqueue(instruction);
	public bool IsLocalDispatchEnabled { get { lock (_gate) { return !_disposed && _dispatcher != null; } } }

	public void SuspendLocalDispatch()
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			_dispatcher?.Dispose();
			_dispatcher = null;
		}
	}

	public void ResumeLocalDispatch()
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			_dispatcher ??= new RuntimeInstructionDispatcher(_fifo, _dispatch);
			if (_fifo.PendingCount > 0)
				_fifo.Signal();
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
				return;
			_disposed = true;
			_dispatcher?.Dispose();
			_dispatcher = null;
			_fifo.Dispose();
		}
	}
}
