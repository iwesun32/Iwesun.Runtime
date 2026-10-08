using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace Iwesun.Runtime.WebView2;

/// <summary>
/// Immutable, renderer-revision evidence acquired with CDP. The snapshot is
/// columnar at the protocol boundary and indexed by backendNodeId exactly once.
/// Property Fill subsequently performs only in-process lookups.
/// </summary>
public sealed class WebRuntimeCdpEvidenceSnapshot
{
	private readonly FrozenDictionary<int, WebRuntimeCdpNodeEvidence> _nodes;

	internal WebRuntimeCdpEvidenceSnapshot(
		Uri pageUri,
		DateTimeOffset capturedAt,
		IEnumerable<WebRuntimeCdpNodeEvidence> nodes)
	{
		ArgumentNullException.ThrowIfNull(pageUri);
		ArgumentNullException.ThrowIfNull(nodes);
		var materialized = nodes.ToArray();
		PageUri = pageUri;
		CapturedAt = capturedAt;
		_nodes = materialized
			.Where(static node => node.BackendNodeId > 0)
			.ToFrozenDictionary(static node => node.BackendNodeId);
	}

	public Uri PageUri { get; }

	public DateTimeOffset CapturedAt { get; }

	public int NodeCount => _nodes.Count;

	public IReadOnlyList<double> ReadLayoutBounds(int backendNodeId)
	{
		if (!_nodes.TryGetValue(backendNodeId, out var evidence))
			return [];
		return evidence.Bounds.ToArray();
	}

	internal bool TryGetNode(
		int backendNodeId,
		out WebRuntimeCdpNodeEvidence evidence) =>
		_nodes.TryGetValue(backendNodeId, out evidence!);

	internal static WebRuntimeCdpEvidenceSnapshot Parse(
		Uri pageUri,
		string responseJson,
		IReadOnlyList<string> computedStyleNames,
		double geometryScale)
	{
		using var response = JsonDocument.Parse(
			responseJson,
			new JsonDocumentOptions { MaxDepth = 4096 });
		var root = response.RootElement;
		var strings = root.GetProperty("strings")
			.EnumerateArray()
			.Select(static item => item.GetString() ?? string.Empty)
			.ToArray();
		var nodes = new List<WebRuntimeCdpNodeEvidence>();
		foreach (var document in root.GetProperty("documents").EnumerateArray())
		{
			ParseDocument(
				document,
				strings,
				computedStyleNames,
				geometryScale,
				nodes);
		}
		return new(pageUri, DateTimeOffset.UtcNow, nodes);
	}

	private static void ParseDocument(
		JsonElement document,
		IReadOnlyList<string> strings,
		IReadOnlyList<string> computedStyleNames,
		double geometryScale,
		List<WebRuntimeCdpNodeEvidence> output)
	{
		var nodeTable = document.GetProperty("nodes");
		var backendIds = ReadInt32Array(nodeTable, "backendNodeId");
		var names = ReadStringTableArray(nodeTable, "nodeName", strings);
		var values = ReadStringTableArray(nodeTable, "nodeValue", strings);
		var attributes = ReadAttributes(nodeTable, strings, backendIds.Length);
		var textValues = ReadRareStrings(nodeTable, "textValue", strings);
		var inputValues = ReadRareStrings(nodeTable, "inputValue", strings);
		var inputChecked = ReadRareBooleans(nodeTable, "inputChecked");
		var optionSelected = ReadRareBooleans(nodeTable, "optionSelected");
		var currentSourceUrls =
			ReadRareStrings(nodeTable, "currentSourceURL", strings);
		var layoutByNode = ReadLayout(
			document,
			strings,
			computedStyleNames);
		for (var index = 0; index < backendIds.Length; index++)
		{
			layoutByNode.TryGetValue(index, out var layout);
			output.Add(new(
				backendIds[index],
				index < names.Length ? names[index] : string.Empty,
				index < values.Length ? values[index] : string.Empty,
				attributes[index],
				textValues.GetValueOrDefault(index) ?? string.Empty,
				inputValues.GetValueOrDefault(index) ?? string.Empty,
				inputChecked.Contains(index),
				optionSelected.Contains(index),
				currentSourceUrls.GetValueOrDefault(index) ?? string.Empty,
				layout?.ComputedStyles
					?? FrozenDictionary<string, string>.Empty,
				ScaleRectangle(layout?.Bounds ?? [], geometryScale),
				ScaleRectangle(layout?.ClientRect ?? [], geometryScale),
				ScaleRectangle(layout?.ScrollRect ?? [], geometryScale)));
		}
	}

	private static IReadOnlyList<double> ScaleRectangle(
		IReadOnlyList<double> rectangle,
		double scale)
	{
		if (rectangle.Count < 4 || Math.Abs(scale - 1) <= 0.000001)
			return rectangle;
		var scaled = rectangle.ToArray();
		scaled[0] *= scale;
		scaled[1] *= scale;
		scaled[2] *= scale;
		scaled[3] *= scale;
		return scaled;
	}

