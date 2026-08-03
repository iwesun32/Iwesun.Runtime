using System.Net;
using System.Net.Sockets;

namespace Iwesun.Runtime.Networks;

/// <summary>
/// Single-function adapter for Windows ARP table snapshots.
/// One arp.exe output can emit many IPv4 ARP records.
/// </summary>
public sealed class WindowsArpTableEndpoint : NetworkAsyncEndpointBase<WindowsArpTableRequest, WindowsArpTableRecord>
{
	private readonly ProcessCommandEndpoint _processEndpoint;

	public WindowsArpTableEndpoint(
		SynchronizationContext? eventContext = null,
		int maxSendQueueLength = 64,
		int maxReceiveQueueLength = 4096,
		int sendQueueAvailableThreshold = 1)
		: base(eventContext, maxSendQueueLength, maxReceiveQueueLength, sendQueueAvailableThreshold)
	{
		_processEndpoint = new ProcessCommandEndpoint(eventContext, maxSendQueueLength, maxReceiveQueueLength, sendQueueAvailableThreshold);
		_processEndpoint.ReceiveCompleted += OnProcessReceiveCompleted;
	}

	protected override bool OnValidateSend(WindowsArpTableRequest item, out string? reason)
	{
		reason = item.RequestId == Guid.Empty
			? NetworkAccessFailureCodes.RequestIdEmpty
			: null;
		return reason is null;
	}

	protected override bool OnAnalyzeSend(
		WindowsArpTableRequest item,
		out WindowsArpTableRequest analyzedItem,
		out string? reason)
	{
		analyzedItem = item with
		{
			TimeoutMs = item.TimeoutMs <= 0 ? 3000 : item.TimeoutMs
		};
		reason = null;
		return true;
	}

	protected override NetworkSendResult OnSend(WindowsArpTableRequest item)
	{
		_processEndpoint.Send(new ProcessCommandRequest(
			item.RequestId,
			"arp",
			"-a",
			null,
			item.TimeoutMs));

		return NetworkSendResult.Sent();
	}

	protected override bool OnAnalyzeReceive(object raw, Action<WindowsArpTableRecord> emit, out string? reason)
	{
		if (raw is not ProcessCommandResult result)
		{
			return base.OnAnalyzeReceive(raw, emit, out reason);
		}

		if (result.TimedOut || result.ExitCode != 0)
		{
			reason = result.TimedOut ? "arp-timeout" : "arp-command-failed";
			return false;
		}

		var emitted = 0;
		foreach (var line in SplitLines(result.StandardOutput))
		{
			if (TryParseLine(result.RequestId, line, out var record))
			{
				emit(record);
				emitted++;
			}
		}

		reason = emitted == 0 ? "arp-empty" : null;
		return emitted > 0;
	}

	protected override void OnStopping()
	{
		_processEndpoint.Stop();
	}

	protected override void OnResetHardware()
	{
		_processEndpoint.ResetHardware();
	}

	private void OnProcessReceiveCompleted(object? sender, NetworkReceiveEventArgs<ProcessCommandResult> e)
	{
		PublishReceivedRaw(e.Item);
	}

	internal static bool TryParseLine(Guid requestId, string line, out WindowsArpTableRecord record)
	{
		record = default;
		var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 3
			|| !IPAddress.TryParse(parts[0], out var ip)
			|| ip.AddressFamily != AddressFamily.InterNetwork
			|| IPAddress.IsLoopback(ip)
			|| IsIgnoredIpv4(ip))
		{
			return false;
		}

		if (!MacAddressValue.TryParse(parts[1], null, out var mac) || !mac.IsValidIdentity)
		{
			return false;
		}

		record = new WindowsArpTableRecord(
			requestId,
			mac,
			NetworkIpAddressInterop.FromSystemAddress(ip),
			parts[2],
			DateTime.UtcNow);
		return true;
	}

	private static string[] SplitLines(string text)
	{
		return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	private static bool IsIgnoredIpv4(IPAddress ip)
	{
		return ip.GetAddressBytes() is [169, 254, ..] or [224, ..] or [239, ..] or [255, ..];
	}
}

public readonly record struct WindowsArpTableRequest(
	Guid RequestId = default,
	int TimeoutMs = 3000);

public readonly record struct WindowsArpTableRecord(
	Guid RequestId,
	MacAddressValue Mac,
	IpAddressValue Ip,
	string Type,
	DateTime ObservedAtUtc);
