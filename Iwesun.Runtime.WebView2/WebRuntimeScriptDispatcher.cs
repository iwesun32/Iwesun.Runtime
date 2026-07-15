using System.Text.Json;

namespace Iwesun.Runtime.WebView2;

/// <summary>Result returned by the controlled page-script dispatcher.</summary>
public sealed record WebRuntimeScriptDispatchResult(
	bool Handled,
	bool Success,
	object? Value = null,
	string? ErrorCode = null,
	string? Error = null)
{
	public static WebRuntimeScriptDispatchResult Completed(object value) => new(true, true, value);
	public static WebRuntimeScriptDispatchResult Failed(string code, string error) => new(true, false, null, code, error);
}

public sealed record WebRuntimeScriptAuditRecord(
	DateTimeOffset TimestampUtc,
	string Action,
	int ScriptLength,
	bool Success,
	string? ErrorCode,
	string? Error);

/// <summary>Bounded in-memory audit sink for explicitly requested script evaluations.</summary>
public sealed class WebRuntimeScriptAuditLog
{
	private const int DefaultLimit = 256;
	private readonly object _gate = new();
	private readonly Queue<WebRuntimeScriptAuditRecord> _records = new();
	private readonly int _limit;

	public WebRuntimeScriptAuditLog(int limit = DefaultLimit)
	{
		if (limit is < 1 or > 4096)
			throw new ArgumentOutOfRangeException(nameof(limit));
		_limit = limit;
	}

	public IReadOnlyList<WebRuntimeScriptAuditRecord> Snapshot()
	{
		lock (_gate)
			return _records.ToArray();
	}

	public void Clear()
	{
		lock (_gate)
			_records.Clear();
	}

	internal void Add(WebRuntimeScriptAuditRecord record)
	{
		lock (_gate)
		{
			_records.Enqueue(record);
			while (_records.Count > _limit)
				_records.Dequeue();
		}
	}
}

/// <summary>
/// Dispatches the explicitly requested script-evaluation action through the existing
/// WebView2 session. This is not a general command router: callers must opt in with
/// script.evaluate/eval and the script is bounded before it reaches WebView2.
/// </summary>
public static class WebRuntimeScriptDispatcher
{
	public const int MaxScriptLength = 256 * 1024;

	public static async Task<WebRuntimeScriptDispatchResult> TryExecuteAsync(
		IWebRuntimeScriptSession session,
		WebRuntimeControlRequest request,
		CancellationToken ct,
		WebRuntimeScriptAuditLog? auditLog = null)
	{
		ArgumentNullException.ThrowIfNull(session);
		ArgumentNullException.ThrowIfNull(request);

		if (!IsEvaluateAction(request.Action))
			return new WebRuntimeScriptDispatchResult(false, false);

		var script = request.Script;
		if (string.IsNullOrWhiteSpace(script) && request.Args is not null
			&& request.Args.TryGetValue("script", out var scriptElement)
			&& scriptElement.ValueKind == JsonValueKind.String)
			script = scriptElement.GetString();
		script ??= request.Text;

		if (string.IsNullOrWhiteSpace(script))
		{
			var result = WebRuntimeScriptDispatchResult.Failed("MISSING_SCRIPT", "script.evaluate requires a script argument.");
			auditLog?.Add(new(DateTimeOffset.UtcNow, request.Action, 0, false, result.ErrorCode, result.Error));
			return result;
		}
		if (script.Length > MaxScriptLength)
		{
			var result = WebRuntimeScriptDispatchResult.Failed("SCRIPT_TOO_LARGE", $"Script exceeds {MaxScriptLength} characters.");
			auditLog?.Add(new(DateTimeOffset.UtcNow, request.Action, script.Length, false, result.ErrorCode, result.Error));
			return result;
		}

		try
		{
			var result = await session.EvaluateStringAsync(script, ct).ConfigureAwait(false);
			var completed = WebRuntimeScriptDispatchResult.Completed(new
			{
				executed = true,
				length = script.Length,
				result
			});
			auditLog?.Add(new(DateTimeOffset.UtcNow, request.Action, script.Length, true, null, null));
			return completed;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			var failed = WebRuntimeScriptDispatchResult.Failed("SCRIPT_EVALUATION_FAILED", ex.Message);
			auditLog?.Add(new(DateTimeOffset.UtcNow, request.Action, script.Length, false, failed.ErrorCode, failed.Error));
			return failed;
		}
	}

	private static bool IsEvaluateAction(string? action) =>
		action is not null && (action.Equals(WebRuntimeScriptActions.Evaluate, StringComparison.OrdinalIgnoreCase)
			|| action.Equals(WebRuntimeScriptActions.EvalAlias, StringComparison.OrdinalIgnoreCase)
			|| action.Replace(".", "", StringComparison.Ordinal).Equals("scriptevaluate", StringComparison.OrdinalIgnoreCase));
}
