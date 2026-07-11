using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

internal sealed unsafe class RuntimeSharedAtomicFifo : IDisposable
{
	private const int Magic = 0x52464946;
	private const int Version = 1;
	private const int HeaderBytes = 64;
	private const int SlotBytes = 8 + 48;
	private readonly MemoryMappedFile? _mapping;
	private readonly MemoryMappedViewAccessor? _view;
	private readonly EventWaitHandle _wakeEvent;
	private readonly nint _attachedMappingHandle;
	private readonly bool _attached;
	private readonly int _capacity;
	private readonly int _mask;
	private readonly object _lifetimeGate = new();
	private byte* _pointer;
	private int _disposed;

	public RuntimeSharedAtomicFifo(int capacity)
	{
		if (capacity < 2 || (capacity & (capacity - 1)) != 0)
			throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be a power of two.");

		_capacity = capacity;
		_mask = capacity - 1;
		_mapping = MemoryMappedFile.CreateNew(null, HeaderBytes + (long)SlotBytes * capacity, MemoryMappedFileAccess.ReadWrite);
		_view = _mapping.CreateViewAccessor();
		_view.SafeMemoryMappedViewHandle.AcquirePointer(ref _pointer);
		_wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
		Initialize();
	}

	private RuntimeSharedAtomicFifo(nint mappingHandle, nint eventHandle, int capacity)
	{
		if (capacity < 2 || (capacity & (capacity - 1)) != 0)
			throw new ArgumentOutOfRangeException(nameof(capacity));
		_capacity = capacity;
		_mask = capacity - 1;
		_attachedMappingHandle = mappingHandle;
		_pointer = (byte*)MapViewOfFile(mappingHandle, 0x0002 | 0x0004, 0, 0, (nuint)(HeaderBytes + (long)SlotBytes * capacity));
		if (_pointer == null)
			throw new InvalidOperationException($"MapViewOfFile failed: {Marshal.GetLastPInvokeError()}");
		_wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset);
		_wakeEvent.SafeWaitHandle = new SafeWaitHandle(eventHandle, ownsHandle: true);
		_attached = true;
		if (Unsafe.ReadUnaligned<int>(_pointer) != Magic || Unsafe.ReadUnaligned<int>(_pointer + 4) != Version || Unsafe.ReadUnaligned<int>(_pointer + 8) != capacity)
			throw new InvalidOperationException("Invalid runtime instruction FIFO descriptor.");
	}

	public static RuntimeSharedAtomicFifo Attach(nint mappingHandle, nint eventHandle, int capacity) =>
		new(mappingHandle, eventHandle, capacity);

	public nint MappingHandle { get { lock (_lifetimeGate) { ThrowIfDisposed(); return _mapping?.SafeMemoryMappedFileHandle.DangerousGetHandle() ?? _attachedMappingHandle; } } }
	public nint EventHandle { get { lock (_lifetimeGate) { ThrowIfDisposed(); return _wakeEvent.SafeWaitHandle.DangerousGetHandle(); } } }

	public int Capacity => _capacity;
	public long PendingCount { get { lock (_lifetimeGate) { ThrowIfDisposed(); return Math.Max(0, Volatile.Read(ref WritePosition) - Volatile.Read(ref ReadPosition)); } } }
	public long DroppedCount { get { lock (_lifetimeGate) { ThrowIfDisposed(); return Volatile.Read(ref Dropped); } } }
	public long CommitGeneration { get { lock (_lifetimeGate) { ThrowIfDisposed(); return Volatile.Read(ref Generation); } } }
	public WaitHandle WakeHandle { get { lock (_lifetimeGate) { ThrowIfDisposed(); return _wakeEvent; } } }

	private ref long WritePosition => ref Unsafe.AsRef<long>(_pointer + 16);
	private ref long ReadPosition => ref Unsafe.AsRef<long>(_pointer + 24);
	private ref long Dropped => ref Unsafe.AsRef<long>(_pointer + 32);
	private ref long Generation => ref Unsafe.AsRef<long>(_pointer + 40);

	public bool TryEnqueue(in RuntimeValueInstruction instruction)
	{
		lock (_lifetimeGate)
		{
		ThrowIfDisposed();
		while (true)
		{
			var position = Volatile.Read(ref WritePosition);
			var slot = GetSlot(position);
			var sequence = Volatile.Read(ref slot->Sequence);
			var difference = sequence - position;
			if (difference == 0)
			{
				if (Interlocked.CompareExchange(ref WritePosition, position + 1, position) != position)
					continue;
				slot->Instruction = instruction;
				Volatile.Write(ref slot->Sequence, position + 1);
				Interlocked.Increment(ref Generation);
				_wakeEvent.Set();
				return true;
			}
			if (difference < 0)
			{
				Interlocked.Increment(ref Dropped);
				return false;
			}
			Thread.SpinWait(1);
		}
		}
	}

	public bool TryDequeue(out RuntimeValueInstruction instruction)
	{
		instruction = default;
		lock (_lifetimeGate)
		{
		ThrowIfDisposed();
		while (true)
		{
			var position = Volatile.Read(ref ReadPosition);
			var slot = GetSlot(position);
			var sequence = Volatile.Read(ref slot->Sequence);
			var difference = sequence - (position + 1);
			if (difference == 0)
			{
				if (Interlocked.CompareExchange(ref ReadPosition, position + 1, position) != position)
					continue;
				instruction = slot->Instruction;
				Volatile.Write(ref slot->Sequence, position + _capacity);
				return true;
			}
			if (difference < 0)
				return false;
			Thread.SpinWait(1);
		}
		}
	}

	public void Signal()
	{
		lock (_lifetimeGate)
		{
			ThrowIfDisposed();
			_wakeEvent.Set();
		}
	}

	private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

	private void Initialize()
	{
		Unsafe.WriteUnaligned(_pointer, Magic);
		Unsafe.WriteUnaligned(_pointer + 4, Version);
		Unsafe.WriteUnaligned(_pointer + 8, _capacity);
		WritePosition = 0;
		ReadPosition = 0;
		Dropped = 0;
		Generation = 0;
		for (var i = 0; i < _capacity; i++)
			GetSlot(i)->Sequence = i;
	}

	private Slot* GetSlot(long position) => (Slot*)(_pointer + HeaderBytes + (position & _mask) * SlotBytes);

	public void Dispose()
	{
		lock (_lifetimeGate)
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 1)
				return;
			_wakeEvent.Set();
			if (_view != null)
				_view.SafeMemoryMappedViewHandle.ReleasePointer();
			else if (_attached && _pointer != null)
				UnmapViewOfFile((nint)_pointer);
			_wakeEvent.Dispose();
			_view?.Dispose();
			_mapping?.Dispose();
			_pointer = null;
		}
	}

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern nint MapViewOfFile(nint mappingHandle, uint desiredAccess, uint fileOffsetHigh, uint fileOffsetLow, nuint bytesToMap);

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool UnmapViewOfFile(nint baseAddress);

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	private struct Slot
	{
		public long Sequence;
		public RuntimeValueInstruction Instruction;
	}
}
