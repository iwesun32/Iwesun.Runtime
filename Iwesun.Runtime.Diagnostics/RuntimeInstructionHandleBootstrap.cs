using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Iwesun.Runtime.Diagnostics;

internal readonly record struct RuntimeInstructionHandleDescriptor(
	int Version,
	long ControllerMappingHandle,
	long ControllerEventHandle,
	int ControllerCapacity,
	long UnitMappingHandle,
	long UnitEventHandle,
	int UnitCapacity);

internal static class RuntimeInstructionHandleBootstrap
{
	private const uint DuplicateSameAccess = 0x00000002;

	public static RuntimeInstructionHandleDescriptor DuplicateToProcess(
		Process process,
		RuntimeSharedAtomicFifo controller,
		RuntimeSharedAtomicFifo unit)
	{
		ArgumentNullException.ThrowIfNull(process);
		return new RuntimeInstructionHandleDescriptor(
			1,
			Duplicate(process, controller.MappingHandle).ToInt64(),
			Duplicate(process, controller.EventHandle).ToInt64(),
			controller.Capacity,
			Duplicate(process, unit.MappingHandle).ToInt64(),
			Duplicate(process, unit.EventHandle).ToInt64(),
			unit.Capacity);
	}

	private static nint Duplicate(Process targetProcess, nint sourceHandle)
	{
		if (!DuplicateHandle(
			GetCurrentProcess(),
			sourceHandle,
			targetProcess.Handle,
			out var targetHandle,
			0,
			false,
			DuplicateSameAccess))
			throw new InvalidOperationException($"DuplicateHandle failed: {Marshal.GetLastPInvokeError()}");
		return targetHandle;
	}

	[DllImport("kernel32.dll")]
	private static extern nint GetCurrentProcess();

	[DllImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool DuplicateHandle(
		nint sourceProcessHandle,
		nint sourceHandle,
		nint targetProcessHandle,
		out nint targetHandle,
		uint desiredAccess,
		[MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
		uint options);
}
