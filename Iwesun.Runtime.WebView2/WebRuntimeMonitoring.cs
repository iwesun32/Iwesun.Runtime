using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeMonitorFilter(string FilterId, string? Kind = null, string? UrlContains = null, bool Enabled = true);

public sealed class WebRuntimeMonitorFilterRegistry
{
	private readonly ConcurrentDictionary<string, WebRuntimeMonitorFilter> _filters = new(StringComparer.OrdinalIgnoreCase);
	public void Add(WebRuntimeMonitorFilter filter) { ArgumentNullException.ThrowIfNull(filter); if (string.IsNullOrWhiteSpace(filter.FilterId)) throw new ArgumentException("FilterId is required.", nameof(filter)); _filters[filter.FilterId] = filter; }
	public bool Remove(string filterId) => _filters.TryRemove(filterId, out _);
	public void Clear() => _filters.Clear();
	public IReadOnlyList<WebRuntimeMonitorFilter> Snapshot() => _filters.Values.OrderBy(x => x.FilterId, StringComparer.OrdinalIgnoreCase).ToArray();
	public bool Accept(string kind, string url)
	{
		var filters = Snapshot().Where(x => x.Enabled).ToArray();
		return filters.Length == 0 || filters.Any(x => (x.Kind is null || x.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase)) && (x.UrlContains is null || url.Contains(x.UrlContains, StringComparison.OrdinalIgnoreCase)));
	}
}

public static class WebRuntimeEvidenceScripts
{
	public static string HighlightXPath(string xpath, string color = "#ff4d4f")
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(xpath);
		var x = System.Text.Json.JsonSerializer.Serialize(xpath);
		var c = System.Text.Json.JsonSerializer.Serialize(color);
		return $"(() => {{ const n=document.evaluate({x},document,null,XPathResult.FIRST_ORDERED_NODE_TYPE,null).singleNodeValue; if(!n) return JSON.stringify({{success:false,code:'XPATH_NOT_FOUND'}}); n.dataset.iwesunRuntimeHighlight='1'; n.style.outline='2px solid '+{c}; return JSON.stringify({{success:true,xpath:{x}}}); }})()";
	}
	public static string ClearHighlights() => "(() => { document.querySelectorAll('[data-iwesun-runtime-highlight=\"1\"]').forEach(n => { n.style.outline=''; delete n.dataset.iwesunRuntimeHighlight; }); return JSON.stringify({success:true}); })()";
}

public static class DataStreamRecorderActions
{
	public const string Create = "data.recorder.create";
	public const string Start = "data.recorder.start";
	public const string Status = "data.recorder.status";
	public const string List = "data.recorder.list";
	public const string Update = "data.recorder.update";
	public const string Stop = "data.recorder.stop";
	public const string Delete = "data.recorder.delete";
	public const string Events = "data.recorder.events";
}

[JsonConverter(typeof(JsonStringEnumConverter<DataStreamTransport>))]
public enum DataStreamTransport { Http, Fetch, XmlHttpRequest, WebSocket, ServerSentEvents, WebMessage, Download }
[JsonConverter(typeof(JsonStringEnumConverter<DataStreamDirection>))]
public enum DataStreamDirection { Request, Response, IncomingMessage, OutgoingMessage }
[JsonConverter(typeof(JsonStringEnumConverter<DataStreamMonitorState>))]
public enum DataStreamMonitorState { Created, Running, Stopped, Faulted }
[JsonConverter(typeof(JsonStringEnumConverter<DataStreamRecorderEventKind>))]
public enum DataStreamRecorderEventKind { MonitorCreated, MonitorStarted, RecordMatched, RecordWritten, RecordDropped, RecordFailed, MonitorUpdated, MonitorStopped, MonitorFaulted, MonitorDeleted }
[JsonConverter(typeof(JsonStringEnumConverter<DataStreamConditionKind>))]
public enum DataStreamConditionKind { Leaf, All, Any, Not, AtLeast, Always, Never }
[JsonConverter(typeof(JsonStringEnumConverter<DataStreamConditionOperator>))]
public enum DataStreamConditionOperator { Exists, NotExists, Equals, NotEquals, Contains, NotContains, StartsWith, EndsWith, Regex, In, NotIn, GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, TypeIs, IsArray, IsObject, ArrayCount, PathExists, PathValue, MagicBytes }
[JsonConverter(typeof(JsonStringEnumConverter<DataStreamCustomMatchMode>))]
public enum DataStreamCustomMatchMode { None, And, Or, Override }
[JsonConverter(typeof(JsonStringEnumConverter<DataStreamDelegateDataAccess>))]
public enum DataStreamDelegateDataAccess { MetadataOnly, BodyPreview, FullBody, ParsedText, ParsedJson }

public sealed record DataStreamCondition
{
	public DataStreamConditionKind Kind { get; init; } = DataStreamConditionKind.Leaf;
	public string? Field { get; init; }
	public DataStreamConditionOperator Operator { get; init; } = DataStreamConditionOperator.Exists;
	public JsonElement? Value { get; init; }
	public int MinimumMatches { get; init; } = 1;
	public IReadOnlyList<DataStreamCondition> Conditions { get; init; } = Array.Empty<DataStreamCondition>();
}

public sealed record DataStreamCustomMatchDefinition
{
	public string? MatcherId { get; init; }
	public DataStreamCustomMatchMode Mode { get; init; }
	public DataStreamDelegateDataAccess DataAccess { get; init; } = DataStreamDelegateDataAccess.FullBody;
	public int PreviewBytes { get; init; } = 8192;
	public bool AlwaysInvokeForExtraction { get; init; }
	public int MaxConsecutiveErrors { get; init; } = 5;
}

public sealed record DataStreamSourceMatch
{
	public string? BackendId { get; init; }
	public IReadOnlyList<DataStreamTransport>? Transports { get; init; }
	public IReadOnlyList<DataStreamDirection>? Directions { get; init; }
	public string? UrlEquals { get; init; }
	public string? UrlContains { get; init; }
	public string? UrlRegex { get; init; }
	public IReadOnlyList<string>? Methods { get; init; }
	public IReadOnlyList<int>? StatusCodes { get; init; }
	public string? ContentTypeContains { get; init; }
}

public sealed record JsonPathCondition
{
	public required string Path { get; init; }
	public JsonElement? ExpectedValue { get; init; }
}

public sealed record DataStreamContentMatch
{
	public IReadOnlyList<string>? RequiredText { get; init; }
	public IReadOnlyList<JsonPathCondition>? JsonConditions { get; init; }
	public bool ParseStringifiedJson { get; init; }
	public string? ClassifierId { get; init; }
}

public sealed record DataStreamOutputDefinition
{
	public required string RootDirectory { get; init; }
	public string DirectoryTemplate { get; init; } = "{monitorId}/{date}";
	public string FileNameTemplate { get; init; } = "{timestamp}-{sequence}-{dataType}-{identity}.{extension}";
	public bool WriteManifest { get; init; } = true;
	public bool WriteRequestBody { get; init; }
}

