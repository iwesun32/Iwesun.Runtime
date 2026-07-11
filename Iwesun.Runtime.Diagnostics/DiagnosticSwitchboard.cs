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
	string? FilePath,
	string? ResolvedFilePath,
	string FileWriteMode,
	string FileFormat,
	bool ProxyModuleWhitelistEnabled,
	IReadOnlyList<string> ProxyAllowedModules,
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
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private static ConcurrentQueue<string> Fifo = new();
	private static DiagnosticSwitchboardConfigStore? _configStore;
	private static RuntimeDiagnosticHub? _hub;
	private static CancellationTokenSource? _pumpCts;
	private static Task? _pumpTask;
	private static List<DiagnosticOutputPointConfig> _outputPoints = [];
	private static int _fifoDepth = 1024;
	private static int _fifoCount;
	private static long _fifoDropped;
	private static string? _filePath;
	private static FileWriteMode _fileWriteMode = FileWriteMode.Append;
	private static DiagnosticFileFormat _fileFormat = DiagnosticFileFormat.CompactJson;
	private static string? _resolvedFilePath;

	// ── File-write thread ──────────────────────────────────────────────────────
	private static readonly ConcurrentQueue<string> FileQueue = new();
	private static readonly SemaphoreSlim FileSignal = new(0);
	private static int _fileQueueCount;
	private static long _fileQueueDropped;
	private static int _fileQueueDepth = 512;
	private static Thread? _fileWriteThread;
	private static CancellationTokenSource? _fileWriteCts;
	private static RuntimeExecutionManager? _fileWriteExecution;
	private static string _runtimeDiagnosticsPipeName = DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName;
	private static bool _proxyModuleWhitelistEnabled = true;
	private static List<string> _proxyAllowedModules = ["web.runtime"];

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

	public static void AttachExecutionManager(RuntimeExecutionManager execution)
	{
		_fileWriteExecution = execution;
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
			_filePath,
			_resolvedFilePath,
			_fileWriteMode.ToString(),
			_fileFormat.ToString(),
			_proxyModuleWhitelistEnabled,
			_proxyAllowedModules.ToArray(),
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

	public static bool IsProxyModuleAllowed(string module)
	{
		if (!_proxyModuleWhitelistEnabled)
			return true;
		if (string.IsNullOrWhiteSpace(module))
			return false;

		var normalized = module.Trim();
		return _proxyAllowedModules.Any(allowed =>
			normalized.Equals(allowed, StringComparison.OrdinalIgnoreCase)
			|| normalized.StartsWith($"{allowed}.", StringComparison.OrdinalIgnoreCase));
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
		if (!RuntimeOutputSwitch.Enabled)
			return false;

		value ??= "";
		if (Volatile.Read(ref _fifoCount) >= _fifoDepth)
		{
			Interlocked.Increment(ref _fifoDropped);
			return false;
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
		PutFifo(raw);
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
		if (FileOutputEnabled)
		{
			var fileRecord = new DiagnosticFileRecord(
				outputPointId,
				section,
				kind,
				message,
				envelope.Payload,
				envelope.Timestamp,
				envelope.ProcessId,
				envelope.ManagedThreadId,
				id);
			var line = envelope.Raw ?? DiagnosticFileOutputFilter.Format(fileRecord, _fileFormat);
			EnqueueFileWrite(line);
		}
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
		_fileFormat = config.FileFormat;
		InitializeFileOutput(config.FileWriteMode, config.FilePath);
		_runtimeDiagnosticsPipeName = string.IsNullOrWhiteSpace(config.RuntimeDiagnosticsPipeName)
			? DiagnosticSwitchboardCompiledConfig.DefaultRuntimeDiagnosticsPipeName
			: config.RuntimeDiagnosticsPipeName;
		_proxyModuleWhitelistEnabled = config.ProxyModuleWhitelistEnabled;
		_proxyAllowedModules = (config.ProxyAllowedModules ?? [])
			.Where(x => !string.IsNullOrWhiteSpace(x))
			.Select(x => x.Trim())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (_proxyAllowedModules.Count == 0)
			_proxyAllowedModules = ["web.runtime"];
		_fifoDepth = Math.Max(1024, config.FifoDepth);
		_outputPoints = config.OutputPoints ?? [];
		foreach (var item in config.Sections)
		{
			var state = Sections.GetOrAdd(NormalizeSection(item.Key), static key => new DiagnosticSectionState(key));
			state.Enabled = item.Value;
		}

		UpdateInputEnabled();
		EnsurePump();
		if (publishEvent)
			PublishControlEvent("runtime.diagnostics", "config-reloaded", new { global = GlobalEnabled, sections = config.Sections.Count });
	}

	private static DiagnosticSwitchboardConfig ToConfig()
	{
		return new DiagnosticSwitchboardConfig
		{
			RuntimeDiagnosticsPipeName = _runtimeDiagnosticsPipeName,
			ProxyModuleWhitelistEnabled = _proxyModuleWhitelistEnabled,
			ProxyAllowedModules = _proxyAllowedModules.ToList(),
			GlobalEnabled = GlobalEnabled,
			PipeOutputEnabled = PipeOutputEnabled,
			FileOutputEnabled = FileOutputEnabled,
			FilePath = _filePath,
			FileWriteMode = _fileWriteMode,
			FileFormat = _fileFormat,
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

	private static readonly object FileWriteGate = new();

	private static void EnsureFileWriteThread()
	{
		lock (FileWriteGate)
		{
			if (_fileWriteThread is { IsAlive: true })
				return;

			var cts = new CancellationTokenSource();
			_fileWriteCts = cts;

			var thread = new Thread(() => FileWriteLoop(cts.Token))
			{
				Name = "diagnostics.file-write",
				IsBackground = true
			};
			_fileWriteThread = thread;

			// Register with the framework execution manager if available.
			_fileWriteExecution?.RegisterThread(
				"diagnostics.file-write",
				"Diagnostics File Write",
				RuntimeExecutionLifetime.Static,
				RuntimeThreadKind.Worker,
				owner: "Iwesun.Runtime.Diagnostics",
				sourceLocation: "DiagnosticSwitchboard.EnsureFileWriteThread",
				managedThreadId: 0);

			thread.Start();
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

		// Stop file-write thread first so the pump can still enqueue final lines while draining.
		Thread? fileWriteThread;
		CancellationTokenSource? fileWriteCts;
		lock (FileWriteGate)
		{
			fileWriteThread = _fileWriteThread;
			fileWriteCts = _fileWriteCts;
			_fileWriteThread = null;
			_fileWriteCts = null;
		}

		try { fileWriteCts?.Cancel(); FileSignal.Release(); } catch { }

		if (pumpCts == null)
		{
			fileWriteThread?.Join(500);
			fileWriteCts?.Dispose();
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
		}

		// Wait for file thread to drain its queue (up to 2 s).
		fileWriteThread?.Join(2000);
		fileWriteCts?.Dispose();
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

		}
	}

	private static readonly object FileWriteLock = new();

	// ── File queue helpers ─────────────────────────────────────────────────────

	// Special sentinel prefix written into the file when the queue was full.
	private const string FileDroppedPrefix = "[DROPPED]";

	/// <summary>Enqueue a formatted line for the file-write thread. Satisfies the
	/// "満了直接跳过，但留标记" contract: when the queue is full, a single DROPPED
	/// marker line is pushed (overwriting the previous marker if already pending)
	/// so the file reader always knows records are missing here.</summary>
	private static void EnqueueFileWrite(string line)
	{
		if (!FileOutputEnabled)
			return;
		if (string.IsNullOrWhiteSpace(_resolvedFilePath))
			return;

		if (Volatile.Read(ref _fileQueueCount) >= _fileQueueDepth)
		{
			// Queue is full – record the drop count, inject/update a DROPPED marker.
			var dropped = Interlocked.Increment(ref _fileQueueDropped);
			// Replace the tail marker if it already is a DROPPED line, otherwise enqueue a fresh one
			// (best-effort; we never block the caller).
			var marker = $"{FileDroppedPrefix} +{dropped} messages dropped (file-queue full)";
			FileQueue.Enqueue(marker);
			Interlocked.Increment(ref _fileQueueCount);
			FileSignal.Release();
			return;
		}

		FileQueue.Enqueue(line);
		Interlocked.Increment(ref _fileQueueCount);
		FileSignal.Release();
		EnsureFileWriteThread();
	}

	/// <summary>File-write background thread main loop.</summary>
	private static void FileWriteLoop(CancellationToken ct)
	{
		while (!ct.IsCancellationRequested)
		{
			try
			{
				FileSignal.Wait(100, ct);
			}
			catch (OperationCanceledException)
			{
				break;
			}

			while (FileQueue.TryDequeue(out var line))
			{
				Interlocked.Decrement(ref _fileQueueCount);
				WriteFileLine(line);
			}
		}

		// Drain on exit.
		while (FileQueue.TryDequeue(out var line))
		{
			Interlocked.Decrement(ref _fileQueueCount);
			WriteFileLine(line);
		}
	}

	/// <summary>Performs the actual file append. Retries up to 3 times on transient
	/// IOException (antivirus, content indexing). Only called from the file-write thread.</summary>
	private static void WriteFileLine(string line)
	{
		var filePath = _resolvedFilePath;
		if (string.IsNullOrWhiteSpace(filePath))
			return;

		var text = line + Environment.NewLine;
		for (var attempt = 0; attempt < 3; attempt++)
		{
			try
			{
				var dir = Path.GetDirectoryName(filePath);
				if (!string.IsNullOrWhiteSpace(dir))
					Directory.CreateDirectory(dir);
				lock (FileWriteLock)
				{
					File.AppendAllText(filePath, text, Encoding.UTF8);
				}
				return;
			}
			catch (IOException) when (attempt < 2)
			{
				Thread.Sleep(20 * (attempt + 1));
			}
			catch
			{
				// Diagnostic output must never affect the source runtime.
				return;
			}
		}
	}

	/// <summary>Direct synchronous write used only for startup markers and control messages
	/// that must land in the file before the file-write thread is warmed up.</summary>
	private static void WriteFileLineIfEnabled(string line)
	{
		if (!FileOutputEnabled)
			return;
		WriteFileLine(line);
	}

	/// <summary>
	/// Apply file-output defaults from an assembly-level <see cref="DiagnosticFileOutputAttribute"/>.
	/// Only takes effect when the current JSON config has no file path set (FilePath is null/empty),
	/// so persisted user config always wins.
	/// </summary>
	public static void TryApplyAssemblyFileDefaults(string filePath, FileWriteMode writeMode, DiagnosticFileFormat format)
	{
		if (!string.IsNullOrWhiteSpace(_filePath))
			return; // JSON config already has a path – do not override

		_filePath = filePath;
		_fileFormat = format;
		FileOutputEnabled = true;
		InitializeFileOutput(writeMode, filePath);
		UpdateInputEnabled();
		// Write a startup marker so the file is created immediately and the path is confirmed.
		WriteFileLineIfEnabled(DiagnosticFileOutputFilter.Format(
			new DiagnosticFileRecord(null, "runtime.diagnostics", "startup",
				$"File output initialized. format={format}, mode={writeMode}", null,
				DateTimeOffset.UtcNow, Environment.ProcessId, Environment.CurrentManagedThreadId, "startup-marker"),
			_fileFormat));
	}

	private static void InitializeFileOutput(FileWriteMode mode, string? templatePath)
	{
		_fileWriteMode = mode;
		if (string.IsNullOrWhiteSpace(templatePath))
		{
			_resolvedFilePath = null;
			return;
		}

		switch (mode)
		{
			case FileWriteMode.CreateNew:
				_resolvedFilePath = BuildTimestampedFilePath(templatePath);
				break;
			case FileWriteMode.Overwrite:
				_resolvedFilePath = templatePath;
				TruncateFile(templatePath);
				break;
			default: // Append
				_resolvedFilePath = templatePath;
				break;
		}

		RuntimeFileRegistry.Register(
			"runtime.diagnostics.output",
			templatePath,
			_resolvedFilePath,
			mode);
	}

	private static string BuildTimestampedFilePath(string templatePath)
	{
		var dir = Path.GetDirectoryName(templatePath) ?? string.Empty;
		var name = Path.GetFileNameWithoutExtension(templatePath);
		var ext = Path.GetExtension(templatePath);
		var stamp = DateTimeOffset.Now.ToString("yyyyMMddTHHmmss");
		var fileName = $"{name}-{stamp}{ext}";
		return string.IsNullOrWhiteSpace(dir) ? fileName : Path.Combine(dir, fileName);
	}

	private static void TruncateFile(string filePath)
	{
		try
		{
			var dir = Path.GetDirectoryName(filePath);
			if (!string.IsNullOrWhiteSpace(dir))
				Directory.CreateDirectory(dir);
			File.WriteAllText(filePath, string.Empty, Encoding.UTF8);
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
