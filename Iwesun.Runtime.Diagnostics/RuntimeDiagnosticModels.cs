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
	public string? ErrorCode { get; init; }

	public static RuntimeDiagnosticActionResult Ok(string targetId, string action, object? value = null) =>
		new() { Success = true, TargetId = targetId, Action = action, Value = value };

	public static RuntimeDiagnosticActionResult Fail(string targetId, string action, string error, string? errorCode = null) =>
		new() { Success = false, TargetId = targetId, Action = action, Error = error, ErrorCode = errorCode };
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
	public const string V3Schema = "rtdiag/3.0";
}

public sealed class RuntimeDiagnosticFrame
{
	public RuntimeDiagnosticFrameHeader Header { get; init; } = new();
	public RuntimeDiagnosticFrameCommand? Command { get; init; }
	public RuntimeDiagnosticBatchRequest? Batch { get; init; }
	public RuntimeDiagnosticBatchResult? BatchResult { get; init; }
	public RuntimeDiagnosticFrameStatus? Status { get; init; }
	public Dictionary<string, JsonElement>? ExtStatus { get; init; }
	public JsonElement? Data { get; init; }
	public RuntimeDiagnosticFrameMeta? Meta { get; init; }
}

public sealed class RuntimeDiagnosticBatchRequest
{
	public List<RuntimeDiagnosticBatchStep> Steps { get; init; } = [];
	public RuntimeDiagnosticBatchOptions Options { get; init; } = new();
}

public sealed class RuntimeDiagnosticBatchOptions
{
	public bool StopOnError { get; init; } = true;
	public int DeadlineMs { get; init; } = 30_000;
}

public sealed class RuntimeDiagnosticBatchStep
{
	public string Id { get; init; } = "";
	public RuntimeDiagnosticFrameCommand Command { get; init; } = new();
	public RuntimeDiagnosticBatchCondition? When { get; init; }
	public Dictionary<string, RuntimeDiagnosticBatchBinding>? Bindings { get; init; }
	public bool ContinueOnError { get; init; }
	public int DelayMs { get; init; }
}

public sealed class RuntimeDiagnosticBatchCondition
{
	public string StepId { get; init; } = "";
	public bool RequireOk { get; init; } = true;
	public string? Path { get; init; }
	public bool? Exists { get; init; }
	public JsonElement? Expected { get; init; }
}

public sealed class RuntimeDiagnosticBatchBinding
{
	public string StepId { get; init; } = "";
	public string Path { get; init; } = "";
	public bool Required { get; init; } = true;
}

public sealed class RuntimeDiagnosticBatchResult
{
	public List<RuntimeDiagnosticBatchStepResult> Steps { get; init; } = [];
	public bool StoppedOnError { get; init; }
	public long DurationMs { get; init; }
	public int TotalSteps { get; init; }
	public int SuccessfulSteps { get; init; }
	public int FailedSteps { get; init; }
	public int SkippedSteps { get; init; }
	public string? FirstFailureCode { get; init; }
}

public sealed class RuntimeDiagnosticBatchStepResult
{
	public string Id { get; init; } = "";
	public bool Ok { get; init; }
	public bool Skipped { get; init; }
	public string Code { get; init; } = "OK";
	public string? Error { get; init; }
	public JsonElement? Data { get; init; }
	public long DurationMs { get; init; }
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