public sealed record DataStreamLimits
{
	public long MaxBodyBytes { get; init; } = 64 * 1024 * 1024;
	public long MaxTotalBytes { get; init; } = 2L * 1024 * 1024 * 1024;
	public int MaxFiles { get; init; } = 10_000;
}

public sealed record DataStreamMonitorDefinition
{
	public required string MonitorId { get; init; }
	public string? DisplayName { get; init; }
	public required DataStreamSourceMatch Source { get; init; }
	public DataStreamContentMatch? Content { get; init; }
	public DataStreamCondition? Condition { get; init; }
	public DataStreamCustomMatchDefinition? CustomMatch { get; init; }
	public required DataStreamOutputDefinition Output { get; init; }
	public DataStreamLimits Limits { get; init; } = new();
	public IReadOnlyDictionary<string, string>? Tags { get; init; }
}

public sealed record DataStreamDeleteOptions(bool ForceStop = false, bool DeleteOutputFiles = false);
public sealed record DataStreamEventSubscription(string? MonitorId = null, int Capacity = 256);

public sealed record DataStreamRecord
{
	public string RecordId { get; init; } = Guid.NewGuid().ToString("N");
	public required string BackendId { get; init; }
	public DataStreamTransport Transport { get; init; } = DataStreamTransport.Http;
	public DataStreamDirection Direction { get; init; } = DataStreamDirection.Response;
	public string Method { get; init; } = "GET";
	public required string Url { get; init; }
	public int? StatusCode { get; init; }
	public string? ContentType { get; init; }
	public IReadOnlyDictionary<string, string>? RequestHeaders { get; init; }
	public IReadOnlyDictionary<string, string>? ResponseHeaders { get; init; }
	public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
	public ReadOnlyMemory<byte> RequestContent { get; init; }
	public required ReadOnlyMemory<byte> Content { get; init; }
}

public sealed record DataStreamClassificationResult
{
	public bool Matched { get; init; } = true;
	public string DataType { get; init; } = "raw";
	public string? Identity { get; init; }
	public string? SuggestedExtension { get; init; }
}

public sealed record DataStreamMatchContext
{
	public required string MonitorId { get; init; }
	public required DataStreamRecord Record { get; init; }
	public ReadOnlyMemory<byte> RequestBody { get; init; }
	public ReadOnlyMemory<byte> Body { get; init; }
	public string? RequestText { get; init; }
	public string? Text { get; init; }
	public JsonElement? RequestJson { get; init; }
	public JsonElement? ParsedJson { get; init; }
}

public sealed record DataStreamCustomMatchResult
{
	public bool Matched { get; init; }
	public string? Reason { get; init; }
	public string? DataType { get; init; }
	public string? Identity { get; init; }
	public string? SuggestedExtension { get; init; }
}

public delegate ValueTask<DataStreamCustomMatchResult> DataStreamMatchDelegate(DataStreamMatchContext context, CancellationToken ct);

public interface IDataStreamContentClassifier
{
	string ClassifierId { get; }
	ValueTask<DataStreamClassificationResult> ClassifyAsync(DataStreamRecord record, CancellationToken ct);
}

public sealed record DataStreamMonitorInfo
{
	public required string MonitorId { get; init; }
	public long Revision { get; init; }
	public DataStreamMonitorState State { get; init; }
	public DateTimeOffset? StartedAt { get; init; }
	public DateTimeOffset? StoppedAt { get; init; }
	public long MatchedCount { get; init; }
	public long WrittenFileCount { get; init; }
	public long WrittenBytes { get; init; }
	public long DroppedCount { get; init; }
	public string? LastOutputPath { get; init; }
	public string? LastError { get; init; }
	public required DataStreamMonitorDefinition Definition { get; init; }
}

public sealed record DataStreamRecorderEvent
{
	public string EventId { get; init; } = Guid.NewGuid().ToString("N");
	public required string MonitorId { get; init; }
	public required DataStreamRecorderEventKind Kind { get; init; }
	public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
	public string? RecordId { get; init; }
	public string? DataType { get; init; }
	public string? Identity { get; init; }
	public string? OutputPath { get; init; }
	public long? OutputBytes { get; init; }
	public string? Error { get; init; }
}

public interface IDataStreamRecorderManager : IAsyncDisposable
{
	void RegisterClassifier(IDataStreamContentClassifier classifier);
	void RegisterMatcher(string matcherId, DataStreamMatchDelegate matcher);
	bool RemoveMatcher(string matcherId);
	Task<DataStreamMonitorInfo> CreateAsync(DataStreamMonitorDefinition definition, CancellationToken ct = default);
	Task<DataStreamMonitorInfo> CreateAsync(DataStreamMonitorDefinition definition, DataStreamMatchDelegate matcher, DataStreamCustomMatchMode mode, DataStreamDelegateDataAccess dataAccess, CancellationToken ct = default);
	Task<DataStreamMonitorInfo> SetMatcherAsync(string monitorId, DataStreamMatchDelegate matcher, DataStreamCustomMatchMode mode, DataStreamDelegateDataAccess dataAccess, CancellationToken ct = default);
	Task<DataStreamMonitorInfo> StartAsync(string monitorId, CancellationToken ct = default);
	Task<DataStreamMonitorInfo> StopAsync(string monitorId, CancellationToken ct = default);
	Task<DataStreamMonitorInfo> UpdateAsync(string monitorId, DataStreamMonitorDefinition definition, CancellationToken ct = default);
	Task<bool> DeleteAsync(string monitorId, DataStreamDeleteOptions options, CancellationToken ct = default);
	DataStreamMonitorInfo? Get(string monitorId);
	IReadOnlyList<DataStreamMonitorInfo> List();
	bool HasMetadataMatch(DataStreamRecord record);
	Task ObserveAsync(DataStreamRecord record, CancellationToken ct = default);
	IAsyncEnumerable<DataStreamRecorderEvent> WatchEventsAsync(DataStreamEventSubscription subscription, CancellationToken ct = default);
}

/// <summary>Thread-safe public manager used directly by hosts and through Runtime CLI actions.</summary>
public sealed class DataStreamRecorderManager : IDataStreamRecorderManager
{
	private sealed class Monitor(DataStreamMonitorDefinition definition)
	{
		public readonly object Gate = new();
		public readonly SemaphoreSlim WriteGate = new(1, 1);
		public DataStreamMonitorDefinition Definition = definition;
		public DataStreamMonitorState State = DataStreamMonitorState.Created;
		public long Revision = 1;
		public DateTimeOffset? StartedAt;
		public DateTimeOffset? StoppedAt;
		public long MatchedCount;
		public long WrittenFileCount;
		public long WrittenBytes;
		public long DroppedCount;
		public string? LastOutputPath;
		public string? LastError;
		public DataStreamMatchDelegate? MatchDelegate;
		public int ConsecutiveMatcherErrors;
	}

