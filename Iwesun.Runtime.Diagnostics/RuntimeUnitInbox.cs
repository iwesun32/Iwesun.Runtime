using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

internal sealed class RuntimeUnitInbox : IDisposable
{
	private readonly RuntimeSharedAtomicFifo _fifo = new(64);
	private readonly RuntimeInstructionDispatcher _dispatcher;

	public RuntimeUnitInbox(Action<RuntimeValueInstruction> dispatch)
	{
		_dispatcher = new RuntimeInstructionDispatcher(_fifo, dispatch);
	}

	public int Capacity => _fifo.Capacity;
	public long PendingCount => _fifo.PendingCount;
	public long DroppedCount => _fifo.DroppedCount;
	internal RuntimeSharedAtomicFifo Fifo => _fifo;
	public bool TrySend(in RuntimeValueInstruction instruction) => _fifo.TryEnqueue(instruction);

	public void Dispose()
	{
		_dispatcher.Dispose();
		_fifo.Dispose();
	}
}
