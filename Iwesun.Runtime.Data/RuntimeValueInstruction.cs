using System.Runtime.InteropServices;

namespace Iwesun.Runtime.Data;

public enum RuntimeInstructionEntityKind
{
	Controller = 1,
	Process = 2,
	Thread = 3,
	Task = 4,
	Business = 5
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct RuntimeValueInstruction(
	long sequence,
	int processId,
	int managedThreadId,
	int entityKind,
	int entityIdHash,
	int value,
	int flags,
	long arg0,
	long arg1)
{
	public long Sequence { get; } = sequence;
	public int ProcessId { get; } = processId;
	public int ManagedThreadId { get; } = managedThreadId;
	public int EntityKind { get; } = entityKind;
	public int EntityIdHash { get; } = entityIdHash;
	public int Value { get; } = value;
	public int Flags { get; } = flags;
	public long Arg0 { get; } = arg0;
	public long Arg1 { get; } = arg1;
}
