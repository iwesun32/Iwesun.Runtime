using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

internal static class RuntimeManagedPayloadInterpreter
{
	public static string ToJson(object payload) => RuntimeDiagnosticJson.Serialize(payload);

	public static bool TryParseObject(string? payload, out JsonElement element)
	{
		element = default;
		if (string.IsNullOrWhiteSpace(payload))
		{
			return false;
		}

		try
		{
			element = RuntimeDiagnosticJson.Deserialize<JsonElement>(payload);
			return element.ValueKind == JsonValueKind.Object;
		}
		catch (JsonException)
		{
			return false;
		}
	}

	public static string? GetString(string? payload, string key)
	{
		if (!TryParseObject(payload, out var element))
		{
			return null;
		}

		return element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	public static DateTimeOffset? GetDateTimeOffset(string? payload, string key)
	{
		var raw = GetString(payload, key);
		if (string.IsNullOrWhiteSpace(raw))
		{
			return null;
		}

		return DateTimeOffset.TryParse(raw, out var parsed)
			? parsed
			: null;
	}
}
