using System.Text.Json;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public sealed class DiagnosticSwitchboardConfig
{
	public int SchemaVersion { get; set; } = 1;
	public string GeneratedFrom { get; set; } = DiagnosticSwitchboardCompiledConfig.GeneratedFrom;
	public string RuntimeDiagnosticsPipeName { get; set; } = DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName;
	public bool GlobalEnabled { get; set; }
	public bool PipeOutputEnabled { get; set; }
	public bool FileOutputEnabled { get; set; }
	public string? FilePath { get; set; }
	public int FifoDepth { get; set; } = 1024;
	public Dictionary<string, bool> Sections { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	public List<DiagnosticOutputPointConfig> OutputPoints { get; set; } = [];
}

public sealed class DiagnosticOutputPointConfig
{
	public string Id { get; set; } = "";
	public string Category { get; set; } = "";
	public string Purpose { get; set; } = "";
	public string SourceLocation { get; set; } = "";
	public string RuntimePath { get; set; } = "";
	public string DataKind { get; set; } = "";
	public string RelatedData { get; set; } = "";
	public string EventKind { get; set; } = "";
	public string Trigger { get; set; } = "";
	public string Section { get; set; } = "";
	public bool Enabled { get; set; }
}

public sealed class DiagnosticSwitchboardConfigStore
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	public DiagnosticSwitchboardConfigStore(string? runtimeDirectory = null)
	{
		RuntimeDirectory = ResolveRuntimeDirectory(runtimeDirectory);
	}

	public string RuntimeDirectory { get; }
	public string ConfigPath => Path.Combine(RuntimeDirectory, "diagnostic-switchboard.json");

	public DiagnosticSwitchboardConfig Load()
	{
		Directory.CreateDirectory(RuntimeDirectory);
		if (!File.Exists(ConfigPath))
		{
			var defaults = DiagnosticSwitchboardCompiledConfig.CreateDefaults();
			Save(defaults);
			return defaults;
		}

		try
		{
			var json = File.ReadAllText(ConfigPath);
			var config = JsonSerializer.Deserialize<DiagnosticSwitchboardConfig>(json, JsonOptions);
			config = DiagnosticSwitchboardCompiledConfig.MergeWithCompiledDefaults(config);
			Save(config);
			return config;
		}
		catch
		{
			var defaults = DiagnosticSwitchboardCompiledConfig.CreateDefaults();
			Save(defaults);
			return defaults;
		}
	}

	public void Save(DiagnosticSwitchboardConfig config)
	{
		config = DiagnosticSwitchboardCompiledConfig.MergeWithCompiledDefaults(config);
		Directory.CreateDirectory(RuntimeDirectory);
		File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, JsonOptions));
	}

	private static string ResolveRuntimeDirectory(string? runtimeDirectory)
	{
		if (!string.IsNullOrWhiteSpace(runtimeDirectory))
			return Path.GetFullPath(runtimeDirectory);

		var localApplicationData = Environment.GetFolderPath(
			Environment.SpecialFolder.LocalApplicationData,
			Environment.SpecialFolderOption.Create);
		return Path.Combine(localApplicationData, "Iwesun", "RuntimeDiagnostics");
	}
}

public static class DiagnosticSwitchboardCompiledConfig
{
	public const int SchemaVersion = 4;
	public const string GeneratedFrom = "compiled:DdnsSnapDiagnosticSwitchboard:v4";
	public const string DefaultRuntimeDiagnosticsPipeName = "DdnsSnap.RuntimeDiagnostics";

