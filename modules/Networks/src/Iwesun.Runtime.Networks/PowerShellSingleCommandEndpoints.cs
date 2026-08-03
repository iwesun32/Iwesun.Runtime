using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;

namespace Iwesun.Runtime.Networks;

[InlineArray(32)]
public struct FixedBytes32
{
	private byte _element0;
}

[InlineArray(8192)]
public struct FixedBytes8192
{
	private byte _element0;
}

[InlineArray(32768)]
public struct FixedBytes32768
{
	private byte _element0;
}

public static class FixedBytesCodec
{
	public static ushort CopyFrom(ref FixedBytes32 target, ReadOnlySpan<byte> source)
	{
		var length = Math.Min(source.Length, 32);
		for (var i = 0; i < length; i++)
		{
			target[i] = source[i];
		}

		return (ushort)length;
	}

	public static ushort CopyFrom(ref FixedBytes8192 target, ReadOnlySpan<byte> source)
	{
		var length = Math.Min(source.Length, 8192);
		for (var i = 0; i < length; i++)
		{
			target[i] = source[i];
		}

		return (ushort)length;
	}

	public static ushort CopyFrom(ref FixedBytes32768 target, ReadOnlySpan<byte> source)
	{
		var length = Math.Min(source.Length, 32768);
		for (var i = 0; i < length; i++)
		{
			target[i] = source[i];
		}

		return (ushort)length;
	}

	public static byte[] ToArray(in FixedBytes32 source, int length)
	{
		var actual = Math.Max(0, Math.Min(length, 32));
		var buffer = new byte[actual];
		for (var i = 0; i < actual; i++)
		{
			buffer[i] = source[i];
		}

		return buffer;
	}

	public static byte[] ToArray(in FixedBytes8192 source, int length)
	{
		var actual = Math.Max(0, Math.Min(length, 8192));
		var buffer = new byte[actual];
		for (var i = 0; i < actual; i++)
		{
			buffer[i] = source[i];
		}

		return buffer;
	}

	public static byte[] ToArray(in FixedBytes32768 source, int length)
	{
		var actual = Math.Max(0, Math.Min(length, 32768));
		var buffer = new byte[actual];
		for (var i = 0; i < actual; i++)
		{
			buffer[i] = source[i];
		}

		return buffer;
	}
}

public enum PowerShellWriteOutputCommandKind : byte
{
	Alpha = 1,
	Beta = 2
}

public readonly record struct PowerShellWriteOutputBinaryRequest(
	Guid RequestId,
	PowerShellWriteOutputCommandKind CommandKind,
	uint TimeoutMs = 3000,
	Guid CorrelationId = default)
{
	public static PowerShellWriteOutputBinaryRequest Alpha(Guid requestId, uint timeoutMs = 3000)
	{
		return new PowerShellWriteOutputBinaryRequest(requestId, PowerShellWriteOutputCommandKind.Alpha, timeoutMs);
	}

	public static PowerShellWriteOutputBinaryRequest Beta(Guid requestId, uint timeoutMs = 3000)
	{
		return new PowerShellWriteOutputBinaryRequest(requestId, PowerShellWriteOutputCommandKind.Beta, timeoutMs);
	}
}

public readonly record struct PowerShellWriteOutputBinaryResult(
	Guid RequestId,
	PowerShellWriteOutputCommandKind CommandKind,
	FixedBytes32 Output,
	ushort OutputLength,
	int ExitCode,
	bool TimedOut,
	BinaryNetworkError Error,
	long CompletedAtUnixMs,
	Guid CorrelationId = default)
{
	public byte[] GetOutputBytes()
	{
		return FixedBytesCodec.ToArray(Output, OutputLength);
	}
}

public sealed class PowerShellWriteOutputEndpoint : NetworkAsyncEndpointBase<PowerShellWriteOutputBinaryRequest, PowerShellWriteOutputBinaryResult>
{
	private const string Executable = "powershell";
	private readonly ProcessCommandEndpoint _processEndpoint;
	private readonly ConcurrentDictionary<Guid, PowerShellWriteOutputBinaryRequest> _pending = new();

	public PowerShellWriteOutputEndpoint(
		SynchronizationContext? eventContext = null,
		int maxSendQueueLength = 256,
		int maxReceiveQueueLength = 256,
		int sendQueueAvailableThreshold = 1)
		: base(eventContext, maxSendQueueLength, maxReceiveQueueLength, sendQueueAvailableThreshold)
	{
		_processEndpoint = new ProcessCommandEndpoint(eventContext, maxSendQueueLength, maxReceiveQueueLength, sendQueueAvailableThreshold);
		_processEndpoint.ReceiveCompleted += OnProcessReceiveCompleted;
	}

