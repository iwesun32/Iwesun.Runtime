using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeDomEvidenceProgress(
	string Operation,
	int ProcessedElements,
	int PhaseTotalElements,
	int TreeTotalElements,
	int CurrentHierarchyLevel,
	int MaximumHierarchyLevel,
	int NodeId,
	int BackendNodeId,
	string DocumentScope,
	string XPath);

/// <summary>
/// Formal DOM evidence reader backed exclusively by CDP domains. It captures
/// one immutable DOMSnapshot and performs CSS/event/runtime enrichments once
/// per distinct element, never once per reflected property.
/// </summary>
public sealed class WebRuntimeCdpDomEvidenceReader(
	IWebRuntimeDevToolsSession session,
	Func<WebRuntimeDomTreeSnapshot> currentTree) : IWebRuntimeDomEvidenceReader
{
	private const int MatchedStyleRequestBatchSize = 4;
	private static readonly JsonSerializerOptions ProtocolJsonOptions =
		new(JsonSerializerDefaults.Web)
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
	private readonly IWebRuntimeDevToolsSession _session =
		session ?? throw new ArgumentNullException(nameof(session));
	private readonly Func<WebRuntimeDomTreeSnapshot> _currentTree =
		currentTree ?? throw new ArgumentNullException(nameof(currentTree));

	public Uri CurrentPageUrl => _currentTree().PageUri;

	public WebRuntimeCdpEvidenceSnapshot? LastSnapshot { get; private set; }

	public async ValueTask<IReadOnlyList<WebRuntimeDomIndexedProperty>>
		ReadDomPropertiesAsync(
			IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
			CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(requests);
		if (requests.Count == 0)
			return [];
		ValidateStrongIdentities(requests);
		var sourceTree = _currentTree();
		var treeTotalElements = requests
			.Select(static request =>
				(request.DocumentScope, request.XPath))
			.Distinct()
			.Count();
		var maximumHierarchyLevel = requests.Max(
			static request => request.HierarchyLevel);
		var byBackendNodeId = requests
			.Where(static request => request.BackendNodeId > 0)
			.GroupBy(static request => request.BackendNodeId)
			.ToDictionary(
				static group => group.Key,
				static group => group.First());
		var computedStyleNames = requests
			.Where(static request =>
				request.PropertyName.StartsWith("style.", StringComparison.Ordinal))
			.Select(static request => ToCssName(request.PropertyName[6..]))
			.Append("animation-name")
			.Append("animation-duration")
			.Append("animation-delay")
			.Append("animation-timing-function")
			.Append("animation-iteration-count")
			.Append("animation-direction")
			.Append("animation-fill-mode")
			.Append("animation-play-state")
			.Append("transition-property")
			.Append("transition-duration")
			.Append("transition-delay")
			.Append("transition-timing-function")
			.Append("content")
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(static name => name, StringComparer.Ordinal)
			.ToArray();
		var snapshotJson = await _session.CallDevToolsProtocolMethodAsync(
			"DOMSnapshot.captureSnapshot",
			JsonSerializer.Serialize(
				new
				{
					computedStyles = computedStyleNames,
					includePaintOrder = true,
					includeDOMRects = true,
					includeBlendedBackgroundColors = true,
					includeTextColorOpacities = true
				},
				ProtocolJsonOptions),
			cancellationToken).ConfigureAwait(false);
		var snapshot = WebRuntimeCdpEvidenceSnapshot.Parse(
			sourceTree.PageUri,
			snapshotJson,
			computedStyleNames);
		LastSnapshot = snapshot;
		var byNodeId = requests
			.Where(static request => request.NodeId > 0)
			.GroupBy(static request => request.NodeId)
			.ToDictionary(
				static group => group.Key,
				static group => group.First());

		EnsureTreeRevision(sourceTree);
		var treeElements = sourceTree.Elements
			.Where(static element => element.BackendNodeId > 0)
			.GroupBy(static element => element.BackendNodeId)
			.ToFrozenDictionary(
				static group => group.Key,
				static group => group.Single());
		var authoredStyleNamesByNode = requests
			.Where(static request =>
				request.PropertyName.StartsWith("style.", StringComparison.Ordinal)
				&& request.Slot != WebRuntimeDomDataSlot.Runtime)
			.Where(static request => request.NodeId > 0)
			.GroupBy(static request => request.NodeId)
			.ToDictionary(
				static group => group.Key,
				static group => group
					.Select(static request =>
						ToCssName(request.PropertyName[6..]))
					.Distinct(StringComparer.OrdinalIgnoreCase)
					.ToHashSet(StringComparer.OrdinalIgnoreCase));
		var styleRequests = authoredStyleNamesByNode.Keys.ToArray();
		var computedStyleRequests = requests
			.Where(static request =>
				request.PropertyName.StartsWith("style.", StringComparison.Ordinal)
				&& request.Slot == WebRuntimeDomDataSlot.Runtime)
			.Select(static request => request.NodeId)
			.Where(static nodeId => nodeId > 0)
			.Distinct()
			.ToArray();
		var computedStyleFallbackRequests = computedStyleRequests
			.Where(nodeId =>
			{
				var request = byNodeId[nodeId];
				return !snapshot.TryGetNode(request.BackendNodeId, out var evidence)
					|| computedStyleNames.Any(name =>
						!evidence.ComputedStyles.ContainsKey(name));
			})
			.ToArray();
		var eventRequests = requests
			.Where(static request => request.OwnerKind == WebRuntimeDomOwnerKind.Event)
			.Select(static request => request.BackendNodeId)
			.Where(static backendNodeId => backendNodeId > 0)
			.Distinct()
			.ToArray();
		var matchedStyles = await ReadMatchedStylesAsync(
			styleRequests,
			byNodeId,
			authoredStyleNamesByNode,
			treeTotalElements,
			maximumHierarchyLevel,
			cancellationToken).ConfigureAwait(false);
		// DOMSnapshot is the primary same-revision source. Chromium omits the
		// computed-style table for some nodes without a layout entry, so query
		// only those incomplete nodes instead of repeating the call for the tree.
		var computedStyles = await ReadComputedStylesAsync(
			computedStyleFallbackRequests,
			byNodeId,
			treeTotalElements,
			maximumHierarchyLevel,
			cancellationToken).ConfigureAwait(false);
		var eventListeners = await ReadEventListenersAsync(
			eventRequests,
			byBackendNodeId,
			treeTotalElements,
			maximumHierarchyLevel,
			cancellationToken).ConfigureAwait(false);

		var results = new WebRuntimeDomIndexedProperty[requests.Count];
		for (var index = 0; index < requests.Count; index++)
		{
			var request = requests[index];
			if (!snapshot.TryGetNode(request.BackendNodeId, out var evidence))
			{
				throw new InvalidDataException(
					$"CDP snapshot omitted backendNodeId {request.BackendNodeId} "
					+ $"for {request.DocumentScope}::{request.XPath}.");
			}
			treeElements.TryGetValue(request.BackendNodeId, out var treeElement);
			results[index] = Read(
				request,
				evidence,
				treeElement,
				matchedStyles.GetValueOrDefault(request.NodeId),
				computedStyles.GetValueOrDefault(request.NodeId),
				eventListeners.GetValueOrDefault(request.BackendNodeId));
		}
		EnsureTreeRevision(sourceTree);
		return results;
	}

	private async Task<IReadOnlyDictionary<int, int>>
		ResolveCurrentNodeIdsAsync(
			IReadOnlyList<int> backendNodeIds,
			CancellationToken cancellationToken)
	{
		if (backendNodeIds.Count == 0)
			return FrozenDictionary<int, int>.Empty;
		await EnableDomainAsync("DOM.enable", cancellationToken)
			.ConfigureAwait(false);
		await _session.CallDevToolsProtocolMethodAsync(
			"DOM.getDocument",
			"""{"depth":0,"pierce":true}""",
			cancellationToken).ConfigureAwait(false);
		var json = await _session.CallDevToolsProtocolMethodAsync(
			"DOM.pushNodesByBackendIdsToFrontend",
			JsonSerializer.Serialize(
				new { backendNodeIds },
				ProtocolJsonOptions),
			cancellationToken).ConfigureAwait(false);
		using var response = JsonDocument.Parse(json);
		var nodeIds = response.RootElement
			.GetProperty("nodeIds")
			.EnumerateArray()
			.Select(static node => node.GetInt32())
			.ToArray();
		if (nodeIds.Length != backendNodeIds.Count)
		{
			throw new InvalidDataException(
				"CDP returned a different nodeId count for the pinned "
					+ "backend-node identity set.");
		}
		var result = new Dictionary<int, int>();
		for (var index = 0; index < nodeIds.Length; index++)
		{
			if (nodeIds[index] > 0)
				result[backendNodeIds[index]] = nodeIds[index];
		}
		return result.ToFrozenDictionary();
	}

	private void EnsureTreeRevision(WebRuntimeDomTreeSnapshot sourceTree)
	{
		var current = _currentTree();
		if (current.Revision != sourceTree.Revision
			|| !current.PageUri.Equals(sourceTree.PageUri))
		{
			throw new InvalidOperationException(
				"The CDP DOM tree changed while batch evidence was being read.");
		}
	}

	private async Task<IReadOnlyDictionary<
		int,
		CdpMatchedStyleEvidence>>
		ReadMatchedStylesAsync(
			IReadOnlyList<int> nodeIds,
			IReadOnlyDictionary<int, WebRuntimeDomPropertyRequest> requests,
			IReadOnlyDictionary<int, HashSet<string>> requestedCssNames,
			int treeTotalElements,
			int maximumHierarchyLevel,
			CancellationToken cancellationToken)
	{
		if (nodeIds.Count == 0)
		{
			return FrozenDictionary<
				int,
				CdpMatchedStyleEvidence>.Empty;
		}
		await EnableDomainAsync("DOM.enable", cancellationToken)
			.ConfigureAwait(false);
		await EnableDomainAsync("CSS.enable", cancellationToken)
			.ConfigureAwait(false);
		var result = new Dictionary<
			int,
			CdpMatchedStyleEvidence>();
		for (var batchStart = 0;
			batchStart < nodeIds.Count;
			batchStart += MatchedStyleRequestBatchSize)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var batchLength = Math.Min(
				MatchedStyleRequestBatchSize,
				nodeIds.Count - batchStart);
			var pending = new Task<(int NodeId, string Json)>[batchLength];
			for (var offset = 0; offset < batchLength; offset++)
			{
				pending[offset] = CaptureMatchedStyleAsync(
					nodeIds[batchStart + offset],
					cancellationToken);
			}
			var batch = await Task.WhenAll(pending).ConfigureAwait(false);
			for (var offset = 0; offset < batch.Length; offset++)
			{
				var (nodeId, json) = batch[offset];
				using var response = JsonDocument.Parse(json);
				var pseudoElementsJson = response.RootElement.TryGetProperty(
					"pseudoElements",
					out var pseudoElements)
					&& pseudoElements.GetArrayLength() != 0
						? pseudoElements.GetRawText()
						: string.Empty;
				result[nodeId] = new(
					IndexDeclarations(
						response.RootElement,
						requestedCssNames[nodeId]),
					pseudoElementsJson);
				TraceProgress(
					"MatchedStyles",
					batchStart + offset + 1,
					nodeIds.Count,
					treeTotalElements,
					maximumHierarchyLevel,
					requests[nodeId]);
			}
		}
		return result.ToFrozenDictionary();

		async Task<(int NodeId, string Json)> CaptureMatchedStyleAsync(
			int nodeId,
			CancellationToken token)
		{
			try
			{
				var json = await _session.CallDevToolsProtocolMethodAsync(
					"CSS.getMatchedStylesForNode",
					JsonSerializer.Serialize(new { nodeId }, ProtocolJsonOptions),
					token).ConfigureAwait(false);
				return (nodeId, json);
			}
			catch (Exception exception)
				when (exception is not OperationCanceledException)
			{
				var request = requests[nodeId];
				throw new InvalidDataException(
					$"CDP matched-style lookup failed for nodeId {nodeId}, "
						+ $"backendNodeId {request.BackendNodeId}, "
						+ $"{request.DocumentScope}::{request.XPath}, "
						+ $"tag={request.TagName}.",
					exception);
			}
		}
	}

	private async Task<IReadOnlyDictionary<
		int,
		IReadOnlyDictionary<string, string>>> ReadComputedStylesAsync(
			IReadOnlyList<int> nodeIds,
			IReadOnlyDictionary<int, WebRuntimeDomPropertyRequest> requests,
			int treeTotalElements,
			int maximumHierarchyLevel,
			CancellationToken cancellationToken)
	{
		if (nodeIds.Count == 0)
		{
			return FrozenDictionary<
				int,
				IReadOnlyDictionary<string, string>>.Empty;
		}
		await EnableDomainAsync("DOM.enable", cancellationToken)
			.ConfigureAwait(false);
		await EnableDomainAsync("CSS.enable", cancellationToken)
			.ConfigureAwait(false);
		var result =
			new Dictionary<int, IReadOnlyDictionary<string, string>>();
		for (var batchStart = 0;
			batchStart < nodeIds.Count;
			batchStart += MatchedStyleRequestBatchSize)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var batchLength = Math.Min(
				MatchedStyleRequestBatchSize,
				nodeIds.Count - batchStart);
			var pending = new Task<(int NodeId, string Json)>[batchLength];
			for (var offset = 0; offset < batchLength; offset++)
			{
				var nodeId = nodeIds[batchStart + offset];
				pending[offset] = ReadComputedStyleAsync(
					nodeId,
					cancellationToken);
			}
			var batch = await Task.WhenAll(pending).ConfigureAwait(false);
			for (var offset = 0; offset < batch.Length; offset++)
			{
				var (nodeId, json) = batch[offset];
				using var response = JsonDocument.Parse(json);
				var values = new Dictionary<string, string>(
					StringComparer.OrdinalIgnoreCase);
				foreach (var property in response.RootElement
					.GetProperty("computedStyle")
					.EnumerateArray())
				{
					var name = ReadString(property, "name");
					if (name.Length != 0)
						values[name] = ReadString(property, "value");
				}
				result[nodeId] = values.ToFrozenDictionary(
					StringComparer.OrdinalIgnoreCase);
				TraceProgress(
					"ComputedStyles",
					batchStart + offset + 1,
					nodeIds.Count,
					treeTotalElements,
					maximumHierarchyLevel,
					requests[nodeId]);
			}
		}
		return result.ToFrozenDictionary();

		async Task<(int NodeId, string Json)> ReadComputedStyleAsync(
			int nodeId,
			CancellationToken token) =>
			(nodeId, await _session.CallDevToolsProtocolMethodAsync(
				"CSS.getComputedStyleForNode",
				JsonSerializer.Serialize(new { nodeId }, ProtocolJsonOptions),
				token).ConfigureAwait(false));
	}

	private async Task<IReadOnlyDictionary<int, IReadOnlyList<CdpEventListener>>>
		ReadEventListenersAsync(
			IReadOnlyList<int> backendNodeIds,
			IReadOnlyDictionary<int, WebRuntimeDomPropertyRequest> requests,
			int treeTotalElements,
			int maximumHierarchyLevel,
			CancellationToken cancellationToken)
	{
		if (backendNodeIds.Count == 0)
		{
			return FrozenDictionary<int, IReadOnlyList<CdpEventListener>>.Empty;
		}
		var result =
			new Dictionary<int, IReadOnlyList<CdpEventListener>>();
		for (var index = 0; index < backendNodeIds.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var backendNodeId = backendNodeIds[index];
			var resolveJson = await _session.CallDevToolsProtocolMethodAsync(
				"DOM.resolveNode",
				JsonSerializer.Serialize(
					new { backendNodeId },
					ProtocolJsonOptions),
				cancellationToken).ConfigureAwait(false);
			using var resolveResponse = JsonDocument.Parse(resolveJson);
			var objectId = resolveResponse.RootElement
				.GetProperty("object")
				.GetProperty("objectId")
				.GetString();
			if (string.IsNullOrWhiteSpace(objectId))
			{
				throw new InvalidDataException(
					$"CDP could not resolve backendNodeId {backendNodeId}.");
			}
			var listenersJson = await _session.CallDevToolsProtocolMethodAsync(
				"DOMDebugger.getEventListeners",
				JsonSerializer.Serialize(
					new { objectId, depth = -1, pierce = true },
					ProtocolJsonOptions),
				cancellationToken).ConfigureAwait(false);
			using var listenersResponse = JsonDocument.Parse(listenersJson);
			result[backendNodeId] = listenersResponse.RootElement
				.GetProperty("listeners")
				.EnumerateArray()
				.Select(ParseEventListener)
				.ToArray();
			TraceProgress(
				"EventListeners",
				index + 1,
				backendNodeIds.Count,
				treeTotalElements,
				maximumHierarchyLevel,
				requests[backendNodeId]);
		}
		return result.ToFrozenDictionary();
	}

	private static void TraceProgress(
		string operation,
		int processedElements,
		int phaseTotalElements,
		int treeTotalElements,
		int maximumHierarchyLevel,
		WebRuntimeDomPropertyRequest request)
	{
		if (!RuntimeOutput.Enabled)
			return;
		var progress = new WebRuntimeDomEvidenceProgress(
			operation,
			processedElements,
			phaseTotalElements,
			treeTotalElements,
			request.HierarchyLevel,
			maximumHierarchyLevel,
			request.NodeId,
			request.BackendNodeId,
			request.DocumentScope,
			request.XPath);
		RuntimeOutput.TracePoint(
			"log.pipeline",
			"pipeline",
			"log.html-reconstruction.cdp-evidence-progress",
			$"CDP Evidence [{operation}] "
				+ $"{processedElements}/{phaseTotalElements} "
				+ $"tree={treeTotalElements} "
				+ $"L{request.HierarchyLevel}/{maximumHierarchyLevel}",
			progress);
	}

	private async Task EnableDomainAsync(
		string method,
		CancellationToken cancellationToken)
	{
		await _session.CallDevToolsProtocolMethodAsync(
			method,
			"{}",
			cancellationToken).ConfigureAwait(false);
	}

	private static WebRuntimeDomIndexedProperty Read(
		WebRuntimeDomPropertyRequest request,
		WebRuntimeCdpNodeEvidence evidence,
		WebRuntimeDomTreeElement? treeElement,
		CdpMatchedStyleEvidence? matchedStyles,
		IReadOnlyDictionary<string, string>? computedStyles,
		IReadOnlyList<CdpEventListener>? listeners)
	{
		var identity = WebRuntimeDomPropertyIdentity.From(request);
		var name = request.PropertyName;
		if (request.OwnerKind == WebRuntimeDomOwnerKind.Event)
			return ReadEvent(identity, request, evidence, listeners ?? []);
		if (name.StartsWith("style.", StringComparison.Ordinal))
		{
			var cssName = ToCssName(name[6..]);
			if (request.Slot == WebRuntimeDomDataSlot.Runtime)
			{
				var styles = computedStyles ?? evidence.ComputedStyles;
				return styles.TryGetValue(cssName, out var value)
					&& value.Length != 0
					? Captured(
						identity,
						value,
						computedStyles is null
							? "CDP DOMSnapshot computed style."
							: "CDP CSS.getComputedStyleForNode value.")
					: Absent(identity, "CDP computed style is absent.");
			}
			var declaration = matchedStyles is not null
				&& matchedStyles.Declarations.TryGetValue(
					cssName,
					out var authored)
					? authored
					: null;
			if (declaration is null)
				return Absent(identity, "No authored CSS declaration matched.");
			if (request.Slot == WebRuntimeDomDataSlot.Link)
			{
				return Captured(
					identity,
					declaration.Identity,
					"CDP matched CSS rule link.",
					WebRuntimeDomValueSource.LinkedCalculation,
					WebRuntimeDomLinkKind.CssExpression,
					declaration.Identity);
			}
			return Captured(
				identity,
				declaration.Value,
				"CDP matched authored CSS declaration.");
		}
		if (name.StartsWith("rect.", StringComparison.Ordinal))
		{
			if (request.Slot == WebRuntimeDomDataSlot.Initialization)
				return Absent(identity, "Absolute runtime geometry is not design input.");
			if (request.Slot == WebRuntimeDomDataSlot.Link)
			{
				var link = $"cdp-layout:{request.BackendNodeId}:{name[5..]}";
				return Captured(
					identity,
					link,
					"CDP layout-tree geometry link.",
					WebRuntimeDomValueSource.LinkedCalculation,
					WebRuntimeDomLinkKind.LayoutExpression,
					link);
			}
			var value = evidence.ReadRectangleMember(name[5..]);
			return value.Length == 0
				? Absent(identity, "CDP layout rectangle member is absent.")
				: Captured(identity, value, "CDP DOMSnapshot layout bounds.");
		}
		if (name == "effect.animations")
		{
			var animationProperties = evidence.ComputedStyles
				.Where(static pair =>
					pair.Key.StartsWith("animation-", StringComparison.Ordinal)
					|| pair.Key.StartsWith("transition-", StringComparison.Ordinal))
				.ToDictionary(
					static pair => pair.Key,
					static pair => pair.Value,
					StringComparer.Ordinal);
			if (request.Slot == WebRuntimeDomDataSlot.Link)
			{
				var link = $"cdp-animation:{request.BackendNodeId}";
				return Captured(
					identity,
					link,
					"CDP animation style link.",
					WebRuntimeDomValueSource.LinkedCalculation,
					WebRuntimeDomLinkKind.CssExpression,
					link);
			}
			var animationName = animationProperties.GetValueOrDefault(
				"animation-name",
				"none");
			var animationDuration = animationProperties.GetValueOrDefault(
				"animation-duration",
				"0s");
			var transitionDuration = animationProperties.GetValueOrDefault(
				"transition-duration",
				"0s");
			return animationName.Equals("none", StringComparison.OrdinalIgnoreCase)
				&& IsZeroCssTimeList(animationDuration)
				&& IsZeroCssTimeList(transitionDuration)
					? Absent(identity, "CDP reports no active CSS animation.")
					: Captured(
						identity,
						JsonSerializer.Serialize(
							new
							{
								schema =
									"iwesun.webview2.cdp-animation-timeline/1.0",
								animations = new[]
								{
									new
									{
										type = "css-computed",
										backendNodeId = request.BackendNodeId,
										properties = animationProperties
									}
								}
							},
							ProtocolJsonOptions),
						"CDP computed animation and transition state.");
		}
		if (name == "effect.pseudoElements")
		{
			if (matchedStyles is null
				|| matchedStyles.PseudoElementsJson.Length == 0)
			{
				return Absent(identity, "CDP reports no matched pseudo-element.");
			}
			var evidenceValue = matchedStyles.PseudoElementsJson;
			if (request.Slot == WebRuntimeDomDataSlot.Link)
			{
				var link = $"cdp-pseudo:{request.NodeId}";
				return Captured(
					identity,
					link,
					"CDP pseudo-element rule link.",
					WebRuntimeDomValueSource.LinkedCalculation,
					WebRuntimeDomLinkKind.CssExpression,
					link);
			}
			return Captured(
				identity,
				evidenceValue,
				"CDP matched pseudo-element declarations.");
		}
		if (request.Slot == WebRuntimeDomDataSlot.Link)
		{
			return Absent(
				identity,
				"Scalar DOM values and attributes have no independent link.");
		}
		if (name == "content.ownText")
		{
			var value = treeElement?.OwnText ?? evidence.TextValue;
			return string.IsNullOrWhiteSpace(value)
				? Absent(identity, "Element has no direct text-node content.")
				: Captured(identity, value, "CDP DOM tree direct text content.");
		}
		if (name == "content.textContent")
		{
			var value = treeElement?.TextContent ?? evidence.TextValue;
			return string.IsNullOrWhiteSpace(value)
				? Absent(identity, "Element has no text content.")
				: Captured(identity, value, "CDP DOM tree text content.");
		}
		if (name == "content.namespaceUri")
		{
			var value = evidence.NodeName.StartsWith("#", StringComparison.Ordinal)
				? string.Empty
				: request.Specialization == WebRuntimeDomElementSpecialization.Svg
					? "http://www.w3.org/2000/svg"
					: "http://www.w3.org/1999/xhtml";
			return Captured(identity, value, "CDP node namespace classification.");
		}
		if (name is "content.value" or "state.value")
		{
			if (request.Slot == WebRuntimeDomDataSlot.Initialization)
				return ReadAttribute(identity, evidence, "value");
			return evidence.InputValue.Length == 0
				? Absent(identity, "CDP input value is absent.")
				: Captured(identity, evidence.InputValue, "CDP DOMSnapshot input value.");
		}
		if (name is "state.checked" or "content.checked")
		{
			if (!request.TagName.Equals("input", StringComparison.OrdinalIgnoreCase))
				return Absent(identity, "Checked state is not defined for this HTML element type.");
			return request.Slot == WebRuntimeDomDataSlot.Initialization
				? ReadAttribute(identity, evidence, "checked")
				: Captured(
					identity,
					evidence.InputChecked ? "true" : "false",
					"CDP DOMSnapshot checked state.");
		}
		if (name is "state.selected" or "content.selected")
		{
			if (!request.TagName.Equals("option", StringComparison.OrdinalIgnoreCase))
				return Absent(identity, "Selected state is not defined for this HTML element type.");
			return request.Slot == WebRuntimeDomDataSlot.Initialization
				? ReadAttribute(identity, evidence, "selected")
				: Captured(
					identity,
					evidence.OptionSelected ? "true" : "false",
					"CDP DOMSnapshot option selection.");
		}
		if (name.StartsWith("resource.", StringComparison.Ordinal))
		{
			if (request.Slot == WebRuntimeDomDataSlot.Link)
				return Absent(identity, "The CDP resource has no separate binding link.");
			var tag = request.TagName.ToLowerInvariant();
			var attributeName = name switch
			{
				"resource.imageSourceUrl"
					when tag is "img" or "input" => "src",
				"resource.mediaSourceUrl"
					when tag is "audio" or "video" or "source" or "track" =>
						"src",
				"resource.embeddedSourceUrl"
					when tag == "object" => "data",
				"resource.embeddedSourceUrl"
					when tag is "iframe" or "embed" => "src",
				"resource.posterSourceUrl" when tag == "video" => "poster",
				"resource.references"
					when tag is "img" or "source" => "srcset",
				_ => string.Empty
			};
			if (name == "resource.canvasCommandStream")
			{
				return Absent(
					identity,
					tag == "canvas"
						? "CDP DOMSnapshot has no recorded canvas command stream."
						: "Canvas command streams do not apply to this element.");
			}
			if (attributeName.Length == 0)
				return Absent(identity, "This resource kind does not apply to the element.");
			var value = request.Slot == WebRuntimeDomDataSlot.Runtime
				&& name is "resource.imageSourceUrl" or "resource.mediaSourceUrl"
				? evidence.CurrentSourceUrl
				: string.Empty;
			if (value.Length == 0)
				evidence.Attributes.TryGetValue(attributeName, out value);
			return string.IsNullOrEmpty(value)
				? Absent(identity, "CDP resource URL is absent.")
				: Captured(identity, value, "CDP selected resource URL.");
		}
		if (request.OwnerKind is WebRuntimeDomOwnerKind.Attribute
			or WebRuntimeDomOwnerKind.ExtensionAttribute)
		{
			return ReadAttribute(identity, evidence, name);
		}
		var memberName = name.Contains('.', StringComparison.Ordinal)
			? name[(name.IndexOf('.', StringComparison.Ordinal) + 1)..]
			: name;
		if (evidence.Attributes.TryGetValue(memberName, out var memberValue))
			return Captured(identity, memberValue, "CDP DOM attribute/property state.");
		return Absent(identity, "CDP DOM property is absent.");
	}

	private static WebRuntimeDomIndexedProperty ReadEvent(
		WebRuntimeDomPropertyIdentity identity,
		WebRuntimeDomPropertyRequest request,
		WebRuntimeCdpNodeEvidence evidence,
		IReadOnlyList<CdpEventListener> listeners)
	{
		var eventName = NormalizeEventName(request.PropertyName);
		var matching = listeners.Where(listener =>
			listener.Type.Equals(eventName, StringComparison.OrdinalIgnoreCase))
			.ToArray();
		evidence.Attributes.TryGetValue("on" + eventName, out var inlineHandler);
		if (request.Slot == WebRuntimeDomDataSlot.Runtime)
		{
			return Absent(
				identity,
				"CDP acquisition interval contains no event invocation record.");
		}
		if (matching.Length == 0 && string.IsNullOrEmpty(inlineHandler))
			return Absent(identity, "CDP event registration is absent.");
		var value = JsonSerializer.Serialize(
			new
			{
				eventName,
				inlineHandler,
				listeners = matching
			},
			ProtocolJsonOptions);
		if (request.Slot == WebRuntimeDomDataSlot.Link)
		{
			var link = $"cdp-event:{request.BackendNodeId}:{eventName}";
			return Captured(
				identity,
				link,
				"CDP DOMDebugger event link.",
				WebRuntimeDomValueSource.LinkedCalculation,
				WebRuntimeDomLinkKind.CustomString,
				link);
		}
		return Captured(identity, value, "CDP DOMDebugger event registrations.");
	}

	private static WebRuntimeDomIndexedProperty ReadAttribute(
		WebRuntimeDomPropertyIdentity identity,
		WebRuntimeCdpNodeEvidence evidence,
		string name) =>
		evidence.Attributes.TryGetValue(name, out var value)
			? Captured(identity, value, "CDP DOMSnapshot attribute.")
			: Absent(identity, "CDP DOM attribute is absent.");

	private static IReadOnlyDictionary<string, CdpCssDeclaration>
		IndexDeclarations(
			JsonElement response,
			IReadOnlySet<string> requestedCssNames)
	{
		var result = new Dictionary<string, CdpCssDeclaration>(
			StringComparer.OrdinalIgnoreCase);
		if (response.TryGetProperty("inlineStyle", out var inline))
		{
			var styleIdentity = ReadStyleIdentity(inline);
			AddStyleDeclarations(
				inline,
				requestedCssNames,
				result,
				(cssName, value, important) =>
					$"inline:{styleIdentity}:{cssName}:{value}"
					+ (important ? ":!important" : string.Empty));
		}
		if (response.TryGetProperty("matchedCSSRules", out var rules))
		{
			foreach (var match in rules.EnumerateArray().Reverse())
			{
				if (!match.TryGetProperty("rule", out var rule)
					|| !rule.TryGetProperty("style", out var style))
				{
					continue;
				}
				var styleIdentity = ReadStyleIdentity(style);
				var selector = rule.TryGetProperty(
					"selectorList",
					out var selectorList)
					&& selectorList.TryGetProperty("text", out var text)
						? text.GetString() ?? string.Empty
						: string.Empty;
				var conditions = ReadRuleConditions(rule);
				AddStyleDeclarations(
					style,
					requestedCssNames,
					result,
					(cssName, value, important) =>
						$"rule:{styleIdentity}:{conditions}:{selector}:"
						+ $"{cssName}:{value}:"
						+ $"{ReadCssVariableDependencies(value)}"
						+ (important ? ":!important" : string.Empty));
			}
		}
		if (response.TryGetProperty("inherited", out var inherited))
		{
			foreach (var inheritedEntry in inherited.EnumerateArray())
			{
				var remaining = requestedCssNames
					.Where(name => !result.ContainsKey(name))
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				if (remaining.Count == 0)
					break;
				foreach (var pair in IndexDeclarations(
					inheritedEntry,
					remaining))
				{
					result.TryAdd(
						pair.Key,
						pair.Value with
						{
							Identity = "inherited:" + pair.Value.Identity
						});
				}
			}
		}
		return result.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
	}

	private static void AddStyleDeclarations(
		JsonElement style,
		IReadOnlySet<string> requestedCssNames,
		IDictionary<string, CdpCssDeclaration> result,
		Func<string, string, bool, string> createIdentity)
	{
		if (!style.TryGetProperty("cssProperties", out var properties))
			return;
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var property in properties.EnumerateArray().Reverse())
		{
			var name = property.TryGetProperty("name", out var nameElement)
				? nameElement.GetString()
				: null;
			if (string.IsNullOrWhiteSpace(name)
				|| !requestedCssNames.Contains(name)
				|| result.ContainsKey(name)
				|| !seen.Add(name))
			{
				continue;
			}
			var value = property.TryGetProperty("value", out var valueElement)
				? valueElement.GetString() ?? string.Empty
				: string.Empty;
			var important = property.TryGetProperty(
				"important",
				out var marker)
				&& marker.GetBoolean();
			const string importantSuffix = "!important";
			if (value.EndsWith(
				importantSuffix,
				StringComparison.OrdinalIgnoreCase))
			{
				value = value[..^importantSuffix.Length].TrimEnd();
				important = true;
			}
			result.Add(
				name,
				new(value, createIdentity(name, value, important)));
		}
	}

	private static CdpCssDeclaration? FindDeclaration(
		JsonElement response,
		string cssName)
	{
		if (response.TryGetProperty("inlineStyle", out var inline)
			&& TryReadStyle(
				inline,
				cssName,
				out var inlineValue,
				out var inlineImportant))
		{
			return new(
				inlineValue,
				$"inline:{ReadStyleIdentity(inline)}:{cssName}:{inlineValue}"
				+ (inlineImportant ? ":!important" : string.Empty));
		}
		if (response.TryGetProperty("matchedCSSRules", out var rules))
		{
			foreach (var match in rules.EnumerateArray().Reverse())
			{
				if (!match.TryGetProperty("rule", out var rule)
					|| !rule.TryGetProperty("style", out var style)
					|| !TryReadStyle(
						style,
						cssName,
						out var value,
						out var important))
					continue;
				var selector = rule.TryGetProperty("selectorList", out var selectorList)
					&& selectorList.TryGetProperty("text", out var text)
						? text.GetString() ?? string.Empty
						: string.Empty;
				var conditions = ReadRuleConditions(rule);
				var variables = ReadCssVariableDependencies(value);
				return new(
					value,
					$"rule:{ReadStyleIdentity(style)}:{conditions}:{selector}:"
					+ $"{cssName}:{value}:{variables}"
					+ (important ? ":!important" : string.Empty));
			}
		}
		if (response.TryGetProperty("inherited", out var inherited))
		{
			foreach (var inheritedEntry in inherited.EnumerateArray())
			{
				var found = FindDeclaration(inheritedEntry, cssName);
				if (found is not null)
					return found with { Identity = "inherited:" + found.Identity };
			}
		}
		return null;
	}

	private static bool TryReadStyle(
		JsonElement style,
		string cssName,
		out string value,
		out bool important)
	{
		value = string.Empty;
		important = false;
		if (!style.TryGetProperty("cssProperties", out var properties))
			return false;
		foreach (var property in properties.EnumerateArray().Reverse())
		{
			var name = property.TryGetProperty("name", out var nameElement)
				? nameElement.GetString()
				: null;
			if (!string.Equals(name, cssName, StringComparison.OrdinalIgnoreCase))
				continue;
			value = property.TryGetProperty("value", out var valueElement)
				? valueElement.GetString() ?? string.Empty
				: string.Empty;
			important = property.TryGetProperty("important", out var marker)
				&& marker.GetBoolean();
			const string importantSuffix = "!important";
			if (value.EndsWith(
				importantSuffix,
				StringComparison.OrdinalIgnoreCase))
			{
				value = value[..^importantSuffix.Length].TrimEnd();
				important = true;
			}
			return true;
		}
		return false;
	}

	private static string ReadStyleIdentity(JsonElement style)
	{
		var styleSheetId = style.TryGetProperty("styleSheetId", out var id)
			? id.GetString() ?? "inline"
			: "inline";
		if (!style.TryGetProperty("range", out var range))
			return styleSheetId;
		return $"{styleSheetId}:{ReadInt(range, "startLine")}:{ReadInt(range, "startColumn")}";
	}

	private static string ReadRuleConditions(JsonElement rule)
	{
		var values = new List<string>();
		foreach (var propertyName in new[]
		{
			"media",
			"containerQueries",
			"supports",
			"layers",
			"scopes"
		})
		{
			if (!rule.TryGetProperty(propertyName, out var conditions)
				|| conditions.ValueKind != JsonValueKind.Array)
				continue;
			foreach (var condition in conditions.EnumerateArray())
			{
				var text = condition.TryGetProperty("text", out var textElement)
					? textElement.GetString()
					: condition.TryGetProperty("name", out var nameElement)
						? nameElement.GetString()
						: null;
				if (!string.IsNullOrWhiteSpace(text))
					values.Add($"{propertyName}:{text}");
			}
		}
		return values.Count == 0 ? "unconditional" : string.Join("&", values);
	}

	private static string ReadCssVariableDependencies(string value)
	{
		var matches = System.Text.RegularExpressions.Regex.Matches(
			value,
			@"var\(\s*(--[\w-]+)",
			System.Text.RegularExpressions.RegexOptions.CultureInvariant);
		return matches.Count == 0
			? "variables:none"
			: "variables:" + string.Join(
				",",
				matches.Select(static match => match.Groups[1].Value)
					.Distinct(StringComparer.Ordinal));
	}

	private static bool IsZeroCssTimeList(string value) =>
		value.Split(',', StringSplitOptions.RemoveEmptyEntries)
			.Select(static item => item.Trim())
			.All(static item =>
				item.Equals("0s", StringComparison.OrdinalIgnoreCase)
				|| item.Equals("0ms", StringComparison.OrdinalIgnoreCase)
				|| item.Equals("0", StringComparison.OrdinalIgnoreCase));

	private static CdpEventListener ParseEventListener(JsonElement listener) =>
		new(
			ReadString(listener, "type"),
			ReadBoolean(listener, "useCapture"),
			ReadBoolean(listener, "passive"),
			ReadBoolean(listener, "once"),
			ReadString(listener, "scriptId"),
			ReadInt(listener, "lineNumber"),
			ReadInt(listener, "columnNumber"));

	private static void ValidateStrongIdentities(
		IReadOnlyList<WebRuntimeDomPropertyRequest> requests)
	{
		var invalid = requests.FirstOrDefault(static request =>
			request.NodeId <= 0 || request.BackendNodeId <= 0);
		if (invalid is not null)
		{
			throw new InvalidDataException(
				$"Formal CDP Fill requires immutable nodeId/backendNodeId; "
				+ $"{invalid.DocumentScope}::{invalid.XPath} has "
				+ $"{invalid.NodeId}/{invalid.BackendNodeId}.");
		}
	}

	private static string NormalizeEventName(string value) =>
		value.StartsWith("handler.on", StringComparison.Ordinal)
			? value["handler.on".Length..]
			: value.StartsWith("event.", StringComparison.Ordinal)
				? value["event.".Length..].Split('.')[0]
				: value.StartsWith("on", StringComparison.Ordinal)
					? value[2..]
					: value;

	private static string ToCssName(string value)
	{
		var buffer = new System.Text.StringBuilder(value.Length + 8);
		foreach (var character in value)
		{
			if (char.IsUpper(character))
				buffer.Append('-').Append(char.ToLowerInvariant(character));
			else
				buffer.Append(character);
		}
		return buffer.ToString();
	}

	private static WebRuntimeDomIndexedProperty Captured(
		WebRuntimeDomPropertyIdentity identity,
		string value,
		string description,
		WebRuntimeDomValueSource valueSource =
			WebRuntimeDomValueSource.DirectConstant,
		WebRuntimeDomLinkKind linkKind = WebRuntimeDomLinkKind.None,
		string linkIdentity = "") =>
		new(
			identity,
			WebRuntimeDomPropertyStatus.Captured,
			value,
			valueSource,
			linkKind,
			linkIdentity,
			description);

	private static WebRuntimeDomIndexedProperty Absent(
		WebRuntimeDomPropertyIdentity identity,
		string description) =>
		new(
			identity,
			WebRuntimeDomPropertyStatus.ConfirmedAbsent,
			string.Empty,
			WebRuntimeDomValueSource.Unspecified,
			WebRuntimeDomLinkKind.None,
			string.Empty,
			description);

	private static string ReadString(JsonElement owner, string name) =>
		owner.TryGetProperty(name, out var value)
			? value.GetString() ?? string.Empty
			: string.Empty;

	private static bool ReadBoolean(JsonElement owner, string name) =>
		owner.TryGetProperty(name, out var value) && value.GetBoolean();

	private static int ReadInt(JsonElement owner, string name) =>
		owner.TryGetProperty(name, out var value) ? value.GetInt32() : 0;
}

internal sealed record CdpCssDeclaration(string Value, string Identity);

internal sealed record CdpMatchedStyleEvidence(
	IReadOnlyDictionary<string, CdpCssDeclaration> Declarations,
	string PseudoElementsJson);

internal sealed record CdpEventListener(
	string Type,
	bool UseCapture,
	bool Passive,
	bool Once,
	string ScriptId,
	int LineNumber,
	int ColumnNumber);
