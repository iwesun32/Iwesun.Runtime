using System.Text.Json;
namespace Iwesun.Runtime.WebView2;

/// <summary>
/// Builds the typed DOM object tree exclusively from the CDP DOM domain.
/// No JavaScript source is accepted or executed by this reader.
/// </summary>
public sealed class WebRuntimeCdpDomTreeReader(
	IWebRuntimeDevToolsSession session,
	Func<long>? revisionProvider = null) : IWebRuntimeDomTreeReader
{
	private static long _revision;
	private readonly IWebRuntimeDevToolsSession _session =
		session ?? throw new ArgumentNullException(nameof(session));
	private readonly Func<long> _revisionProvider =
		revisionProvider ?? CreateRevision;

	public async ValueTask<WebRuntimeDomTreeSnapshot> ReadAsync(
		CancellationToken cancellationToken = default)
	{
		var json = await _session.CallDevToolsProtocolMethodAsync(
			"DOM.getDocument",
			"""{"depth":-1,"pierce":true}""",
			cancellationToken);
		using var response = JsonDocument.Parse(
			json,
			new JsonDocumentOptions { MaxDepth = 4096 });
		var root = response.RootElement.GetProperty("root");
		var pageUri = ResolveUri(
			ReadString(root, "documentURL"),
			_session.CurrentUrl);
		var nodes = new List<WebRuntimeDomTreeElement>();
		var documents = new List<WebRuntimeDomDocumentScope>();
		var textContentByBackendNodeId = BuildTextContentIndex(root);
		var nextFrameIndex = 0;
		ParseDocument(
			root,
			"document",
			pageUri,
			null,
			nodes,
			documents,
			textContentByBackendNodeId,
			ref nextFrameIndex);
		return new(
			_revisionProvider(),
			pageUri,
			DateTimeOffset.UtcNow,
			nodes,
			documents);
	}

	private static void ParseDocument(
		JsonElement documentNode,
		string scope,
		Uri fallbackUri,
		int? frameIndex,
		List<WebRuntimeDomTreeElement> nodes,
		List<WebRuntimeDomDocumentScope> documents,
		IReadOnlyDictionary<int, string> textContentByBackendNodeId,
		ref int nextFrameIndex)
	{
		var documentUri = ResolveUri(
			ReadString(documentNode, "documentURL"),
			fallbackUri.AbsoluteUri);
		documents.Add(new(scope, documentUri, frameIndex));
		var roots = ReadElementChildren(documentNode);
		if (roots.Count == 0)
		{
			throw new InvalidDataException(
				$"CDP document scope '{scope}' has no document element.");
		}
		var root = roots.SingleOrDefault(static node =>
			ReadTagName(node).Equals("html", StringComparison.OrdinalIgnoreCase));
		if (root.ValueKind == JsonValueKind.Undefined)
		{
			if (roots.Count != 1)
			{
				throw new InvalidDataException(
					$"CDP document scope '{scope}' has no unambiguous root.");
			}
			root = roots[0];
		}
		ParseElement(
			root,
			scope,
			$"/{ReadTagName(root)}",
			null,
			null,
			null,
			documentUri,
			nodes,
			documents,
			textContentByBackendNodeId,
			ref nextFrameIndex);
	}

	private static void ParseElement(
		JsonElement node,
		string scope,
		string xpath,
		string? parentXPath,
		string? leftSiblingXPath,
		string? rightSiblingXPath,
		Uri documentUri,
		List<WebRuntimeDomTreeElement> nodes,
		List<WebRuntimeDomDocumentScope> documents,
		IReadOnlyDictionary<int, string> textContentByBackendNodeId,
		ref int nextFrameIndex)
	{
		var children = CreateElementChildren(node, xpath);
		var attributes = ReadAttributes(node);
		nodes.Add(new(
			scope,
			xpath,
			ReadTagName(node),
			parentXPath,
			children.Select(static child => child.XPath).ToArray(),
			leftSiblingXPath,
			rightSiblingXPath)
		{
			NodeId = ReadInt32(node, "nodeId"),
			BackendNodeId = ReadInt32(node, "backendNodeId"),
			AttributeNames = attributes.Keys.ToArray(),
			AttributeValues = attributes,
			OwnText = ReadOwnText(node),
			OwnTextElementInsertionIndex =
				ReadOwnTextElementInsertionIndex(node),
			TextContent = textContentByBackendNodeId.GetValueOrDefault(
				ReadInt32(node, "backendNodeId"),
				string.Empty)
		});
		for (var index = 0; index < children.Count; index++)
		{
			ParseElement(
				children[index].Node,
				scope,
				children[index].XPath,
				xpath,
				index == 0 ? null : children[index - 1].XPath,
				index + 1 == children.Count
					? null
					: children[index + 1].XPath,
				documentUri,
				nodes,
				documents,
				textContentByBackendNodeId,
				ref nextFrameIndex);
		}
		if (node.TryGetProperty("shadowRoots", out var shadowRoots)
			&& shadowRoots.ValueKind == JsonValueKind.Array)
		{
			var shadowScope = $"{ElementIdentity(scope, xpath)}#shadow-root";
			foreach (var shadowRoot in shadowRoots.EnumerateArray())
			{
				if (ReadString(shadowRoot, "shadowRootType").Equals(
					"user-agent",
					StringComparison.OrdinalIgnoreCase))
				{
					// Chromium control implementation is not authored HTML and
					// is intentionally absent from DOMSnapshot.captureSnapshot.
					continue;
				}
				ParseDetachedChildren(
					shadowRoot,
					shadowScope,
					documentUri,
					nodes,
					documents,
					textContentByBackendNodeId,
					ref nextFrameIndex);
			}
		}
		if (node.TryGetProperty("contentDocument", out var contentDocument)
			&& contentDocument.ValueKind == JsonValueKind.Object)
		{
			ParseDocument(
				contentDocument,
				ElementIdentity(scope, xpath),
				documentUri,
				nextFrameIndex++,
				nodes,
				documents,
				textContentByBackendNodeId,
				ref nextFrameIndex);
		}
	}

	private static void ParseDetachedChildren(
		JsonElement container,
		string scope,
		Uri documentUri,
		List<WebRuntimeDomTreeElement> nodes,
		List<WebRuntimeDomDocumentScope> documents,
		IReadOnlyDictionary<int, string> textContentByBackendNodeId,
		ref int nextFrameIndex)
	{
		var children = CreateElementChildren(container, string.Empty);
		for (var index = 0; index < children.Count; index++)
		{
			ParseElement(
				children[index].Node,
				scope,
				children[index].XPath,
				null,
				index == 0 ? null : children[index - 1].XPath,
				index + 1 == children.Count
					? null
					: children[index + 1].XPath,
				documentUri,
				nodes,
				documents,
				textContentByBackendNodeId,
				ref nextFrameIndex);
		}
	}

	private static IReadOnlyList<CdpElementChild> CreateElementChildren(
		JsonElement parent,
		string parentXPath)
	{
		var children = ReadElementChildren(parent);
		var totals = children
			.GroupBy(ReadTagName, StringComparer.OrdinalIgnoreCase)
			.ToDictionary(
				static group => group.Key,
				static group => group.Count(),
				StringComparer.OrdinalIgnoreCase);
		var ordinals = new Dictionary<string, int>(
			StringComparer.OrdinalIgnoreCase);
		var result = new List<CdpElementChild>(children.Count);
		foreach (var child in children)
		{
			var tag = ReadTagName(child);
			ordinals.TryGetValue(tag, out var previous);
			var ordinal = previous + 1;
			ordinals[tag] = ordinal;
			var segment = totals[tag] == 1 ? tag : $"{tag}[{ordinal}]";
			result.Add(new(
				child,
				string.IsNullOrEmpty(parentXPath)
					? $"/{segment}"
					: $"{parentXPath}/{segment}"));
		}
		return result;
	}

	private static IReadOnlyList<JsonElement> ReadElementChildren(
		JsonElement node)
	{
		if (!node.TryGetProperty("children", out var children)
			|| children.ValueKind != JsonValueKind.Array)
		{
			return [];
		}
		return children
			.EnumerateArray()
			.Where(static child =>
				ReadInt32(child, "nodeType") == 1)
			.ToArray();
	}

	private static IReadOnlyDictionary<string, string> ReadAttributes(
		JsonElement node)
	{
		var values = new Dictionary<string, string>(
			StringComparer.OrdinalIgnoreCase);
		if (!node.TryGetProperty("attributes", out var attributes)
			|| attributes.ValueKind != JsonValueKind.Array)
		{
			return values;
		}
		var items = attributes.EnumerateArray().ToArray();
		for (var index = 0; index + 1 < items.Length; index += 2)
		{
			var name = items[index].GetString() ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(name))
				values[name] = items[index + 1].GetString() ?? string.Empty;
		}
		return values;
	}

	private static string ReadOwnText(JsonElement node) =>
		node.TryGetProperty("children", out var children)
			&& children.ValueKind == JsonValueKind.Array
				? string.Concat(children.EnumerateArray()
					.Where(static child =>
						ReadInt32(child, "nodeType") == 3)
					.Select(static child =>
						ReadString(child, "nodeValue")))
				: string.Empty;

	private static int ReadOwnTextElementInsertionIndex(JsonElement node)
	{
		if (!node.TryGetProperty("children", out var children)
			|| children.ValueKind != JsonValueKind.Array)
		{
			return -1;
		}
		var elementIndex = 0;
		foreach (var child in children.EnumerateArray())
		{
			var nodeType = ReadInt32(child, "nodeType");
			if (nodeType == 1)
			{
				elementIndex++;
				continue;
			}
			if (nodeType == 3
				&& !string.IsNullOrWhiteSpace(ReadString(child, "nodeValue")))
			{
				return elementIndex;
			}
		}
		return -1;
	}

	private static IReadOnlyDictionary<int, string> BuildTextContentIndex(
		JsonElement root)
	{
		var values = new Dictionary<int, string>();
		var pool = new Dictionary<string, string>(StringComparer.Ordinal);
		IndexNode(root);
		return values;

		string IndexNode(JsonElement node)
		{
			if (ReadInt32(node, "nodeType") == 3)
				return Share(ReadString(node, "nodeValue"));
			var text = node.TryGetProperty("children", out var children)
				&& children.ValueKind == JsonValueKind.Array
					? Share(string.Concat(
						children.EnumerateArray().Select(IndexNode)))
					: string.Empty;
			var backendNodeId = ReadInt32(node, "backendNodeId");
			if (backendNodeId > 0)
				values[backendNodeId] = text;
			if (node.TryGetProperty("shadowRoots", out var shadowRoots)
				&& shadowRoots.ValueKind == JsonValueKind.Array)
			{
				foreach (var shadowRoot in shadowRoots.EnumerateArray())
					IndexNode(shadowRoot);
			}
			if (node.TryGetProperty("contentDocument", out var contentDocument)
				&& contentDocument.ValueKind == JsonValueKind.Object)
			{
				IndexNode(contentDocument);
			}
			return text;
		}

		string Share(string value)
		{
			if (value.Length == 0)
				return string.Empty;
			if (pool.TryGetValue(value, out var shared))
				return shared;
			pool.Add(value, value);
			return value;
		}
	}

	private static string ReadTagName(JsonElement node) =>
		(ReadString(node, "localName") is { Length: > 0 } localName
			? localName
			: ReadString(node, "nodeName")).ToLowerInvariant();

	private static string ReadString(
		JsonElement element,
		string propertyName) =>
		element.TryGetProperty(propertyName, out var value)
			? value.GetString() ?? string.Empty
			: string.Empty;

	private static int ReadInt32(
		JsonElement element,
		string propertyName) =>
		element.TryGetProperty(propertyName, out var value)
			&& value.TryGetInt32(out var result)
				? result
				: 0;

	private static Uri ResolveUri(string candidate, string fallback) =>
		Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
			? uri
			: new Uri(fallback, UriKind.Absolute);

	private static string ElementIdentity(string scope, string xpath) =>
		scope.Equals("document", StringComparison.Ordinal)
			? xpath
			: $"{scope}::{xpath}";

	private static long CreateRevision() =>
		Interlocked.Increment(ref _revision);

	private sealed record CdpElementChild(JsonElement Node, string XPath);
}
