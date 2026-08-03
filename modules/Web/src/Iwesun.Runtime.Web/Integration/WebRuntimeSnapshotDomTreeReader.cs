using System.Text.Json;
using Iwesun.Runtime.WebView2;

namespace Iwesun.Runtime.Web;

/// <summary>
/// Converts the module's fixed complete-DOM capture into a typed tree. JSON is
/// confined to this WebView2 protocol adapter and never crosses the public
/// Fill or element-tree APIs.
/// </summary>
public sealed class WebRuntimeSnapshotDomTreeReader(
	IWebRuntimeScriptSession scripts,
	Func<long>? revisionProvider = null) : IWebRuntimeDomTreeReader
{
	private static long _revision;
	private readonly IWebRuntimeScriptSession _scripts =
		scripts ?? throw new ArgumentNullException(nameof(scripts));
	private readonly Func<long> _revisionProvider =
		revisionProvider ?? CreateRevision;

	public async ValueTask<WebRuntimeDomTreeSnapshot> ReadAsync(
		CancellationToken cancellationToken = default)
	{
		var json = await WebRuntimeDomSnapshot.CaptureAsync(
			_scripts,
			cancellationToken);
		using var document = JsonDocument.Parse(
			json,
			new JsonDocumentOptions { MaxDepth = 4096 });
		var root = document.RootElement;
		var top = root.GetProperty("top");
		var pageUri = new Uri(
			top.GetProperty("url").GetString()
				?? throw new InvalidDataException(
					"The DOM snapshot top document has no URL."),
			UriKind.Absolute);
		var capturedAt = root.TryGetProperty("capturedAt", out var captured)
			&& captured.TryGetDateTimeOffset(out var timestamp)
				? timestamp
				: DateTimeOffset.UtcNow;
		var nodes = new List<WebRuntimeDomTreeElement>();
		var documents = new List<WebRuntimeDomDocumentScope>();
		var frameIndex = 0;
		ParseDocument(
			top,
			"document",
			null,
			nodes,
			documents,
			ref frameIndex);
		return new(
			_revisionProvider(),
			pageUri,
			capturedAt,
			nodes,
			documents);
	}

	private static void ParseDocument(
		JsonElement document,
		string scope,
		int? currentFrameIndex,
		List<WebRuntimeDomTreeElement> nodes,
		List<WebRuntimeDomDocumentScope> documents,
		ref int nextFrameIndex)
	{
		if (!document.TryGetProperty("documentElement", out var documentElement)
			|| documentElement.ValueKind != JsonValueKind.Object)
		{
			throw new InvalidDataException(
				$"DOM document scope '{scope}' has no document element.");
		}
		var url = new Uri(
			RequiredString(document, "url"),
			UriKind.Absolute);
		documents.Add(new(scope, url, currentFrameIndex));
		ParseElement(
			documentElement,
			scope,
			null,
			null,
			null,
			nodes,
			documents,
			ref nextFrameIndex);
	}

	private static void ParseElement(
		JsonElement node,
		string scope,
		string? parentXPath,
		string? leftSiblingXPath,
		string? rightSiblingXPath,
		List<WebRuntimeDomTreeElement> nodes,
		List<WebRuntimeDomDocumentScope> documents,
		ref int nextFrameIndex)
	{
		if (!IsElement(node))
			throw new InvalidDataException("DOM tree parser received a non-element node.");
		var xpath = RequiredString(node, "path");
		var tagName = node.TryGetProperty("localName", out var localName)
			? localName.GetString()
			: null;
		tagName ??= RequiredString(node, "nodeName").ToLowerInvariant();
		var childElements = ReadElementChildren(node, "childNodes");
		var childPaths = childElements
			.Select(static child => RequiredString(child, "path"))
			.ToArray();
		var attributeValues = ReadAttributeValues(node);
		nodes.Add(new WebRuntimeDomTreeElement(
			scope,
			xpath,
			tagName,
			parentXPath,
			childPaths,
			leftSiblingXPath,
			rightSiblingXPath)
		{
			AttributeNames = attributeValues.Keys.ToArray(),
			AttributeValues = attributeValues,
			OwnText = ReadOwnText(node),
			TextContent = ReadTextContent(node)
		});
		for (var index = 0; index < childElements.Count; index++)
		{
			ParseElement(
				childElements[index],
				scope,
				xpath,
				index == 0 ? null : childPaths[index - 1],
				index + 1 == childPaths.Length ? null : childPaths[index + 1],
				nodes,
				documents,
				ref nextFrameIndex);
		}
		if (node.TryGetProperty("shadowRoot", out var shadowRoot)
			&& shadowRoot.ValueKind == JsonValueKind.Object)
		{
			var shadowScope = $"{ElementIdentity(scope, xpath)}#shadow-root";
			ParseDetachedChildren(
				shadowRoot,
				shadowScope,
				nodes,
				documents,
				ref nextFrameIndex);
		}
		if (node.TryGetProperty("contentDocument", out var contentDocument)
			&& contentDocument.ValueKind == JsonValueKind.Object)
		{
			var frameIndex = nextFrameIndex++;
			ParseDocument(
				contentDocument,
				ElementIdentity(scope, xpath),
				frameIndex,
				nodes,
				documents,
				ref nextFrameIndex);
		}
	}

	private static void ParseDetachedChildren(
		JsonElement container,
		string scope,
		List<WebRuntimeDomTreeElement> nodes,
		List<WebRuntimeDomDocumentScope> documents,
		ref int nextFrameIndex)
	{
		var children = ReadElementChildren(container, "childNodes");
		var paths = children
			.Select(static child => RequiredString(child, "path"))
			.ToArray();
		for (var index = 0; index < children.Count; index++)
		{
			ParseElement(
				children[index],
				scope,
				null,
				index == 0 ? null : paths[index - 1],
				index + 1 == paths.Length ? null : paths[index + 1],
				nodes,
				documents,
				ref nextFrameIndex);
		}
	}

	private static IReadOnlyList<JsonElement> ReadElementChildren(
		JsonElement node,
		string propertyName)
	{
		if (!node.TryGetProperty(propertyName, out var children)
			|| children.ValueKind != JsonValueKind.Array)
		{
			return [];
		}
		return children
			.EnumerateArray()
			.Where(IsElement)
			.ToArray();
	}

	private static IReadOnlyList<string> ReadAttributeNames(JsonElement node)
	{
		if (!node.TryGetProperty("attributes", out var attributes)
			|| attributes.ValueKind != JsonValueKind.Array)
		{
			return [];
		}
		return attributes
			.EnumerateArray()
			.Select(static attribute => RequiredString(attribute, "name"))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static IReadOnlyDictionary<string, string> ReadAttributeValues(
		JsonElement node)
	{
		if (!node.TryGetProperty("attributes", out var attributes)
			|| attributes.ValueKind != JsonValueKind.Array)
		{
			return new Dictionary<string, string>(
				StringComparer.OrdinalIgnoreCase);
		}
		return attributes
			.EnumerateArray()
			.GroupBy(
				static attribute => RequiredString(attribute, "name"),
				StringComparer.OrdinalIgnoreCase)
			.ToDictionary(
				static group => group.Key,
				static group =>
					group.Last().TryGetProperty("value", out var value)
						? value.GetString() ?? string.Empty
						: string.Empty,
				StringComparer.OrdinalIgnoreCase);
	}

	private static string ReadOwnText(JsonElement node)
	{
		if (!node.TryGetProperty("childNodes", out var children)
			|| children.ValueKind != JsonValueKind.Array)
		{
			return string.Empty;
		}
		return string.Concat(children
			.EnumerateArray()
			.Where(static child =>
				child.TryGetProperty("nodeType", out var type)
				&& type.GetInt32() == 3)
			.Select(static child =>
				child.TryGetProperty("nodeValue", out var value)
					? value.GetString() ?? string.Empty
					: string.Empty));
	}

	private static string ReadTextContent(JsonElement node)
	{
		if (node.TryGetProperty("nodeType", out var nodeType)
			&& nodeType.GetInt32() == 3)
		{
			return node.TryGetProperty("nodeValue", out var value)
				? value.GetString() ?? string.Empty
				: string.Empty;
		}
		if (!node.TryGetProperty("childNodes", out var children)
			|| children.ValueKind != JsonValueKind.Array)
		{
			return string.Empty;
		}
		return string.Concat(
			children.EnumerateArray().Select(ReadTextContent));
	}

	private static bool IsElement(JsonElement node) =>
		node.ValueKind == JsonValueKind.Object
		&& node.TryGetProperty("nodeType", out var type)
		&& type.GetInt32() == 1;

	private static string RequiredString(
		JsonElement node,
		string propertyName)
	{
		if (node.TryGetProperty(propertyName, out var property)
			&& property.GetString() is { Length: > 0 } value)
		{
			return value;
		}
		throw new InvalidDataException(
			$"DOM element is missing '{propertyName}'.");
	}

	private static string ElementIdentity(string scope, string xpath) =>
		scope.Equals("document", StringComparison.Ordinal)
			? xpath
			: $"{scope}::{xpath}";

	private static long CreateRevision() =>
		Interlocked.Increment(ref _revision);
}
