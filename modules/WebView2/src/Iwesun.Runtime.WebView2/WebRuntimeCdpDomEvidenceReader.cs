using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using Iwesun.Runtime.Diagnostics;

namespace Iwesun.Runtime.WebView2;

public sealed record WebRuntimeDomEvidenceProgress(
	int CaptureSequence,
	int RequestCount,
	int StyleRequestCount,
	int EventRequestCount,
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

public sealed record WebRuntimeCdpPlatformFontEvidence(
	int NodeId,
	string FamilyName,
	string PostScriptName,
	int GlyphCount,
	bool IsCustomFont);

/// <summary>
/// Formal DOM evidence reader backed exclusively by CDP domains. It captures
/// one immutable DOMSnapshot and performs CSS/event/runtime enrichments once
/// per distinct element, never once per reflected property.
/// </summary>
public sealed class WebRuntimeCdpDomEvidenceReader(
	IWebRuntimeDevToolsSession session,
	Func<WebRuntimeDomTreeSnapshot> currentTree) : IWebRuntimeDomEvidenceReader
{
	private static readonly FrozenSet<string> InheritedCssProperties =
		new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"accent-color", "border-collapse", "border-spacing", "caption-side",
			"clip-rule", "color", "color-interpolation", "color-rendering",
			"color-scheme", "cursor", "direction", "empty-cells", "fill",
			"fill-opacity", "fill-rule", "font", "font-family",
			"font-feature-settings", "font-kerning", "font-language-override",
			"font-optical-sizing", "font-palette", "font-size",
			"font-size-adjust", "font-stretch", "font-style", "font-synthesis",
			"font-synthesis-position", "font-synthesis-small-caps",
			"font-synthesis-style", "font-synthesis-weight", "font-variant",
			"font-variant-alternates", "font-variant-caps",
			"font-variant-east-asian", "font-variant-emoji",
			"font-variant-ligatures", "font-variant-numeric",
			"font-variant-position", "font-weight", "forced-color-adjust",
			"hyphens", "image-rendering", "letter-spacing", "line-break",
			"line-height", "list-style", "list-style-image",
			"list-style-position", "list-style-type", "marker", "marker-end",
			"marker-mid", "marker-start", "orphans", "paint-order",
			"pointer-events", "quotes", "ruby-align", "ruby-position",
			"shape-rendering", "stroke", "stroke-dasharray", "stroke-dashoffset",
			"stroke-linecap", "stroke-linejoin", "stroke-miterlimit",
			"stroke-opacity", "stroke-width", "tab-size", "text-align",
			"text-align-last", "text-anchor", "text-combine-upright",
			"text-indent", "text-justify", "text-orientation", "text-rendering",
			"text-shadow", "text-transform", "text-underline-position",
			"visibility", "white-space", "white-space-collapse", "widows",
			"word-break", "word-spacing", "word-wrap", "writing-mode"
		}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	// Chromium exposes matched styles per node. The owner-thread CDP adapter is
	// serialized, so each group is processed sequentially; queuing a whole group
	// only amplifies one stalled request into many timeouts. The group size is
	// retained solely as the progress/cancellation checkpoint interval while the
	// DOM snapshot and backend-node identities remain pinned.
	private const int PerNodeCdpRequestBatchSize = 64;
	// DOMSnapshot layout bounds are not final screen-space bounds for SVG geometry.
	// Read SVG box models through the structured DOM domain.  This remains pure CDP and
	// never executes page JavaScript through Runtime.callFunctionOn.
	private static bool EnableSvgRuntimeBoundingRectangleOverride => true;
	private static readonly JsonSerializerOptions ProtocolJsonOptions =
		new(JsonSerializerDefaults.Web)
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
		};
	private readonly IWebRuntimeDevToolsSession _session =
		session ?? throw new ArgumentNullException(nameof(session));
	private readonly Func<WebRuntimeDomTreeSnapshot> _currentTree =
		currentTree ?? throw new ArgumentNullException(nameof(currentTree));
	private readonly SemaphoreSlim _domainGate = new(1, 1);
	private readonly object _platformFontEvidenceGate = new();
	private readonly object _matchedStyleEvidenceGate = new();
	private readonly HashSet<string> _enabledDomains =
		new(StringComparer.Ordinal);
	private readonly Dictionary<int, WebRuntimeCdpPlatformFontEvidence>
		_platformFontsByNodeId = [];
	private readonly Dictionary<
		int,
		IReadOnlyList<WebRuntimeCdpPlatformFontEvidence>>
		_platformFontRunsByNodeId = [];
	private readonly Dictionary<int, CdpMatchedStyleRawEvidence>
		_matchedStyleEvidenceByNodeId = [];
	private long _platformFontEvidenceTreeRevision;
	private Uri? _platformFontEvidencePageUri;
	private long _matchedStyleEvidenceTreeRevision;
	private Uri? _matchedStyleEvidencePageUri;
	private int _captureSequence;
	private int _currentRequestCount;
	private int _currentStyleRequestCount;
	private int _currentEventRequestCount;

	public Uri CurrentPageUrl => _currentTree().PageUri;

	public WebRuntimeCdpEvidenceSnapshot? LastSnapshot { get; private set; }

	public IReadOnlyDictionary<int, WebRuntimeCdpPlatformFontEvidence>
		LastPlatformFonts { get; private set; } =
		FrozenDictionary<int, WebRuntimeCdpPlatformFontEvidence>.Empty;

	public IReadOnlyDictionary<int, IReadOnlyList<WebRuntimeCdpPlatformFontEvidence>>
		LastPlatformFontRuns { get; private set; } =
		FrozenDictionary<int, IReadOnlyList<WebRuntimeCdpPlatformFontEvidence>>.Empty;

	public event EventHandler<WebRuntimeDomEvidenceProgress>?
		ProgressChanged;

	/// <summary>
	/// Enables the CDP domains required by immutable DOM/CSS evidence before
	/// the owning context pauses virtual time and the JavaScript debugger.
	/// Domain lifecycle setup must never be attempted after Debugger.pause.
	/// </summary>
	public async ValueTask PrepareAsync(
		CancellationToken cancellationToken = default)
	{
		await EnableDomainAsync("DOM.enable", cancellationToken)
			.ConfigureAwait(false);
		await EnableDomainAsync("CSS.enable", cancellationToken)
			.ConfigureAwait(false);
	}

	public async ValueTask<IReadOnlyList<WebRuntimeDomIndexedProperty>>
		ReadDomPropertiesAsync(
			IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
			CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(requests);
		if (requests.Count == 0)
			return [];
		ValidateStrongIdentities(requests);
		Interlocked.Increment(ref _captureSequence);
		_currentRequestCount = requests.Count;
		_currentStyleRequestCount = requests.Count(static request =>
			request.PropertyName.StartsWith("style.", StringComparison.Ordinal));
		_currentEventRequestCount = requests.Count(static request =>
			request.OwnerKind == WebRuntimeDomOwnerKind.Event);
		var sourceTree = _currentTree();
		var treeTotalElements = requests
			.Select(static request =>
				(request.DocumentScope, request.XPath))
			.Distinct()
			.Count();
		var maximumHierarchyLevel = requests.Max(
			static request => request.HierarchyLevel);
		TraceProgress(
			"Capture.Start",
			0,
			1,
			treeTotalElements,
			maximumHierarchyLevel,
			requests[0]);
		var byBackendNodeId = requests
			.Where(static request => request.BackendNodeId > 0)
			.GroupBy(static request => request.BackendNodeId)
			.ToDictionary(
				static group => group.Key,
				static group => group.First());
		var computedStyleNames = requests
			.Where(static request =>
				request.PropertyName.StartsWith("style.", StringComparison.Ordinal)
				&& !request.PropertyName.Equals(
					"style.scrollbarInlineSize",
					StringComparison.Ordinal))
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
		// DOMSnapshot.captureSnapshot rejects CSS custom-property names in its
		// computedStyles parameter on current WebView2/Chromium builds. Keep the
		// complete requested set for fallback detection, but ask DOMSnapshot only
		// for standard CSS properties. Missing custom properties are then read in
		// one CSS.getComputedStyleForNode call per affected node below.
		var snapshotComputedStyleNames = computedStyleNames
			.Where(static name => !name.StartsWith("--", StringComparison.Ordinal))
			.ToArray();
		if (_currentStyleRequestCount > 0)
		{
			// Enable the CSS agent before the large immutable DOMSnapshot call.
			// Initializing it afterwards can stall Chromium while the snapshot's
			// style tables are still being released. Domain enablement is a
			// session lifecycle operation and must occur exactly once.
			TraceProgress("Domains.Start", 0, 2, treeTotalElements,
				maximumHierarchyLevel, requests[0]);
			await EnableDomainAsync("DOM.enable", cancellationToken)
				.ConfigureAwait(false);
			TraceProgress("Domains.DOM", 1, 2, treeTotalElements,
				maximumHierarchyLevel, requests[0]);
			await EnableDomainAsync("CSS.enable", cancellationToken)
				.ConfigureAwait(false);
			TraceProgress("Domains.Completed", 2, 2, treeTotalElements,
				maximumHierarchyLevel, requests[0]);
		}
		TraceProgress(
			"LayoutMetrics.Start",
			0,
			1,
			treeTotalElements,
			maximumHierarchyLevel,
			requests[0]);
		var layoutMetricsJson = await _session.CallDevToolsProtocolMethodAsync(
			"Page.getLayoutMetrics",
			"{}",
			cancellationToken).ConfigureAwait(false);
		TraceProgress(
			"LayoutMetrics.Completed",
			1,
			1,
			treeTotalElements,
			maximumHierarchyLevel,
			requests[0]);
		var geometryScale = ReadCssGeometryScale(layoutMetricsJson);
		// Request only snapshot fields consumed by the immutable evidence model.
		// Paint order, blended backgrounds, and text-color opacity trigger an
		// expensive full-page paint analysis and are not part of any DOM slot.
		TraceProgress(
			"DomSnapshot.Start",
			0,
			1,
			treeTotalElements,
			maximumHierarchyLevel,
			requests[0]);
		var snapshotJson = await _session.CallDevToolsProtocolMethodAsync(
			"DOMSnapshot.captureSnapshot",
			JsonSerializer.Serialize(
				new
				{
					computedStyles = snapshotComputedStyleNames,
					includeDOMRects = true
				},
				ProtocolJsonOptions),
			cancellationToken).ConfigureAwait(false);
		TraceProgress(
			"DomSnapshot.Completed",
			1,
			1,
			treeTotalElements,
			maximumHierarchyLevel,
			requests[0]);
		var snapshot = WebRuntimeCdpEvidenceSnapshot.Parse(
			sourceTree.PageUri,
			snapshotJson,
			snapshotComputedStyleNames,
			geometryScale);
		LastSnapshot = snapshot;
		TraceProgress(
			"DomSnapshot.Parsed",
			1,
			1,
			treeTotalElements,
			maximumHierarchyLevel,
			requests[0]);
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
				&& !request.PropertyName.Equals(
					"style.scrollbarInlineSize",
					StringComparison.Ordinal)
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
		var platformFontRequests = requests
			.Where(static request =>
				request.PropertyName.StartsWith(
					"text.platformFont",
					StringComparison.Ordinal)
				&& request.Slot == WebRuntimeDomDataSlot.Runtime)
			.Select(static request => request.NodeId)
			.Where(static nodeId => nodeId > 0)
			.Distinct()
			.ToArray();
		var matchedStyles = await ReadMatchedStylesAsync(
			styleRequests,
			byNodeId,
			authoredStyleNamesByNode,
			sourceTree,
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
		var boxModelRequests = requests
			.Where(static request =>
				request.PropertyName.Equals(
						"style.scrollbarInlineSize",
						StringComparison.Ordinal)
					&& request.Slot == WebRuntimeDomDataSlot.Runtime)
			.Select(static request => request.NodeId)
			.Where(static nodeId => nodeId > 0)
			.Distinct()
			.Where(nodeId =>
			{
				var request = byNodeId[nodeId];
				if (!snapshot.TryGetNode(request.BackendNodeId, out var evidence)
					|| !double.TryParse(
						evidence.ReadRectangleMember("width"),
						System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture,
						out var width)
					|| width <= 0)
				{
					return false;
				}
				var styles = computedStyles.GetValueOrDefault(nodeId)
					?? evidence.ComputedStyles;
				if (styles.TryGetValue("scrollbar-gutter", out var gutter)
					&& !gutter.Equals("auto", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
				if (!styles.TryGetValue("overflow-y", out var overflowY))
					return false;
				if (overflowY.Equals("scroll", StringComparison.OrdinalIgnoreCase)
					|| overflowY.Equals("overlay", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
				return overflowY.Equals("auto", StringComparison.OrdinalIgnoreCase)
					&& evidence.ScrollRect.Count >= 4
					&& evidence.ClientRect.Count >= 4
					&& evidence.ScrollRect[3] > evidence.ClientRect[3] + 0.01;
			})
			.ToArray();
		var boundingRectangleRequests = EnableSvgRuntimeBoundingRectangleOverride
			? requests
			.Where(static request =>
				request.Specialization == WebRuntimeDomElementSpecialization.Svg
				&& request.PropertyName.StartsWith("rect.", StringComparison.Ordinal)
				&& request.Slot == WebRuntimeDomDataSlot.Runtime)
			.Select(static request => request.NodeId)
			.Where(static nodeId => nodeId > 0)
			.Distinct()
			.Where(nodeId =>
				snapshot.TryGetNode(byNodeId[nodeId].BackendNodeId, out var evidence)
				&& double.TryParse(
					evidence.ReadRectangleMember("width"),
					System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture,
					out var width)
				&& width > 0
				&& double.TryParse(
					evidence.ReadRectangleMember("height"),
					System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture,
					out var height)
				&& height > 0)
			.ToArray()
			: [];
		var platformFonts = await ReadPlatformFontsAsync(
			platformFontRequests,
			byNodeId,
			sourceTree,
			treeTotalElements,
			maximumHierarchyLevel,
			cancellationToken).ConfigureAwait(false);
		var boxModels = await ReadBoxModelsAsync(
			boxModelRequests,
			byNodeId,
			cancellationToken).ConfigureAwait(false);
		var boundingRectangles = await ReadBoundingRectanglesAsync(
			boundingRectangleRequests,
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
			if (index % 10_000 == 0)
			{
				TraceProgress(
					"MaterializeProperties",
					index,
					requests.Count,
					treeTotalElements,
					maximumHierarchyLevel,
					requests[index]);
			}
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
				platformFonts.GetValueOrDefault(request.NodeId),
				boxModels.GetValueOrDefault(request.NodeId),
				boundingRectangles.GetValueOrDefault(request.NodeId),
				eventListeners.GetValueOrDefault(request.BackendNodeId));
		}
		TraceProgress(
			"MaterializeProperties",
			requests.Count,
			requests.Count,
			treeTotalElements,
			maximumHierarchyLevel,
			requests[^1]);
		EnsureTreeRevision(sourceTree);
		return results;
	}

	private async Task<IReadOnlyDictionary<int, WebRuntimeCdpPlatformFontEvidence>>
		ReadPlatformFontsAsync(
			IReadOnlyList<int> nodeIds,
			IReadOnlyDictionary<int, WebRuntimeDomPropertyRequest> requests,
			WebRuntimeDomTreeSnapshot sourceTree,
			int treeTotalElements,
			int maximumHierarchyLevel,
			CancellationToken cancellationToken)
	{
		if (nodeIds.Count == 0)
		{
			PublishPlatformFontEvidence(
				sourceTree,
				FrozenDictionary<int, WebRuntimeCdpPlatformFontEvidence>.Empty,
				FrozenDictionary<
					int,
					IReadOnlyList<WebRuntimeCdpPlatformFontEvidence>>.Empty);
			return FrozenDictionary<int, WebRuntimeCdpPlatformFontEvidence>.Empty;
		}
		await EnableDomainAsync("DOM.enable", cancellationToken)
			.ConfigureAwait(false);
		await EnableDomainAsync("CSS.enable", cancellationToken)
			.ConfigureAwait(false);
		var result = new Dictionary<int, WebRuntimeCdpPlatformFontEvidence>();
		var runs = new Dictionary<
			int,
			IReadOnlyList<WebRuntimeCdpPlatformFontEvidence>>();
		for (var index = 0; index < nodeIds.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var originalNodeId = nodeIds[index];
			var json = await ReadAsync(originalNodeId, cancellationToken)
				.ConfigureAwait(false);
			using var response = JsonDocument.Parse(json);
			var fonts = response.RootElement.GetProperty("fonts")
				.EnumerateArray()
				.Select(font => new WebRuntimeCdpPlatformFontEvidence(
					originalNodeId,
					ReadString(font, "familyName"),
					ReadString(font, "postScriptName"),
					font.TryGetProperty("glyphCount", out var count)
						&& count.TryGetInt32(out var value) ? value : 0,
					font.TryGetProperty(
							"isCustomFont",
							out var isCustomFont)
						&& isCustomFont.ValueKind is JsonValueKind.True))
				.Where(static font => font.FamilyName.Length != 0)
				.ToArray();
			if (fonts.Length != 0)
			{
				result[originalNodeId] = fonts
					.OrderByDescending(static font => font.GlyphCount)
					.First();
				runs[originalNodeId] = fonts;
			}
			TraceProgress(
				"PlatformFonts",
				index + 1,
				nodeIds.Count,
				treeTotalElements,
				maximumHierarchyLevel,
				requests[originalNodeId]);
		}
		PublishPlatformFontEvidence(sourceTree, result, runs);
		return result.ToFrozenDictionary();

		async Task<string> ReadAsync(int originalNodeId, CancellationToken token)
		{
			try
			{
				return await CallAsync(originalNodeId, token).ConfigureAwait(false);
			}
			catch (Exception exception)
				when (exception is not OperationCanceledException)
			{
				var restored = await ResolveCurrentNodeIdsForRequestsAsync(
					[originalNodeId],
					requests,
					token).ConfigureAwait(false);
				try
				{
					return await CallAsync(restored[originalNodeId], token)
						.ConfigureAwait(false);
				}
				catch (Exception retryException)
					when (retryException is not OperationCanceledException)
				{
					throw new InvalidDataException(
						$"CDP platform-font lookup failed for "
							+ $"{requests[originalNodeId].DocumentScope}::"
							+ requests[originalNodeId].XPath,
						new AggregateException(exception, retryException));
				}
			}
		}

		Task<string> CallAsync(int nodeId, CancellationToken token) =>
			_session.CallDevToolsProtocolMethodAsync(
				"CSS.getPlatformFontsForNode",
				JsonSerializer.Serialize(new { nodeId }, ProtocolJsonOptions),
				token);
	}

	private void PublishPlatformFontEvidence(
		WebRuntimeDomTreeSnapshot sourceTree,
		IReadOnlyDictionary<int, WebRuntimeCdpPlatformFontEvidence> fonts,
		IReadOnlyDictionary<
			int,
			IReadOnlyList<WebRuntimeCdpPlatformFontEvidence>> runs)
	{
		lock (_platformFontEvidenceGate)
		{
			if (_platformFontEvidenceTreeRevision != sourceTree.Revision
				|| !Equals(_platformFontEvidencePageUri, sourceTree.PageUri))
			{
				_platformFontsByNodeId.Clear();
				_platformFontRunsByNodeId.Clear();
				_platformFontEvidenceTreeRevision = sourceTree.Revision;
				_platformFontEvidencePageUri = sourceTree.PageUri;
			}
			foreach (var (nodeId, font) in fonts)
				_platformFontsByNodeId[nodeId] = font;
			foreach (var (nodeId, fontRuns) in runs)
				_platformFontRunsByNodeId[nodeId] = fontRuns;
			LastPlatformFonts = _platformFontsByNodeId.ToFrozenDictionary();
			LastPlatformFontRuns = _platformFontRunsByNodeId.ToFrozenDictionary();
		}
	}

	private async Task<IReadOnlyDictionary<int, CdpBoxModelEvidence>>
		ReadBoxModelsAsync(
			IReadOnlyList<int> nodeIds,
			IReadOnlyDictionary<int, WebRuntimeDomPropertyRequest> requests,
			CancellationToken cancellationToken)
	{
		if (nodeIds.Count == 0)
			return FrozenDictionary<int, CdpBoxModelEvidence>.Empty;
		await EnableDomainAsync("DOM.enable", cancellationToken)
			.ConfigureAwait(false);
		var result = new Dictionary<int, CdpBoxModelEvidence>();
		foreach (var originalNodeId in nodeIds)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var json = await ReadAsync(originalNodeId, cancellationToken)
				.ConfigureAwait(false);
			using var response = JsonDocument.Parse(json);
			var model = response.RootElement.GetProperty("model");
			var border = ReadQuadBounds(model.GetProperty("border"));
			var padding = ReadQuadBounds(model.GetProperty("padding"));
			var content = ReadQuadBounds(model.GetProperty("content"));
			result[originalNodeId] = new(
				border.X,
				border.Y,
				border.Width,
				border.Height,
				padding.Width,
				content.Width);
		}
		return result.ToFrozenDictionary();

		async Task<string> ReadAsync(int originalNodeId, CancellationToken token)
		{
			try
			{
				return await CallAsync(originalNodeId, token).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				var restored = await ResolveCurrentNodeIdsForRequestsAsync(
					[originalNodeId],
					requests,
					token).ConfigureAwait(false);
				try
				{
					return await CallAsync(restored[originalNodeId], token)
						.ConfigureAwait(false);
				}
				catch (Exception retryException)
					when (retryException is not OperationCanceledException)
				{
					throw new InvalidDataException(
						$"CDP box-model lookup failed for "
							+ $"{requests[originalNodeId].DocumentScope}::"
							+ requests[originalNodeId].XPath,
						new AggregateException(exception, retryException));
				}
			}
		}

		Task<string> CallAsync(int nodeId, CancellationToken token) =>
			_session.CallDevToolsProtocolMethodAsync(
				"DOM.getBoxModel",
				JsonSerializer.Serialize(new { nodeId }, ProtocolJsonOptions),
				token);

		static (double X, double Y, double Width, double Height) ReadQuadBounds(
			JsonElement quad)
		{
			var values = quad.EnumerateArray()
				.Select(static value => value.GetDouble())
				.ToArray();
			if (values.Length != 8)
				throw new InvalidDataException("CDP box-model quad must contain 8 coordinates.");
			var minimumX = Math.Min(
				Math.Min(values[0], values[2]),
				Math.Min(values[4], values[6]));
			var maximumX = Math.Max(
				Math.Max(values[0], values[2]),
				Math.Max(values[4], values[6]));
			var minimumY = Math.Min(
				Math.Min(values[1], values[3]),
				Math.Min(values[5], values[7]));
			var maximumY = Math.Max(
				Math.Max(values[1], values[3]),
				Math.Max(values[5], values[7]));
			return (
				minimumX,
				minimumY,
				Math.Max(0, maximumX - minimumX),
				Math.Max(0, maximumY - minimumY));
		}
	}

	private async Task<IReadOnlyDictionary<int, CdpBoundingRectangleEvidence>>
		ReadBoundingRectanglesAsync(
			IReadOnlyList<int> nodeIds,
			IReadOnlyDictionary<int, WebRuntimeDomPropertyRequest> requests,
			int treeTotalElements,
			int maximumHierarchyLevel,
			CancellationToken cancellationToken)
	{
		if (nodeIds.Count == 0)
			return FrozenDictionary<int, CdpBoundingRectangleEvidence>.Empty;
		await EnableDomainAsync("DOM.enable", cancellationToken)
			.ConfigureAwait(false);
		var result = new Dictionary<int, CdpBoundingRectangleEvidence>();
		for (var index = 0; index < nodeIds.Count; index++)
		{
			var originalNodeId = nodeIds[index];
			cancellationToken.ThrowIfCancellationRequested();
			var request = requests[originalNodeId];
			TraceProgress(
				"BoundingRectangles",
				index,
				nodeIds.Count,
				treeTotalElements,
				maximumHierarchyLevel,
				request);
			var json = await ReadAsync(originalNodeId, cancellationToken)
				.ConfigureAwait(false);
			using var response = JsonDocument.Parse(json);
			var borderQuad = response.RootElement
				.GetProperty("model")
				.GetProperty("border");
			var coordinates = borderQuad
				.EnumerateArray()
				.Select(static coordinate => coordinate.GetDouble())
				.ToArray();
			if (coordinates.Length != 8)
				throw new InvalidDataException(
					"CDP DOM.getBoxModel returned an invalid SVG border quad.");
			var xCoordinates = coordinates.Where(
				static (_, coordinateIndex) => coordinateIndex % 2 == 0);
			var yCoordinates = coordinates.Where(
				static (_, coordinateIndex) => coordinateIndex % 2 != 0);
			var minimumX = xCoordinates.Min();
			var maximumX = xCoordinates.Max();
			var minimumY = yCoordinates.Min();
			var maximumY = yCoordinates.Max();
			result[originalNodeId] = new(
				minimumX,
				minimumY,
				Math.Max(0, maximumX - minimumX),
				Math.Max(0, maximumY - minimumY));
		}
		TraceProgress(
			"BoundingRectangles",
			nodeIds.Count,
			nodeIds.Count,
			treeTotalElements,
			maximumHierarchyLevel,
			requests[nodeIds[^1]]);
		return result.ToFrozenDictionary();

		async Task<string> ReadAsync(int originalNodeId, CancellationToken token)
		{
			try
			{
				return await CallAsync(originalNodeId, token).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				var restored = await ResolveCurrentNodeIdsForRequestsAsync(
					[originalNodeId],
					requests,
					token).ConfigureAwait(false);
				try
				{
					return await CallAsync(restored[originalNodeId], token)
						.ConfigureAwait(false);
				}
				catch (Exception retryException)
					when (retryException is not OperationCanceledException)
				{
					throw new InvalidDataException(
						$"CDP runtime bounding-rectangle lookup failed for "
							+ $"{requests[originalNodeId].DocumentScope}::"
							+ requests[originalNodeId].XPath,
						new AggregateException(exception, retryException));
				}
			}
		}

		Task<string> CallAsync(int nodeId, CancellationToken token) =>
			_session.CallDevToolsProtocolMethodAsync(
				"DOM.getBoxModel",
				JsonSerializer.Serialize(new { nodeId }, ProtocolJsonOptions),
				token);
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

	private void EnsureMatchedStyleEvidenceRevision(
		WebRuntimeDomTreeSnapshot sourceTree)
	{
		lock (_matchedStyleEvidenceGate)
		{
			if (_matchedStyleEvidenceTreeRevision == sourceTree.Revision
				&& Equals(_matchedStyleEvidencePageUri, sourceTree.PageUri))
			{
				return;
			}
			_matchedStyleEvidenceByNodeId.Clear();
			_matchedStyleEvidenceTreeRevision = sourceTree.Revision;
			_matchedStyleEvidencePageUri = sourceTree.PageUri;
		}
	}

	private bool TryReadMatchedStyleEvidence(
		int nodeId,
		out CdpMatchedStyleRawEvidence evidence)
	{
		lock (_matchedStyleEvidenceGate)
			return _matchedStyleEvidenceByNodeId.TryGetValue(nodeId, out evidence!);
	}

	private void StoreMatchedStyleEvidence(
		int nodeId,
		CdpMatchedStyleRawEvidence evidence)
	{
		lock (_matchedStyleEvidenceGate)
			_matchedStyleEvidenceByNodeId[nodeId] = evidence;
	}

	private async Task<IReadOnlyDictionary<
		int,
		CdpMatchedStyleEvidence>>
		ReadMatchedStylesAsync(
			IReadOnlyList<int> nodeIds,
			IReadOnlyDictionary<int, WebRuntimeDomPropertyRequest> requests,
			IReadOnlyDictionary<int, HashSet<string>> requestedCssNames,
			WebRuntimeDomTreeSnapshot sourceTree,
			int treeTotalElements,
			int maximumHierarchyLevel,
			CancellationToken cancellationToken)
	{
		EnsureMatchedStyleEvidenceRevision(sourceTree);
		if (nodeIds.Count == 0)
		{
			return FrozenDictionary<
				int,
				CdpMatchedStyleEvidence>.Empty;
		}
		TraceProgress(
			"MatchedStyles.Start",
			0,
			nodeIds.Count,
			treeTotalElements,
			maximumHierarchyLevel,
			requests[nodeIds[0]]);
		await EnableDomainAsync("DOM.enable", cancellationToken)
			.ConfigureAwait(false);
		await EnableDomainAsync("CSS.enable", cancellationToken)
			.ConfigureAwait(false);
		var result = new Dictionary<
			int,
			CdpMatchedStyleEvidence>();
		for (var batchStart = 0;
			batchStart < nodeIds.Count;
			batchStart += PerNodeCdpRequestBatchSize)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var batchLength = Math.Min(
				PerNodeCdpRequestBatchSize,
				nodeIds.Count - batchStart);
			for (var offset = 0; offset < batchLength; offset++)
			{
				var nodeId = nodeIds[batchStart + offset];
				if (!TryReadMatchedStyleEvidence(nodeId, out var rawEvidence))
				{
					var capture = await CaptureMatchedStyleAsync(
						nodeId,
						cancellationToken).ConfigureAwait(false);
					using var capturedResponse = JsonDocument.Parse(capture.Json);
					var pseudoRulesJson = capturedResponse.RootElement.TryGetProperty(
						"pseudoElements",
						out var capturedPseudoElements)
						&& capturedPseudoElements.GetArrayLength() != 0
							? capturedPseudoElements.GetRawText()
							: string.Empty;
					var pseudoComputedJson = HasRenderablePseudoDeclaration(
							capturedPseudoElements)
						? await ReadPseudoComputedStylesAsync(
							capture.QueryNodeId,
							capturedPseudoElements,
							cancellationToken).ConfigureAwait(false)
						: string.Empty;
					rawEvidence = new(
						capture.Json,
						pseudoRulesJson,
						pseudoComputedJson);
					StoreMatchedStyleEvidence(nodeId, rawEvidence);
				}
				using var response = JsonDocument.Parse(rawEvidence.ResponseJson);
				result[nodeId] = new(
					IndexDeclarations(
						response.RootElement,
						requestedCssNames[nodeId]),
					rawEvidence.PseudoRulesJson,
					rawEvidence.PseudoComputedJson);
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

		async Task<(int NodeId, int QueryNodeId, string Json)>
			CaptureMatchedStyleAsync(
			int originalNodeId,
			CancellationToken token)
		{
			try
			{
				return (
					originalNodeId,
					originalNodeId,
					await ReadAsync(originalNodeId, token).ConfigureAwait(false));
			}
			catch (Exception exception)
				when (exception is not OperationCanceledException)
			{
				var request = requests[originalNodeId];
				var restored = await ResolveCurrentNodeIdsForRequestsAsync(
					[originalNodeId],
					requests,
					token).ConfigureAwait(false);
				var currentNodeId = restored[originalNodeId];
				try
				{
					return (
						originalNodeId,
						currentNodeId,
						await ReadAsync(currentNodeId, token).ConfigureAwait(false));
				}
				catch (Exception retryException)
					when (retryException is not OperationCanceledException)
				{
					throw new InvalidDataException(
						$"CDP matched-style lookup failed for source nodeId "
							+ $"{originalNodeId}, restored nodeId {currentNodeId}, "
							+ $"backendNodeId {request.BackendNodeId}, "
							+ $"{request.DocumentScope}::{request.XPath}, "
							+ $"tag={request.TagName}.",
						new AggregateException(exception, retryException));
				}
			}

			Task<string> ReadAsync(int nodeId, CancellationToken ct) =>
				_session.CallDevToolsProtocolMethodAsync(
					"CSS.getMatchedStylesForNode",
					JsonSerializer.Serialize(new { nodeId }, ProtocolJsonOptions),
					ct);
		}
	}

	private static bool HasRenderablePseudoDeclaration(
		JsonElement pseudoElements)
	{
		if (pseudoElements.ValueKind != JsonValueKind.Array)
			return false;
		foreach (var pseudo in pseudoElements.EnumerateArray())
		{
			if (PseudoHasRenderableDeclaration(pseudo))
				return true;
		}
		return false;
	}

	private static bool PseudoHasRenderableDeclaration(JsonElement pseudo)
	{
			if (!pseudo.TryGetProperty("matches", out var matches)
				|| matches.ValueKind != JsonValueKind.Array)
			{
				return false;
			}
			foreach (var match in matches.EnumerateArray())
			{
				if (!match.TryGetProperty("rule", out var rule)
					|| !rule.TryGetProperty("style", out var style)
					|| !style.TryGetProperty("cssProperties", out var properties)
					|| properties.ValueKind != JsonValueKind.Array)
				{
					continue;
				}
				foreach (var property in properties.EnumerateArray())
				{
					var name = ReadString(property, "name");
					var value = ReadString(property, "value").Trim();
					if (name.Equals("content", StringComparison.OrdinalIgnoreCase)
						&& value.Length != 0
						&& value is not "none" and not "normal")
					{
						return true;
					}
				}
			}
		return false;
	}

	private async Task<string> ReadPseudoComputedStylesAsync(
		int ownerNodeId,
		JsonElement pseudoRules,
		CancellationToken cancellationToken)
	{
		var renderableTypes = pseudoRules.EnumerateArray()
			.Where(PseudoHasRenderableDeclaration)
			.Select(static pseudo => ReadString(pseudo, "pseudoType"))
			.Where(static value => value.Length != 0)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);
		if (renderableTypes.Count == 0)
			return string.Empty;
		var describedJson = await _session.CallDevToolsProtocolMethodAsync(
			"DOM.describeNode",
			JsonSerializer.Serialize(
				new { nodeId = ownerNodeId, depth = 1, pierce = true },
				ProtocolJsonOptions),
			cancellationToken).ConfigureAwait(false);
		using var described = JsonDocument.Parse(describedJson);
		if (!described.RootElement.TryGetProperty("node", out var node)
			|| !node.TryGetProperty("pseudoElements", out var pseudoNodes)
			|| pseudoNodes.ValueKind != JsonValueKind.Array)
		{
			return string.Empty;
		}
		var values = new List<object>();
		foreach (var pseudoNode in pseudoNodes.EnumerateArray())
		{
			var pseudoType = ReadString(pseudoNode, "pseudoType");
			if (!renderableTypes.Contains(pseudoType))
				continue;
			var pseudoNodeId = ReadInt(pseudoNode, "nodeId");
			if (pseudoNodeId <= 0)
				continue;
			var computedJson = await _session.CallDevToolsProtocolMethodAsync(
				"CSS.getComputedStyleForNode",
				JsonSerializer.Serialize(
					new { nodeId = pseudoNodeId },
					ProtocolJsonOptions),
				cancellationToken).ConfigureAwait(false);
			using var computed = JsonDocument.Parse(computedJson);
			var styles = computed.RootElement.GetProperty("computedStyle")
				.EnumerateArray()
				.Select(static property => new
				{
					name = ReadString(property, "name"),
					value = ReadString(property, "value")
				})
				.Where(static property => property.name.Length != 0)
				.ToDictionary(
					static property => property.name,
					static property => property.value,
					StringComparer.OrdinalIgnoreCase);
			values.Add(new
			{
				pseudoType,
				pseudoIdentifier = ReadString(pseudoNode, "pseudoIdentifier"),
				nodeId = pseudoNodeId,
				backendNodeId = ReadInt(pseudoNode, "backendNodeId"),
				computedStyles = styles
			});
		}
		return values.Count == 0
			? string.Empty
			: JsonSerializer.Serialize(
				new
				{
					schema = "iwesun.webview2.cdp-pseudo-computed/1.0",
					pseudoElements = values
				},
				ProtocolJsonOptions);
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
			batchStart += PerNodeCdpRequestBatchSize)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var batchLength = Math.Min(
				PerNodeCdpRequestBatchSize,
				nodeIds.Count - batchStart);
			for (var offset = 0; offset < batchLength; offset++)
			{
				var nodeId = nodeIds[batchStart + offset];
				var (_, json) = await ReadComputedStyleAsync(
					nodeId,
					cancellationToken).ConfigureAwait(false);
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
			int originalNodeId,
			CancellationToken token)
		{
			try
			{
				return (
					originalNodeId,
					await ReadAsync(originalNodeId, token).ConfigureAwait(false));
			}
			catch (Exception exception)
				when (exception is not OperationCanceledException)
			{
				var restored = await ResolveCurrentNodeIdsForRequestsAsync(
					[originalNodeId],
					requests,
					token).ConfigureAwait(false);
				var currentNodeId = restored[originalNodeId];
				try
				{
					return (
						originalNodeId,
						await ReadAsync(currentNodeId, token).ConfigureAwait(false));
				}
				catch (Exception retryException)
					when (retryException is not OperationCanceledException)
				{
					var request = requests[originalNodeId];
					throw new InvalidDataException(
						$"CDP computed-style lookup failed for source nodeId "
							+ $"{originalNodeId}, restored nodeId {currentNodeId}, "
							+ $"backendNodeId {request.BackendNodeId}, "
							+ $"{request.DocumentScope}::{request.XPath}, "
							+ $"tag={request.TagName}.",
						new AggregateException(exception, retryException));
				}
			}

			Task<string> ReadAsync(int nodeId, CancellationToken ct) =>
				_session.CallDevToolsProtocolMethodAsync(
					"CSS.getComputedStyleForNode",
					JsonSerializer.Serialize(new { nodeId }, ProtocolJsonOptions),
					ct);
		}
	}

	private async Task<IReadOnlyDictionary<int, int>>
		ResolveCurrentNodeIdsForRequestsAsync(
			IReadOnlyList<int> sourceNodeIds,
			IReadOnlyDictionary<int, WebRuntimeDomPropertyRequest> requests,
			CancellationToken cancellationToken)
	{
		var backendNodeIds = sourceNodeIds
			.Select(nodeId => requests[nodeId].BackendNodeId)
			.ToArray();
		if (backendNodeIds.Any(static backendNodeId => backendNodeId <= 0))
		{
			throw new InvalidDataException(
				"A CSS evidence request has no stable backend-node identity.");
		}
		var currentByBackend = await ResolveCurrentNodeIdsAsync(
			backendNodeIds,
			cancellationToken).ConfigureAwait(false);
		var result = new Dictionary<int, int>();
		for (var index = 0; index < sourceNodeIds.Count; index++)
		{
			var backendNodeId = backendNodeIds[index];
			if (!currentByBackend.TryGetValue(backendNodeId, out var currentNodeId))
			{
				var request = requests[sourceNodeIds[index]];
				throw new InvalidDataException(
					$"CDP could not restore backendNodeId {backendNodeId} for "
						+ $"{request.DocumentScope}::{request.XPath}.");
			}
			result[sourceNodeIds[index]] = currentNodeId;
		}
		return result.ToFrozenDictionary();
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
		for (var batchStart = 0;
			batchStart < backendNodeIds.Count;
			batchStart += PerNodeCdpRequestBatchSize)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var batchLength = Math.Min(
				PerNodeCdpRequestBatchSize,
				backendNodeIds.Count - batchStart);
			for (var offset = 0; offset < batchLength; offset++)
			{
				var (backendNodeId, listeners) =
					await ReadEventListenersForNodeAsync(
					backendNodeIds[batchStart + offset],
					cancellationToken).ConfigureAwait(false);
				result[backendNodeId] = listeners;
				TraceProgress(
					"EventListeners",
					batchStart + offset + 1,
					backendNodeIds.Count,
					treeTotalElements,
					maximumHierarchyLevel,
					requests[backendNodeId]);
			}
		}
		return result.ToFrozenDictionary();

		async Task<(int BackendNodeId, IReadOnlyList<CdpEventListener> Listeners)>
			ReadEventListenersForNodeAsync(
				int backendNodeId,
				CancellationToken token)
		{
			var resolveJson = await _session.CallDevToolsProtocolMethodAsync(
				"DOM.resolveNode",
				JsonSerializer.Serialize(
					new { backendNodeId },
					ProtocolJsonOptions),
				token).ConfigureAwait(false);
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
					new { objectId, depth = 0, pierce = false },
					ProtocolJsonOptions),
				token).ConfigureAwait(false);
			using var listenersResponse = JsonDocument.Parse(listenersJson);
			var listeners = listenersResponse.RootElement
				.GetProperty("listeners")
				.EnumerateArray()
				.Select(ParseEventListener)
				.ToArray();
			return (backendNodeId, listeners);
		}
	}

	private void TraceProgress(
		string operation,
		int processedElements,
		int phaseTotalElements,
		int treeTotalElements,
		int maximumHierarchyLevel,
		WebRuntimeDomPropertyRequest request)
	{
		var progress = new WebRuntimeDomEvidenceProgress(
			Volatile.Read(ref _captureSequence),
			_currentRequestCount,
			_currentStyleRequestCount,
			_currentEventRequestCount,
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
		ProgressChanged?.Invoke(this, progress);
		if (!RuntimeOutput.Enabled)
			return;
		if (operation is not (
			"MatchedStyles"
			or "ComputedStyles"
			or "PlatformFonts"
			or "BoundingRectangles"
			or "EventListeners")
			|| (processedElements != phaseTotalElements
				&& processedElements % PerNodeCdpRequestBatchSize != 0))
		{
			return;
		}
		RuntimeOutput.TracePoint(
			"log.pipeline",
			"pipeline",
			"log.html-reconstruction.cdp-evidence-progress",
			$"CDP Evidence [{operation}] "
				+ $"capture={progress.CaptureSequence} "
				+ $"{processedElements}/{phaseTotalElements} "
				+ $"tree={treeTotalElements} "
				+ $"L{request.HierarchyLevel}/{maximumHierarchyLevel}",
			progress);
	}

	private async Task EnableDomainAsync(
		string method,
		CancellationToken cancellationToken)
	{
		await _domainGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (_enabledDomains.Contains(method))
				return;
			await _session.CallDevToolsProtocolMethodAsync(
				method,
				"{}",
				cancellationToken).ConfigureAwait(false);
			_enabledDomains.Add(method);
		}
		finally
		{
			_domainGate.Release();
		}
	}

	private static WebRuntimeDomIndexedProperty Read(
		WebRuntimeDomPropertyRequest request,
		WebRuntimeCdpNodeEvidence evidence,
		WebRuntimeDomTreeElement? treeElement,
		CdpMatchedStyleEvidence? matchedStyles,
		IReadOnlyDictionary<string, string>? computedStyles,
		WebRuntimeCdpPlatformFontEvidence? platformFont,
		CdpBoxModelEvidence? boxModel,
		CdpBoundingRectangleEvidence? boundingRectangle,
		IReadOnlyList<CdpEventListener>? listeners)
	{
		var identity = WebRuntimeDomPropertyIdentity.From(request);
		var name = request.PropertyName;
		if (name.Equals("style.scrollbarInlineSize", StringComparison.Ordinal))
		{
			if (request.Slot == WebRuntimeDomDataSlot.Initialization)
				return Absent(identity, "Scrollbar gutter size is a runtime CSS used value.");
			if (boxModel is null)
				return Absent(identity, "The element has no authored stable scrollbar gutter.");
			if (request.Slot == WebRuntimeDomDataSlot.Link)
			{
				var link = $"cdp-boxmodel:{request.BackendNodeId}:scrollbar-inline-size";
				return Captured(
					identity,
					link,
					"CDP scrollbar-gutter layout link.",
					WebRuntimeDomValueSource.LinkedCalculation,
					WebRuntimeDomLinkKind.LayoutExpression,
					link);
			}
			var borderLeft = ReadCssPixel(evidence.ComputedStyles, "border-left-width");
			var borderRight = ReadCssPixel(evidence.ComputedStyles, "border-right-width");
			var gutter = Math.Max(
				0,
				boxModel.BorderWidth - boxModel.ContentWidth
					- borderLeft - borderRight
					- ReadCssPixel(evidence.ComputedStyles, "padding-left")
					- ReadCssPixel(evidence.ComputedStyles, "padding-right"));
			return Captured(
				identity,
				gutter.ToString(System.Globalization.CultureInfo.InvariantCulture),
				"CDP DOM.getBoxModel stable scrollbar inline gutter.");
		}
		if (name.Equals("text.platformFontFamily", StringComparison.Ordinal))
		{
			if (request.Slot != WebRuntimeDomDataSlot.Runtime)
			{
				return Absent(
					identity,
					"Platform font is runtime evidence, not authored CSS.");
			}
			return platformFont is null
				? Absent(identity, "CDP reports no platform font for this node.")
				: Captured(
					identity,
					platformFont.FamilyName,
					"CDP CSS.getPlatformFontsForNode actual font family.");
		}
		if (name.Equals("text.platformFontWeight", StringComparison.Ordinal))
		{
			if (request.Slot != WebRuntimeDomDataSlot.Runtime)
			{
				return Absent(
					identity,
					"Platform font face weight is runtime evidence, not authored CSS.");
			}
			return platformFont is null
				? Absent(identity, "CDP reports no platform font for this node.")
				: Captured(
					identity,
					ResolvePlatformFontWeight(
						platformFont.PostScriptName).ToString(
							System.Globalization.CultureInfo.InvariantCulture),
					"CDP CSS.getPlatformFontsForNode actual font face weight.");
		}
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
			var rectangleMemberName = name[5..];
			var value = boundingRectangle is not null
				? rectangleMemberName switch
				{
					"x" => boundingRectangle.X.ToString(
						System.Globalization.CultureInfo.InvariantCulture),
					"y" => boundingRectangle.Y.ToString(
						System.Globalization.CultureInfo.InvariantCulture),
					"width" => boundingRectangle.Width.ToString(
						System.Globalization.CultureInfo.InvariantCulture),
					"height" => boundingRectangle.Height.ToString(
						System.Globalization.CultureInfo.InvariantCulture),
					_ => string.Empty
				}
				: evidence.ReadRectangleMember(rectangleMemberName);
			return value.Length == 0
				? Absent(identity, "CDP layout rectangle member is absent.")
				: Captured(
					identity,
					value,
					boundingRectangle is null
						? "CDP DOMSnapshot layout bounds."
						: "CDP fixed getBoundingClientRect runtime bounds.");
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
				|| matchedStyles.PseudoRulesJson.Length == 0)
			{
				return Absent(identity, "CDP reports no matched pseudo-element.");
			}
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
			if (request.Slot == WebRuntimeDomDataSlot.Runtime)
			{
				return matchedStyles.PseudoComputedJson.Length == 0
					? Absent(
						identity,
						"CDP reports no rendered pseudo-element computed style.")
					: Captured(
						identity,
						matchedStyles.PseudoComputedJson,
						"CDP rendered pseudo-element computed styles.");
			}
			return Captured(
				identity,
				matchedStyles.PseudoRulesJson,
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
		if (name == "state.disabled")
		{
			if (!IsDisabledStateElement(request.TagName))
			{
				return Absent(
					identity,
					"Disabled state is not defined for this HTML element type.");
			}
			if (request.Slot == WebRuntimeDomDataSlot.Initialization)
				return ReadAttribute(identity, evidence, "disabled");
			return Captured(
				identity,
				evidence.Attributes.ContainsKey("disabled") ? "true" : "false",
				"CDP DOMSnapshot reflected disabled state.");
		}
		if (name == "state.readOnly")
		{
			if (!request.TagName.Equals("input", StringComparison.OrdinalIgnoreCase)
				&& !request.TagName.Equals(
					"textarea",
					StringComparison.OrdinalIgnoreCase))
			{
				return Absent(
					identity,
					"Read-only state is not defined for this HTML element type.");
			}
			if (request.Slot == WebRuntimeDomDataSlot.Initialization)
				return ReadAttribute(identity, evidence, "readonly");
			// DOMSnapshot has no readOnly column. HTMLInputElement and
			// HTMLTextAreaElement define readOnly as a reflected boolean content
			// attribute, so its live IDL value is recovered without JavaScript from
			// the same-revision attribute table.
			return Captured(
				identity,
				evidence.Attributes.ContainsKey("readonly") ? "true" : "false",
				"CDP DOMSnapshot reflected read-only state.");
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

	private static bool IsDisabledStateElement(string tagName) =>
		tagName.ToLowerInvariant() is
			"button"
			or "fieldset"
			or "input"
			or "optgroup"
			or "option"
			or "select"
			or "textarea";

	private static int ResolvePlatformFontWeight(string postScriptName)
	{
		var face = postScriptName.Trim();
		if (face.Contains("Black", StringComparison.OrdinalIgnoreCase)
			|| face.Contains("Heavy", StringComparison.OrdinalIgnoreCase))
		{
			return 900;
		}
		if (face.Contains("ExtraBold", StringComparison.OrdinalIgnoreCase)
			|| face.Contains("UltraBold", StringComparison.OrdinalIgnoreCase))
		{
			return 800;
		}
		if (face.Contains("SemiBold", StringComparison.OrdinalIgnoreCase)
			|| face.Contains("DemiBold", StringComparison.OrdinalIgnoreCase))
		{
			return 600;
		}
		if (face.Contains("Bold", StringComparison.OrdinalIgnoreCase))
			return 700;
		if (face.Contains("Medium", StringComparison.OrdinalIgnoreCase))
			return 500;
		if (face.Contains("ExtraLight", StringComparison.OrdinalIgnoreCase)
			|| face.Contains("UltraLight", StringComparison.OrdinalIgnoreCase))
		{
			return 200;
		}
		if (face.Contains("Light", StringComparison.OrdinalIgnoreCase))
			return 300;
		if (face.Contains("Thin", StringComparison.OrdinalIgnoreCase))
			return 100;
		return 400;
	}

	private static double ReadCssPixel(
		IReadOnlyDictionary<string, string> styles,
		string name)
	{
		if (!styles.TryGetValue(name, out var value))
			return 0;
		value = value.Trim();
		if (value.EndsWith("px", StringComparison.OrdinalIgnoreCase))
			value = value[..^2].Trim();
		return double.TryParse(
			value,
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;
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
					.Where(name => !result.ContainsKey(name)
						&& IsInheritedCssProperty(name))
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

	private static bool IsInheritedCssProperty(string name) =>
		name.StartsWith("--", StringComparison.Ordinal)
		|| InheritedCssProperties.Contains(name);

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
			var identity = createIdentity(name, value, important);
			if (requestedCssNames.Contains(name))
			{
				AddCascadedDeclaration(
					result,
					name,
					new(value, identity, important));
			}
			foreach (var expanded in ExpandBoxEdgeShorthand(name, value))
			{
				if (!requestedCssNames.Contains(expanded.Name))
					continue;
				AddCascadedDeclaration(
					result,
					expanded.Name,
					new(
						expanded.Value,
						$"{identity}:expands:{expanded.Name}",
						important));
			}
		}
	}

	private static void AddCascadedDeclaration(
		IDictionary<string, CdpCssDeclaration> declarations,
		string name,
		CdpCssDeclaration candidate)
	{
		if (!declarations.TryGetValue(name, out var current))
		{
			declarations.Add(name, candidate);
			return;
		}
		// Candidates arrive in descending normal cascade priority: inline first,
		// then matched author rules from the most effective rule to the least.
		// A later !important declaration must still replace an earlier normal
		// declaration. Once an important declaration is selected, subsequent
		// lower-priority important declarations cannot replace it.
		if (candidate.Important && !current.Important)
			declarations[name] = candidate;
	}

	private static IEnumerable<(string Name, string Value)>
		ExpandBoxEdgeShorthand(string name, string value)
	{
		if (name.Equals("overflow", StringComparison.OrdinalIgnoreCase))
		{
			var overflowTokens = SplitTopLevelCssTokens(value);
			if (overflowTokens.Count is 1 or 2)
			{
				yield return ("overflow-x", overflowTokens[0]);
				yield return (
					"overflow-y",
					overflowTokens.Count == 2
						? overflowTokens[1]
						: overflowTokens[0]);
			}
			yield break;
		}
		if (name.Equals("gap", StringComparison.OrdinalIgnoreCase))
		{
			var gapTokens = SplitTopLevelCssTokens(value);
			if (gapTokens.Count is 1 or 2)
			{
				yield return ("row-gap", gapTokens[0]);
				yield return (
					"column-gap",
					gapTokens.Count == 2 ? gapTokens[1] : gapTokens[0]);
			}
			yield break;
		}
		var suffix = name switch
		{
			"margin" => string.Empty,
			"padding" => string.Empty,
			"inset" => string.Empty,
			"border-width" => "-width",
			"border-style" => "-style",
			"border-color" => "-color",
			_ => null
		};
		if (suffix is null)
			yield break;
		var prefix = name switch
		{
			"border-width" or "border-style" or "border-color" => "border-",
			"inset" => string.Empty,
			_ => name + "-"
		};
		var tokens = SplitTopLevelCssTokens(value);
		if (tokens.Count is < 1 or > 4)
			yield break;
		var top = tokens[0];
		var right = tokens.Count > 1 ? tokens[1] : top;
		var bottom = tokens.Count > 2 ? tokens[2] : top;
		var left = tokens.Count > 3 ? tokens[3] : right;
		yield return ($"{prefix}top{suffix}", top);
		yield return ($"{prefix}right{suffix}", right);
		yield return ($"{prefix}bottom{suffix}", bottom);
		yield return ($"{prefix}left{suffix}", left);
	}

	private static IReadOnlyList<string> SplitTopLevelCssTokens(string value)
	{
		var tokens = new List<string>();
		var start = 0;
		var parenthesisDepth = 0;
		for (var index = 0; index < value.Length; index++)
		{
			var character = value[index];
			if (character == '(')
				parenthesisDepth++;
			else if (character == ')')
				parenthesisDepth = Math.Max(0, parenthesisDepth - 1);
			else if (char.IsWhiteSpace(character) && parenthesisDepth == 0)
			{
				if (index > start)
					tokens.Add(value[start..index].Trim());
				start = index + 1;
			}
		}
		if (start < value.Length)
			tokens.Add(value[start..].Trim());
		return tokens.Where(static token => token.Length != 0).ToArray();
	}

	private static CdpCssDeclaration? FindDeclaration(
		JsonElement response,
		string cssName)
	{
		var requested = new HashSet<string>(
			[cssName],
			StringComparer.OrdinalIgnoreCase);
		var indexed = IndexDeclarations(response, requested);
		return indexed.TryGetValue(cssName, out var declaration)
			? declaration
			: null;
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

	private static double ReadCssGeometryScale(string layoutMetricsJson)
	{
		using var response = JsonDocument.Parse(layoutMetricsJson);
		var root = response.RootElement;
		if (!root.TryGetProperty("layoutViewport", out var deviceViewport)
			|| !root.TryGetProperty("cssLayoutViewport", out var cssViewport))
		{
			return 1;
		}
		var deviceWidth = ReadPositiveDouble(deviceViewport, "clientWidth");
		var cssWidth = ReadPositiveDouble(cssViewport, "clientWidth");
		var scale = cssWidth > 0 && deviceWidth > 0
			? cssWidth / deviceWidth
			: 1;
		return double.IsFinite(scale) && scale > 0 ? scale : 1;

		static double ReadPositiveDouble(JsonElement owner, string name) =>
			owner.TryGetProperty(name, out var property)
			&& property.TryGetDouble(out var value)
			&& double.IsFinite(value)
			&& value > 0
				? value
				: 0;
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
		// CSS custom-property names are case-sensitive and are already expressed
		// in their authored CSS form. Only standard DOM-style camelCase names may
		// be converted to kebab-case.
		if (value.StartsWith("--", StringComparison.Ordinal))
			return value;
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

internal sealed record CdpCssDeclaration(
	string Value,
	string Identity,
	bool Important);

internal sealed record CdpMatchedStyleEvidence(
	IReadOnlyDictionary<string, CdpCssDeclaration> Declarations,
	string PseudoRulesJson,
	string PseudoComputedJson);

internal sealed record CdpMatchedStyleRawEvidence(
	string ResponseJson,
	string PseudoRulesJson,
	string PseudoComputedJson);

internal sealed record CdpBoxModelEvidence(
	double BorderX,
	double BorderY,
	double BorderWidth,
	double BorderHeight,
	double PaddingWidth,
	double ContentWidth);

internal sealed record CdpBoundingRectangleEvidence(
	double X,
	double Y,
	double Width,
	double Height);

internal sealed record CdpEventListener(
	string Type,
	bool UseCapture,
	bool Passive,
	bool Once,
	string ScriptId,
	int LineNumber,
	int ColumnNumber);
