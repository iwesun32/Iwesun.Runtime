using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.WebView2;

public sealed class WebRuntimeFrameOptions
{
	public string? RequestId { get; init; }
	public string? CorrelationId { get; init; }
	public string Source { get; init; } = "web.runtime.client";
	public string? Destination { get; init; }
}

public sealed record WebRuntimeProtocolValidation(bool IsValid, string Code = "OK", string Message = "");

public sealed class WebRuntimeCommandException : Exception
{
	public WebRuntimeCommandException(string code, string message, bool retryable, string? requestId)
		: base(message)
	{
		Code = code;
		Retryable = retryable;
		RequestId = requestId;
	}

	public string Code { get; }
	public bool Retryable { get; }
	public string? RequestId { get; }
}

/// <summary>Canonical WebRuntime mapping onto the shared RuntimeDiagnosticFrame protocol.</summary>
public static class WebRuntimeProtocol
{
	public const string Domain = "web.runtime";
	public const string DefaultProgramId = "aigateway.webview2";
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
	};

	public static RuntimeDiagnosticFrame CreateRequestFrame(
		WebRuntimeControlRequest request,
		WebRuntimeFrameOptions? options = null)
	{
		ArgumentNullException.ThrowIfNull(request);
		options ??= new WebRuntimeFrameOptions();
		var args = BuildArgs(request);
		return new RuntimeDiagnosticFrame
		{
			Header = new RuntimeDiagnosticFrameHeader
			{
				Schema = RuntimeDiagnosticProtocol.V2Schema,
				FrameType = "request",
				Category = "instruction",
				Operation = "invoke",
				RequestId = string.IsNullOrWhiteSpace(options.RequestId) ? Guid.NewGuid().ToString("N") : options.RequestId,
				CorrelationId = options.CorrelationId,
				Source = options.Source,
				Destination = options.Destination
			},
			Command = new RuntimeDiagnosticFrameCommand
			{
				Domain = Domain,
				Target = request.BackendId,
				Action = request.Action,
				Args = args.Count == 0 ? null : args
			}
		};
	}

	public static WebRuntimeProtocolValidation ValidateRequestFrame(RuntimeDiagnosticFrame frame)
	{
		ArgumentNullException.ThrowIfNull(frame);
		if (!frame.Header.Schema.Equals(RuntimeDiagnosticProtocol.V2Schema, StringComparison.OrdinalIgnoreCase))
			return new(false, "UNSUPPORTED_SCHEMA", $"Expected {RuntimeDiagnosticProtocol.V2Schema}.");
		if (!frame.Header.FrameType.Equals("request", StringComparison.OrdinalIgnoreCase))
			return new(false, "INVALID_FRAME_TYPE", "WebRuntime requires a request frame.");
		if (frame.Command is null)
			return new(false, "MISSING_COMMAND", "A WebRuntime command is required.");
		if (!frame.Command.Domain.Equals(Domain, StringComparison.OrdinalIgnoreCase))
			return new(false, "INVALID_DOMAIN", $"Expected domain '{Domain}'.");
		if (string.IsNullOrWhiteSpace(frame.Command.Target))
			return new(false, "MISSING_TARGET", "A backend target is required.");
		if (string.IsNullOrWhiteSpace(frame.Command.Action))
			return new(false, "MISSING_ACTION", "A command action is required.");
		return new(true);
	}

	public static WebRuntimeProtocolValidation ValidateResponseFrame(
		RuntimeDiagnosticFrame request,
		RuntimeDiagnosticFrame response)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(response);
		if (!response.Header.Schema.Equals(RuntimeDiagnosticProtocol.V2Schema, StringComparison.OrdinalIgnoreCase))
			return new(false, "UNSUPPORTED_SCHEMA", $"Expected {RuntimeDiagnosticProtocol.V2Schema}.");
		if (!response.Header.FrameType.Equals("response", StringComparison.OrdinalIgnoreCase))
			return new(false, "INVALID_FRAME_TYPE", "WebRuntime command requires a response frame.");
		var expectedCorrelationId = request.Header.RequestId ?? request.Header.CorrelationId;
		if (string.IsNullOrWhiteSpace(expectedCorrelationId)
			|| !string.Equals(response.Header.CorrelationId, expectedCorrelationId, StringComparison.Ordinal))
			return new(false, "CORRELATION_MISMATCH", "WebRuntime response does not match the request correlation id.");
		return new(true);
	}

	public static WebRuntimeControlRequest ParseRequestFrame(RuntimeDiagnosticFrame frame)
	{
		var validation = ValidateRequestFrame(frame);
		if (!validation.IsValid)
			throw new WebRuntimeCommandException(validation.Code, validation.Message, false, frame.Header.RequestId);

		var command = frame.Command!;
		var json = new JsonObject();
		if (command.Args is not null)
		{
			foreach (var item in command.Args)
				json[item.Key] = JsonNode.Parse(item.Value.GetRawText());
		}
		json["backendId"] = command.Target;
		json["action"] = command.Action;
		json["programId"] ??= DefaultProgramId;
		return json.Deserialize<WebRuntimeControlRequest>(JsonOptions)
			?? throw new WebRuntimeCommandException("INVALID_COMMAND", "Unable to deserialize WebRuntime command.", false, frame.Header.RequestId);
	}

	public static Dictionary<string, JsonElement> BuildArgs(WebRuntimeControlRequest request)
	{
		var root = JsonSerializer.SerializeToElement(request, JsonOptions);
		var args = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
		foreach (var property in root.EnumerateObject())
		{
			if (property.NameEquals("backendId") || property.NameEquals("action"))
				continue;
			if (property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
				continue;
			args[property.Name] = property.Value.Clone();
		}
		return args;
	}
}
