using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Iwesun.Runtime.Diagnostics;

/// <summary>
/// Base class for diagnostics models that participate in the shared JSON contract.
/// </summary>
public abstract class RuntimeDiagnosticJsonModel
{
	public JsonElement ToJsonElement() => RuntimeDiagnosticJson.SerializePayload(this);

	public string ToJson(bool writeIndented = false) =>
		RuntimeDiagnosticJson.Serialize(ToJsonElement(), writeIndented);
}

/// <summary>
/// Central JSON contract for diagnostics models, frames, events, and arbitrary payloads.
/// </summary>
public static class RuntimeDiagnosticJson
{
	private static readonly JsonSerializerOptions CompactOptions = CreateOptions();
	private static readonly JsonSerializerOptions IndentedOptions = CreateOptions(writeIndented: true);

	public static JsonSerializerOptions Options => CompactOptions;

	public static JsonSerializerOptions CreateOptions(
		int maxDepth = 64,
		bool writeIndented = false,
		JsonSerializerOptions? source = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDepth);
		var options = source == null
			? new JsonSerializerOptions(JsonSerializerDefaults.Web)
			: new JsonSerializerOptions(source);
		options.WriteIndented = writeIndented;
		options.NumberHandling |= JsonNumberHandling.AllowNamedFloatingPointLiterals;
		options.ReferenceHandler ??= ReferenceHandler.IgnoreCycles;
		options.MaxDepth = maxDepth;
		options.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
		options.MakeReadOnly();
		return options;
	}

	public static JsonElement SerializeToElement(object value)
	{
		ArgumentNullException.ThrowIfNull(value);
		return JsonSerializer.SerializeToElement(value, value.GetType(), CompactOptions);
	}

	public static JsonElement SerializePayload(object value)
	{
		ArgumentNullException.ThrowIfNull(value);
		try
		{
			return SerializeToElement(value);
		}
		catch (Exception ex) when (IsPayloadSerializationFailure(ex))
		{
			return JsonSerializer.SerializeToElement(
				RuntimeDiagnosticSerializationFailure.From(value, ex),
				CompactOptions);
		}
	}

	public static string Serialize(object value, bool writeIndented = false)
	{
		ArgumentNullException.ThrowIfNull(value);
		return JsonSerializer.Serialize(
			value,
			value.GetType(),
			writeIndented ? IndentedOptions : CompactOptions);
	}

	public static byte[] SerializeToUtf8Bytes<T>(T value)
	{
		ArgumentNullException.ThrowIfNull(value);
		return JsonSerializer.SerializeToUtf8Bytes(value, CompactOptions);
	}

	public static T? Deserialize<T>(string json)
	{
		ArgumentNullException.ThrowIfNull(json);
		return JsonSerializer.Deserialize<T>(json, CompactOptions);
	}

	public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json) =>
		JsonSerializer.Deserialize<T>(utf8Json, CompactOptions);

	private static bool IsPayloadSerializationFailure(Exception exception) =>
		exception is not OutOfMemoryException;
}

public sealed class RuntimeDiagnosticSerializationFailure : RuntimeDiagnosticJsonModel
{
	public string Kind { get; init; } = "serializationFailure";
	public string SourceType { get; init; } = "";
	public string ErrorType { get; init; } = "";
	public string Message { get; init; } = "";

	internal static RuntimeDiagnosticSerializationFailure From(object value, Exception exception) =>
		new()
		{
			SourceType = value.GetType().FullName ?? value.GetType().Name,
			ErrorType = exception.GetType().FullName ?? exception.GetType().Name,
			Message = exception.Message
		};
}