	private static Dictionary<int, WebRuntimeCdpLayoutEvidence> ReadLayout(
		JsonElement document,
		IReadOnlyList<string> strings,
		IReadOnlyList<string> computedStyleNames)
	{
		var result = new Dictionary<int, WebRuntimeCdpLayoutEvidence>();
		if (!document.TryGetProperty("layout", out var layout))
			return result;
		var nodeIndexes = ReadInt32Array(layout, "nodeIndex");
		var bounds = layout.TryGetProperty("bounds", out var boundsElement)
			? boundsElement.EnumerateArray()
				.Select(static row => row.EnumerateArray()
					.Select(static value => value.GetDouble())
					.ToArray())
				.ToArray()
			: [];
		var clientRects = layout.TryGetProperty("clientRects", out var clientRectsElement)
			? clientRectsElement.EnumerateArray()
				.Select(static row => row.EnumerateArray()
					.Select(static value => value.GetDouble())
					.ToArray())
				.ToArray()
			: [];
		var scrollRects = layout.TryGetProperty("scrollRects", out var scrollRectsElement)
			? scrollRectsElement.EnumerateArray()
				.Select(static row => row.EnumerateArray()
					.Select(static value => value.GetDouble())
					.ToArray())
				.ToArray()
			: [];
		var styles = layout.TryGetProperty("styles", out var stylesElement)
			? stylesElement.EnumerateArray().ToArray()
			: [];
		for (var index = 0; index < nodeIndexes.Length; index++)
		{
			var values = new Dictionary<string, string>(
				StringComparer.OrdinalIgnoreCase);
			if (index < styles.Length)
			{
				var styleIndexes = styles[index]
					.EnumerateArray()
					.Select(static item => item.GetInt32())
					.ToArray();
				for (var styleIndex = 0;
					styleIndex < computedStyleNames.Count
						&& styleIndex < styleIndexes.Length;
					styleIndex++)
				{
					values[computedStyleNames[styleIndex]] =
						ReadString(strings, styleIndexes[styleIndex]);
				}
			}
			result[nodeIndexes[index]] = new(
				values.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase),
				index < bounds.Length
					? bounds[index]
					: [],
				index < clientRects.Length
					? clientRects[index]
					: [],
				index < scrollRects.Length
					? scrollRects[index]
					: []);
		}
		return result;
	}

	private static IReadOnlyDictionary<string, string>[] ReadAttributes(
		JsonElement nodeTable,
		IReadOnlyList<string> strings,
		int nodeCount)
	{
		var result = Enumerable.Range(0, nodeCount)
			.Select(static _ => (IReadOnlyDictionary<string, string>)
				FrozenDictionary<string, string>.Empty)
			.ToArray();
		if (!nodeTable.TryGetProperty("attributes", out var attributes))
			return result;
		var rows = attributes.EnumerateArray().ToArray();
		for (var index = 0; index < rows.Length && index < result.Length; index++)
		{
			var indexes = rows[index].EnumerateArray()
				.Select(static item => item.GetInt32())
				.ToArray();
			var values = new Dictionary<string, string>(
				StringComparer.OrdinalIgnoreCase);
			for (var pair = 0; pair + 1 < indexes.Length; pair += 2)
			{
				values[ReadString(strings, indexes[pair])] =
					ReadString(strings, indexes[pair + 1]);
			}
			result[index] =
				values.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
		}
		return result;
	}

	private static Dictionary<int, string> ReadRareStrings(
		JsonElement owner,
		string propertyName,
		IReadOnlyList<string> strings)
	{
		var result = new Dictionary<int, string>();
		if (!owner.TryGetProperty(propertyName, out var rare))
			return result;
		var indexes = ReadInt32Array(rare, "index");
		var values = ReadInt32Array(rare, "value");
		for (var index = 0; index < indexes.Length && index < values.Length; index++)
			result[indexes[index]] = ReadString(strings, values[index]);
		return result;
	}

	private static HashSet<int> ReadRareBooleans(
		JsonElement owner,
		string propertyName)
	{
		if (!owner.TryGetProperty(propertyName, out var rare))
			return [];
		return ReadInt32Array(rare, "index").ToHashSet();
	}

	private static int[] ReadInt32Array(
		JsonElement owner,
		string propertyName) =>
		owner.TryGetProperty(propertyName, out var values)
			? values.EnumerateArray().Select(static item => item.GetInt32()).ToArray()
			: [];

	private static string[] ReadStringTableArray(
		JsonElement owner,
		string propertyName,
		IReadOnlyList<string> strings) =>
		ReadInt32Array(owner, propertyName)
			.Select(index => ReadString(strings, index))
			.ToArray();

	private static string ReadString(
		IReadOnlyList<string> strings,
		int index) =>
		index >= 0 && index < strings.Count ? strings[index] : string.Empty;
}

internal sealed record WebRuntimeCdpNodeEvidence(
	int BackendNodeId,
	string NodeName,
	string NodeValue,
	IReadOnlyDictionary<string, string> Attributes,
	string TextValue,
	string InputValue,
	bool InputChecked,
	bool OptionSelected,
	string CurrentSourceUrl,
	IReadOnlyDictionary<string, string> ComputedStyles,
	IReadOnlyList<double> Bounds,
	IReadOnlyList<double> ClientRect,
	IReadOnlyList<double> ScrollRect)
{
	public string ReadRectangleMember(string member) =>
		member.ToLowerInvariant() switch
		{
			"x" or "left" => ReadBound(0),
			"y" or "top" => ReadBound(1),
			"width" => ReadBound(2),
			"height" => ReadBound(3),
			"right" => SumBounds(0, 2),
			"bottom" => SumBounds(1, 3),
			_ => string.Empty
		};

	private string ReadBound(int index) =>
		index < Bounds.Count
			? Bounds[index].ToString("R", CultureInfo.InvariantCulture)
			: string.Empty;

	private string SumBounds(int first, int second) =>
		first < Bounds.Count && second < Bounds.Count
			? (Bounds[first] + Bounds[second]).ToString(
				"R",
				CultureInfo.InvariantCulture)
			: string.Empty;
}

internal sealed record WebRuntimeCdpLayoutEvidence(
	IReadOnlyDictionary<string, string> ComputedStyles,
	IReadOnlyList<double> Bounds,
	IReadOnlyList<double> ClientRect,
	IReadOnlyList<double> ScrollRect);