	protected override bool OnValidateSend(PowerShellWriteOutputBinaryRequest item, out string? reason)
	{
		reason = item.RequestId == Guid.Empty
			? NetworkAccessFailureCodes.RequestIdEmpty
			: null;
		return reason is null;
	}

	protected override bool OnFilterSend(PowerShellWriteOutputBinaryRequest item, out string? reason)
	{
		reason = null;
		return item.CommandKind is PowerShellWriteOutputCommandKind.Alpha or PowerShellWriteOutputCommandKind.Beta;
	}

	protected override bool OnAnalyzeSend(PowerShellWriteOutputBinaryRequest item, out PowerShellWriteOutputBinaryRequest analyzedItem, out string? reason)
	{
		analyzedItem = item with
		{
			TimeoutMs = item.TimeoutMs == 0 ? 3000u : item.TimeoutMs
		};
		reason = null;
		return true;
	}

	protected override NetworkSendResult OnSend(PowerShellWriteOutputBinaryRequest item)
	{
		var script = BuildScript(item.CommandKind);
		var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
		var arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";
		_pending[item.RequestId] = item;
		_processEndpoint.Send(new ProcessCommandRequest(item.RequestId, Executable, arguments, null, checked((int)Math.Min(item.TimeoutMs, int.MaxValue))));
		return NetworkSendResult.Sent();
	}

	protected override bool OnAnalyzeReceive(object raw, Action<PowerShellWriteOutputBinaryResult> emit, out string? reason)
	{
		if (raw is not PowerShellWriteOutputRaw completed)
		{
			reason = "powershell-write-output-raw-type-mismatch";
			return false;
		}

		var outputText = completed.StandardOutput.Trim();
		var outputBytes = Encoding.UTF8.GetBytes(outputText);
		FixedBytes32 output = default;
		var outputLength = FixedBytesCodec.CopyFrom(ref output, outputBytes);

		emit(new PowerShellWriteOutputBinaryResult(
			completed.Request.RequestId,
			completed.Request.CommandKind,
			output,
			outputLength,
			completed.ExitCode,
			completed.TimedOut,
			BinaryNetworkError.FromException(completed.Error, completed.TimedOut, false),
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			completed.Request.CorrelationId));

		reason = null;
		return true;
	}

	protected override void OnStopping()
	{
		_processEndpoint.Stop();
		_pending.Clear();
	}

	protected override void OnResetHardware()
	{
		_processEndpoint.ResetHardware();
	}

	private void OnProcessReceiveCompleted(object? sender, NetworkReceiveEventArgs<ProcessCommandResult> e)
	{
		var request = _pending.TryRemove(e.Item.RequestId, out var pending)
			? pending
			: new PowerShellWriteOutputBinaryRequest(e.Item.RequestId, PowerShellWriteOutputCommandKind.Alpha, 0);
		PublishReceivedRaw(new PowerShellWriteOutputRaw(
			request,
			e.Item.ExitCode,
			e.Item.TimedOut,
			e.Item.StandardOutput,
			e.Item.Error));
	}

	private static string BuildScript(PowerShellWriteOutputCommandKind kind)
	{
		return kind switch
		{
			PowerShellWriteOutputCommandKind.Alpha => "Write-Output alpha",
			PowerShellWriteOutputCommandKind.Beta => "Write-Output beta",
			_ => throw new NotSupportedException($"Unsupported PowerShell command kind: {kind}")
		};
	}

	private readonly record struct PowerShellWriteOutputRaw(
		PowerShellWriteOutputBinaryRequest Request,
		int ExitCode,
		bool TimedOut,
		string StandardOutput,
		Exception? Error);
}

public readonly record struct PowerShellIpv6NeighborSnapshotBinaryRequest(
	Guid RequestId,
	uint TimeoutMs = 3000,
	Guid CorrelationId = default);

public readonly record struct PowerShellIpv6NeighborSnapshotBinaryResult(
	Guid RequestId,
	FixedBytes32768 Csv,
	ushort CsvLength,
	int ExitCode,
	bool TimedOut,
	BinaryNetworkError Error,
	long CompletedAtUnixMs,
	Guid CorrelationId = default)
{
	public byte[] GetCsvBytes()
	{
		return FixedBytesCodec.ToArray(Csv, CsvLength);
	}
}