	private sealed record Subscriber(string? MonitorId, Channel<DataStreamRecorderEvent> Channel);
	private readonly ConcurrentDictionary<string, Monitor> _monitors = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, IDataStreamContentClassifier> _classifiers = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<string, DataStreamMatchDelegate> _matchers = new(StringComparer.OrdinalIgnoreCase);
	private readonly ConcurrentDictionary<Guid, Subscriber> _subscribers = new();
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	private bool _disposed;

	public void RegisterClassifier(IDataStreamContentClassifier classifier)
	{
		ArgumentNullException.ThrowIfNull(classifier);
		if (!_classifiers.TryAdd(classifier.ClassifierId, classifier))
			throw new InvalidOperationException($"Classifier already exists: {classifier.ClassifierId}");
	}

	public void RegisterMatcher(string matcherId, DataStreamMatchDelegate matcher)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(matcherId);
		ArgumentNullException.ThrowIfNull(matcher);
		if (!_matchers.TryAdd(matcherId, matcher))
			throw new InvalidOperationException($"Matcher already exists: {matcherId}");
	}

	public bool RemoveMatcher(string matcherId) => _matchers.TryRemove(matcherId, out _);

	public Task<DataStreamMonitorInfo> CreateAsync(DataStreamMonitorDefinition definition, CancellationToken ct = default)
	{
		ThrowIfDisposed();
		Validate(definition);
		ct.ThrowIfCancellationRequested();
		var monitor = new Monitor(Normalize(definition));
		monitor.MatchDelegate = ResolveConfiguredMatcher(definition.CustomMatch);
		if (!_monitors.TryAdd(definition.MonitorId, monitor))
			throw new InvalidOperationException($"Monitor already exists: {definition.MonitorId}");
		Publish(new() { MonitorId = definition.MonitorId, Kind = DataStreamRecorderEventKind.MonitorCreated });
		return Task.FromResult(ToInfo(monitor));
	}

	public async Task<DataStreamMonitorInfo> CreateAsync(DataStreamMonitorDefinition definition, DataStreamMatchDelegate matcher, DataStreamCustomMatchMode mode, DataStreamDelegateDataAccess dataAccess, CancellationToken ct = default)
	{
		if (mode == DataStreamCustomMatchMode.None) throw new ArgumentException("A direct matcher requires And, Or, or Override mode.", nameof(mode));
		var configured = definition with { CustomMatch = null };
		var created = await CreateAsync(configured, ct).ConfigureAwait(false);
		return await SetMatcherAsync(created.MonitorId, matcher, mode, dataAccess, ct).ConfigureAwait(false);
	}

	public Task<DataStreamMonitorInfo> SetMatcherAsync(string monitorId, DataStreamMatchDelegate matcher, DataStreamCustomMatchMode mode, DataStreamDelegateDataAccess dataAccess, CancellationToken ct = default)
	{
		if (mode == DataStreamCustomMatchMode.None) throw new ArgumentException("A direct matcher requires And, Or, or Override mode.", nameof(mode));
		ArgumentNullException.ThrowIfNull(matcher);
		var monitor = Required(monitorId);
		ct.ThrowIfCancellationRequested();
		lock (monitor.Gate)
		{
			monitor.MatchDelegate = matcher;
			monitor.Definition = monitor.Definition with { CustomMatch = new() { Mode = mode, DataAccess = dataAccess } };
			monitor.Revision++;
			monitor.ConsecutiveMatcherErrors = 0;
		}
		Publish(new() { MonitorId = monitorId, Kind = DataStreamRecorderEventKind.MonitorUpdated });
		return Task.FromResult(ToInfo(monitor));
	}

	public Task<DataStreamMonitorInfo> StartAsync(string monitorId, CancellationToken ct = default)
	{
		var monitor = Required(monitorId);
		ct.ThrowIfCancellationRequested();
		lock (monitor.Gate)
		{
			if (monitor.State == DataStreamMonitorState.Running) return Task.FromResult(ToInfo(monitor));
			EnsureMatcherAvailable(monitor);
			Directory.CreateDirectory(monitor.Definition.Output.RootDirectory);
			monitor.State = DataStreamMonitorState.Running;
			monitor.StartedAt = DateTimeOffset.UtcNow;
			monitor.StoppedAt = null;
		}
		Publish(new() { MonitorId = monitorId, Kind = DataStreamRecorderEventKind.MonitorStarted });
		return Task.FromResult(ToInfo(monitor));
	}

	public Task<DataStreamMonitorInfo> StopAsync(string monitorId, CancellationToken ct = default)
	{
		var monitor = Required(monitorId);
		ct.ThrowIfCancellationRequested();
		lock (monitor.Gate)
		{
			monitor.State = DataStreamMonitorState.Stopped;
			monitor.StoppedAt = DateTimeOffset.UtcNow;
		}
		Publish(new() { MonitorId = monitorId, Kind = DataStreamRecorderEventKind.MonitorStopped });
		return Task.FromResult(ToInfo(monitor));
	}

	public Task<DataStreamMonitorInfo> UpdateAsync(string monitorId, DataStreamMonitorDefinition definition, CancellationToken ct = default)
	{
		var monitor = Required(monitorId);
		Validate(definition);
		if (!monitorId.Equals(definition.MonitorId, StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException("MonitorId cannot be changed.", nameof(definition));
		ct.ThrowIfCancellationRequested();
		lock (monitor.Gate)
		{
			var customMode = definition.CustomMatch?.Mode ?? DataStreamCustomMatchMode.None;
			var configuredMatcher = customMode == DataStreamCustomMatchMode.None
				? null
				: !string.IsNullOrWhiteSpace(definition.CustomMatch?.MatcherId)
					? ResolveConfiguredMatcher(definition.CustomMatch)
					: monitor.MatchDelegate;
			if (customMode != DataStreamCustomMatchMode.None && configuredMatcher is null)
				throw new InvalidOperationException("Updated monitor requires a registered or directly attached matcher.");
			monitor.Definition = Normalize(definition);
			monitor.MatchDelegate = configuredMatcher;
			monitor.Revision++;
			monitor.ConsecutiveMatcherErrors = 0;
		}
		Publish(new() { MonitorId = monitorId, Kind = DataStreamRecorderEventKind.MonitorUpdated });
		return Task.FromResult(ToInfo(monitor));
	}

	public async Task<bool> DeleteAsync(string monitorId, DataStreamDeleteOptions options, CancellationToken ct = default)
	{
		var monitor = Required(monitorId);
		if (monitor.State == DataStreamMonitorState.Running && !options.ForceStop)
			throw new InvalidOperationException("Running monitor must be stopped before deletion.");
		if (monitor.State == DataStreamMonitorState.Running)
			await StopAsync(monitorId, ct).ConfigureAwait(false);
		if (!_monitors.TryRemove(monitorId, out monitor)) return false;
		monitor.State = DataStreamMonitorState.Stopped;
		await monitor.WriteGate.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			if (options.DeleteOutputFiles && Directory.Exists(monitor.Definition.Output.RootDirectory))
				Directory.Delete(monitor.Definition.Output.RootDirectory, true);
		}
		finally
		{
			monitor.WriteGate.Release();
		}
		Publish(new() { MonitorId = monitorId, Kind = DataStreamRecorderEventKind.MonitorDeleted });
		return true;
	}

	public DataStreamMonitorInfo? Get(string monitorId) => _monitors.TryGetValue(monitorId, out var monitor) ? ToInfo(monitor) : null;
	public IReadOnlyList<DataStreamMonitorInfo> List() => _monitors.Values.Select(ToInfo).OrderBy(x => x.MonitorId, StringComparer.OrdinalIgnoreCase).ToArray();
	public bool HasMetadataMatch(DataStreamRecord record) => _monitors.Values.Any(monitor =>
	{
		if (monitor.State != DataStreamMonitorState.Running) return false;
		var mode = monitor.Definition.CustomMatch?.Mode ?? DataStreamCustomMatchMode.None;
		return mode is DataStreamCustomMatchMode.Or or DataStreamCustomMatchMode.Override || MetadataMatches(monitor.Definition.Source, record);
	});

	public async Task ObserveAsync(DataStreamRecord record, CancellationToken ct = default)
	{
		ThrowIfDisposed();
		ArgumentNullException.ThrowIfNull(record);
		foreach (var monitor in _monitors.Values.Where(x => x.State == DataStreamMonitorState.Running).ToArray())
		{
			var definition = monitor.Definition;
			var mode = definition.CustomMatch?.Mode ?? DataStreamCustomMatchMode.None;
			var sourceMatched = MetadataMatches(definition.Source, record);
			if ((mode is DataStreamCustomMatchMode.None or DataStreamCustomMatchMode.And) && !sourceMatched) continue;
			var exchangeBytes = (long)record.Content.Length + (definition.Output.WriteRequestBody ? record.RequestContent.Length : 0);
			if (record.RequestContent.Length > definition.Limits.MaxBodyBytes || record.Content.Length > definition.Limits.MaxBodyBytes || monitor.WrittenFileCount >= definition.Limits.MaxFiles || monitor.WrittenBytes + exchangeBytes > definition.Limits.MaxTotalBytes)
			{
				Interlocked.Increment(ref monitor.DroppedCount);
				Publish(new() { MonitorId = definition.MonitorId, Kind = DataStreamRecorderEventKind.RecordDropped, RecordId = record.RecordId, Error = "Capture limit exceeded." });
				continue;
			}

			var generalClassification = mode == DataStreamCustomMatchMode.Override
				? new DataStreamClassificationResult { Matched = false, SuggestedExtension = DetectExtension(record.ContentType, record.Content.Span) }
				: sourceMatched
					? await ClassifyAsync(definition.Content, definition.Condition, record, ct).ConfigureAwait(false)
					: new DataStreamClassificationResult { Matched = false };
			var classification = await CombineCustomMatchAsync(monitor, definition, record, generalClassification, ct).ConfigureAwait(false);
			if (!classification.Matched) continue;
			Interlocked.Increment(ref monitor.MatchedCount);
			Publish(new() { MonitorId = definition.MonitorId, Kind = DataStreamRecorderEventKind.RecordMatched, RecordId = record.RecordId, DataType = classification.DataType, Identity = classification.Identity });
			await WriteAsync(monitor, definition, record, classification, ct).ConfigureAwait(false);
		}
	}

	public async IAsyncEnumerable<DataStreamRecorderEvent> WatchEventsAsync(DataStreamEventSubscription subscription, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
	{
		var id = Guid.NewGuid();
		var channel = Channel.CreateBounded<DataStreamRecorderEvent>(new BoundedChannelOptions(Math.Clamp(subscription.Capacity, 16, 4096)) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
		_subscribers[id] = new(subscription.MonitorId, channel);
		try
		{
			await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) yield return item;
		}
		finally
		{
			if (_subscribers.TryRemove(id, out var removed)) removed.Channel.Writer.TryComplete();
		}
	}

	private async ValueTask<DataStreamClassificationResult> ClassifyAsync(DataStreamContentMatch? match, DataStreamCondition? condition, DataStreamRecord record, CancellationToken ct)
	{
		if (match?.RequiredText is { Count: > 0 })
		{
			var text = Encoding.UTF8.GetString(record.Content.Span);
			if (match.RequiredText.Any(required => !text.Contains(required, StringComparison.OrdinalIgnoreCase))) return new() { Matched = false };
		}
		if (match?.JsonConditions is { Count: > 0 } && !MatchesJson(record.Content.Span, match.JsonConditions, match.ParseStringifiedJson)) return new() { Matched = false };
		if (condition is not null && !EvaluateCondition(condition, record)) return new() { Matched = false };
		if (!string.IsNullOrWhiteSpace(match?.ClassifierId))
		{
			if (!_classifiers.TryGetValue(match.ClassifierId, out var classifier)) throw new InvalidOperationException($"Classifier is not registered: {match.ClassifierId}");
			return await classifier.ClassifyAsync(record, ct).ConfigureAwait(false);
		}
		return new() { SuggestedExtension = DetectExtension(record.ContentType, record.Content.Span), DataType = "raw" };
	}

	private async ValueTask<DataStreamClassificationResult> CombineCustomMatchAsync(Monitor monitor, DataStreamMonitorDefinition definition, DataStreamRecord record, DataStreamClassificationResult general, CancellationToken ct)
	{
		var custom = definition.CustomMatch;
		var mode = custom?.Mode ?? DataStreamCustomMatchMode.None;
		if (mode == DataStreamCustomMatchMode.None) return general;
		if (mode == DataStreamCustomMatchMode.And && !general.Matched) return general;
		if (mode == DataStreamCustomMatchMode.Or && general.Matched && custom?.AlwaysInvokeForExtraction != true) return general;
		var matcher = monitor.MatchDelegate ?? throw new InvalidOperationException($"Monitor matcher is not available: {definition.MonitorId}");
		try
		{
			var context = BuildMatchContext(definition.MonitorId, record, custom ?? new());
			var result = await matcher(context, ct).ConfigureAwait(false);
			monitor.ConsecutiveMatcherErrors = 0;
			var finalMatched = mode switch
			{
				DataStreamCustomMatchMode.And => general.Matched && result.Matched,
				DataStreamCustomMatchMode.Or => general.Matched || result.Matched,
				DataStreamCustomMatchMode.Override => result.Matched,
				_ => general.Matched
			};
			if (!finalMatched) return new() { Matched = false };
			return new()
			{
				Matched = true,
				DataType = result.DataType ?? general.DataType,
				Identity = result.Identity ?? general.Identity,
				SuggestedExtension = result.SuggestedExtension ?? general.SuggestedExtension
			};
		}
		catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
		{
			monitor.LastError = ex.Message;
			var errors = Interlocked.Increment(ref monitor.ConsecutiveMatcherErrors);
			Interlocked.Increment(ref monitor.DroppedCount);
			Publish(new() { MonitorId = definition.MonitorId, Kind = DataStreamRecorderEventKind.RecordFailed, RecordId = record.RecordId, Error = $"Matcher failed: {ex.Message}" });
			if (errors >= Math.Max(1, custom?.MaxConsecutiveErrors ?? 5))
			{
				monitor.State = DataStreamMonitorState.Faulted;
				Publish(new() { MonitorId = definition.MonitorId, Kind = DataStreamRecorderEventKind.MonitorFaulted, Error = ex.Message });
			}
			return new() { Matched = false };
		}
	}

	private static DataStreamMatchContext BuildMatchContext(string monitorId, DataStreamRecord record, DataStreamCustomMatchDefinition custom)
	{
		ReadOnlyMemory<byte> SelectBody(ReadOnlyMemory<byte> content) => custom.DataAccess switch
		{
			DataStreamDelegateDataAccess.MetadataOnly => ReadOnlyMemory<byte>.Empty,
			DataStreamDelegateDataAccess.BodyPreview => content[..Math.Min(content.Length, Math.Clamp(custom.PreviewBytes, 1, 1024 * 1024))],
			_ => content
		};
		var requestBody = SelectBody(record.RequestContent);
		var body = SelectBody(record.Content);
		string? requestText = null;
		string? text = null;
		JsonElement? requestJson = null;
		JsonElement? json = null;
		if (custom.DataAccess is DataStreamDelegateDataAccess.ParsedText or DataStreamDelegateDataAccess.ParsedJson)
		{
			requestText = Encoding.UTF8.GetString(record.RequestContent.Span);
			text = Encoding.UTF8.GetString(record.Content.Span);
		}
		if (custom.DataAccess == DataStreamDelegateDataAccess.ParsedJson && TryParseJson(record.RequestContent.Span, out var parsedRequest))
			requestJson = parsedRequest;
		if (custom.DataAccess == DataStreamDelegateDataAccess.ParsedJson && TryParseJson(record.Content.Span, out var parsed))
			json = parsed;
		return new() { MonitorId = monitorId, Record = record with { RequestContent = requestBody, Content = body }, RequestBody = requestBody, Body = body, RequestText = requestText, Text = text, RequestJson = requestJson, ParsedJson = json };
	}

	private static bool EvaluateCondition(DataStreamCondition condition, DataStreamRecord record)
	{
		var children = condition.Conditions ?? Array.Empty<DataStreamCondition>();
		return condition.Kind switch
		{
			DataStreamConditionKind.Always => true,
			DataStreamConditionKind.Never => false,
			DataStreamConditionKind.All => children.All(child => EvaluateCondition(child, record)),
			DataStreamConditionKind.Any => children.Any(child => EvaluateCondition(child, record)),
			DataStreamConditionKind.Not => children.Count == 1 && !EvaluateCondition(children[0], record),
			DataStreamConditionKind.AtLeast => children.Count(child => EvaluateCondition(child, record)) >= Math.Clamp(condition.MinimumMatches, 0, children.Count),
			_ => EvaluateLeaf(condition, record)
		};
	}

	private static bool EvaluateLeaf(DataStreamCondition condition, DataStreamRecord record)
	{
		var fieldExists = TryResolveField(condition.Field, record, out var actual);
		if (condition.Operator == DataStreamConditionOperator.Exists) return fieldExists;
		if (condition.Operator == DataStreamConditionOperator.NotExists) return !fieldExists;
		if (!fieldExists) return false;

		if (condition.Operator is DataStreamConditionOperator.PathExists or DataStreamConditionOperator.PathValue)
			return EvaluateJsonPath(condition, actual);
		if (condition.Operator == DataStreamConditionOperator.MagicBytes)
			return EvaluateMagicBytes(record.Content.Span, condition.Value);

		var expected = condition.Value;
		return condition.Operator switch
		{
			DataStreamConditionOperator.Equals => expected.HasValue && JsonElement.DeepEquals(actual, expected.Value),
			DataStreamConditionOperator.NotEquals => expected.HasValue && !JsonElement.DeepEquals(actual, expected.Value),
			DataStreamConditionOperator.Contains => Contains(actual, expected, false),
			DataStreamConditionOperator.NotContains => !Contains(actual, expected, false),
			DataStreamConditionOperator.StartsWith => CompareString(actual, expected, (left, right) => left.StartsWith(right, StringComparison.OrdinalIgnoreCase)),
			DataStreamConditionOperator.EndsWith => CompareString(actual, expected, (left, right) => left.EndsWith(right, StringComparison.OrdinalIgnoreCase)),
			DataStreamConditionOperator.Regex => CompareString(actual, expected, (left, right) => Regex.IsMatch(left, right, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250))),
			DataStreamConditionOperator.In => InSet(actual, expected),
			DataStreamConditionOperator.NotIn => !InSet(actual, expected),
			DataStreamConditionOperator.GreaterThan => CompareNumber(actual, expected, value => value > 0),
			DataStreamConditionOperator.GreaterThanOrEqual => CompareNumber(actual, expected, value => value >= 0),
			DataStreamConditionOperator.LessThan => CompareNumber(actual, expected, value => value < 0),
			DataStreamConditionOperator.LessThanOrEqual => CompareNumber(actual, expected, value => value <= 0),
			DataStreamConditionOperator.TypeIs => TypeIs(actual, expected),
			DataStreamConditionOperator.IsArray => actual.ValueKind == JsonValueKind.Array,
			DataStreamConditionOperator.IsObject => actual.ValueKind == JsonValueKind.Object,
			DataStreamConditionOperator.ArrayCount => actual.ValueKind == JsonValueKind.Array && expected.HasValue && expected.Value.TryGetInt32(out var count) && actual.GetArrayLength() == count,
			_ => false
		};
	}

	private static bool TryResolveField(string? field, DataStreamRecord record, out JsonElement value)
	{
		value = default;
		if (string.IsNullOrWhiteSpace(field)) return false;
		object? scalar = field.ToLowerInvariant() switch
		{
			"backendid" => record.BackendId,
			"transport" => record.Transport.ToString(),
			"direction" => record.Direction.ToString(),
			"request.url" or "response.url" => record.Url,
			"request.method" => record.Method,
			"request.bodylength" => record.RequestContent.Length,
			"request.text" => Encoding.UTF8.GetString(record.RequestContent.Span),
			"response.statuscode" => record.StatusCode,
			"response.contenttype" => record.ContentType,
			"response.bodylength" => record.Content.Length,
			"response.text" => Encoding.UTF8.GetString(record.Content.Span),
			_ => null
		};
		if (scalar is not null) { value = JsonSerializer.SerializeToElement(scalar, JsonOptions); return true; }
		if (field.Equals("request.json", StringComparison.OrdinalIgnoreCase)) return TryParseJson(record.RequestContent.Span, out value);
		if (field.Equals("request.body", StringComparison.OrdinalIgnoreCase)) { value = JsonSerializer.SerializeToElement(Convert.ToBase64String(record.RequestContent.Span), JsonOptions); return true; }
		if (field.Equals("response.json", StringComparison.OrdinalIgnoreCase)) return TryParseJson(record.Content.Span, out value);
		if (field.Equals("response.body", StringComparison.OrdinalIgnoreCase)) { value = JsonSerializer.SerializeToElement(Convert.ToBase64String(record.Content.Span), JsonOptions); return true; }
		if (field.StartsWith("request.query.", StringComparison.OrdinalIgnoreCase)) return TryResolveQuery(record.Url, field[14..], out value);
		if (field.StartsWith("request.header.", StringComparison.OrdinalIgnoreCase)) return TryResolveHeader(record.RequestHeaders, field[15..], out value);
		if (field.StartsWith("response.header.", StringComparison.OrdinalIgnoreCase)) return TryResolveHeader(record.ResponseHeaders, field[16..], out value);
		return false;
	}

	private static bool TryResolveQuery(string url, string name, out JsonElement value)
	{
		value = default;
		if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
		foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var pair = part.Split('=', 2);
			if (!Uri.UnescapeDataString(pair[0]).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
			value = JsonSerializer.SerializeToElement(pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : "", JsonOptions);
			return true;
		}
		return false;
	}

	private static bool TryResolveHeader(IReadOnlyDictionary<string, string>? headers, string name, out JsonElement value)
	{
		value = default;
		if (headers is null) return false;
		var item = headers.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
		if (item.Key is null) return false;
		value = JsonSerializer.SerializeToElement(item.Value, JsonOptions);
		return true;
	}

	private static bool EvaluateJsonPath(DataStreamCondition condition, JsonElement actual)
	{
		if (!condition.Value.HasValue) return false;
		string? path;
		JsonElement? expected = null;
		if (condition.Value.Value.ValueKind == JsonValueKind.String) path = condition.Value.Value.GetString();
		else if (condition.Value.Value.ValueKind == JsonValueKind.Object)
		{
			path = condition.Value.Value.TryGetProperty("path", out var pathValue) ? pathValue.GetString() : null;
			if (condition.Value.Value.TryGetProperty("expectedValue", out var expectedValue)) expected = expectedValue;
		}
		else return false;
		if (string.IsNullOrWhiteSpace(path) || !FindJsonValue(actual, path, true, out var found)) return false;
		return condition.Operator == DataStreamConditionOperator.PathExists || (expected.HasValue && JsonElement.DeepEquals(found, expected.Value));
	}

	private static bool EvaluateMagicBytes(ReadOnlySpan<byte> body, JsonElement? expected)
	{
		if (!expected.HasValue || expected.Value.ValueKind != JsonValueKind.String) return false;
		var text = expected.Value.GetString()?.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
		if (string.IsNullOrWhiteSpace(text) || text.Length % 2 != 0) return false;
		try
		{
			var bytes = Convert.FromHexString(text);
			return body.StartsWith(bytes);
		}
		catch (FormatException) { return false; }
	}

	private static bool Contains(JsonElement actual, JsonElement? expected, bool caseSensitive) => CompareString(actual, expected, (left, right) => left.Contains(right, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
	private static bool CompareString(JsonElement actual, JsonElement? expected, Func<string, string, bool> compare) => expected.HasValue && expected.Value.ValueKind == JsonValueKind.String && compare(JsonValueText(actual), expected.Value.GetString() ?? "");
	private static string JsonValueText(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();
	private static bool InSet(JsonElement actual, JsonElement? expected) => expected.HasValue && expected.Value.ValueKind == JsonValueKind.Array && expected.Value.EnumerateArray().Any(item => JsonElement.DeepEquals(actual, item));
	private static bool CompareNumber(JsonElement actual, JsonElement? expected, Func<double, bool> predicate) => actual.TryGetDouble(out var left) && expected.HasValue && expected.Value.TryGetDouble(out var right) && predicate(left - right);
	private static bool TypeIs(JsonElement actual, JsonElement? expected) => expected.HasValue && expected.Value.ValueKind == JsonValueKind.String && actual.ValueKind.ToString().Equals(expected.Value.GetString(), StringComparison.OrdinalIgnoreCase);

	private static bool TryParseJson(ReadOnlySpan<byte> content, out JsonElement value)
	{
		value = default;
		try
		{
			using var document = JsonDocument.Parse(content.ToArray());
			value = document.RootElement.Clone();
			return true;
		}
		catch (JsonException) { return false; }
	}

	private async Task WriteAsync(Monitor monitor, DataStreamMonitorDefinition definition, DataStreamRecord record, DataStreamClassificationResult classification, CancellationToken ct)
	{
		await monitor.WriteGate.WaitAsync(ct).ConfigureAwait(false);
		try
		{
			if (monitor.State != DataStreamMonitorState.Running) return;
			var sequence = monitor.WrittenFileCount + 1;
			var extension = classification.SuggestedExtension ?? DetectExtension(record.ContentType, record.Content.Span);
			var values = BuildTemplateValues(definition.MonitorId, record, classification, sequence, extension);
			var directory = ResolveSafePath(definition.Output.RootDirectory, ApplyTemplate(definition.Output.DirectoryTemplate, values));
			Directory.CreateDirectory(directory);
			var fileName = SanitizeFileName(ApplyTemplate(definition.Output.FileNameTemplate, values));
			var outputPath = UniquePath(directory, fileName);
			var temporaryPath = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
			await File.WriteAllBytesAsync(temporaryPath, record.Content.ToArray(), ct).ConfigureAwait(false);
			File.Move(temporaryPath, outputPath);
			string? requestBodyPath = null;
			if (definition.Output.WriteRequestBody && !record.RequestContent.IsEmpty)
			{
				requestBodyPath = UniquePath(directory, Path.GetFileNameWithoutExtension(fileName) + ".request.bin");
				var requestTemporaryPath = requestBodyPath + ".tmp-" + Guid.NewGuid().ToString("N");
				await File.WriteAllBytesAsync(requestTemporaryPath, record.RequestContent.ToArray(), ct).ConfigureAwait(false);
				File.Move(requestTemporaryPath, requestBodyPath);
			}
			if (definition.Output.WriteManifest)
			{
				var manifest = JsonSerializer.Serialize(new { definition.MonitorId, record.RecordId, record.Timestamp, record.BackendId, record.Transport, record.Direction, record.Method, record.Url, record.StatusCode, record.ContentType, classification.DataType, classification.Identity, requestBodyPath, requestBodyBytes = record.RequestContent.Length, outputPath, bodyBytes = record.Content.Length }, JsonOptions);
				await File.AppendAllTextAsync(Path.Combine(directory, "manifest.jsonl"), manifest + Environment.NewLine, Encoding.UTF8, ct).ConfigureAwait(false);
			}
			Interlocked.Add(ref monitor.WrittenFileCount, requestBodyPath is null ? 1 : 2);
			Interlocked.Add(ref monitor.WrittenBytes, record.Content.Length + (requestBodyPath is null ? 0 : record.RequestContent.Length));
			monitor.LastOutputPath = outputPath;
			Publish(new() { MonitorId = definition.MonitorId, Kind = DataStreamRecorderEventKind.RecordWritten, RecordId = record.RecordId, DataType = classification.DataType, Identity = classification.Identity, OutputPath = outputPath, OutputBytes = record.Content.Length });
		}
		catch (Exception ex)
		{
			monitor.LastError = ex.Message;
			Interlocked.Increment(ref monitor.DroppedCount);
			Publish(new() { MonitorId = definition.MonitorId, Kind = DataStreamRecorderEventKind.RecordFailed, RecordId = record.RecordId, Error = ex.Message });
		}
		finally { monitor.WriteGate.Release(); }
	}

	private static bool MetadataMatches(DataStreamSourceMatch match, DataStreamRecord record)
	{
		if (!string.IsNullOrWhiteSpace(match.BackendId) && !match.BackendId.Equals(record.BackendId, StringComparison.OrdinalIgnoreCase)) return false;
		if (match.Transports is { Count: > 0 } && !match.Transports.Contains(record.Transport)) return false;
		if (match.Directions is { Count: > 0 } && !match.Directions.Contains(record.Direction)) return false;
		if (!string.IsNullOrWhiteSpace(match.UrlEquals) && !match.UrlEquals.Equals(record.Url, StringComparison.OrdinalIgnoreCase)) return false;
		if (!string.IsNullOrWhiteSpace(match.UrlContains) && !record.Url.Contains(match.UrlContains, StringComparison.OrdinalIgnoreCase)) return false;
		if (!string.IsNullOrWhiteSpace(match.UrlRegex) && !Regex.IsMatch(record.Url, match.UrlRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250))) return false;
		if (match.Methods is { Count: > 0 } && !match.Methods.Contains(record.Method, StringComparer.OrdinalIgnoreCase)) return false;
		if (match.StatusCodes is { Count: > 0 } && (!record.StatusCode.HasValue || !match.StatusCodes.Contains(record.StatusCode.Value))) return false;
		return string.IsNullOrWhiteSpace(match.ContentTypeContains) || (record.ContentType?.Contains(match.ContentTypeContains, StringComparison.OrdinalIgnoreCase) ?? false);
	}

	private static bool MatchesJson(ReadOnlySpan<byte> content, IReadOnlyList<JsonPathCondition> conditions, bool parseStringifiedJson)
	{
		try
		{
			using var document = JsonDocument.Parse(content.ToArray());
			return conditions.All(condition => FindJsonValue(document.RootElement, condition.Path, parseStringifiedJson, out var value) && (!condition.ExpectedValue.HasValue || JsonElement.DeepEquals(value, condition.ExpectedValue.Value)));
		}
		catch (JsonException) { return false; }
	}

	private static bool FindJsonValue(JsonElement root, string path, bool parseStringifiedJson, out JsonElement value)
	{
		value = root;
		if (path.StartsWith("$..", StringComparison.Ordinal)) return FindPropertyRecursive(root, path[3..], parseStringifiedJson, 0, out value);
		foreach (var segment in path.TrimStart('$', '.').Split('.', StringSplitOptions.RemoveEmptyEntries))
		{
			if (parseStringifiedJson && value.ValueKind == JsonValueKind.String && TryParseEmbedded(value.GetString(), out var embedded)) value = embedded;
			if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value)) return false;
		}
		return true;
	}

	private static bool FindPropertyRecursive(JsonElement element, string name, bool parseStringifiedJson, int depth, out JsonElement value)
	{
		if (depth > 16) { value = default; return false; }
		if (element.ValueKind == JsonValueKind.Object)
			foreach (var property in element.EnumerateObject())
			{
				if (property.NameEquals(name)) { value = property.Value; return true; }
				if (FindPropertyRecursive(property.Value, name, parseStringifiedJson, depth + 1, out value)) return true;
			}
		else if (element.ValueKind == JsonValueKind.Array)
			foreach (var item in element.EnumerateArray()) if (FindPropertyRecursive(item, name, parseStringifiedJson, depth + 1, out value)) return true;
		else if (parseStringifiedJson && element.ValueKind == JsonValueKind.String && TryParseEmbedded(element.GetString(), out var embedded))
			return FindPropertyRecursive(embedded, name, true, depth + 1, out value);
		value = default;
		return false;
	}

	private static bool TryParseEmbedded(string? text, out JsonElement value)
	{
		value = default;
		if (string.IsNullOrWhiteSpace(text) || text.Length > 64 * 1024 * 1024) return false;
		var trimmed = text.AsSpan().TrimStart();
		if (trimmed.IsEmpty || trimmed[0] is not ('{' or '[')) return false;
		try
		{
			using var document = JsonDocument.Parse(text);
			value = document.RootElement.Clone();
			return true;
		}
		catch (JsonException) { return false; }
	}

	private static Dictionary<string, string> BuildTemplateValues(string monitorId, DataStreamRecord record, DataStreamClassificationResult classification, long sequence, string extension) => new(StringComparer.OrdinalIgnoreCase)
	{
		["monitorId"] = monitorId,
		["backendId"] = record.BackendId,
		["date"] = record.Timestamp.ToString("yyyyMMdd"),
		["timestamp"] = record.Timestamp.ToString("yyyyMMdd-HHmmssfff"),
		["sequence"] = sequence.ToString("D6"),
		["dataType"] = classification.DataType,
		["identity"] = classification.Identity ?? record.RecordId,
		["extension"] = extension.TrimStart('.'),
		["hash"] = Convert.ToHexString(SHA256.HashData(record.Content.Span))[..16].ToLowerInvariant()
	};

	private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string> values)
	{
		foreach (var pair in values) template = template.Replace("{" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
		return template;
	}

	private static string ResolveSafePath(string root, string relative)
	{
		var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		var resolved = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
		if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Output template escapes the configured root directory.");
		return resolved;
	}

	private static string SanitizeFileName(string value)
	{
		foreach (var character in Path.GetInvalidFileNameChars()) value = value.Replace(character, '_');
		return string.IsNullOrWhiteSpace(value) ? "record.bin" : value;
	}

	private static string UniquePath(string directory, string fileName)
	{
		var path = Path.Combine(directory, fileName);
		if (!File.Exists(path)) return path;
		var stem = Path.GetFileNameWithoutExtension(fileName);
		var extension = Path.GetExtension(fileName);
		for (var index = 2; ; index++)
		{
			path = Path.Combine(directory, $"{stem}-{index}{extension}");
			if (!File.Exists(path)) return path;
		}
	}

	private static string DetectExtension(string? contentType, ReadOnlySpan<byte> body)
	{
		if (body.StartsWith("%PDF-"u8)) return "pdf";
		if (body.Length >= 4 && body[..4].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47 })) return "png";
		if (body.Length >= 3 && body[..3].SequenceEqual(new byte[] { 0xFF, 0xD8, 0xFF })) return "jpg";
		if (contentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true) return "json";
		if (contentType?.Contains("markdown", StringComparison.OrdinalIgnoreCase) == true) return "md";
		if (contentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true) return "html";
		if (contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true) return "txt";
		return "bin";
	}

	private void Publish(DataStreamRecorderEvent item)
	{
		foreach (var subscriber in _subscribers.Values)
			if (string.IsNullOrWhiteSpace(subscriber.MonitorId) || subscriber.MonitorId.Equals(item.MonitorId, StringComparison.OrdinalIgnoreCase)) subscriber.Channel.Writer.TryWrite(item);
	}

	private DataStreamMatchDelegate? ResolveConfiguredMatcher(DataStreamCustomMatchDefinition? custom)
	{
		if (custom is null || custom.Mode == DataStreamCustomMatchMode.None) return null;
		if (string.IsNullOrWhiteSpace(custom.MatcherId))
			throw new InvalidOperationException("A configured custom matcher requires MatcherId or a direct C# delegate.");
		return _matchers.TryGetValue(custom.MatcherId, out var matcher)
			? matcher
			: throw new KeyNotFoundException($"Matcher is not registered: {custom.MatcherId}");
	}

	private static void EnsureMatcherAvailable(Monitor monitor)
	{
		if ((monitor.Definition.CustomMatch?.Mode ?? DataStreamCustomMatchMode.None) != DataStreamCustomMatchMode.None && monitor.MatchDelegate is null)
			throw new InvalidOperationException($"Monitor matcher is not available: {monitor.Definition.MonitorId}");
	}

	private Monitor Required(string monitorId)
	{
		ThrowIfDisposed();
		return _monitors.TryGetValue(monitorId, out var monitor) ? monitor : throw new KeyNotFoundException($"Monitor was not found: {monitorId}");
	}

	private static void Validate(DataStreamMonitorDefinition definition)
	{
		ArgumentNullException.ThrowIfNull(definition);
		if (string.IsNullOrWhiteSpace(definition.MonitorId)) throw new ArgumentException("MonitorId is required.", nameof(definition));
		if (string.IsNullOrWhiteSpace(definition.Output.RootDirectory)) throw new ArgumentException("Output root directory is required.", nameof(definition));
		if (definition.Limits.MaxBodyBytes < 1 || definition.Limits.MaxTotalBytes < definition.Limits.MaxBodyBytes || definition.Limits.MaxFiles < 1) throw new ArgumentException("Invalid monitor limits.", nameof(definition));
		if (!string.IsNullOrWhiteSpace(definition.Source.UrlRegex)) _ = new Regex(definition.Source.UrlRegex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
		if (definition.Condition is not null) ValidateCondition(definition.Condition, 0);
		if (definition.CustomMatch is { } custom)
		{
			if (custom.PreviewBytes is < 1 or > 1024 * 1024) throw new ArgumentException("Custom matcher PreviewBytes must be between 1 and 1048576.", nameof(definition));
			if (custom.MaxConsecutiveErrors < 1) throw new ArgumentException("Custom matcher MaxConsecutiveErrors must be positive.", nameof(definition));
		}
	}

	private static void ValidateCondition(DataStreamCondition condition, int depth)
	{
		if (depth > 32) throw new ArgumentException("Condition nesting exceeds 32 levels.", nameof(condition));
		var children = condition.Conditions ?? Array.Empty<DataStreamCondition>();
		switch (condition.Kind)
		{
			case DataStreamConditionKind.Leaf when string.IsNullOrWhiteSpace(condition.Field):
				throw new ArgumentException("Leaf condition requires Field.", nameof(condition));
			case DataStreamConditionKind.Leaf when condition.Operator is not (DataStreamConditionOperator.Exists or DataStreamConditionOperator.NotExists or DataStreamConditionOperator.IsArray or DataStreamConditionOperator.IsObject) && !condition.Value.HasValue:
				throw new ArgumentException($"Leaf operator {condition.Operator} requires Value.", nameof(condition));
			case DataStreamConditionKind.Not when children.Count != 1:
				throw new ArgumentException("Not condition requires exactly one child.", nameof(condition));
			case DataStreamConditionKind.AtLeast when condition.MinimumMatches < 0 || condition.MinimumMatches > children.Count:
				throw new ArgumentException("AtLeast MinimumMatches is outside the child range.", nameof(condition));
		}
		if (condition.Kind == DataStreamConditionKind.Leaf && condition.Operator == DataStreamConditionOperator.Regex && condition.Value is { ValueKind: JsonValueKind.String })
			_ = new Regex(condition.Value.Value.GetString() ?? "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
		foreach (var child in children) ValidateCondition(child, depth + 1);
	}

	private static DataStreamMonitorDefinition Normalize(DataStreamMonitorDefinition definition) => definition with { Output = definition.Output with { RootDirectory = Path.GetFullPath(definition.Output.RootDirectory) } };

	private static DataStreamMonitorInfo ToInfo(Monitor monitor)
	{
		lock (monitor.Gate)
			return new() { MonitorId = monitor.Definition.MonitorId, Revision = monitor.Revision, State = monitor.State, StartedAt = monitor.StartedAt, StoppedAt = monitor.StoppedAt, MatchedCount = monitor.MatchedCount, WrittenFileCount = monitor.WrittenFileCount, WrittenBytes = monitor.WrittenBytes, DroppedCount = monitor.DroppedCount, LastOutputPath = monitor.LastOutputPath, LastError = monitor.LastError, Definition = monitor.Definition };
	}

	private void ThrowIfDisposed() { ObjectDisposedException.ThrowIf(_disposed, this); }

	public async ValueTask DisposeAsync()
	{
		if (_disposed) return;
		_disposed = true;
		foreach (var monitor in _monitors.Values)
		{
			monitor.State = DataStreamMonitorState.Stopped;
			await monitor.WriteGate.WaitAsync().ConfigureAwait(false);
			monitor.WriteGate.Release();
			monitor.WriteGate.Dispose();
		}
		_monitors.Clear();
		foreach (var subscriber in _subscribers.Values) subscriber.Channel.Writer.TryComplete();
		_subscribers.Clear();
	}
}
