using System.Runtime.InteropServices;

namespace Iwesun.Runtime.Data;

public enum RuntimeInjectorChannelKind
{
	Monitor = 1,
	Breakpoint = 2,
	Command = 3,
	State = 4
}

public enum RuntimeInjectorFrameKind
{
	Diagnostic = 1,
	Command = 2,
	State = 3
}

public enum RuntimeStaticOutputPoint
{
	RuntimeConsole,
	RuntimeError,
	DiagnosticsFifoInvalid,
	DiagnosticsSharedFifoInvalid,
	SwitchboardControl,
	HookGcCleaned,
	LogConsole,
	LogPipeline,
	LogDns,
	LogNetwork,
	LogPeerSync,
	LogAgentSync,
	LogAddressProbing,
	LogConfiguration,
	LogSecurity,
	LogAgent,
	LogUi,
	LogCore,
	LogService
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct RuntimeSharedFifoChannelHeader(
	int magic,
	int version,
	int writeOffset,
	int readOffset,
	int usedBytes,
	int droppedCount)
{
	public int Magic { get; } = magic;
	public int Version { get; } = version;
	public int WriteOffset { get; } = writeOffset;
	public int ReadOffset { get; } = readOffset;
	public int UsedBytes { get; } = usedBytes;
	public int DroppedCount { get; } = droppedCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly struct RuntimeSharedFifoSlotHeader(
	long sequence,
	int payloadLength,
	int flags)
{
	public long Sequence { get; } = sequence;
	public int PayloadLength { get; } = payloadLength;
	public int Flags { get; } = flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct RuntimeInjectorDescriptor(
	string IdPattern,
	RuntimeInjectorChannelKind Channel,
	RuntimeInjectorFrameKind FrameKind,
	string Section,
	string Kind,
	bool PrefixMatch = false);

public static class RuntimeInjectorIdPatterns
{
	public const string RuntimeConsole = "runtime.console";
	public const string RuntimeError = "runtime.error";
	public const string DiagnosticsFifoInvalid = "diagnostics.fifo.invalid";
	public const string DiagnosticsSharedFifoInvalid = "diagnostics.sharedfifo.invalid";
	public const string SwitchboardControl = "switchboard.control";
	public const string BreakpointHitPrefix = "breakpoint.hit.";
	public const string BreakpointTimeoutPrefix = "breakpoint.timeout.";
	public const string HookAttachedPrefix = "hook.attached.";
	public const string HookDetachedPrefix = "hook.detached.";
	public const string HookFiredPrefix = "hook.fired.";
	public const string HookGcCleaned = "hook.gc-cleaned";
	public const string LogConsole = "log.console";
	public const string LogPipeline = "log.pipeline";
	public const string LogDns = "log.dns";
	public const string LogNetwork = "log.network";
	public const string LogPeerSync = "log.peer-sync";
	public const string LogAgentSync = "log.agent-sync";
	public const string LogAddressProbing = "log.address-probing";
	public const string LogConfiguration = "log.configuration";
	public const string LogSecurity = "log.security";
	public const string LogAgent = "log.agent";
	public const string LogUi = "log.ui";
	public const string LogCore = "log.core";
	public const string LogService = "log.service";
}

public static class RuntimeStaticInjectorCatalog
{
	private static readonly RuntimeInjectorDescriptor[] MonitorDescriptors =
	[
		new(RuntimeInjectorIdPatterns.RuntimeConsole, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "console", "console"),
		new(RuntimeInjectorIdPatterns.RuntimeError, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "error", "error"),
		new(RuntimeInjectorIdPatterns.DiagnosticsFifoInvalid, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "error", "diagnostic.fifo.invalid"),
		new(RuntimeInjectorIdPatterns.DiagnosticsSharedFifoInvalid, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "error", "diagnostic.sharedfifo.invalid"),
		new(RuntimeInjectorIdPatterns.SwitchboardControl, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "runtime.diagnostics", "switch"),
		new(RuntimeInjectorIdPatterns.HookAttachedPrefix, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "hooks", "attach", PrefixMatch: true),
		new(RuntimeInjectorIdPatterns.HookDetachedPrefix, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "hooks", "detach", PrefixMatch: true),
		new(RuntimeInjectorIdPatterns.HookFiredPrefix, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "hooks", "fired", PrefixMatch: true),
		new(RuntimeInjectorIdPatterns.HookGcCleaned, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "hooks", "gc"),
		new(RuntimeInjectorIdPatterns.LogConsole, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "console", "log.*"),
		new(RuntimeInjectorIdPatterns.LogPipeline, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "pipeline", "log.*"),
		new(RuntimeInjectorIdPatterns.LogDns, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "dns", "log.*"),
		new(RuntimeInjectorIdPatterns.LogNetwork, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "network", "log.*"),
		new(RuntimeInjectorIdPatterns.LogPeerSync, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "peer-sync", "log.*"),
		new(RuntimeInjectorIdPatterns.LogAgentSync, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "agent-sync", "log.*"),
		new(RuntimeInjectorIdPatterns.LogAddressProbing, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "address-probing", "log.*"),
		new(RuntimeInjectorIdPatterns.LogConfiguration, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "configuration", "log.*"),
		new(RuntimeInjectorIdPatterns.LogSecurity, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "security", "log.*"),
		new(RuntimeInjectorIdPatterns.LogAgent, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "agent", "log.*"),
		new(RuntimeInjectorIdPatterns.LogUi, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "ui", "log.*"),
		new(RuntimeInjectorIdPatterns.LogCore, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "core", "log.*"),
		new(RuntimeInjectorIdPatterns.LogService, RuntimeInjectorChannelKind.Monitor, RuntimeInjectorFrameKind.Diagnostic, "service", "log.*")
	];

	private static readonly RuntimeInjectorDescriptor[] BreakpointDescriptors =
	[
		new(RuntimeInjectorIdPatterns.BreakpointHitPrefix, RuntimeInjectorChannelKind.Breakpoint, RuntimeInjectorFrameKind.Diagnostic, "breakpoints", "hit", PrefixMatch: true),
		new(RuntimeInjectorIdPatterns.BreakpointTimeoutPrefix, RuntimeInjectorChannelKind.Breakpoint, RuntimeInjectorFrameKind.Diagnostic, "breakpoints", "timeout", PrefixMatch: true)
	];

	private static readonly RuntimeInjectorDescriptor[] AllDescriptors = MonitorDescriptors.Concat(BreakpointDescriptors).ToArray();
	private static readonly Dictionary<string, RuntimeInjectorDescriptor> Exact = AllDescriptors
		.Where(x => !x.PrefixMatch)
		.ToDictionary(x => x.IdPattern, StringComparer.OrdinalIgnoreCase);
	private static readonly RuntimeInjectorDescriptor[] Prefix = AllDescriptors
		.Where(x => x.PrefixMatch)
		.OrderByDescending(x => x.IdPattern.Length)
		.ToArray();

	public static IReadOnlyList<RuntimeInjectorDescriptor> All => AllDescriptors;

	public static string GetOutputPointId(RuntimeStaticOutputPoint point) => point switch
	{
		RuntimeStaticOutputPoint.RuntimeConsole => RuntimeInjectorIdPatterns.RuntimeConsole,
		RuntimeStaticOutputPoint.RuntimeError => RuntimeInjectorIdPatterns.RuntimeError,
		RuntimeStaticOutputPoint.DiagnosticsFifoInvalid => RuntimeInjectorIdPatterns.DiagnosticsFifoInvalid,
		RuntimeStaticOutputPoint.DiagnosticsSharedFifoInvalid => RuntimeInjectorIdPatterns.DiagnosticsSharedFifoInvalid,
		RuntimeStaticOutputPoint.SwitchboardControl => RuntimeInjectorIdPatterns.SwitchboardControl,
		RuntimeStaticOutputPoint.HookGcCleaned => RuntimeInjectorIdPatterns.HookGcCleaned,
		RuntimeStaticOutputPoint.LogConsole => RuntimeInjectorIdPatterns.LogConsole,
		RuntimeStaticOutputPoint.LogPipeline => RuntimeInjectorIdPatterns.LogPipeline,
		RuntimeStaticOutputPoint.LogDns => RuntimeInjectorIdPatterns.LogDns,
		RuntimeStaticOutputPoint.LogNetwork => RuntimeInjectorIdPatterns.LogNetwork,
		RuntimeStaticOutputPoint.LogPeerSync => RuntimeInjectorIdPatterns.LogPeerSync,
		RuntimeStaticOutputPoint.LogAgentSync => RuntimeInjectorIdPatterns.LogAgentSync,
		RuntimeStaticOutputPoint.LogAddressProbing => RuntimeInjectorIdPatterns.LogAddressProbing,
		RuntimeStaticOutputPoint.LogConfiguration => RuntimeInjectorIdPatterns.LogConfiguration,
		RuntimeStaticOutputPoint.LogSecurity => RuntimeInjectorIdPatterns.LogSecurity,
		RuntimeStaticOutputPoint.LogAgent => RuntimeInjectorIdPatterns.LogAgent,
		RuntimeStaticOutputPoint.LogUi => RuntimeInjectorIdPatterns.LogUi,
		RuntimeStaticOutputPoint.LogCore => RuntimeInjectorIdPatterns.LogCore,
		RuntimeStaticOutputPoint.LogService => RuntimeInjectorIdPatterns.LogService,
		_ => throw new ArgumentOutOfRangeException(nameof(point), point, "Unknown static output point.")
	};

	public static string ComposePrefixedId(string prefixPattern, string runtimeSuffix)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(prefixPattern);
		ArgumentException.ThrowIfNullOrWhiteSpace(runtimeSuffix);
		if (!prefixPattern.EndsWith(".", StringComparison.Ordinal))
		{
			throw new ArgumentException("Prefix pattern must end with '.'", nameof(prefixPattern));
		}

		var matched = Prefix.Any(x => x.IdPattern.Equals(prefixPattern, StringComparison.OrdinalIgnoreCase));
		if (!matched)
		{
			throw new InvalidOperationException($"Prefix pattern '{prefixPattern}' is not registered in static injector catalog.");
		}

		return $"{prefixPattern}{runtimeSuffix}";
	}

	public static void ValidateOrThrow()
	{
		var duplicates = AllDescriptors
			.GroupBy(x => $"{x.PrefixMatch}:{x.IdPattern}", StringComparer.OrdinalIgnoreCase)
			.Where(x => x.Count() > 1)
			.Select(x => x.Key)
			.ToArray();
		if (duplicates.Length > 0)
		{
			throw new InvalidOperationException($"Duplicate injector descriptors detected: {string.Join(", ", duplicates)}");
		}

		var invalid = AllDescriptors
			.Where(x => string.IsNullOrWhiteSpace(x.IdPattern) || string.IsNullOrWhiteSpace(x.Section) || string.IsNullOrWhiteSpace(x.Kind))
			.ToArray();
		if (invalid.Length > 0)
		{
			throw new InvalidOperationException("Injector descriptor contains empty IdPattern/Section/Kind.");
		}
	}

	public static bool TryResolve(string? outputPointId, string section, string kind, out RuntimeInjectorDescriptor descriptor)
	{
		outputPointId = string.IsNullOrWhiteSpace(outputPointId) ? null : outputPointId.Trim();
		if (!string.IsNullOrWhiteSpace(outputPointId))
		{
			if (Exact.TryGetValue(outputPointId, out descriptor))
			{
				return true;
			}

			foreach (var prefix in Prefix)
			{
				if (outputPointId.StartsWith(prefix.IdPattern, StringComparison.OrdinalIgnoreCase))
				{
					descriptor = prefix;
					return true;
				}
			}
		}

		if (section.Equals("breakpoints", StringComparison.OrdinalIgnoreCase))
		{
			descriptor = BreakpointDescriptors[0];
			return true;
		}

		descriptor = default;
		return false;
	}
}