public sealed class PowerShellIpv6NeighborSnapshotEndpoint : NetworkAsyncEndpointBase<PowerShellIpv6NeighborSnapshotBinaryRequest, PowerShellIpv6NeighborSnapshotBinaryResult>
{
	private const string Executable = "powershell";
	private const string SnapshotScript =
		"Get-NetNeighbor -AddressFamily IPv6 | " +
		"Where-Object { -not $_.IPAddress.StartsWith('ff') } | " +
		"Sort-Object @{Expression={ if ($_.LinkLayerAddress -match '^([0-9A-Fa-f]{2}-){5}[0-9A-Fa-f]{2}$' -and $_.LinkLayerAddress -ne '00-00-00-00-00-00') { 0 } else { 1 } }}, InterfaceIndex, IPAddress | " +
		"Select-Object IPAddress,LinkLayerAddress,State,InterfaceIndex | " +
		"ConvertTo-Csv -NoTypeInformation";

	private readonly ProcessCommandEndpoint _processEndpoint;
	private readonly ConcurrentDictionary<Guid, PowerShellIpv6NeighborSnapshotBinaryRequest> _pending = new();

	public PowerShellIpv6NeighborSnapshotEndpoint(
		SynchronizationContext? eventContext = null,
		int maxSendQueueLength = 64,
		int maxReceiveQueueLength = 4096,
		int sendQueueAvailableThreshold = 1)
		: base(eventContext, maxSendQueueLength, maxReceiveQueueLength, sendQueueAvailableThreshold)
	{
		_processEndpoint = new ProcessCommandEndpoint(eventContext, maxSendQueueLength, maxReceiveQueueLength, sendQueueAvailableThreshold);
		_processEndpoint.ReceiveCompleted += OnProcessReceiveCompleted;
	}

	protected override bool OnValidateSend(PowerShellIpv6NeighborSnapshotBinaryRequest item, out string? reason)
	{
		reason = item.RequestId == Guid.Empty
			? NetworkAccessFailureCodes.RequestIdEmpty
			: null;
		return reason is null;
	}

	protected override bool OnAnalyzeSend(PowerShellIpv6NeighborSnapshotBinaryRequest item, out PowerShellIpv6NeighborSnapshotBinaryRequest analyzedItem, out string? reason)
	{
		analyzedItem = item with
		{
			TimeoutMs = item.TimeoutMs == 0 ? 3000u : item.TimeoutMs
		};
		reason = null;
		return true;
	}

	protected override NetworkSendResult OnSend(PowerShellIpv6NeighborSnapshotBinaryRequest item)
	{
		var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(SnapshotScript));
		var arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}";
		_pending[item.RequestId] = item;
		_processEndpoint.Send(new ProcessCommandRequest(item.RequestId, Executable, arguments, null, checked((int)Math.Min(item.TimeoutMs, int.MaxValue))));
		return NetworkSendResult.Sent();
	}

	protected override bool OnAnalyzeReceive(object raw, Action<PowerShellIpv6NeighborSnapshotBinaryResult> emit, out string? reason)
	{
		if (raw is not PowerShellIpv6NeighborSnapshotRaw completed)
		{
			reason = "powershell-ipv6-neighbor-raw-type-mismatch";
			return false;
		}

		var csvBytes = Encoding.UTF8.GetBytes(completed.StandardOutput);
		FixedBytes32768 csv = default;
		var csvLength = FixedBytesCodec.CopyFrom(ref csv, csvBytes);

		emit(new PowerShellIpv6NeighborSnapshotBinaryResult(
			completed.Request.RequestId,
			csv,
			csvLength,
			completed.ExitCode,
			completed.TimedOut,
			BinaryNetworkError.FromException(completed.Error, completed.TimedOut, false),
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
			completed.Request.CorrelationId));

		reason = null;
		return true;
	}

	protected override void OnStopping()
	{
		_processEndpoint.Stop();
		_pending.Clear();
	}

	protected override void OnResetHardware()
	{
		_processEndpoint.ResetHardware();
	}

	private void OnProcessReceiveCompleted(object? sender, NetworkReceiveEventArgs<ProcessCommandResult> e)
	{
		var request = _pending.TryRemove(e.Item.RequestId, out var pending)
			? pending
			: new PowerShellIpv6NeighborSnapshotBinaryRequest(e.Item.RequestId, 0);
		PublishReceivedRaw(new PowerShellIpv6NeighborSnapshotRaw(
			request,
			e.Item.ExitCode,
			e.Item.TimedOut,
			e.Item.StandardOutput,
			e.Item.Error));
	}

	private readonly record struct PowerShellIpv6NeighborSnapshotRaw(
		PowerShellIpv6NeighborSnapshotBinaryRequest Request,
		int ExitCode,
		bool TimedOut,
		string StandardOutput,
		Exception? Error);
}
