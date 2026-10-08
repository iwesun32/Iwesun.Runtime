using System.Text.Json;

namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeCdpDomAccessResult(
	long Revision,
	string DocumentScope,
	string XPath,
	int NodeId,
	int BackendNodeId)
{
	public int? InputX { get; init; }
	public int? InputY { get; init; }
	public int? HitNodeId { get; init; }
	public int? HitBackendNodeId { get; init; }
}

/// <summary>
/// Fixed DOM operations resolved against the browser's current DOM and
/// executed with CDP node identities. The tracked tree remains the revision
/// and batch-query authority. No operation injects JavaScript into the page.
/// </summary>
public sealed class WebRuntimeCdpDomAccess(
	IWebRuntimeDevToolsSession session,
	WebRuntimeTrackedCdpDomTreeSession tree)
{
	private static readonly JsonSerializerOptions ProtocolJsonOptions =
		new(JsonSerializerDefaults.Web);
	private readonly IWebRuntimeDevToolsSession _session =
		session ?? throw new ArgumentNullException(nameof(session));
	private readonly WebRuntimeTrackedCdpDomTreeSession _tree =
		tree ?? throw new ArgumentNullException(nameof(tree));

	public ValueTask<string> GetOuterHtmlAsync(
		string xpath,
		string documentScope = "document",
		CancellationToken cancellationToken = default) =>
		UseLiveNodeAsync(
			documentScope,
			xpath,
			async (node, ct) =>
			{
				var json = await CallAsync(
					"DOM.getOuterHTML",
					new { nodeId = node.NodeId },
					ct).ConfigureAwait(false);
				using var response = JsonDocument.Parse(json);
				return response.RootElement.TryGetProperty(
					"outerHTML",
					out var html)
						? html.GetString() ?? string.Empty
						: string.Empty;
			},
			cancellationToken);

	public ValueTask<IReadOnlyDictionary<string, string>> GetAttributesAsync(
		string xpath,
		string documentScope = "document",
		CancellationToken cancellationToken = default) =>
		UseLiveNodeAsync<IReadOnlyDictionary<string, string>>(
			documentScope,
			xpath,
			async (node, ct) =>
			{
				var json = await CallAsync(
					"DOM.getAttributes",
					new { nodeId = node.NodeId },
					ct).ConfigureAwait(false);
				using var response = JsonDocument.Parse(json);
				var result = new Dictionary<string, string>(
					StringComparer.OrdinalIgnoreCase);
				if (!response.RootElement.TryGetProperty(
					"attributes",
					out var attributes)
					|| attributes.ValueKind != JsonValueKind.Array)
				{
					return result;
				}
				var values = attributes.EnumerateArray().ToArray();
				for (var index = 0; index + 1 < values.Length; index += 2)
				{
					var name = values[index].GetString();
					if (!string.IsNullOrWhiteSpace(name))
					{
						result[name] =
							values[index + 1].GetString() ?? string.Empty;
					}
				}
				return result;
			},
			cancellationToken);

	public ValueTask<WebRuntimeCdpDomAccessResult> SetAttributeAsync(
		string xpath,
		string name,
		string value,
		string documentScope = "document",
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		ArgumentNullException.ThrowIfNull(value);
		return ExecuteAsync(
			xpath,
			documentScope,
			"DOM.setAttributeValue",
			node => new { nodeId = node.NodeId, name, value },
			cancellationToken);
	}

	public ValueTask<WebRuntimeCdpDomAccessResult> RemoveAttributeAsync(
		string xpath,
		string name,
		string documentScope = "document",
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		return ExecuteAsync(
			xpath,
			documentScope,
			"DOM.removeAttribute",
			node => new { nodeId = node.NodeId, name },
			cancellationToken);
	}

	public ValueTask<WebRuntimeCdpDomAccessResult> FocusAsync(
		string xpath,
		string documentScope = "document",
		CancellationToken cancellationToken = default) =>
		ExecuteAsync(
			xpath,
			documentScope,
			"DOM.focus",
			static node => new { nodeId = node.NodeId },
			cancellationToken);

	public ValueTask<WebRuntimeCdpDomAccessResult> HighlightAsync(
		string xpath,
		string documentScope = "document",
		CancellationToken cancellationToken = default) =>
		UseLiveNodeAsync(
			documentScope,
			xpath,
			async (node, ct) =>
			{
				await CallAsync("Overlay.enable", new { }, ct)
					.ConfigureAwait(false);
				await CallAsync(
					"Overlay.highlightNode",
					new
					{
						nodeId = node.NodeId,
						highlightConfig = new
						{
							showInfo = true,
							showStyles = false,
							contentColor = new
							{
								r = 255,
								g = 77,
								b = 79,
								a = 0.25
							},
							borderColor = new
							{
								r = 255,
								g = 77,
								b = 79,
								a = 1
							}
						}
					},
					ct).ConfigureAwait(false);
				return CreateResult(node);
			},
			cancellationToken);

	public async ValueTask HideHighlightAsync(
		CancellationToken cancellationToken = default)
	{
		await CallAsync(
			"Overlay.hideHighlight",
			new { },
			cancellationToken).ConfigureAwait(false);
	}

	public ValueTask<WebRuntimeCdpDomAccessResult> ClickAsync(
		string xpath,
		string documentScope = "document",
		CancellationToken cancellationToken = default) =>
		UseLiveNodeAsync(
			documentScope,
			xpath,
			async (node, ct) =>
			{
				await CallAsync(
					"Page.bringToFront",
					new { },
					ct).ConfigureAwait(false);
				await CallAsync(
					"DOM.scrollIntoViewIfNeeded",
					new { backendNodeId = node.BackendNodeId },
					ct).ConfigureAwait(false);
				var quadsJson = await CallAsync(
					"DOM.getContentQuads",
					new { backendNodeId = node.BackendNodeId },
					ct).ConfigureAwait(false);
				var metricsJson = await CallAsync(
					"Page.getLayoutMetrics",
					new { },
					ct).ConfigureAwait(false);
				var hit = await FindVisibleClickHitAsync(
					node,
					quadsJson,
					metricsJson,
					ct)
					.ConfigureAwait(false);
				if (hit is null)
				{
					throw new InvalidOperationException(
						"The visible DOM content quads do not hit the "
						+ "requested node or one of its descendants.");
				}
				await CallAsync(
					"Input.dispatchMouseEvent",
					new
					{
						type = "mousePressed",
						x = hit.Value.Point.X,
						y = hit.Value.Point.Y,
						button = "left",
						buttons = 1,
						clickCount = 1,
						pointerType = "mouse"
					},
					ct).ConfigureAwait(false);
				await CallAsync(
					"Input.dispatchMouseEvent",
					new
					{
						type = "mouseReleased",
						x = hit.Value.Point.X,
						y = hit.Value.Point.Y,
						button = "left",
						buttons = 0,
						clickCount = 1,
						pointerType = "mouse"
					},
					ct).ConfigureAwait(false);
				return CreateResult(node) with
				{
					InputX = hit.Value.Point.X,
					InputY = hit.Value.Point.Y,
					HitNodeId = hit.Value.NodeId,
					HitBackendNodeId = hit.Value.BackendNodeId
				};
			},
			cancellationToken);

	private async Task<ClickHit?> FindVisibleClickHitAsync(
		LiveNode semanticTarget,
		string semanticTargetQuadsJson,
		string metricsJson,
		CancellationToken cancellationToken)
	{
		var hitTarget = semanticTarget;
		for (var ancestorDepth = 0; ancestorDepth < 8; ancestorDepth++)
		{
			var quadsJson = ancestorDepth == 0
				? semanticTargetQuadsJson
				: await CallAsync(
					"DOM.getContentQuads",
					new { nodeId = hitTarget.NodeId },
					cancellationToken).ConfigureAwait(false);
			IReadOnlyList<ClickPoint> points;
			try
			{
				points = ReadVisibleContentPoints(quadsJson, metricsJson);
			}
			catch (InvalidDataException)
			{
				if (!TryGetTrackedParent(hitTarget, out hitTarget))
					return null;
				continue;
			}
			foreach (var point in points)
			{
				var beforeMove = await ReadHitAsync(point, cancellationToken)
					.ConfigureAwait(false);
				if (!IsTargetOrDescendant(hitTarget, beforeMove))
					continue;
				await CallAsync(
					"Input.dispatchMouseEvent",
					new
					{
						type = "mouseMoved",
						x = point.X,
						y = point.Y,
						button = "none",
						buttons = 0,
						clickCount = 0,
						pointerType = "mouse"
					},
					cancellationToken).ConfigureAwait(false);
				var afterMove = await ReadHitAsync(point, cancellationToken)
					.ConfigureAwait(false);
				if (IsTargetOrDescendant(hitTarget, afterMove))
					return afterMove;
			}
			if (!TryGetTrackedParent(hitTarget, out hitTarget))
				return null;
		}
		return null;
	}

	private bool TryGetTrackedParent(
		LiveNode node,
		out LiveNode parent)
	{
		parent = default;
		var index = _tree.Current;
		if (index is null
			|| !index.TryGetNode(node.NodeId, out var tracked)
			|| tracked.Parent is not { } trackedParent)
		{
			return false;
		}
		parent = new(
			index.Revision,
			trackedParent.DocumentScope,
			trackedParent.XPath,
			trackedParent.NodeId,
			trackedParent.BackendNodeId);
		return true;
	}


	private ValueTask<WebRuntimeCdpDomAccessResult> ExecuteAsync(
		string xpath,
		string documentScope,
		string method,
		Func<LiveNode, object> createParameters,
		CancellationToken cancellationToken) =>
		UseLiveNodeAsync(
			documentScope,
			xpath,
			async (node, ct) =>
			{
				await CallAsync(method, createParameters(node), ct)
					.ConfigureAwait(false);
				return CreateResult(node);
			},
			cancellationToken);

	private async ValueTask<T> UseLiveNodeAsync<T>(
		string documentScope,
		string xpath,
		Func<LiveNode, CancellationToken, Task<T>> operation,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentScope);
		ArgumentException.ThrowIfNullOrWhiteSpace(xpath);
		ArgumentNullException.ThrowIfNull(operation);
		for (var attempt = 0; ; attempt++)
		{
			try
			{
				var node = await ResolveLiveNodeAsync(
					documentScope,
					xpath,
					cancellationToken).ConfigureAwait(false);
				return await operation(node, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (Exception exception)
				when ((attempt == 0
						&& WebRuntimeTrackedCdpDomTreeSession
							.IsInvalidNodeIdentityException(exception))
					|| (attempt < 4
						&& WebRuntimeTrackedCdpDomTreeSession
							.IsCurrentTreeUnavailableException(exception)))
			{
				await _tree.RefreshAsync(cancellationToken)
					.ConfigureAwait(false);
			}
		}
	}

	private async Task<LiveNode> ResolveLiveNodeAsync(
		string documentScope,
		string xpath,
		CancellationToken cancellationToken)
	{
		return await _tree.UseNodeAsync(
			documentScope,
			xpath,
			(indexed, _) => Task.FromResult(
				new LiveNode(
					_tree.Current?.Revision
						?? throw new InvalidOperationException(
							"The tracked DOM tree has no current revision."),
					documentScope,
					xpath,
					indexed.NodeId,
					indexed.BackendNodeId)),
			cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask<long> EnsureTreeMatchesLiveNodeAsync(
		string documentScope,
		string xpath,
		int nodeId,
		int backendNodeId,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < 2; attempt++)
		{
			await _tree.InitializeAsync(cancellationToken).ConfigureAwait(false);
			var current = _tree.Current;
			if (current is null
				|| !PageUrisMatch(
					current.Snapshot.PageUri,
					_session.CurrentUrl))
			{
				await _tree.RefreshAsync(cancellationToken)
					.ConfigureAwait(false);
				current = _tree.Current;
			}
			if (current is not null)
			{
				try
				{
					var indexed = current.ResolveXPath(documentScope, xpath);
					if (indexed.NodeId == nodeId
						&& indexed.BackendNodeId == backendNodeId)
					{
						return current.Revision;
					}
				}
				catch (Exception exception)
					when (exception is KeyNotFoundException
						or InvalidOperationException)
				{
				}
			}
			_tree.Invalidate();
			await _tree.RefreshAsync(cancellationToken).ConfigureAwait(false);
		}
		throw new InvalidOperationException(
			"The live CDP XPath result does not belong to the requested "
			+ "document scope and current DOM tree revision.");
	}

	private static bool PageUrisMatch(Uri pageUri, string currentUrl) =>
		Uri.TryCreate(currentUrl, UriKind.Absolute, out var browserUri)
		&& pageUri.Equals(browserUri);

	private Task<string> CallAsync(
		string method,
		object parameters,
		CancellationToken cancellationToken) =>
		_session.CallDevToolsProtocolMethodAsync(
			method,
			JsonSerializer.Serialize(parameters, ProtocolJsonOptions),
			cancellationToken);

	private WebRuntimeCdpDomAccessResult CreateResult(
		LiveNode node) =>
		new(
			node.Revision,
			node.DocumentScope,
			node.XPath,
			node.NodeId,
			node.BackendNodeId);

	private async Task<ClickHit> ReadHitAsync(
		ClickPoint point,
		CancellationToken cancellationToken)
	{
		var json = await CallAsync(
			"DOM.getNodeForLocation",
			new
			{
				x = point.X,
				y = point.Y,
				includeUserAgentShadowDOM = true,
				ignorePointerEventsNone = false
			},
			cancellationToken).ConfigureAwait(false);
		using var response = JsonDocument.Parse(json);
		var root = response.RootElement;
		var nodeId = root.TryGetProperty("nodeId", out var node)
			? node.GetInt32()
			: 0;
		var backendNodeId = root.TryGetProperty(
			"backendNodeId",
			out var backendNode)
				? backendNode.GetInt32()
				: 0;
		if (nodeId <= 0 && backendNodeId <= 0)
		{
			throw new InvalidDataException(
				"DOM.getNodeForLocation returned no node identity.");
		}
		return new(point, nodeId, backendNodeId);
	}

	private bool IsTargetOrDescendant(
		LiveNode target,
		ClickHit hit)
	{
		var index = _tree.Current
			?? throw new InvalidOperationException(
				"The CDP DOM tree has no current revision.");
		WebRuntimeCdpDomTreeNode? current = null;
		if (hit.NodeId > 0)
			index.TryGetNode(hit.NodeId, out current!);
		if (current is null && hit.BackendNodeId > 0)
			index.TryGetBackendNode(hit.BackendNodeId, out current!);
		while (current is not null)
		{
			if (current.NodeId == target.NodeId
				&& current.BackendNodeId == target.BackendNodeId)
				return true;
			current = current.Parent;
		}
		return false;
	}

	private static IReadOnlyList<ClickPoint> ReadVisibleContentPoints(
		string quadsJson,
		string metricsJson)
	{
		var viewport = ReadViewport(metricsJson);
		using var response = JsonDocument.Parse(quadsJson);
		if (!response.RootElement.TryGetProperty("quads", out var quads)
			|| quads.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException(
				"DOM.getContentQuads returned no content quads.");
		}
		var candidates = new List<(double Area, ClickPoint Point)>();
		foreach (var quad in quads.EnumerateArray())
		{
			if (quad.ValueKind != JsonValueKind.Array)
				continue;
			var values = quad.EnumerateArray()
				.Select(static value => value.GetDouble())
				.ToArray();
			if (values.Length != 8)
				continue;
			IReadOnlyList<GeometryPoint> polygon =
			[
				new(values[0] - viewport.PageX, values[1] - viewport.PageY),
				new(values[2] - viewport.PageX, values[3] - viewport.PageY),
				new(values[4] - viewport.PageX, values[5] - viewport.PageY),
				new(values[6] - viewport.PageX, values[7] - viewport.PageY)
			];
			polygon = ClipPolygon(
				polygon,
				static point => point.X >= 0,
				static (start, end) => IntersectX(start, end, 0));
			polygon = ClipPolygon(
				polygon,
				point => point.X <= viewport.Width,
				(start, end) => IntersectX(
					start,
					end,
					viewport.Width));
			polygon = ClipPolygon(
				polygon,
				static point => point.Y >= 0,
				static (start, end) => IntersectY(start, end, 0));
			polygon = ClipPolygon(
				polygon,
				point => point.Y <= viewport.Height,
				(start, end) => IntersectY(
					start,
					end,
					viewport.Height));
			var area = Math.Abs(SignedArea(polygon));
			if (area <= 1)
				continue;
			var center = ReadCentroid(polygon);
			AddCandidate(candidates, area, center, viewport);
			foreach (var vertex in polygon)
			{
				AddCandidate(
					candidates,
					area,
					Blend(center, vertex, 0.5),
					viewport);
			}
			for (var index = 0; index < polygon.Count; index++)
			{
				var next = polygon[(index + 1) % polygon.Count];
				var edgeCenter = Blend(polygon[index], next, 0.5);
				AddCandidate(
					candidates,
					area,
					Blend(center, edgeCenter, 0.5),
					viewport);
			}
		}
		if (candidates.Count == 0)
		{
			throw new InvalidDataException(
				"The DOM node has no visible content quad in the CSS viewport. "
				+ $"Viewport=({viewport.PageX},{viewport.PageY},"
				+ $"{viewport.Width},{viewport.Height}); "
				+ $"Quads={BoundDiagnosticText(quadsJson)}");
		}
		return candidates.OrderByDescending(static candidate => candidate.Area)
			.Select(static candidate => candidate.Point)
			.Distinct()
			.ToArray();
	}

	private static string BoundDiagnosticText(string value) =>
		value.Length <= 512 ? value : value[..512] + "...";

	private static void AddCandidate(
		ICollection<(double Area, ClickPoint Point)> candidates,
		double area,
		GeometryPoint point,
		(double PageX, double PageY, double Width, double Height) viewport)
	{
		var x = Math.Clamp(
			(int)Math.Round(point.X),
			0,
			Math.Max(0, (int)Math.Floor(viewport.Width) - 1));
		var y = Math.Clamp(
			(int)Math.Round(point.Y),
			0,
			Math.Max(0, (int)Math.Floor(viewport.Height) - 1));
		candidates.Add((area, new(x, y)));
	}

	private static GeometryPoint Blend(
		GeometryPoint start,
		GeometryPoint end,
		double ratio) =>
		new(
			start.X + ((end.X - start.X) * ratio),
			start.Y + ((end.Y - start.Y) * ratio));

	private static (
		double PageX,
		double PageY,
		double Width,
		double Height) ReadViewport(string json)
	{
		using var response = JsonDocument.Parse(json);
		var root = response.RootElement;
		JsonElement viewport;
		if (!root.TryGetProperty("cssLayoutViewport", out viewport)
			&& !root.TryGetProperty("layoutViewport", out viewport))
		{
			throw new InvalidDataException(
				"Page.getLayoutMetrics returned no CSS layout viewport.");
		}
		var width = viewport.GetProperty("clientWidth").GetDouble();
		var height = viewport.GetProperty("clientHeight").GetDouble();
		var pageX = viewport.TryGetProperty("pageX", out var pageXProperty)
			? pageXProperty.GetDouble()
			: 0;
		var pageY = viewport.TryGetProperty("pageY", out var pageYProperty)
			? pageYProperty.GetDouble()
			: 0;
		if (width <= 0 || height <= 0)
			throw new InvalidDataException("The CSS layout viewport is empty.");
		return (pageX, pageY, width, height);
	}

	private static IReadOnlyList<GeometryPoint> ClipPolygon(
		IReadOnlyList<GeometryPoint> polygon,
		Func<GeometryPoint, bool> isInside,
		Func<GeometryPoint, GeometryPoint, GeometryPoint> intersect)
	{
		if (polygon.Count == 0)
			return [];
		var result = new List<GeometryPoint>();
		var start = polygon[^1];
		var startInside = isInside(start);
		foreach (var end in polygon)
		{
			var endInside = isInside(end);
			if (endInside)
			{
				if (!startInside)
					result.Add(intersect(start, end));
				result.Add(end);
			}
			else if (startInside)
			{
				result.Add(intersect(start, end));
			}
			start = end;
			startInside = endInside;
		}
		return result;
	}

	private static GeometryPoint IntersectX(
		GeometryPoint start,
		GeometryPoint end,
		double x)
	{
		var delta = end.X - start.X;
		var ratio = Math.Abs(delta) < double.Epsilon
			? 0
			: (x - start.X) / delta;
		return new(x, start.Y + ((end.Y - start.Y) * ratio));
	}

	private static GeometryPoint IntersectY(
		GeometryPoint start,
		GeometryPoint end,
		double y)
	{
		var delta = end.Y - start.Y;
		var ratio = Math.Abs(delta) < double.Epsilon
			? 0
			: (y - start.Y) / delta;
		return new(start.X + ((end.X - start.X) * ratio), y);
	}

	private static double SignedArea(IReadOnlyList<GeometryPoint> polygon)
	{
		var sum = 0d;
		for (var index = 0; index < polygon.Count; index++)
		{
			var next = polygon[(index + 1) % polygon.Count];
			sum += (polygon[index].X * next.Y)
				- (next.X * polygon[index].Y);
		}
		return sum / 2d;
	}

	private static GeometryPoint ReadCentroid(
		IReadOnlyList<GeometryPoint> polygon)
	{
		var area = SignedArea(polygon);
		if (Math.Abs(area) < double.Epsilon)
		{
			return new(
				polygon.Average(static point => point.X),
				polygon.Average(static point => point.Y));
		}
		var x = 0d;
		var y = 0d;
		for (var index = 0; index < polygon.Count; index++)
		{
			var next = polygon[(index + 1) % polygon.Count];
			var cross = (polygon[index].X * next.Y)
				- (next.X * polygon[index].Y);
			x += (polygon[index].X + next.X) * cross;
			y += (polygon[index].Y + next.Y) * cross;
		}
		return new(x / (6d * area), y / (6d * area));
	}

	private readonly record struct GeometryPoint(double X, double Y);
	private readonly record struct LiveNode(
		long Revision,
		string DocumentScope,
		string XPath,
		int NodeId,
		int BackendNodeId);
	private readonly record struct ClickPoint(int X, int Y);
	private readonly record struct ClickHit(
		ClickPoint Point,
		int NodeId,
		int BackendNodeId);
}
