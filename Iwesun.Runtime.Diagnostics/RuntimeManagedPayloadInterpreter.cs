using System.Text.Json;

namespace Iwesun.Runtime.Diagnostics;

internal static class RuntimeManagedPayloadInterpreter
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public static string ToJson(object payload) => JsonSerializer.Serialize(payload, JsonOptions);

	public static bool TryParseObject(string? payload, out JsonElement element)
	{
		element = default;
		if (string.IsNullOrWhiteSpace(payload))
		{
			return false;
		}

		try
		{
			element = JsonSerializer.Deserialize<JsonElement>(payload, JsonOptions);
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
