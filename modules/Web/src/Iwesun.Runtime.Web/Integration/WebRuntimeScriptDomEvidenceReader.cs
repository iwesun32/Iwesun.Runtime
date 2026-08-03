using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using Iwesun.Runtime.WebView2;

namespace Iwesun.Runtime.Web;

/// <summary>
/// Executes one fixed, bounded property-read program per document scope for a
/// whole-tree request batch. Callers submit typed requests and receive typed
/// records; script text and JSON remain private protocol details.
/// </summary>
public sealed class WebRuntimeScriptDomEvidenceReader(
	IWebRuntimeScriptSession scripts,
	Func<WebRuntimeDomTreeSnapshot> currentTree,
	bool useSinglePropertyQueries = false,
	HtmlRuntimeDesignRuntime? designRuntime = null) :
	IWebRuntimeDomEvidenceReader,
	IWebRuntimeSingleDomEvidenceReader
{
	private static readonly JsonSerializerOptions ScriptJsonOptions =
		new(JsonSerializerDefaults.Web);
	private readonly IWebRuntimeScriptSession _scripts =
		scripts ?? throw new ArgumentNullException(nameof(scripts));
	private readonly Func<WebRuntimeDomTreeSnapshot> _currentTree =
		currentTree ?? throw new ArgumentNullException(nameof(currentTree));
	private readonly bool _useSinglePropertyQueries =
		useSinglePropertyQueries;
	private readonly HtmlRuntimeDesignRuntime? _designRuntime = designRuntime;

	public Uri CurrentPageUrl => _currentTree().PageUri;

	public async ValueTask<WebRuntimeDomIndexedProperty> ReadDomPropertyAsync(
		WebRuntimeDomPropertyRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		var tree = _currentTree();
		var document = tree.Documents.SingleOrDefault(item =>
			item.DocumentScope.Equals(
				request.DocumentScope,
				StringComparison.Ordinal))
			?? throw new InvalidDataException(
				$"DOM document scope '{request.DocumentScope}' is unavailable.");
		var record = await ReadSingleAsync(
			document,
			request,
			cancellationToken);
		if (record.Status == 2
			&& record.Description?.Equals(
				"Absolute XPath did not resolve to an element.",
				StringComparison.Ordinal) == true)
		{
			throw UnresolvedXPath(request);
		}
		return Observe(ToIndexedProperty(request, record));
	}

	public async ValueTask<IReadOnlyList<WebRuntimeDomIndexedProperty>>
		ReadDomPropertiesAsync(
			IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
			CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(requests);
		var tree = _currentTree();
		var documents = tree.Documents.ToDictionary(
			static document => document.DocumentScope,
			StringComparer.Ordinal);
		if (!_useSinglePropertyQueries)
		{
			return await ReadAllDocumentScopesInOneBatchAsync(
				requests,
				documents,
				cancellationToken);
		}
		var results = new List<WebRuntimeDomIndexedProperty>(requests.Count);
		foreach (var group in requests.GroupBy(
			static request => request.DocumentScope,
			StringComparer.Ordinal))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var scopedRequests = group.ToArray();
			if (!documents.TryGetValue(group.Key, out var document))
			{
				results.AddRange(scopedRequests.Select(static request =>
					Unsupported(
						request,
						"The requested DOM document scope is unavailable.")));
				continue;
			}
			if (group.Key.EndsWith("#shadow-root", StringComparison.Ordinal))
			{
				results.AddRange(scopedRequests.Select(static request =>
					Unsupported(
						request,
						"Shadow-root property routing requires a shadow scope adapter.")));
				continue;
			}
			var records = _useSinglePropertyQueries
				? await ReadIndividuallyAsync(
					document,
					scopedRequests,
					cancellationToken)
				: await ReadBatchAsync(
					document,
					scopedRequests,
					cancellationToken);
			var byId = records.ToDictionary(
				static record => record.QueryId,
				StringComparer.Ordinal);
			foreach (var request in scopedRequests)
			{
				if (!byId.Remove(request.QueryId, out var record))
				{
					throw new InvalidDataException(
						$"DOM batch omitted query '{request.QueryId}'.");
				}
				results.Add(Observe(ToIndexedProperty(request, record)));
			}
			if (byId.Count != 0)
			{
				throw new InvalidDataException(
					"DOM batch returned results outside the requested query plan.");
			}
		}
		return results;
	}

	private async ValueTask<IReadOnlyList<WebRuntimeDomIndexedProperty>>
		ReadAllDocumentScopesInOneBatchAsync(
			IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
			IReadOnlyDictionary<string, WebRuntimeDomDocumentScope> documents,
			CancellationToken cancellationToken)
	{
		var supported = requests
			.Where(request =>
				documents.ContainsKey(request.DocumentScope)
				&& !request.DocumentScope.EndsWith(
					"#shadow-root",
					StringComparison.Ordinal))
			.ToArray();
		var records = supported.Length == 0
			? []
			: await ReadBatchAcrossDocumentsAsync(
				supported,
				cancellationToken);
		var byId = records.ToDictionary(
			static record => record.QueryId,
			StringComparer.Ordinal);
		var results = new List<WebRuntimeDomIndexedProperty>(requests.Count);
		foreach (var request in requests)
		{
			if (!documents.ContainsKey(request.DocumentScope))
			{
				results.Add(Unsupported(
					request,
					"The requested DOM document scope is unavailable."));
				continue;
			}
			if (request.DocumentScope.EndsWith(
				"#shadow-root",
				StringComparison.Ordinal))
			{
				results.Add(Unsupported(
					request,
					"Shadow-root property routing requires a shadow scope adapter."));
				continue;
			}
			if (!byId.Remove(request.QueryId, out var record))
			{
				throw new InvalidDataException(
					$"DOM evidence batch omitted query '{request.QueryId}'.");
			}
			results.Add(Observe(ToIndexedProperty(request, record)));
		}
		if (byId.Count != 0)
		{
			throw new InvalidDataException(
				"DOM evidence batch returned results outside the query plan.");
		}
		return results;
	}

	private async Task<IReadOnlyList<ScriptResult>> ReadIndividuallyAsync(
		WebRuntimeDomDocumentScope document,
		IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
		CancellationToken cancellationToken)
	{
		var results = new List<ScriptResult>(requests.Count);
		foreach (var request in requests)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var result = await ReadSingleAsync(
				document,
				request,
				cancellationToken);
			if (result.Status == 2
				&& result.Description?.Equals(
					"Absolute XPath did not resolve to an element.",
					StringComparison.Ordinal) == true)
			{
				throw UnresolvedXPath(request);
			}
			results.Add(result);
		}
		return results;
	}

	private async Task<ScriptResult> ReadSingleAsync(
		WebRuntimeDomDocumentScope document,
		WebRuntimeDomPropertyRequest request,
		CancellationToken cancellationToken)
	{
		var expression = SingleReadExpression
			.Replace(
				"__XPATH__",
				JavaScriptString(request.XPath),
				StringComparison.Ordinal)
			.Replace(
				"__PROPERTY_NAME__",
				JavaScriptString(request.PropertyName),
				StringComparison.Ordinal)
			.Replace(
				"__OWNER_KIND__",
				((int)request.OwnerKind).ToString(
					System.Globalization.CultureInfo.InvariantCulture),
				StringComparison.Ordinal)
			.Replace(
				"__SLOT__",
				((int)request.Slot).ToString(
					System.Globalization.CultureInfo.InvariantCulture),
				StringComparison.Ordinal)
			.Replace(
				"__EVIDENCE_CACHE__",
				"{nodes:new WeakMap(),authored:new WeakMap(),"
					+ "computed:new WeakMap(),rects:new WeakMap(),"
					+ "requestedStyleProperties:new Set()}",
				StringComparison.Ordinal);
		var raw = document.FrameIndex is null
			? await _scripts.EvaluateStringAsync(
				expression,
				cancellationToken)
			: await EvaluateFrameAsync(
				document,
				expression,
				cancellationToken);
		var parts = raw.Split('\u001f');
		if (parts.Length != 6
			|| !int.TryParse(
				parts[0],
				System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture,
				out var status))
		{
			throw new InvalidDataException(
				"Single DOM property API returned an invalid scalar frame.");
		}
		return new(
			request.QueryId,
			status,
			Uri.UnescapeDataString(parts[1]),
			Uri.UnescapeDataString(parts[2]),
			int.Parse(
				parts[3],
				System.Globalization.CultureInfo.InvariantCulture),
			int.Parse(
				parts[4],
				System.Globalization.CultureInfo.InvariantCulture),
			Uri.UnescapeDataString(parts[5]));
	}

	private static string JavaScriptString(string value) =>
		$"'{JavaScriptEncoder.Default.Encode(value)}'";

	private static InvalidDataException UnresolvedXPath(
		WebRuntimeDomPropertyRequest request) =>
		new(
			$"Direct DOM property query could not resolve "
			+ $"{request.DocumentScope}::{request.XPath}; "
			+ $"{request.ReflectedPropertyName}/"
			+ $"{request.PropertyName}/{request.Slot}.");

	private async Task<IReadOnlyList<ScriptResult>> ReadBatchAsync(
		WebRuntimeDomDocumentScope document,
		IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
		CancellationToken cancellationToken)
	{
		var payload = JsonSerializer.Serialize(requests.Select(
			static request => new ScriptRequest(
				request.QueryId,
				request.DocumentScope,
				request.XPath,
				request.PropertyName,
				(int)request.OwnerKind,
				(int)request.Slot)),
			ScriptJsonOptions);
		var expression = CreateBatchReadExpression(payload);
		var raw = document.FrameIndex is null
			? await _scripts.EvaluateStringAsync(
				expression,
				cancellationToken)
			: await EvaluateFrameAsync(
				document,
				expression,
				cancellationToken);
		return ParseResults(raw);
	}

	private async Task<IReadOnlyList<ScriptResult>>
		ReadBatchAcrossDocumentsAsync(
			IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
			CancellationToken cancellationToken)
	{
		var payload = JsonSerializer.Serialize(requests.Select(
			static request => new ScriptRequest(
				request.QueryId,
				request.DocumentScope,
				request.XPath,
				request.PropertyName,
				(int)request.OwnerKind,
				(int)request.Slot)),
			ScriptJsonOptions);
		var raw = await _scripts.EvaluateStringAsync(
			CreateCrossDocumentBatchReadExpression(payload),
			cancellationToken);
		return ParseResults(raw);
	}

	private static string CreateBatchReadExpression(string payload)
	{
		var scalarRead = SingleReadExpression
			.Replace("__XPATH__", "request.xpath", StringComparison.Ordinal)
			.Replace(
				"__PROPERTY_NAME__",
				"request.propertyName",
				StringComparison.Ordinal)
			.Replace(
				"__OWNER_KIND__",
				"request.ownerKind",
				StringComparison.Ordinal)
			.Replace("__SLOT__", "request.slot", StringComparison.Ordinal)
			.Replace(
				"__EVIDENCE_CACHE__",
				"{nodes:new WeakMap(),authored:new WeakMap(),"
					+ "computed:new WeakMap(),rects:new WeakMap(),"
					+ "requestedStyleProperties:new Set()}",
				StringComparison.Ordinal);
		return """
			JSON.stringify((() => {
			  const requests = __REQUESTS__;
			  const readScalar = request => __SCALAR_READ__;
			  return requests.map(request => {
			    const parts = String(readScalar(request)).split('\u001f');
			    return {
			      queryId: request.queryId,
			      status: Number(parts[0]),
			      value: decodeURIComponent(parts[1] || ''),
			      description: decodeURIComponent(parts[2] || ''),
			      valueSource: Number(parts[3] || '0'),
			      linkKind: Number(parts[4] || '0'),
			      linkIdentity: decodeURIComponent(parts[5] || '')
			    };
			  });
			})())
			"""
			.Replace("__REQUESTS__", payload, StringComparison.Ordinal)
			.Replace("__SCALAR_READ__", scalarRead, StringComparison.Ordinal);
	}

	private static string CreateCrossDocumentBatchReadExpression(
		string payload)
	{
		const string scalarPrefix = "(() => {";
		const string scalarSuffix = "})()";
		if (!SingleReadExpression.StartsWith(
				scalarPrefix,
				StringComparison.Ordinal)
			|| !SingleReadExpression.EndsWith(
				scalarSuffix,
				StringComparison.Ordinal))
		{
			throw new InvalidOperationException(
				"The fixed scalar DOM reader has an unexpected wrapper.");
		}
		var body = SingleReadExpression[
			scalarPrefix.Length..^scalarSuffix.Length];
		var scalarRead = (
			"((document, request, evidenceCache) => {" + body + "})")
			.Replace("__XPATH__", "request.xpath", StringComparison.Ordinal)
			.Replace(
				"__PROPERTY_NAME__",
				"request.propertyName",
				StringComparison.Ordinal)
			.Replace(
				"__OWNER_KIND__",
				"request.ownerKind",
				StringComparison.Ordinal)
			.Replace("__SLOT__", "request.slot", StringComparison.Ordinal)
			.Replace(
				"__EVIDENCE_CACHE__",
				"evidenceCache",
				StringComparison.Ordinal);
		return CrossDocumentBatchReadExpression
			.Replace("__REQUESTS__", payload, StringComparison.Ordinal)
			.Replace("__SCALAR_READ__", scalarRead, StringComparison.Ordinal);
	}

	private async Task<string> EvaluateFrameAsync(
		WebRuntimeDomDocumentScope document,
		string expression,
		CancellationToken cancellationToken)
	{
		if (_scripts is IWebRuntimeDocumentScopeScriptSession scoped)
		{
			return await scoped.EvaluateStringInDocumentAsync(
				document.DocumentScope,
				document.DocumentUri,
				expression,
				cancellationToken);
		}
		if (document.FrameIndex is not { } frameIndex)
			throw new InvalidOperationException("Frame index is unavailable.");
		if (_scripts is IWebRuntimeIndexedFrameScriptSession indexed)
		{
			return await indexed.EvaluateStringInFrameAsync(
				frameIndex,
				expression,
				cancellationToken);
		}
		return await _scripts.EvaluateStringInFrameAsync(
			document.DocumentUri.AbsoluteUri,
			expression,
			cancellationToken);
	}

	private static IReadOnlyList<ScriptResult> ParseResults(string raw)
	{
		using var outer = JsonDocument.Parse(
			raw,
			new JsonDocumentOptions { MaxDepth = 256 });
		var json = outer.RootElement.ValueKind == JsonValueKind.String
			? outer.RootElement.GetString()
				?? throw new InvalidDataException(
					"DOM batch returned an empty JSON string.")
			: raw;
		return JsonSerializer.Deserialize<ScriptResult[]>(
			json,
			ScriptJsonOptions)
			?? throw new InvalidDataException("DOM batch returned JSON null.");
	}

	private static WebRuntimeDomIndexedProperty ToIndexedProperty(
		WebRuntimeDomPropertyRequest request,
		ScriptResult result)
	{
		var status = result.Status switch
		{
			0 => WebRuntimeDomPropertyStatus.Captured,
			1 => WebRuntimeDomPropertyStatus.ConfirmedAbsent,
			2 => WebRuntimeDomPropertyStatus.SourceUnsupported,
			_ => throw new InvalidDataException(
				$"DOM batch returned invalid status {result.Status}.")
		};
		return new(
			WebRuntimeDomPropertyIdentity.From(request),
			status,
			status == WebRuntimeDomPropertyStatus.Captured
				? result.Value ?? string.Empty
				: string.Empty,
			status == WebRuntimeDomPropertyStatus.Captured
				? result.ValueSource == 0
					? WebRuntimeDomValueSource.DirectConstant
					: (WebRuntimeDomValueSource)result.ValueSource
				: WebRuntimeDomValueSource.Unspecified,
			status == WebRuntimeDomPropertyStatus.Captured
				? (WebRuntimeDomLinkKind)result.LinkKind
				: WebRuntimeDomLinkKind.None,
			status == WebRuntimeDomPropertyStatus.Captured
				? result.LinkIdentity ?? string.Empty
				: string.Empty,
			result.Description ?? string.Empty);
	}

	private WebRuntimeDomIndexedProperty Observe(
		WebRuntimeDomIndexedProperty property)
	{
		_designRuntime?.Observe(property);
		return property;
	}

	private static WebRuntimeDomIndexedProperty Unsupported(
		WebRuntimeDomPropertyRequest request,
		string description) =>
		new(
			WebRuntimeDomPropertyIdentity.From(request),
			WebRuntimeDomPropertyStatus.SourceUnsupported,
			string.Empty,
			WebRuntimeDomValueSource.Unspecified,
			WebRuntimeDomLinkKind.None,
			string.Empty,
			description);

	private sealed record ScriptRequest(
		string QueryId,
		string DocumentScope,
		[property: JsonPropertyName("xpath")]
		string XPath,
		string PropertyName,
		int OwnerKind,
		int Slot);

	private sealed record ScriptResult(
		string QueryId,
		int Status,
		string? Value,
		string? Description,
		int ValueSource = 0,
		int LinkKind = 0,
		string? LinkIdentity = null);

	private const string CrossDocumentBatchReadExpression = """
		JSON.stringify((() => {
		  const requests = __REQUESTS__;
		  const requestedStyleProperties = new Set(
		    requests
		      .filter(request =>
		        request.propertyName.startsWith('style.'))
		      .map(request => request.propertyName
		        .slice(6)
		        .replace(/[A-Z]/g,
		          match => '-' + match.toLowerCase())));
		  requestedStyleProperties.add('animation-name');
		  requestedStyleProperties.add('content');
		  const evidenceCache = {
		    nodes: new WeakMap(),
		    authored: new WeakMap(),
		    computed: new WeakMap(),
		    rects: new WeakMap(),
		    requestedStyleProperties
		  };
		  const scopes = new Map([['document', document]]);
		  const segment = node => {
		    const tag = node.localName || node.nodeName.toLowerCase();
		    const parent = node.parentElement;
		    if (!parent) return tag;
		    const peers = [...parent.children]
		      .filter(item => item.localName === node.localName);
		    return peers.length > 1
		      ? `${tag}[${peers.indexOf(node) + 1}]`
		      : tag;
		  };
		  const pathOf = node => {
		    const parts = [];
		    for (let current = node;
		         current && current.nodeType !== Node.DOCUMENT_NODE;) {
		      parts.unshift(segment(current));
		      current = current.parentNode instanceof ShadowRoot
		        ? current.parentNode.host
		        : current.parentNode;
		    }
		    return '/' + parts.join('/');
		  };
		  const visitFrames = (ownerDocument, ownerScope) => {
		    for (const frame of ownerDocument.querySelectorAll('iframe')) {
		      const framePath = pathOf(frame);
		      const frameScope = ownerScope === 'document'
		        ? framePath
		        : `${ownerScope}::${framePath}`;
		      try {
		        const childDocument = frame.contentDocument;
		        if (!childDocument?.documentElement) continue;
		        scopes.set(frameScope, childDocument);
		        visitFrames(childDocument, frameScope);
		      } catch {
		      }
		    }
		  };
		  visitFrames(document, 'document');
		  const readScalar = __SCALAR_READ__;
		  return requests.map(request => {
		    const targetDocument = scopes.get(request.documentScope);
		    if (!targetDocument) {
		      return {
		        queryId: request.queryId,
		        status: 2,
		        value: '',
		        description:
		          'Document scope is not accessible from the top renderer.',
		        valueSource: 0,
		        linkKind: 0,
		        linkIdentity: ''
		      };
		    }
		    try {
		      const parts = String(
		      readScalar(
		        targetDocument,
		        request,
		        evidenceCache)).split('\u001f');
		      return {
		        queryId: request.queryId,
		        status: Number(parts[0]),
		        value: decodeURIComponent(parts[1] || ''),
		        description: decodeURIComponent(parts[2] || ''),
		        valueSource: Number(parts[3] || '0'),
		        linkKind: Number(parts[4] || '0'),
		        linkIdentity: decodeURIComponent(parts[5] || '')
		      };
		    } catch (error) {
		      return {
		        queryId: request.queryId,
		        status: 2,
		        value: '',
		        description: 'Renderer evidence read failed: ' + String(error),
		        valueSource: 0,
		        linkKind: 0,
		        linkIdentity: ''
		      };
		    }
		  });
		})())
		""";

	private const string BatchReadExpression = """
		JSON.stringify((() => {
		  const requests = __REQUESTS__;
		  const cache = {
		    computed: new WeakMap(),
		    rects: new WeakMap()
		  };
		  const byXPath = path => {
		    try {
		      const resolved = document.evaluate(
		        path,
		        document,
		        null,
		        XPathResult.FIRST_ORDERED_NODE_TYPE,
		        null).singleNodeValue;
		      if (resolved) return resolved;
		    } catch {
		    }
		    const segments = String(path || '').split('/').filter(Boolean);
		    if (!segments.length) return null;
		    let current = document;
		    for (const segment of segments) {
		      const match = /^([A-Za-z_][A-Za-z0-9_.:-]*|\*)(?:\[(\d+)\])?$/
		        .exec(segment);
		      if (!match) return null;
		      const expected = match[1].toLowerCase();
		      const position = Number(match[2] || '1');
		      const candidates = current === document
		        ? [document.documentElement]
		        : Array.from(current.children || []);
		      const matching = candidates.filter(child =>
		        expected === '*'
		        || String(child.localName || child.nodeName || '')
		          .toLowerCase() === expected);
		      current = matching[position - 1] || null;
		      if (!current) return null;
		    }
		    return current;
		  };
		  const cssName = name => name
		    .replace(/^style\./, '')
		    .replace(/[A-Z]/g, match => '-' + match.toLowerCase());
		  const scalar = value => {
		    if (value === null || value === undefined) return null;
		    if (typeof value === 'string') return value;
		    if (typeof value === 'number' || typeof value === 'boolean')
		      return String(value);
		    return null;
		  };
		  const read = (node, request) => {
		    const name = request.propertyName;
		    if (request.slot === 1) {
		      return {
		        status: 2,
		        value: '',
		        description: 'Link evidence requires the CSS/layout connection.'
		      };
		    }
		    if (name.startsWith('style.')) {
		      const property = cssName(name);
		      let computed = cache.computed.get(node);
		      if (!computed) {
		        computed = getComputedStyle(node);
		        cache.computed.set(node, computed);
		      }
		      const value = computed.getPropertyValue(property)
		        || computed[name.slice(6)];
		      return value === null || value === undefined || value === ''
		        ? { status: 1, value: '', description: 'Computed style is absent.' }
		        : { status: 0, value: String(value).trim(), description: 'Live computed style.' };
		    }
		    if (name.startsWith('rect.')) {
		      if (request.slot === 0) {
		        return {
		          status: 1,
		          value: '',
		          description: 'Runtime absolute geometry is not an initialization value.'
		        };
		      }
		      let rectangle = cache.rects.get(node);
		      if (!rectangle) {
		        rectangle = node.getBoundingClientRect();
		        cache.rects.set(node, rectangle);
		      }
		      const value = scalar(rectangle[name.slice(5)]);
		      return value === null
		        ? { status: 1, value: '', description: 'Runtime rectangle member is absent.' }
		        : { status: 0, value, description: 'Live getBoundingClientRect value.' };
		    }
		    if (name === 'content.ownText') {
		      const value = [...node.childNodes]
		        .filter(child => child.nodeType === Node.TEXT_NODE)
		        .map(child => child.nodeValue || '')
		        .join('');
		      return value.trim() === ''
		        ? { status: 1, value: '', description: 'Element has no own text.' }
		        : { status: 0, value, description: 'Live direct text-node content.' };
		    }
		    if ((name === 'content.value' || name === 'content.placeholder')
		        && request.slot === 0) {
		      return {
		        status: 1,
		        value: '',
		        description: 'Live form content is not an initialization value.'
		      };
		    }
		    if (name === 'state.valid'
		        || name === 'state.willValidate'
		        || name === 'state.validationMessage') {
		      if (request.slot === 0) {
		        return {
		          status: 1,
		          value: '',
		          description: 'Live validity is not an initialization value.'
		        };
		      }
		      const value = name === 'state.valid'
		        ? node.validity?.valid
		        : name === 'state.willValidate'
		          ? node.willValidate
		          : node.validationMessage;
		      const scalarValue = scalar(value);
		      return scalarValue === null
		        ? { status: 1, value: '', description: 'Validity member is absent.' }
		        : { status: 0, value: scalarValue, description: 'Live constraint-validation state.' };
		    }
		    if (name === 'content.namespaceUri') {
		      return {
		        status: 0,
		        value: node.namespaceURI || '',
		        description: 'Live DOM namespace URI.'
		      };
		    }
		    if (name === 'resource.imageSourceUrl') {
		      const value = scalar(node.currentSrc || node.src);
		      return value === null || value === ''
		        ? { status: 1, value: '', description: 'Element has no image resource URL.' }
		        : { status: 0, value, description: 'Live selected image resource URL.' };
		    }
		    if (name === 'resource.mediaSourceUrl') {
		      const value = scalar(node.currentSrc || node.src);
		      return value === null || value === ''
		        ? { status: 1, value: '', description: 'Element has no selected media resource URL.' }
		        : { status: 0, value, description: 'Live selected media resource URL.' };
		    }
		    if (name === 'resource.embeddedSourceUrl') {
		      const value = scalar(node.data || node.src);
		      return value === null || value === ''
		        ? { status: 1, value: '', description: 'Element has no embedded resource URL.' }
		        : { status: 0, value, description: 'Live embedded resource URL.' };
		    }
		    if (name === 'resource.posterSourceUrl') {
		      const value = scalar(node.poster);
		      return value === null || value === ''
		        ? { status: 1, value: '', description: 'Element has no poster resource URL.' }
		        : { status: 0, value, description: 'Live resolved poster resource URL.' };
		    }
		    if (name === 'resource.canvasCommandStream') {
		      if (request.slot === 0) {
		        return {
		          status: 1,
		          value: '',
		          description: 'Canvas commands are runtime evidence.'
		        };
		      }
		      const commands = node.__iwesunCanvasCommands;
		      return Array.isArray(commands)
		        ? {
		            status: 0,
		            value: JSON.stringify(commands),
		            description: 'Bounded CanvasRenderingContext2D command stream.'
		          }
		        : {
		            status: 1,
		            value: '',
		            description: 'Canvas command tracking is absent.'
		          };
		    }
		    if (name.startsWith('effect.')) {
		      return {
		        status: 2,
		        value: '',
		        description: 'Effect and animation evidence requires the effect connection.'
		      };
		    }
		    if (request.ownerKind === 5) {
		      const eventName = name.startsWith('handler.on')
		        ? name.slice('handler.on'.length)
		        : name.startsWith('event.')
		          ? name.split('.')[1]
		          : name.startsWith('on') ? name.slice(2) : name;
		      const inlineSource = node.getAttribute?.('on' + eventName);
		      const captured = globalThis.__iwesunEventRegistry
		        ?.query(node, eventName) ?? null;
		      if (request.slot === 0) {
		        const registration = inlineSource !== null
		          ? {
		              eventName,
		              registrationKind: 'InlineAttribute',
		              useCapture: false,
		              passive: false,
		              once: false,
		              source: inlineSource
		            }
		          : captured?.registration;
		        return registration
		          ? {
		              status: 0,
		              value: JSON.stringify(registration),
		              description: 'Live DOM event registration.'
		            }
		          : {
		              status: 1,
		              value: '',
		              description: 'DOM event registration is absent.'
		            };
		      }
		      if (request.slot === 1) {
		        const link = inlineSource !== null
		          ? 'inline-handler:' + inlineSource
		          : captured?.registration
		            ? 'event-listener:' + eventName
		            : '';
		        return link
		          ? {
		              status: 0,
		              value: link,
		              description: 'Live DOM event link.',
		              valueSource: 2,
		              linkKind: 8,
		              linkIdentity: link
		            }
		          : {
		              status: 1,
		              value: '',
		              description: 'DOM event link is absent.'
		            };
		      }
		      return captured?.runtime
		        ? {
		            status: 0,
		            value: JSON.stringify(captured.runtime),
		            description: 'Live DOM event invocation evidence.'
		          }
		        : {
		            status: 1,
		            value: '',
		            description: 'DOM event invocation evidence is absent.'
		          };
		    }
		    if (request.ownerKind === 1 || request.ownerKind === 2) {
		      if (node.hasAttribute(name))
		        return { status: 0, value: node.getAttribute(name) || '', description: 'Live DOM attribute.' };
		      if (typeof node[name] === 'boolean' && node[name])
		        return { status: 0, value: 'true', description: 'Live boolean DOM state.' };
		      return { status: 1, value: '', description: 'DOM attribute is absent.' };
		    }
		    const member = name.startsWith('state.') || name.startsWith('content.')
		      ? name.slice(name.indexOf('.') + 1)
		      : name;
		    const value = scalar(node[member]);
		    return value === null
		      ? { status: 1, value: '', description: 'DOM property is absent.' }
		      : { status: 0, value, description: 'Live DOM property.' };
		  };
		  return requests.map(request => {
		    const node = byXPath(request.xpath);
		    if (!node || node.nodeType !== Node.ELEMENT_NODE) {
		      return {
		        queryId: request.queryId,
		        status: 2,
		        value: '',
		        description: 'Absolute XPath did not resolve to an element.'
		      };
		    }
		    const result = read(node, request);
		    return {
		      queryId: request.queryId,
		      status: result.status,
		      value: result.value,
		      description: result.description,
		      valueSource: result.valueSource || 0,
		      linkKind: result.linkKind || 0,
		      linkIdentity: result.linkIdentity || ''
		    };
		  });
		})())
		""";

	private const string SingleReadExpression = """
		(() => {
		  const path = __XPATH__;
		  const name = __PROPERTY_NAME__;
		  const ownerKind = __OWNER_KIND__;
		  const slot = __SLOT__;
		  const cache = __EVIDENCE_CACHE__;
		  const byXPath = value => {
		    let paths = cache.nodes.get(document);
		    if (!paths) {
		      paths = new Map();
		      cache.nodes.set(document, paths);
		    }
		    if (paths.has(value)) return paths.get(value);
		    try {
		      const resolved = document.evaluate(
		        value,
		        document,
		        null,
		        XPathResult.FIRST_ORDERED_NODE_TYPE,
		        null).singleNodeValue;
		      if (resolved) {
		        paths.set(value, resolved);
		        return resolved;
		      }
		    } catch {
		    }
		    const segments = String(value || '').split('/').filter(Boolean);
		    if (!segments.length) return null;
		    let current = document;
		    for (const segment of segments) {
		      const match = /^([A-Za-z_][A-Za-z0-9_.:-]*|\*)(?:\[(\d+)\])?$/
		        .exec(segment);
		      if (!match) return null;
		      const expected = match[1].toLowerCase();
		      const position = Number(match[2] || '1');
		      const candidates = current === document
		        ? [document.documentElement]
		        : Array.from(current.children || []);
		      const matching = candidates.filter(child =>
		        expected === '*'
		        || String(child.localName || child.nodeName || '')
		          .toLowerCase() === expected);
		      current = matching[position - 1] || null;
		      if (!current) {
		        paths.set(value, null);
		        return null;
		      }
		    }
		    paths.set(value, current);
		    return current;
		  };
		  const cssName = value => value
		    .replace(/^style\./, '')
		    .replace(/[A-Z]/g, match => '-' + match.toLowerCase());
		  const scalar = value => {
		    if (value === null || value === undefined) return null;
		    if (typeof value === 'string') return value;
		    if (typeof value === 'number' || typeof value === 'boolean')
		      return String(value);
		    return null;
		  };
		  const authoredCssLink = (node, property) => {
		    const cachedLinks = cache.authored.get(node);
		    if (cachedLinks) {
		      if (cachedLinks.has(property))
		        return cachedLinks.get(property);
		      const inheritedProperties = new Set([
		        'color', 'cursor', 'direction', 'font', 'font-family',
		        'font-size', 'font-style', 'font-weight', 'letter-spacing',
		        'line-height', 'text-align', 'text-transform', 'visibility',
		        'white-space', 'word-spacing', 'writing-mode'
		      ]);
		      const inherited = inheritedProperties.has(property)
		        && node.parentElement
		          ? authoredCssLink(node.parentElement, property)
		          : null;
		      const resolved = inherited
		        ? {
		            ...inherited,
		            identity: `inherited:${path}:${inherited.identity}`
		          }
		        : null;
		      cachedLinks.set(property, resolved);
		      return resolved;
		    }
		    const links = new Map();
		    cache.authored.set(node, links);
		    const candidates = new Map();
		    let sourceOrder = 0;
		    const specificity = selector => {
		      const ids = (selector.match(/#[\w-]+/g) || []).length;
		      const classes = (selector.match(
		        /\.[\w-]+|\[[^\]]+\]|:(?!:)[\w-]+(?:\([^)]*\))?/g) || []).length;
		      const types = (selector.match(
		        /(?:^|[\s>+~,(])(?:[a-zA-Z][\w-]*|::[\w-]+)/g) || []).length;
		      return ids * 1000000 + classes * 1000 + types;
		    };
		    const add = (
		      declaredProperty,
		      declaredValue,
		      important,
		      selector,
		      sheetSource,
		      inline,
		      condition) => {
		      if (!declaredValue) return;
		      const item = {
		        identity: inline
		          ? `inline:${path}:${declaredProperty}:${declaredValue}`
		          : `rule:${sheetSource}:${condition}:${selector}:${declaredProperty}:${declaredValue}`,
		        value: declaredValue,
		        important,
		        specificity: inline ? 1000000000 : specificity(selector),
		        sourceOrder: sourceOrder++,
		        variables: Array.from(
		          declaredValue.matchAll(/var\(\s*(--[\w-]+)/g),
		          match => match[1])
		      };
		      const existing = candidates.get(declaredProperty);
		      if (!existing
		        || Number(item.important) > Number(existing.important)
		        || (item.important === existing.important
		          && (item.specificity > existing.specificity
		            || (item.specificity === existing.specificity
		              && item.sourceOrder > existing.sourceOrder)))) {
		        candidates.set(declaredProperty, item);
		      }
		    };
		    const visitRules = (rules, sheetSource, condition) => {
		      for (const rule of Array.from(rules || [])) {
		        if (rule instanceof CSSMediaRule) {
		          if (matchMedia(rule.conditionText).matches) {
		            visitRules(
		              rule.cssRules,
		              sheetSource,
		              `${condition}@media(${rule.conditionText})`);
		          }
		          continue;
		        }
		        if (rule instanceof CSSSupportsRule) {
		          if (CSS.supports(rule.conditionText)) {
		            visitRules(
		              rule.cssRules,
		              sheetSource,
		              `${condition}@supports(${rule.conditionText})`);
		          }
		          continue;
		        }
		        if (rule.cssRules && !rule.selectorText) {
		          try { visitRules(rule.cssRules, sheetSource, condition); } catch {}
		          continue;
		        }
		        if (!rule?.selectorText || !rule.style) continue;
		        let matches = false;
		        try { matches = node.matches(rule.selectorText); } catch {}
		        if (!matches) continue;
		        for (const requestedProperty
		             of cache.requestedStyleProperties) {
		          const expandedValue =
		            rule.style.getPropertyValue(requestedProperty)
		            || rule.style[requestedProperty.replace(
		              /-([a-z])/g,
		              (_, letter) => letter.toUpperCase())];
		          if (!expandedValue) continue;
		          add(
		            requestedProperty,
		            String(expandedValue).trim(),
		            rule.style.getPropertyPriority(requestedProperty)
		              === 'important',
		            rule.selectorText,
		            sheetSource,
		            false,
		            condition);
		        }
		        for (let index = 0; index < rule.style.length; index++) {
		          const declaredProperty = rule.style[index];
		          if (declaredProperty === property) continue;
		          add(
		            declaredProperty,
		            rule.style.getPropertyValue(declaredProperty),
		            rule.style.getPropertyPriority(declaredProperty) === 'important',
		            rule.selectorText,
		            sheetSource,
		            false,
		            condition);
		        }
		      }
		    };
		    for (const sheet of Array.from(document.styleSheets || [])) {
		      try {
		        visitRules(
		          sheet.cssRules,
		          String(sheet.href || 'inline-sheet'),
		          '');
		      } catch {}
		    }
		    if (node.style) {
		      for (const requestedProperty
		           of cache.requestedStyleProperties) {
		        const inlineExpanded =
		          node.style.getPropertyValue(requestedProperty)
		          || node.style[requestedProperty.replace(
		            /-([a-z])/g,
		            (_, letter) => letter.toUpperCase())];
		        if (!inlineExpanded) continue;
		        add(
		          requestedProperty,
		          String(inlineExpanded).trim(),
		          node.style.getPropertyPriority(requestedProperty)
		            === 'important',
		          '',
		          '',
		          true,
		          '');
		      }
		      for (let index = 0; index < node.style.length; index++) {
		        const declaredProperty = node.style[index];
		        if (declaredProperty === property) continue;
		        add(
		          declaredProperty,
		          node.style.getPropertyValue(declaredProperty),
		          node.style.getPropertyPriority(declaredProperty) === 'important',
		          '',
		          '',
		          true,
		          '');
		      }
		    }
		    for (const [declaredProperty, candidate] of candidates) {
		      if (candidate.variables.length) {
		        candidate.identity += `:vars(${candidate.variables.join(',')})`;
		      }
		      links.set(declaredProperty, candidate);
		    }
		    const direct = links.get(property) || null;
		    if (direct) return direct;
		    const inheritedProperties = new Set([
		      'color', 'cursor', 'direction', 'font', 'font-family',
		      'font-size', 'font-style', 'font-weight', 'letter-spacing',
		      'line-height', 'text-align', 'text-transform', 'visibility',
		      'white-space', 'word-spacing', 'writing-mode'
		    ]);
		    if (!inheritedProperties.has(property) || !node.parentElement)
		      return null;
		    const inherited = authoredCssLink(node.parentElement, property);
		    const resolved = inherited
		      ? {
		          ...inherited,
		          identity: `inherited:${path}:${inherited.identity}`
		        }
		      : null;
		    links.set(property, resolved);
		    return resolved;
		  };
		  const read = node => {
		    if (slot === 1) {
		      if (name.startsWith('rect.')) {
		        const parentPath = path.includes('/')
		          ? path.slice(0, path.lastIndexOf('/'))
		          : '';
		        const identity = `layout:${parentPath || '/'}->${path}`;
		        return {
		          status: 0,
		          value: identity,
		          description: 'Live DOM parent-layout relationship.',
		          valueSource: 2,
		          linkKind: 5,
		          linkIdentity: identity
		        };
		      }
		      if (name.startsWith('style.')) {
		        const authored = authoredCssLink(node, cssName(name));
		        return authored
		          ? {
		              status: 0,
		              value: authored.identity,
		              description: 'Live authored CSS declaration.',
		              valueSource: 2,
		              linkKind: 4,
		              linkIdentity: authored.identity
		            }
		          : {
		              status: 1,
		              value: '',
		              description: 'No authored CSS declaration links this element.'
		            };
		      }
		      if (name.startsWith('effect.')) {
		        const authored = authoredCssLink(
		          node,
		          name === 'effect.animations' ? 'animation-name' : 'content');
		        return authored
		          ? {
		              status: 0,
		              value: authored.identity,
		              description: 'Live authored effect declaration.',
		              valueSource: 2,
		              linkKind: 4,
		              linkIdentity: authored.identity
		            }
		          : {
		              status: 1,
		              value: '',
		              description: 'No authored effect declaration links this element.'
		            };
		      }
		      return {
		        status: 1,
		        value: '',
		        description: 'This direct DOM property has no link slot.'
		      };
		    }
		    if (name.startsWith('style.')) {
		      const property = cssName(name);
		      if (slot === 0) {
		        const authored = authoredCssLink(node, property);
		        return authored
		          ? {
		              status: 0,
		              value: authored.value,
		              description: 'Authored CSS initialization value.'
		            }
		          : {
		              status: 1,
		              value: '',
		              description: 'No authored CSS initialization value.'
		            };
		      }
		      let computed = cache.computed.get(node);
		      if (!computed) {
		        computed = getComputedStyle(node);
		        cache.computed.set(node, computed);
		      }
		      const value = computed.getPropertyValue(property)
		        || computed[name.slice(6)];
		      return value === null || value === undefined || value === ''
		        ? { status: 1, value: '', description: 'Computed style is absent.' }
		        : { status: 0, value: String(value).trim(), description: 'Live computed style.' };
		    }
		    if (name.startsWith('rect.')) {
		      if (slot === 0) {
		        return {
		          status: 1,
		          value: '',
		          description: 'Runtime absolute geometry is not an initialization value.'
		        };
		      }
		      let rectangle = cache.rects.get(node);
		      if (!rectangle) {
		        rectangle = node.getBoundingClientRect();
		        cache.rects.set(node, rectangle);
		      }
		      const value = scalar(rectangle[name.slice(5)]);
		      return value === null
		        ? { status: 1, value: '', description: 'Runtime rectangle member is absent.' }
		        : { status: 0, value, description: 'Live getBoundingClientRect value.' };
		    }
		    if (name === 'content.ownText') {
		      const value = [...node.childNodes]
		        .filter(child => child.nodeType === Node.TEXT_NODE)
		        .map(child => child.nodeValue || '')
		        .join('');
		      return value.trim() === ''
		        ? { status: 1, value: '', description: 'Element has no own text.' }
		        : { status: 0, value, description: 'Live direct text-node content.' };
		    }
		    if ((name === 'content.value' || name === 'content.placeholder')
		        && slot === 0) {
		      return {
		        status: 1,
		        value: '',
		        description: 'Live form content is not an initialization value.'
		      };
		    }
		    if (name === 'state.valid'
		        || name === 'state.willValidate'
		        || name === 'state.validationMessage') {
		      if (slot === 0) {
		        return {
		          status: 1,
		          value: '',
		          description: 'Live validity is not an initialization value.'
		        };
		      }
		      const value = name === 'state.valid'
		        ? node.validity?.valid
		        : name === 'state.willValidate'
		          ? node.willValidate
		          : node.validationMessage;
		      const scalarValue = scalar(value);
		      return scalarValue === null
		        ? { status: 1, value: '', description: 'Validity member is absent.' }
		        : { status: 0, value: scalarValue, description: 'Live constraint-validation state.' };
		    }
		    if (name === 'content.namespaceUri') {
		      return {
		        status: 0,
		        value: node.namespaceURI || '',
		        description: 'Live DOM namespace URI.'
		      };
		    }
		    if (name === 'resource.imageSourceUrl') {
		      const value = slot === 0
		        ? scalar(node.getAttribute?.('src'))
		        : scalar(node.currentSrc || node.src);
		      return value === null || value === ''
		        ? { status: 1, value: '', description: 'Element has no image resource URL.' }
		        : { status: 0, value, description: 'Live selected image resource URL.' };
		    }
		    if (name === 'resource.mediaSourceUrl') {
		      const value = slot === 0
		        ? scalar(node.getAttribute?.('src'))
		        : scalar(node.currentSrc || node.src);
		      return value === null || value === ''
		        ? { status: 1, value: '', description: 'Element has no selected media resource URL.' }
		        : { status: 0, value, description: 'Live selected media resource URL.' };
		    }
		    if (name === 'resource.embeddedSourceUrl') {
		      const value = slot === 0
		        ? scalar(node.getAttribute?.(node.localName === 'object' ? 'data' : 'src'))
		        : scalar(node.data || node.src);
		      return value === null || value === ''
		        ? { status: 1, value: '', description: 'Element has no embedded resource URL.' }
		        : { status: 0, value, description: 'Live embedded resource URL.' };
		    }
		    if (name === 'resource.posterSourceUrl') {
		      const value = slot === 0
		        ? scalar(node.getAttribute?.('poster'))
		        : scalar(node.poster);
		      return value === null || value === ''
		        ? { status: 1, value: '', description: 'Element has no poster resource URL.' }
		        : { status: 0, value, description: 'Live resolved poster resource URL.' };
		    }
		    if (name === 'resource.canvasCommandStream') {
		      if (slot === 0) {
		        return {
		          status: 1,
		          value: '',
		          description: 'Canvas commands are runtime evidence.'
		        };
		      }
		      const commands = node.__iwesunCanvasCommands;
		      return Array.isArray(commands)
		        ? {
		            status: 0,
		            value: JSON.stringify(commands),
		            description: 'Bounded CanvasRenderingContext2D command stream.'
		          }
		        : {
		            status: 1,
		            value: '',
		            description: 'Canvas command tracking is absent.'
		          };
		    }
		    if (name.startsWith('effect.')) {
		      if (name === 'effect.pseudoElements') {
		        const pseudo = ['::before', '::after'].map(selector => {
		          const style = getComputedStyle(node, selector);
		          return `${selector}{content=${style.content};display=${style.display};`
		            + `position=${style.position};width=${style.width};height=${style.height};`
		            + `color=${style.color};background=${style.background}}`;
		        }).join('|');
		        return {
		          status: 0,
		          value: pseudo,
		          description: 'Live pseudo-element computed effect state.'
		        };
		      }
		      const animations = Array.from(node.getAnimations?.() || [])
		        .map(animation => {
		          const effect = animation.effect;
		          const timing = effect?.getTiming?.() || {};
		          const computed = effect?.getComputedTiming?.() || {};
		          const keyframes = (effect?.getKeyframes?.() || []).map(frame => {
		            const result = {};
		            for (const [key, value] of Object.entries(frame)) {
		              if (['string', 'number', 'boolean'].includes(typeof value)
		                  || value === null)
		                result[key] = value;
		            }
		            return result;
		          });
		          return {
		            animationName: String(animation.animationName || ''),
		            transitionProperty: String(animation.transitionProperty || ''),
		            playState: String(animation.playState || ''),
		            pending: Boolean(animation.pending),
		            replaceState: String(animation.replaceState || ''),
		            currentTime: animation.currentTime ?? null,
		            startTime: animation.startTime ?? null,
		            playbackRate: animation.playbackRate ?? 1,
		            id: String(animation.id || ''),
		            timing: {
		              delay: timing.delay ?? 0,
		              direction: String(timing.direction || 'normal'),
		              duration: timing.duration ?? 0,
		              easing: String(timing.easing || 'linear'),
		              endDelay: timing.endDelay ?? 0,
		              fill: String(timing.fill || 'none'),
		              iterations: timing.iterations ?? 1,
		              iterationStart: timing.iterationStart ?? 0
		            },
		            computed: {
		              progress: computed.progress ?? null,
		              currentIteration: computed.currentIteration ?? null,
		              activeDuration: computed.activeDuration ?? null,
		              endTime: computed.endTime ?? null,
		              localTime: computed.localTime ?? null
		            },
		            keyframes
		          };
		        });
		      return {
		        status: 0,
		        value: JSON.stringify({
		          schema: 'iwesun.web.animations/1',
		          animations
		        }),
		        description: 'Structured live Web Animations timeline state.'
		      };
		    }
		    if (ownerKind === 5) {
		      const eventName = name.startsWith('handler.on')
		        ? name.slice('handler.on'.length)
		        : name.startsWith('event.')
		          ? name.split('.')[1]
		          : name.startsWith('on') ? name.slice(2) : name;
		      const inlineSource = node.getAttribute?.('on' + eventName);
		      const captured = globalThis.__iwesunEventRegistry
		        ?.query(node, eventName) ?? null;
		      if (slot === 0) {
		        const registration = inlineSource !== null
		          ? {
		              eventName,
		              registrationKind: 'InlineAttribute',
		              useCapture: false,
		              passive: false,
		              once: false,
		              source: inlineSource
		            }
		          : captured?.registration;
		        return registration
		          ? {
		              status: 0,
		              value: JSON.stringify(registration),
		              description: 'Live DOM event registration.'
		            }
		          : {
		              status: 1,
		              value: '',
		              description: 'DOM event registration is absent.'
		            };
		      }
		      if (slot === 1) {
		        const link = inlineSource !== null
		          ? 'inline-handler:' + inlineSource
		          : captured?.registration
		            ? 'event-listener:' + eventName
		            : '';
		        return link
		          ? {
		              status: 0,
		              value: link,
		              description: 'Live DOM event link.',
		              valueSource: 2,
		              linkKind: 8,
		              linkIdentity: link
		            }
		          : {
		              status: 1,
		              value: '',
		              description: 'DOM event link is absent.'
		            };
		      }
		      return captured?.runtime
		        ? {
		            status: 0,
		            value: JSON.stringify(captured.runtime),
		            description: 'Live DOM event invocation evidence.'
		          }
		        : {
		            status: 1,
		            value: '',
		            description: 'DOM event invocation evidence is absent.'
		          };
		    }
		    if (ownerKind === 1 || ownerKind === 2) {
		      if (node.hasAttribute(name))
		        return { status: 0, value: node.getAttribute(name) || '', description: 'Live DOM attribute.' };
		      if (typeof node[name] === 'boolean' && node[name])
		        return { status: 0, value: 'true', description: 'Live boolean DOM state.' };
		      return { status: 1, value: '', description: 'DOM attribute is absent.' };
		    }
		    if (slot === 0 && name.startsWith('state.')) {
		      const member = name.slice(6);
		      const attribute = member === 'readOnly'
		        ? 'readonly'
		        : member === 'contentEditable'
		          ? 'contenteditable'
		          : member.toLowerCase();
		      if (node.hasAttribute?.(attribute)) {
		        const raw = node.getAttribute(attribute);
		        return {
		          status: 0,
		          value: raw === '' ? 'true' : String(raw),
		          description: 'DOM state initialization attribute.'
		        };
		      }
		      return {
		        status: 1,
		        value: '',
		        description: 'DOM state has no initialization attribute.'
		      };
		    }
		    if (slot === 0 && name.startsWith('content.')) {
		      const member = name.slice(8);
		      const attribute = member === 'ariaLabel'
		        ? 'aria-label'
		        : member;
		      if (node.hasAttribute?.(attribute)) {
		        return {
		          status: 0,
		          value: node.getAttribute(attribute) || '',
		          description: 'DOM content initialization attribute.'
		        };
		      }
		      if (member === 'value' && 'defaultValue' in node) {
		        return {
		          status: 0,
		          value: String(node.defaultValue || ''),
		          description: 'DOM defaultValue initialization.'
		        };
		      }
		      return {
		        status: 1,
		        value: '',
		        description: 'DOM content has no initialization value.'
		      };
		    }
		    const member = name.startsWith('state.') || name.startsWith('content.')
		      ? name.slice(name.indexOf('.') + 1)
		      : name;
		    const value = scalar(node[member]);
		    return value === null
		      ? { status: 1, value: '', description: 'DOM property is absent.' }
		      : { status: 0, value, description: 'Live DOM property.' };
		  };
		  const node = byXPath(path);
		  const result = !node || node.nodeType !== Node.ELEMENT_NODE
		    ? {
		        status: 2,
		        value: '',
		        description: 'Absolute XPath did not resolve to an element.'
		      }
		    : read(node);
		  return String(result.status)
		    + '\u001f' + encodeURIComponent(String(result.value || ''))
		    + '\u001f' + encodeURIComponent(String(result.description || ''))
		    + '\u001f' + String(result.valueSource || 0)
		    + '\u001f' + String(result.linkKind || 0)
		    + '\u001f' + encodeURIComponent(String(result.linkIdentity || ''));
		})()
		""";
}
