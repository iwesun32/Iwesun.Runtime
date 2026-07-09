using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeDiagnosticAction
{
	public string TargetId { get; init; } = "";
	public string Action { get; init; } = "";
	public string? Member { get; init; }
	public JsonElement? Value { get; init; }
	public Dictionary<string, JsonElement>? Args { get; init; }
}

public sealed class RuntimeDiagnosticActionResult
{
	public bool Success { get; init; }
	public string TargetId { get; init; } = "";
	public string Action { get; init; } = "";
	public object? Value { get; init; }
	public string? Error { get; init; }

	public static RuntimeDiagnosticActionResult Ok(string targetId, string action, object? value = null) =>
		new() { Success = true, TargetId = targetId, Action = action, Value = value };

	public static RuntimeDiagnosticActionResult Fail(string targetId, string action, string error) =>
		new() { Success = false, TargetId = targetId, Action = action, Error = error };
}

public sealed class RuntimeDiagnosticEvent
{
	public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
	public string TargetId { get; init; } = "";
	public string Kind { get; init; } = "";
	public string Message { get; init; } = "";
	public object? Payload { get; init; }
}

public static class RuntimeDiagnosticProtocol
{
	public const string V2Schema = "rtdiag/2.0";
}

public sealed class RuntimeDiagnosticFrame
{
	public RuntimeDiagnosticFrameHeader Header { get; init; } = new();
	public RuntimeDiagnosticFrameCommand? Command { get; init; }
	public RuntimeDiagnosticFrameStatus? Status { get; init; }
	public Dictionary<string, JsonElement>? ExtStatus { get; init; }
	public JsonElement? Data { get; init; }
	public RuntimeDiagnosticFrameMeta? Meta { get; init; }
}

public sealed class RuntimeDiagnosticFrameHeader
{
	public string Schema { get; init; } = RuntimeDiagnosticProtocol.V2Schema;
	public string FrameType { get; init; } = "request";
	public string Category { get; init; } = "query";
	public string Operation { get; init; } = "get";
	public string? RequestId { get; init; }
	public string? CorrelationId { get; init; }
	public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
	public string? Source { get; init; }
	public string? Destination { get; init; }
}

public sealed class RuntimeDiagnosticFrameCommand
{
	public string Domain { get; init; } = "";
	public string Target { get; init; } = "";
	public string? Member { get; init; }
	public string? Action { get; init; }
	public Dictionary<string, JsonElement>? Args { get; init; }
}

public sealed class RuntimeDiagnosticFrameStatus
{
	public bool Ok { get; init; }
	public string Code { get; init; } = "OK";
	public string Message { get; init; } = "";
	public bool Retryable { get; init; }
}

public sealed class RuntimeDiagnosticFrameMeta
{
	public long DurationMs { get; init; }
	public Dictionary<string, JsonElement>? Page { get; init; }
	public string? TraceId { get; init; }
}
