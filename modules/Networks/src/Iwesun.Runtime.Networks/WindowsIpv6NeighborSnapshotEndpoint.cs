using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Iwesun.Runtime.Networks;

/// <summary>
/// Single-function adapter for Windows IPv6 neighbor snapshots.
/// One PowerShell CSV result can emit many IPv6 neighbor records.
/// </summary>
public sealed class WindowsIpv6NeighborSnapshotEndpoint
	: NetworkAsyncEndpointBase<WindowsIpv6NeighborSnapshotRequest, WindowsIpv6NeighborRecord>
{
	private readonly PowerShellIpv6NeighborSnapshotEndpoint _powerShellEndpoint;

	public WindowsIpv6NeighborSnapshotEndpoint(
		SynchronizationContext? eventContext = null,
		int maxSendQueueLength = 64,
		int maxReceiveQueueLength = 4096,
		int sendQueueAvailableThreshold = 1)
		: base(eventContext, maxSendQueueLength, maxReceiveQueueLength, sendQueueAvailableThreshold)
	{
		_powerShellEndpoint = new PowerShellIpv6NeighborSnapshotEndpoint(
			eventContext,
			maxSendQueueLength,
			maxReceiveQueueLength,
			sendQueueAvailableThreshold: sendQueueAvailableThreshold);
		_powerShellEndpoint.ReceiveCompleted += OnPowerShellReceiveCompleted;
	}

	protected override bool OnValidateSend(WindowsIpv6NeighborSnapshotRequest item, out string? reason)
	{
		reason = item.RequestId == Guid.Empty
			? NetworkAccessFailureCodes.RequestIdEmpty
			: null;
		return reason is null;
	}

	protected override bool OnAnalyzeSend(
		WindowsIpv6NeighborSnapshotRequest item,
		out WindowsIpv6NeighborSnapshotRequest analyzedItem,
		out string? reason)
	{
		analyzedItem = item with
		{
			TimeoutMs = item.TimeoutMs <= 0 ? 3000 : item.TimeoutMs
		};
		reason = null;
		return true;
	}

	protected override NetworkSendResult OnSend(WindowsIpv6NeighborSnapshotRequest item)
	{
		_powerShellEndpoint.Send(new PowerShellIpv6NeighborSnapshotBinaryRequest(
			item.RequestId,
			(uint)item.TimeoutMs));

		return NetworkSendResult.Sent();
	}

	protected override bool OnAnalyzeReceive(object raw, Action<WindowsIpv6NeighborRecord> emit, out string? reason)
	{
		if (raw is not PowerShellIpv6NeighborSnapshotBinaryResult result)
		{
			return base.OnAnalyzeReceive(raw, emit, out reason);
		}

		if (result.TimedOut || result.ExitCode != 0)
		{
			reason = result.TimedOut ? "ipv6-neighbor-timeout" : "ipv6-neighbor-command-failed";
			return false;
		}

		var output = Encoding.UTF8.GetString(result.GetCsvBytes());
		var emitted = 0;
		foreach (var line in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
		{
			if (TryParseCsvLine(result.RequestId, line, out var record))
			{
				emit(record);
				emitted++;
			}
		}

		reason = emitted == 0 ? "ipv6-neighbor-empty" : null;
		return emitted > 0;
	}

	protected override void OnStopping()
	{
		_powerShellEndpoint.Stop();
	}

	protected override void OnResetHardware()
	{
		_powerShellEndpoint.ResetHardware();
	}

	private void OnPowerShellReceiveCompleted(object? sender, NetworkReceiveEventArgs<PowerShellIpv6NeighborSnapshotBinaryResult> e)
	{
		PublishReceivedRaw(e.Item);
	}

	internal static bool TryParseCsvLine(Guid requestId, string line, out WindowsIpv6NeighborRecord record)
	{
		record = default;
		var cells = SplitCsvLine(line);
		if (cells.Length < 3)
		{
			return false;
		}

		if (!IPAddress.TryParse(cells[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetworkV6)
		{
			return false;
		}

		if (ip.IsIPv6Multicast)
		{
			return false;
		}

		var mac = ParseMacOrNull(cells[1]);
		var interfaceIndex = 0;
		if (cells.Length >= 4)
		{
			_ = int.TryParse(cells[3], out interfaceIndex);
		}

		record = new WindowsIpv6NeighborRecord(
			requestId,
			mac,
			NetworkIpAddressInterop.FromSystemAddress(ip),
			cells[2],
			interfaceIndex,
			DateTime.UtcNow);
		return true;
	}

	private static string[] SplitCsvLine(string line)
	{
		var csv = line.Trim();
		if (csv.Length < 2)
		{
			return Array.Empty<string>();
		}

		if (csv.StartsWith("\"", StringComparison.Ordinal) && csv.EndsWith("\"", StringComparison.Ordinal))
		{
			csv = csv[1..^1];
		}

		return csv
			.Split("\",\"", StringSplitOptions.None)
			.Select(cell => cell.Trim().Trim('"'))
			.ToArray();
	}

	private static MacAddressValue ParseMacOrNull(string raw)
	{
		if (!MacAddressValue.TryParse(raw, null, out var mac)
			|| !mac.IsValidIdentity
			|| mac.ToString().StartsWith("33:33:", StringComparison.Ordinal))
			return MacAddressValue.Null;
		return mac;
	}
}

public readonly record struct WindowsIpv6NeighborSnapshotRequest(
	Guid RequestId = default,
	int TimeoutMs = 3000);

public readonly record struct WindowsIpv6NeighborRecord(
	Guid RequestId,
	MacAddressValue Mac,
	IpAddressValue Ip,
	string State,
	int InterfaceIndex,
	DateTime ObservedAtUtc);
