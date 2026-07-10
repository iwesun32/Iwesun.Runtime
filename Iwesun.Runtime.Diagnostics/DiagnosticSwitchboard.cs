using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public sealed record DiagnosticSectionSnapshot(
	string Section,
	bool Enabled,
	long Seen,
	long Published,
	long Suppressed);

public sealed record DiagnosticStatementSnapshot(
	string Id,
	string? OutputPointId,
	string Section,
	string Kind,
	string Sample,
	long Seen,
	long Published,
	long Suppressed,
	DateTimeOffset LastSeenAt);

public sealed record DiagnosticSwitchboardSnapshot(
	string Compiled,
	string RuntimeDiagnosticsPipeName,
	bool GlobalEnabled,
	bool InputEnabled,
	bool PipeOutputEnabled,
	bool FileOutputEnabled,
	int FifoDepth,
	int FifoCount,
	long FifoDropped,
	string? ConfigPath,
	IReadOnlyList<DiagnosticSectionSnapshot> Sections,
	IReadOnlyList<DiagnosticStatementSnapshot> Statements,
	IReadOnlyList<DiagnosticOutputPointConfig> OutputPoints);

public static class DiagnosticSwitchboard
{
	private static readonly ConcurrentDictionary<string, DiagnosticSectionState> Sections = new(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, DiagnosticStatementState> Statements = new(StringComparer.OrdinalIgnoreCase);
	private static readonly SemaphoreSlim FifoSignal = new(0);
	private static readonly object PumpGate = new();
	private static readonly object SharedFifoGate = new();
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private static ConcurrentQueue<string> Fifo = new();
	private static DiagnosticSwitchboardConfigStore? _configStore;
	private static RuntimeDiagnosticHub? _hub;
	private static DiagnosticSharedFifoBus? _sharedFifo;
	private static CancellationTokenSource? _pumpCts;
	private static Task? _pumpTask;
	private static List<DiagnosticOutputPointConfig> _outputPoints = [];
	private static int _fifoDepth = 1024;
	private static int _fifoCount;
	private static long _fifoDropped;
	private static string? _filePath;
	private static string _runtimeDiagnosticsPipeName = DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName;

	public static bool InputEnabled
	{
		get => RuntimeOutputSwitch.Enabled;
		set => RuntimeOutputSwitch.Enabled = value;
	}

	public static bool GlobalEnabled { get; private set; }
	public static bool PipeOutputEnabled { get; private set; }
	public static bool FileOutputEnabled { get; private set; }
	public static string? ConfigPath => _configStore?.ConfigPath;

	public static void Attach(RuntimeDiagnosticHub hub)
	{
		_hub = hub;
	}

	public static void Initialize(DiagnosticSwitchboardConfigStore configStore)
	{
		_configStore = configStore;
		ApplyConfig(configStore.Load(), publishEvent: false);
	}

	public static void ReloadConfig()
	{
		if (_configStore == null)
			return;

		ApplyConfig(_configStore.Load(), publishEvent: true);
	}

	public static void SaveConfig()
	{
		_configStore?.Save(ToConfig());
	}

	public static void SetGlobal(bool enabled, bool persist = false)
	{
		GlobalEnabled = enabled;
		UpdateInputEnabled();
		PublishControlEvent("runtime.diagnostics", enabled ? "enabled" : "disabled", new { global = enabled });
		if (persist)
			SaveConfig();
	}

	public static void SetSection(string section, bool enabled, bool persist = false)
	{
		var state = Sections.GetOrAdd(NormalizeSection(section), static key => new DiagnosticSectionState(key));
		state.Enabled = enabled;
		PublishControlEvent(state.Section, enabled ? "section-enabled" : "section-disabled", new { state.Section, enabled });
		if (persist)
			SaveConfig();
	}

	public static DiagnosticSwitchboardSnapshot Snapshot()
	{
		return new DiagnosticSwitchboardSnapshot(
#if DEBUG
			"DEBUG",
#else
			"RELEASE",
#endif
			_runtimeDiagnosticsPipeName,
			GlobalEnabled,
			InputEnabled,
			PipeOutputEnabled,
			FileOutputEnabled,
			_fifoDepth,
			Volatile.Read(ref _fifoCount),
			Interlocked.Read(ref _fifoDropped),
			ConfigPath,
			Sections.Values
				.OrderBy(x => x.Section, StringComparer.OrdinalIgnoreCase)
				.Select(x => new DiagnosticSectionSnapshot(
					x.Section,
					x.Enabled,
					Volatile.Read(ref x.Seen),
					Volatile.Read(ref x.Published),
					Volatile.Read(ref x.Suppressed)))
				.ToArray(),
			Statements.Values
				.OrderByDescending(x => x.LastSeenAt)
				.Take(512)
				.Select(x => new DiagnosticStatementSnapshot(
					x.Id,
					x.OutputPointId,
					x.Section,
					x.Kind,
					x.Sample,
					Volatile.Read(ref x.Seen),
					Volatile.Read(ref x.Published),
					Volatile.Read(ref x.Suppressed),
					x.LastSeenAt))
				.ToArray(),
			_outputPoints.ToArray());
	}

	public static void ReportConsole(string message)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		ReportPoint(RuntimeStaticOutputPoint.RuntimeConsole, InferConsoleSection(message), "console", message, null);
	}