	public static DiagnosticSwitchboardConfig MergeWithCompiledDefaults(DiagnosticSwitchboardConfig? configured)
	{
		var defaults = CreateDefaults();
		if (configured == null)
			return defaults;

		var isCurrentCompiledConfig =
			configured.SchemaVersion == SchemaVersion
			&& string.Equals(configured.GeneratedFrom, GeneratedFrom, StringComparison.Ordinal);

		var config = new DiagnosticSwitchboardConfig
		{
			SchemaVersion = SchemaVersion,
			GeneratedFrom = GeneratedFrom,
			RuntimeDiagnosticsPipeName = string.IsNullOrWhiteSpace(configured.RuntimeDiagnosticsPipeName)
				? DefaultRuntimeDiagnosticsPipeName
				: configured.RuntimeDiagnosticsPipeName,
			GlobalEnabled = isCurrentCompiledConfig && configured.GlobalEnabled,
			PipeOutputEnabled = isCurrentCompiledConfig && configured.PipeOutputEnabled,
			FileOutputEnabled = isCurrentCompiledConfig && configured.FileOutputEnabled,
			FilePath = configured.FilePath,
			FifoDepth = configured.FifoDepth,
			Sections = defaults.Sections,
			OutputPoints = defaults.OutputPoints
		};

		if (isCurrentCompiledConfig && configured.Sections != null)
		{
			foreach (var item in configured.Sections)
				config.Sections[item.Key] = item.Value;
		}

		if (configured.OutputPoints != null && configured.OutputPoints.Count > 0)
		{
			var byId = config.OutputPoints.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
			foreach (var item in configured.OutputPoints.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
			{
				if (byId.TryGetValue(item.Id, out var compiled))
				{
					compiled.Enabled = isCurrentCompiledConfig && item.Enabled;
					continue;
				}

				if (!isCurrentCompiledConfig)
					item.Enabled = false;
				config.OutputPoints.Add(item);
			}
		}

		if (config.FifoDepth < 1024)
			config.FifoDepth = 1024;
		config.Sections = new Dictionary<string, bool>(config.Sections, StringComparer.OrdinalIgnoreCase);
		return config;
	}

	public static DiagnosticSwitchboardConfig CreateDefaults()
	{
		return new DiagnosticSwitchboardConfig
		{
			SchemaVersion = SchemaVersion,
			GeneratedFrom = GeneratedFrom,
			RuntimeDiagnosticsPipeName = DefaultRuntimeDiagnosticsPipeName,
			GlobalEnabled = false,
			PipeOutputEnabled = false,
			FileOutputEnabled = false,
			FilePath = null,
			FifoDepth = 1024,
			Sections = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
			{
				["console"] = false,
				["error"] = false,
				["service"] = false,
				["agent"] = false,
				["ui"] = false,
				["core"] = false,
				["configuration"] = false,
				["security"] = false,
				["dns"] = false,
				["pipeline"] = false,
				["network"] = false,
				["peer-sync"] = false,
				["agent-sync"] = false,
				["address-probing"] = false,
				["runtime.diagnostics"] = false
			},
			OutputPoints = CreateDefaultOutputPoints()
		};
	}

	public static void ValidateStaticCatalogAlignment()
	{
		var required = Enum.GetValues<RuntimeStaticOutputPoint>()
			.Select(RuntimeStaticInjectorCatalog.GetOutputPointId)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var configured = CreateDefaultOutputPoints()
			.Select(x => x.Id)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		var missing = required.Where(x => !configured.Contains(x)).ToArray();
		if (missing.Length > 0)
		{
			throw new InvalidOperationException($"Switchboard default output points missing static catalog IDs: {string.Join(", ", missing)}");
		}
	}

	private static List<DiagnosticOutputPointConfig> CreateDefaultOutputPoints()
	{
		var points = new List<DiagnosticOutputPointConfig>
		{
			Point(
				"runtime.console",
				"runtime",
				"Direct RuntimeOutput text output.",
				"Iwesun.Runtime.Diagnostics.RuntimeOutput",
				"RuntimeOutput.Log -> DiagnosticSwitchboard.ReportConsole -> FIFO",
				"text",
				"message",
				"console",
				"RuntimeOutput.Log call",
				"console"),
			Point(
				"runtime.error",
				"runtime",
				"Direct RuntimeOutput error output.",
				"Iwesun.Runtime.Diagnostics.RuntimeOutput",
				"RuntimeOutput.Error -> DiagnosticSwitchboard.ReportPoint -> FIFO",
				"structured-json",
				"message, payload",
				"error",
				"RuntimeOutput.Error call",
				"error"),
			Point(
				"diagnostics.fifo.invalid",
				"runtime-control",
				"Invalid FIFO envelope fallback monitor.",
				"Iwesun.Runtime.Diagnostics.DiagnosticSwitchboard",
				"DiagnosticSwitchboard.PumpAsync -> ProcessEnvelope",
				"text",
				"raw FIFO line",
				"diagnostic.fifo.invalid",
				"FIFO JSON parse failure",
				"error"),
			Point(
				"diagnostics.sharedfifo.invalid",
				"runtime-control",
				"Invalid shared FIFO envelope fallback monitor.",
				"Iwesun.Runtime.Diagnostics.DiagnosticSwitchboard",
				"DiagnosticSwitchboard.PumpAsync -> ProcessEnvelope",
				"text",
				"raw shared FIFO line",
				"diagnostic.sharedfifo.invalid",
				"Shared FIFO JSON parse failure",
				"error"),
			Point(
				"hook.gc-cleaned",
				"runtime-control",
				"Hook auto-detach diagnostic when weak target was garbage collected.",
				"Iwesun.Runtime.Diagnostics.RuntimeDiagnosticHooks",
				"RuntimeDiagnosticHooks.CheckGcCleanup -> DiagnosticSwitchboard.ReportPoint",
				"structured-json",
				"hookId",
				"gc",
				"WeakReference target was GC collected",
				"hooks"),
			Point(
				"switchboard.control",
				"runtime-control",
				"Runtime switch, configuration reload, and monitoring command events.",
				"Iwesun.Runtime.Diagnostics.DiagnosticSwitchboardTarget",
				"RuntimeDiagnosticAction -> DiagnosticSwitchboardTarget",
				"structured-json",
				"targetId, action, args, switch state",
				"switch/command/diagnostic.selftest",
				"Runtime diagnostics command",
				"runtime.diagnostics"),
			Point(
				"service.cli",
				"service-cli",
				"Service install/uninstall/start/stop command diagnostics.",
				"DdnsSnap.Service.Program",
				"Service CLI command -> RuntimeOutput.TracePoint -> FIFO",
				"structured-json",
				"serviceName, sc.exe args, exitCode",
				"cli.*",
				"Service executable non-host command path",
				"service"),
			Point(
				"service.peer-retry",
				"peer-sync",
				"Peer HTTP retry policy diagnostics.",
				"DdnsSnap.Service.Program",
				"Polly retry callback -> RuntimeOutput.TracePoint -> FIFO",
				"structured-json",
				"peerUrl, exception/statusCode, retryCount, delaySeconds",
				"retry",
				"PeerSync HTTP transient failure",
				"peer-sync"),
			Point(
				"ui.startup",
				"ui",
				"WinUI startup and tray initialization diagnostics.",
				"DdnsSnap.UI.StartupLogger",
				"StartupLogger -> RuntimeOutput.TracePoint -> FIFO",
				"structured-json/text",
				"message, exception",
				"startup.*",
				"UI startup, launch, tray, and startup exception events",
				"ui"),
			Point(
				"agent.worker-trigger",
				"agent-sync",
				"Agent worker wake/trigger diagnostics.",
				"DdnsSnap.Agent.Worker",
				"Worker.TriggerCycle -> RuntimeOutput.TracePoint -> FIFO",
				"structured-json",
				"hasWakeHandle",
				"worker.trigger",
				"Agent worker TriggerCycle invoked by runtime or pipe",
				"agent-sync"),
			Point(
				"agent.worker-cycle",
				"agent-sync",
				"Agent worker cycle lifecycle diagnostics.",
				"DdnsSnap.Agent.Worker",
				"Worker.RunCycleAsync -> RuntimeOutput.TracePoint -> FIFO",
				"structured-json/text",
				"cycle start, config validity, completion",
				"worker.cycle.diagnostic-run/worker.cycle.start/worker.config.incomplete/worker.cycle.complete",
				"Agent worker cycle execution",
				"agent-sync"),
			Point(
				"agent.public-ip",
				"agent-sync",
				"Agent public IP detection diagnostics.",
				"DdnsSnap.Agent.Worker",
				"Worker.DetectPublicIpAsync -> RuntimeOutput.TracePoint -> FIFO",
				"structured-json/text",
				"ipv4, ipv6, exception",
				"public-ip.skipped/public-ip.detected/public-ip.failed",
				"Agent public IP detection step",
				"agent-sync"),
			Point(
				"agent.neighbor-scan",
				"agent-sync",
				"Agent neighbor scan diagnostics.",
				"DdnsSnap.Agent.Worker",
				"Worker.ScanNeighborsAsync -> RuntimeOutput.TracePoint -> FIFO",
				"structured-json/text",
				"machines, unnamed, exception",
				"neighbor.scan.skipped/neighbor.scan.complete/neighbor.scan.failed",
				"Agent neighbor discovery step",
				"agent-sync"),
			Point(
				"agent.heartbeat",
				"agent-sync",
				"Agent heartbeat diagnostics.",
				"DdnsSnap.Agent.Worker",
				"Worker.HeartbeatAsync -> RuntimeOutput.TracePoint -> FIFO",
				"structured-json/text",
				"server count, success count, cached tree summary, exception",
				"heartbeat.skipped/heartbeat.complete/heartbeat.failed",
				"Agent heartbeat step",
				"agent-sync"),
			Point(
				"runner.event-pipeline",
				"runner",
				"Event-driven pipeline E2E runner artifact diagnostics.",
				"DdnsSnap.EventDrivenPipelineE2ERunner.Program",
				"E2E runner -> RuntimeOutput.TracePoint -> FIFO",
				"structured-json",
				"artifact paths, scan summary",
				"runner.*",
				"Manual event pipeline runner",
				"pipeline"),
			Point(
				"runner.china-ip",
				"runner",
				"China IP E2E runner check and artifact diagnostics.",
				"DdnsSnap.ChinaIpE2ERunner.Program",
				"China IP runner -> RuntimeOutput.TracePoint -> FIFO",
				"structured-json",
				"check status, artifact paths, exception",
				"runner.*",
				"Manual China IP runner",
				"network")
		};

		points.AddRange(new[]
		{
			LogPoint("log.service", "service", "Service host and API ILogger events."),
			LogPoint("log.agent", "agent", "Agent process ILogger events."),
			LogPoint("log.ui", "ui", "WinUI startup and UI projection ILogger events."),
			LogPoint("log.core", "core", "Core library ILogger events."),
			LogPoint("log.configuration", "configuration", "Configuration load/save, secrets, and validation ILogger events."),
			LogPoint("log.security", "security", "Token, signature, whitelist, and certificate ILogger events."),
			LogPoint("log.dns", "dns", "DNS provider query/update ILogger events."),
			LogPoint("log.pipeline", "pipeline", "DDNS pipeline stage ILogger events."),
			LogPoint("log.network", "network", "Network scan and IP detection ILogger events."),
			LogPoint("log.peer-sync", "peer-sync", "Peer push/pull synchronization ILogger events."),
			LogPoint("log.agent-sync", "agent-sync", "Agent heartbeat and registration ILogger events."),
			LogPoint("log.address-probing", "address-probing", "Server address probing ILogger events."),
			LogPoint("log.console", "console", "Unclassified Microsoft.Extensions.Logging output.")
		});

		return points;
	}

	private static DiagnosticOutputPointConfig LogPoint(string id, string section, string purpose) =>
		Point(
			id,
			"logging",
			purpose,
			"Iwesun.Runtime.Diagnostics.RuntimeDiagnosticLoggerProvider",
			"ILogger -> RuntimeDiagnosticLogger -> DiagnosticSwitchboard.ReportPoint -> FIFO",
			"structured-json",
			"category, logLevel, eventId, exception, state",
			"log.*",
			"ILogger log call",
			section);

	private static DiagnosticOutputPointConfig Point(
		string id,
		string category,
		string purpose,
		string sourceLocation,
		string runtimePath,
		string dataKind,
		string relatedData,
		string eventKind,
		string trigger,
		string section) =>
		new()
		{
			Id = id,
			Category = category,
			Purpose = purpose,
			SourceLocation = sourceLocation,
			RuntimePath = runtimePath,
			DataKind = dataKind,
			RelatedData = relatedData,
			EventKind = eventKind,
			Trigger = trigger,
			Section = section,
			Enabled = false
		};
}