	public static void ReportTrace(string section, string kind, string message, object? payload = null)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		Report(null, section, kind, message, payload);
	}

	public static void ReportPoint(string outputPointId, string section, string kind, string message, object? payload = null)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		Report(outputPointId, section, kind, message, payload);
	}

	public static void ReportPoint(RuntimeStaticOutputPoint outputPoint, string section, string kind, string message, object? payload = null)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		Report(RuntimeStaticInjectorCatalog.GetOutputPointId(outputPoint), section, kind, message, payload);
	}

	public static bool PutFifo(string value)
	{
		return PutFifo(value, DiagnosticSharedFifoChannelKind.Monitor);
	}

	private static bool PutFifo(string value, DiagnosticSharedFifoChannelKind channelKind)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return false;

		value ??= "";
		if (TryPutSharedFifo(channelKind, value))
		{
			FifoSignal.Release();
			return true;
		}

		while (Volatile.Read(ref _fifoCount) >= _fifoDepth && Fifo.TryDequeue(out _))
		{
			Interlocked.Decrement(ref _fifoCount);
			Interlocked.Increment(ref _fifoDropped);
		}

		Fifo.Enqueue(value);
		Interlocked.Increment(ref _fifoCount);
		FifoSignal.Release();
		return true;
	}

	public static bool TryGetFifo(out string value)
	{
		var ok = Fifo.TryDequeue(out value!);
		if (ok)
			Interlocked.Decrement(ref _fifoCount);
		return ok;
	}

	public static bool TryPutCommandFrame(RuntimeCommandFrame frame)
	{
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		var shared = EnsureSharedFifo();
		return shared?.TryEnqueueCommand(frame) == true;
	}

	public static bool TryGetCommandFrame(out RuntimeCommandFrame frame)
	{
		frame = default;
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		var shared = EnsureSharedFifo();
		return shared?.TryDequeueCommand(out frame) == true;
	}

	public static bool TryPutStateFrame(RuntimeStateFrame frame)
	{
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		var shared = EnsureSharedFifo();
		return shared?.TryEnqueueState(frame) == true;
	}

	public static bool TryGetStateFrame(out RuntimeStateFrame frame)
	{
		frame = default;
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		var shared = EnsureSharedFifo();
		return shared?.TryDequeueState(out frame) == true;
	}

	public static void SetPipeOutput(bool enabled, bool persist = false)
	{
		PipeOutputEnabled = enabled;
		UpdateInputEnabled();
		PublishControlEvent("runtime.diagnostics", enabled ? "pipe-output-enabled" : "pipe-output-disabled", new { enabled });
		if (persist)
			SaveConfig();
	}

	public static void SetFileOutput(bool enabled, bool persist = false)
	{
		FileOutputEnabled = enabled;
		UpdateInputEnabled();
		PublishControlEvent("runtime.diagnostics", enabled ? "file-output-enabled" : "file-output-disabled", new { enabled });
		if (persist)
			SaveConfig();
	}

	public static void SetFilePath(string? path, bool persist = false)
	{
		_filePath = string.IsNullOrWhiteSpace(path) ? null : path;
		if (persist)
			SaveConfig();
	}

	public static void SetFifoDepth(int depth, bool persist = false)
	{
		_fifoDepth = Math.Max(1024, depth);
		while (Volatile.Read(ref _fifoCount) > _fifoDepth && Fifo.TryDequeue(out _))
		{
			Interlocked.Decrement(ref _fifoCount);
			Interlocked.Increment(ref _fifoDropped);
		}

		if (persist)
			SaveConfig();
	}

	public static void SetRuntimeDiagnosticsPipeName(string? pipeName, bool persist = false)
	{
		if (!string.IsNullOrWhiteSpace(pipeName))
			_runtimeDiagnosticsPipeName = pipeName.Trim();
		if (persist)
			SaveConfig();
	}

	public static void SetInput(bool enabled, bool persist = false)
	{
		PipeOutputEnabled = enabled;
		UpdateInputEnabled();
		if (persist)
			SaveConfig();
	}

	public static void SetOutputPoint(string outputPointId, bool enabled, bool persist = false)
	{
		if (string.IsNullOrWhiteSpace(outputPointId))
			return;

		var point = _outputPoints.FirstOrDefault(x => x.Id.Equals(outputPointId, StringComparison.OrdinalIgnoreCase));
		if (point == null)
			return;

		point.Enabled = enabled;
		PublishControlEvent(point.Section, enabled ? "output-point-enabled" : "output-point-disabled", new { outputPointId = point.Id, enabled });
		if (persist)
			SaveConfig();
	}

	public static IReadOnlyList<DiagnosticOutputPointConfig> QueryOutputPoints(
		string? id = null,
		string? section = null,
		string? category = null,
		string? sourceLocation = null,
		string? eventKind = null,
		string? text = null)
	{
		return _outputPoints
			.Where(point => IsNullOrEquals(id, point.Id))
			.Where(point => IsNullOrEquals(section, point.Section))
			.Where(point => IsNullOrEquals(category, point.Category))
			.Where(point => IsNullOrContains(sourceLocation, point.SourceLocation))
			.Where(point => IsNullOrContains(eventKind, point.EventKind))
			.Where(point => MatchesFreeText(point, text))
			.OrderBy(point => point.Section, StringComparer.OrdinalIgnoreCase)
			.ThenBy(point => point.Id, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static void Report(string? outputPointId, string section, string kind, string message, object? payload)
	{
		if (!RuntimeOutputSwitch.Enabled)
			return;

		outputPointId = string.IsNullOrWhiteSpace(outputPointId) ? null : outputPointId.Trim();
		section = NormalizeSection(section);
		kind = string.IsNullOrWhiteSpace(kind) ? "trace" : kind.Trim();
		message ??= "";
		var processId = Environment.ProcessId;
		var managedThreadId = Environment.CurrentManagedThreadId;
		var injectorCompileId = string.IsNullOrWhiteSpace(outputPointId)
			? $"{section}:{kind}"
			: outputPointId;
		var injectorRuntimeId = $"{processId}:{managedThreadId}:{injectorCompileId}";

		var channelKind = ResolveSharedChannelKind(section, kind, outputPointId);
		var raw = JsonSerializer.Serialize(new DiagnosticFifoEnvelope(
			outputPointId,
			section,
			kind,
			message,
			payload == null ? null : JsonSerializer.SerializeToElement(payload, JsonOptions),
			DateTimeOffset.UtcNow,
			processId,
			managedThreadId,
			injectorCompileId,
			injectorRuntimeId), JsonOptions);
		PutFifo(raw, channelKind);
	}

	private static void ProcessEnvelope(DiagnosticFifoEnvelope envelope)
	{
		var outputPointId = string.IsNullOrWhiteSpace(envelope.OutputPointId) ? null : envelope.OutputPointId.Trim();
		var section = NormalizeSection(envelope.Section);
		var kind = string.IsNullOrWhiteSpace(envelope.Kind) ? "trace" : envelope.Kind.Trim();
		var message = envelope.Message ?? "";
		var sectionState = Sections.GetOrAdd(section, static key => new DiagnosticSectionState(key));
		Interlocked.Increment(ref sectionState.Seen);

		var id = BuildStatementId(outputPointId, section, kind, message);
		var statementState = Statements.GetOrAdd(id, _ => new DiagnosticStatementState(id, outputPointId, section, kind, Trim(message, 300)));
		Interlocked.Increment(ref statementState.Seen);
		statementState.LastSeenAt = DateTimeOffset.UtcNow;

		if (!GlobalEnabled || !sectionState.Enabled)
		{
			Interlocked.Increment(ref sectionState.Suppressed);
			Interlocked.Increment(ref statementState.Suppressed);
			return;
		}

		if (!IsOutputPointEnabled(outputPointId, section, kind))
		{
			Interlocked.Increment(ref sectionState.Suppressed);
			Interlocked.Increment(ref statementState.Suppressed);
			return;
		}

		Interlocked.Increment(ref sectionState.Published);
		Interlocked.Increment(ref statementState.Published);
		var diagnosticEvent = new RuntimeDiagnosticEvent
		{
			TargetId = "diagnostics.switchboard",
			Kind = kind,
			Message = message,
			Payload = new
			{
				statementId = id,
				outputPointId,
				section,
				processId = envelope.ProcessId,
				managedThreadId = envelope.ManagedThreadId,
				injectorCompileId = envelope.InjectorCompileId,
				injectorRuntimeId = envelope.InjectorRuntimeId,
				timestamp = envelope.Timestamp,
				payload = envelope.Payload
			}
		};
		if (PipeOutputEnabled)
			_hub?.Publish(diagnosticEvent);
		WriteFileLineIfEnabled(envelope.Raw ?? JsonSerializer.Serialize(diagnosticEvent, JsonOptions));
	}

	private static void PublishControlEvent(string section, string message, object payload)
	{
		if (!PipeOutputEnabled)
			return;

		_hub?.Publish(new RuntimeDiagnosticEvent
		{
			TargetId = "diagnostics.switchboard",
			Kind = "switch",
			Message = message,
			Payload = payload
		});
	}

	private static string NormalizeSection(string section) =>
		string.IsNullOrWhiteSpace(section) ? "general" : section.Trim();

	private static string InferConsoleSection(string message)
	{
		if (string.IsNullOrWhiteSpace(message))
			return "console";

		var start = message.IndexOf('[');
		var end = message.IndexOf(']');
		if (start == 0 && end > 1)
			return message[1..end].Split(' ')[0].Trim(':');

		var firstSpace = message.IndexOf(' ');
		return firstSpace > 0 ? message[..firstSpace].Trim(':') : "console";
	}

	private static string BuildStatementId(string? outputPointId, string section, string kind, string message)
	{
		var normalized = $"{outputPointId}|{section}|{kind}|{Trim(message, 512)}";
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
		return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
	}

	private static string Trim(string value, int max) =>
		value.Length <= max ? value : value[..max];

	private static void ApplyConfig(DiagnosticSwitchboardConfig config, bool publishEvent)
	{
		GlobalEnabled = config.GlobalEnabled;
		PipeOutputEnabled = config.PipeOutputEnabled;
		FileOutputEnabled = config.FileOutputEnabled;
		_filePath = config.FilePath;
		_runtimeDiagnosticsPipeName = string.IsNullOrWhiteSpace(config.RuntimeDiagnosticsPipeName)
			? DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName
			: config.RuntimeDiagnosticsPipeName;
		_fifoDepth = Math.Max(1024, config.FifoDepth);
		_outputPoints = config.OutputPoints ?? [];
		foreach (var item in config.Sections)
		{
			var state = Sections.GetOrAdd(NormalizeSection(item.Key), static key => new DiagnosticSectionState(key));
			state.Enabled = item.Value;
		}

		UpdateInputEnabled();
		ResetSharedFifo();
		EnsurePump();
		if (publishEvent)
			PublishControlEvent("runtime.diagnostics", "config-reloaded", new { global = GlobalEnabled, sections = config.Sections.Count });
	}

	private static DiagnosticSwitchboardConfig ToConfig()
	{
		return new DiagnosticSwitchboardConfig
		{
			RuntimeDiagnosticsPipeName = _runtimeDiagnosticsPipeName,
			GlobalEnabled = GlobalEnabled,
			PipeOutputEnabled = PipeOutputEnabled,
			FileOutputEnabled = FileOutputEnabled,
			FilePath = _filePath,
			FifoDepth = _fifoDepth,
			Sections = Sections.Values
				.OrderBy(x => x.Section, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(x => x.Section, x => x.Enabled, StringComparer.OrdinalIgnoreCase),
			OutputPoints = _outputPoints
		};
	}

	private static void UpdateInputEnabled()
	{
		RuntimeOutputSwitch.Enabled = PipeOutputEnabled || FileOutputEnabled;
		if (RuntimeOutputSwitch.Enabled)
			EnsurePump();
	}

	private static void EnsurePump()
	{
		lock (PumpGate)
		{
			if (_pumpTask is { IsCompleted: false })
				return;

			_pumpCts = new CancellationTokenSource();
			var token = _pumpCts.Token;
			_pumpTask = Task.Run(() => PumpAsync(token), token);
		}
	}

	public static async Task ShutdownAsync()
	{
		Task? pumpTask;
		CancellationTokenSource? pumpCts;

		lock (PumpGate)
		{
			pumpTask = _pumpTask;
			pumpCts = _pumpCts;
			_pumpTask = null;
			_pumpCts = null;
		}

		if (pumpCts == null)
		{
			DisposeSharedFifo();
			return;
		}

		try
		{
			pumpCts.Cancel();
			FifoSignal.Release();
		}
		catch
		{
		}

		try
		{
			if (pumpTask != null)
				await pumpTask.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			pumpCts.Dispose();
			DisposeSharedFifo();
		}
	}

	private static async Task PumpAsync(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			await FifoSignal.WaitAsync(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
			while (TryGetFifo(out var raw))
			{
				try
				{
					var envelope = JsonSerializer.Deserialize<DiagnosticFifoEnvelope>(raw, JsonOptions);
					if (envelope != null)
						ProcessEnvelope(envelope with { Raw = raw });
				}
				catch
				{
					var invalidFifoPoint = RuntimeStaticInjectorCatalog.GetOutputPointId(RuntimeStaticOutputPoint.DiagnosticsFifoInvalid);
					ProcessEnvelope(new DiagnosticFifoEnvelope(invalidFifoPoint, "error", "diagnostic.fifo.invalid", "Invalid diagnostic FIFO entry.", null, DateTimeOffset.UtcNow, Environment.ProcessId, Environment.CurrentManagedThreadId, invalidFifoPoint, $"{Environment.ProcessId}:{Environment.CurrentManagedThreadId}:{invalidFifoPoint}", raw));
				}
			}

			while (TryGetSharedFifo(out var sharedRaw))
			{
				try
				{
					var envelope = JsonSerializer.Deserialize<DiagnosticFifoEnvelope>(sharedRaw, JsonOptions);
					if (envelope != null)
						ProcessEnvelope(envelope with { Raw = sharedRaw });
				}
				catch
				{
					var invalidSharedPoint = RuntimeStaticInjectorCatalog.GetOutputPointId(RuntimeStaticOutputPoint.DiagnosticsSharedFifoInvalid);
					ProcessEnvelope(new DiagnosticFifoEnvelope(invalidSharedPoint, "error", "diagnostic.sharedfifo.invalid", "Invalid shared FIFO entry.", null, DateTimeOffset.UtcNow, Environment.ProcessId, Environment.CurrentManagedThreadId, invalidSharedPoint, $"{Environment.ProcessId}:{Environment.CurrentManagedThreadId}:{invalidSharedPoint}", sharedRaw));
				}
			}
		}
	}

	private static DiagnosticSharedFifoChannelKind ResolveSharedChannelKind(string section, string kind, string? outputPointId)
	{
		if (RuntimeStaticInjectorCatalog.TryResolve(outputPointId, section, kind, out var descriptor))
		{
			return descriptor.Channel == RuntimeInjectorChannelKind.Breakpoint
				? DiagnosticSharedFifoChannelKind.Breakpoint
				: DiagnosticSharedFifoChannelKind.Monitor;
		}

		return DiagnosticSharedFifoChannelKind.Monitor;
	}

	private static bool TryPutSharedFifo(DiagnosticSharedFifoChannelKind kind, string value)
	{
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		var shared = EnsureSharedFifo();
		if (shared == null)
		{
			return false;
		}

		return shared.TryEnqueue(kind, value);
	}

	private static bool TryGetSharedFifo(out string value)
	{
		value = string.Empty;
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		var shared = EnsureSharedFifo();
		if (shared == null)
		{
			return false;
		}

		return shared.TryDequeue(out value);
	}

	private static DiagnosticSharedFifoBus? EnsureSharedFifo()
	{
		if (!OperatingSystem.IsWindows())
		{
			return null;
		}

		lock (SharedFifoGate)
		{
			if (_sharedFifo != null)
			{
				return _sharedFifo;
			}

			var scope = $"{DiagnosticPipePrefix.Prefix}.{_runtimeDiagnosticsPipeName}";
			_sharedFifo = DiagnosticSharedFifoBus.TryCreate(scope);
			return _sharedFifo;
		}
	}

	private static void ResetSharedFifo()
	{
		lock (SharedFifoGate)
		{
			if (OperatingSystem.IsWindows())
			{
				_sharedFifo?.Dispose();
			}

			_sharedFifo = null;
		}
	}

	private static void DisposeSharedFifo()
	{
		lock (SharedFifoGate)
		{
			if (OperatingSystem.IsWindows())
			{
				_sharedFifo?.Dispose();
			}

			_sharedFifo = null;
		}
	}

	private static void WriteFileLineIfEnabled(string line)
	{
		if (!FileOutputEnabled)
			return;

		var filePath = _filePath;
		if (string.IsNullOrWhiteSpace(filePath))
			return;

		try
		{
			var dir = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrWhiteSpace(dir))
				Directory.CreateDirectory(dir);
			File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
		}
		catch
		{
			// Diagnostic output must never affect the source runtime.
		}
	}

	private static bool IsOutputPointEnabled(string? outputPointId, string section, string kind)
	{
		if (!string.IsNullOrWhiteSpace(outputPointId))
		{
			var exact = _outputPoints.FirstOrDefault(x => x.Id.Equals(outputPointId, StringComparison.OrdinalIgnoreCase));
			return exact?.Enabled ?? false;
		}

		var matched = false;
		var enabled = false;
		foreach (var point in _outputPoints)
		{
			if (!MatchesOutputPoint(point, section, kind))
				continue;

			matched = true;
			enabled |= point.Enabled;
		}

		return !matched || enabled;
	}

	private static bool MatchesOutputPoint(DiagnosticOutputPointConfig point, string section, string kind) =>
		MatchesAny(point.Section, section) && MatchesAny(point.EventKind, kind);

	private static bool MatchesAny(string patterns, string value)
	{
		if (string.IsNullOrWhiteSpace(patterns))
			return false;

		foreach (var rawPattern in patterns.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (rawPattern == "*")
				return true;
			if (rawPattern.EndsWith('*') && value.StartsWith(rawPattern[..^1], StringComparison.OrdinalIgnoreCase))
				return true;
			if (rawPattern.Equals(value, StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	private static bool IsNullOrEquals(string? expected, string value) =>
		string.IsNullOrWhiteSpace(expected)
		|| value.Equals(expected, StringComparison.OrdinalIgnoreCase);

	private static bool IsNullOrContains(string? expected, string value) =>
		string.IsNullOrWhiteSpace(expected)
		|| value.Contains(expected, StringComparison.OrdinalIgnoreCase);

	private static bool MatchesFreeText(DiagnosticOutputPointConfig point, string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return true;

		return point.Id.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| point.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| point.Purpose.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| point.SourceLocation.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| point.RuntimePath.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| point.DataKind.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| point.RelatedData.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| point.EventKind.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| point.Trigger.Contains(text, StringComparison.OrdinalIgnoreCase)
			|| point.Section.Contains(text, StringComparison.OrdinalIgnoreCase);
	}

	private sealed class DiagnosticSectionState(string section)
	{
		public string Section { get; } = section;
		public bool Enabled { get; set; }
		public long Seen;
		public long Published;
		public long Suppressed;
	}

	private sealed class DiagnosticStatementState(string id, string? outputPointId, string section, string kind, string sample)
	{
		public string Id { get; } = id;
		public string? OutputPointId { get; } = outputPointId;
		public string Section { get; } = section;
		public string Kind { get; } = kind;
		public string Sample { get; } = sample;
		public long Seen;
		public long Published;
		public long Suppressed;
		public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
	}

	private sealed record DiagnosticFifoEnvelope(
		string? OutputPointId,
		string Section,
		string Kind,
		string Message,
		JsonElement? Payload,
		DateTimeOffset Timestamp,
		int ProcessId,
		int ManagedThreadId,
		string InjectorCompileId,
		string InjectorRuntimeId,
		string? Raw = null);
}
