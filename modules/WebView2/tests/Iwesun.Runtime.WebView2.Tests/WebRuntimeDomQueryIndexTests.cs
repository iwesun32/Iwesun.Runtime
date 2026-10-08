using Xunit;

namespace Iwesun.Runtime.WebView2.Tests;

public sealed class WebRuntimeDomQueryIndexTests
{
	[Fact]
	public void CdpSnapshot_ClientRectangle_IsParsedAndGeometryScaled()
	{
		const string json = """
			{
			  "strings": ["DIV", ""],
			  "documents": [
			    {
			      "nodes": {
			        "backendNodeId": [41],
			        "nodeName": [0],
			        "nodeValue": [1]
			      },
			      "layout": {
			        "nodeIndex": [0],
			        "bounds": [[0, 0, 718.4, 100]],
			        "clientRects": [[0, 0, 703.2, 100]],
			        "scrollRects": [[0, 0, 703.2, 300]],
			        "styles": [[]]
			      }
			    }
			  ]
			}
			""";
		var parse = typeof(WebRuntimeCdpEvidenceSnapshot).GetMethod(
			"Parse",
			System.Reflection.BindingFlags.Static
				| System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(parse);
		var snapshot = parse.Invoke(
			null,
			[new Uri("https://example.test/"), json, Array.Empty<string>(), 2d]);
		Assert.NotNull(snapshot);
		var tryGetNode = snapshot.GetType().GetMethod(
			"TryGetNode",
			System.Reflection.BindingFlags.Instance
				| System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(tryGetNode);
		object?[] arguments = [41, null];
		Assert.True(Assert.IsType<bool>(tryGetNode.Invoke(snapshot, arguments)));
		var evidence = arguments[1]
			?? throw new InvalidOperationException("Expected parsed node evidence.");
		var bounds = Assert.IsAssignableFrom<IReadOnlyList<double>>(
			evidence.GetType().GetProperty("Bounds")?.GetValue(evidence));
		var clientRect = Assert.IsAssignableFrom<IReadOnlyList<double>>(
			evidence.GetType().GetProperty("ClientRect")?.GetValue(evidence));
		var scrollRect = Assert.IsAssignableFrom<IReadOnlyList<double>>(
			evidence.GetType().GetProperty("ScrollRect")?.GetValue(evidence));

		Assert.Equal(1436.8, bounds[2], 6);
		Assert.Equal(1406.4, clientRect[2], 6);
		Assert.Equal(600, scrollRect[3], 6);
		Assert.Equal(30.4, bounds[2] - clientRect[2], 6);
	}

	[Theory]
	[InlineData("color", true)]
	[InlineData("font-size", true)]
	[InlineData("--application-theme", true)]
	[InlineData("width", false)]
	[InlineData("height", false)]
	[InlineData("padding-left", false)]
	[InlineData("display", false)]
	public void MatchedStyleInheritance_OnlyAdmitsCssInheritedProperties(
		string propertyName,
		bool expected)
	{
		var method = typeof(WebRuntimeCdpDomEvidenceReader).GetMethod(
			"IsInheritedCssProperty",
			System.Reflection.BindingFlags.Static
				| System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(method);
		Assert.Equal(expected, method.Invoke(null, [propertyName]));
	}

	[Theory]
	[InlineData("fontSize", "font-size")]
	[InlineData("--LarkSurfaceColor", "--LarkSurfaceColor")]
	public void CssNameConversion_PreservesCaseSensitiveCustomProperties(
		string propertyName,
		string expected)
	{
		var method = typeof(WebRuntimeCdpDomEvidenceReader).GetMethod(
			"ToCssName",
			System.Reflection.BindingFlags.Static
				| System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(method);
		Assert.Equal(expected, method.Invoke(null, [propertyName]));
	}

	[Fact]
	public void CdpEvidenceCaches_AccumulateWithinRevision_AndResetOnRevision()
	{
		var pageUri = new Uri("https://example.test/");
		var current = new WebRuntimeDomTreeSnapshot(
			1, pageUri, DateTimeOffset.UtcNow, [], []);
		var reader = new WebRuntimeCdpDomEvidenceReader(
			new NoOpDevToolsSession(),
			() => current);
		var publishFonts = typeof(WebRuntimeCdpDomEvidenceReader).GetMethod(
			"PublishPlatformFontEvidence",
			System.Reflection.BindingFlags.Instance
				| System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(publishFonts);
		publishFonts.Invoke(reader,
			[current, FontMap(11, "Font A"), FontRunMap(11, "Font A")]);
		publishFonts.Invoke(reader,
			[current, FontMap(12, "Font B"), FontRunMap(12, "Font B")]);
		Assert.Equal(2, reader.LastPlatformFontRuns.Count);

		var ensureMatched = typeof(WebRuntimeCdpDomEvidenceReader).GetMethod(
			"EnsureMatchedStyleEvidenceRevision",
			System.Reflection.BindingFlags.Instance
				| System.Reflection.BindingFlags.NonPublic);
		var storeMatched = typeof(WebRuntimeCdpDomEvidenceReader).GetMethod(
			"StoreMatchedStyleEvidence",
			System.Reflection.BindingFlags.Instance
				| System.Reflection.BindingFlags.NonPublic);
		var tryReadMatched = typeof(WebRuntimeCdpDomEvidenceReader).GetMethod(
			"TryReadMatchedStyleEvidence",
			System.Reflection.BindingFlags.Instance
				| System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(ensureMatched);
		Assert.NotNull(storeMatched);
		Assert.NotNull(tryReadMatched);
		var rawType = typeof(WebRuntimeCdpDomEvidenceReader).Assembly.GetType(
			"Iwesun.Runtime.WebView2.CdpMatchedStyleRawEvidence");
		Assert.NotNull(rawType);
		var raw = Activator.CreateInstance(rawType, "{}", "", "");
		Assert.NotNull(raw);
		ensureMatched.Invoke(reader, [current]);
		storeMatched.Invoke(reader, [11, raw]);
		object?[] readArguments = [11, null];
		Assert.True(Assert.IsType<bool>(tryReadMatched.Invoke(reader, readArguments)));
		Assert.Same(raw, readArguments[1]);

		current = current with { Revision = 2 };
		publishFonts.Invoke(reader,
			[current, FontMap(13, "Font C"), FontRunMap(13, "Font C")]);
		Assert.Single(reader.LastPlatformFontRuns);
		Assert.True(reader.LastPlatformFontRuns.ContainsKey(13));
		ensureMatched.Invoke(reader, [current]);
		readArguments = [11, null];
		Assert.False(Assert.IsType<bool>(tryReadMatched.Invoke(reader, readArguments)));
	}

	private static IReadOnlyDictionary<int, WebRuntimeCdpPlatformFontEvidence>
		FontMap(int nodeId, string family) =>
		new Dictionary<int, WebRuntimeCdpPlatformFontEvidence>
		{
			[nodeId] = new(nodeId, family, family, 1, false)
		};

	private static IReadOnlyDictionary<
		int,
		IReadOnlyList<WebRuntimeCdpPlatformFontEvidence>>
		FontRunMap(int nodeId, string family) =>
		new Dictionary<int, IReadOnlyList<WebRuntimeCdpPlatformFontEvidence>>
		{
			[nodeId] = [new(nodeId, family, family, 1, false)]
		};

	private sealed class NoOpDevToolsSession : IWebRuntimeDevToolsSession
	{
		public string CurrentUrl => "https://example.test/";

		public Task<string> CallDevToolsProtocolMethodAsync(
			string method,
			string parametersJson,
			CancellationToken ct) =>
			Task.FromException<string>(new InvalidOperationException(
				$"Unexpected CDP call: {method} {parametersJson}"));
	}

	[Fact]
	public void MatchedStyleCascade_ImportantRuleOverridesNormalInlineValue()
	{
		var declarations = IndexDeclarations(
			"""
			{
			  "inlineStyle": { "cssProperties": [
			    { "name": "height", "value": "initial" }
			  ] },
			  "matchedCSSRules": [ { "rule": {
			    "selectorList": { "text": ".fill" },
			    "style": { "cssProperties": [
			      { "name": "height", "value": "100%", "important": true }
			    ] }
			  } } ]
			}
			""");

		var height = ReadDeclaration(declarations, "height");
		Assert.Equal("100%", height.Value);
		Assert.True(height.Important);
	}

	[Fact]
	public void MatchedStyleCascade_ImportantInlineValueRetainsPriority()
	{
		var declarations = IndexDeclarations(
			"""
			{
			  "inlineStyle": { "cssProperties": [
			    { "name": "height", "value": "40px", "important": true }
			  ] },
			  "matchedCSSRules": [ { "rule": {
			    "selectorList": { "text": ".fill" },
			    "style": { "cssProperties": [
			      { "name": "height", "value": "100%", "important": true }
			    ] }
			  } } ]
			}
			""");

		Assert.Equal(
			"40px",
			ReadDeclaration(declarations, "height").Value);
	}

	[Fact]
	public void MatchedStyleCascade_HigherPriorityNormalRuleWins()
	{
		var declarations = IndexDeclarations(
			"""
			{
			  "matchedCSSRules": [
			    { "rule": { "selectorList": { "text": ".low" },
			      "style": { "cssProperties": [
			        { "name": "height", "value": "initial" }
			      ] } } },
			    { "rule": { "selectorList": { "text": ".high" },
			      "style": { "cssProperties": [
			        { "name": "height", "value": "100%" }
			      ] } } }
			  ]
			}
			""");

		Assert.Equal(
			"100%",
			ReadDeclaration(declarations, "height").Value);
	}

	private static object IndexDeclarations(string json)
	{
		using var document = System.Text.Json.JsonDocument.Parse(json);
		var method = typeof(WebRuntimeCdpDomEvidenceReader).GetMethod(
			"IndexDeclarations",
			System.Reflection.BindingFlags.Static
				| System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(method);
		var result = method.Invoke(
				null,
				[
					document.RootElement,
					new HashSet<string>(["height"], StringComparer.OrdinalIgnoreCase)
				]);
		Assert.NotNull(result);
		return result;
	}

	private static TestCssDeclaration ReadDeclaration(
		object declarations,
		string name)
	{
		var item = declarations.GetType().GetProperty("Item");
		Assert.NotNull(item);
		var declaration = item.GetValue(declarations, [name]);
		Assert.NotNull(declaration);
		var value = declaration.GetType().GetProperty("Value")?.GetValue(declaration);
		var important = declaration.GetType().GetProperty("Important")?.GetValue(declaration);
		return new(
			Assert.IsType<string>(value),
			Assert.IsType<bool>(important));
	}

	private sealed record TestCssDeclaration(string Value, bool Important);

	[Fact]
	public async Task QueryDomPropertiesAsync_PreservesBatchOrder()
	{
		var first = Identity("/html/body/div[1]", "id");
		var second = Identity("/html/body/div[2]", "class");
		var session = new WebRuntimeIndexedDomQuerySession(
			Index(
				1,
				Captured(first, "first"),
				Captured(second, "second")));

		var results = await session.QueryDomPropertiesAsync(
			[
				Request("b", second),
				Request("a", first)
			]);

		Assert.Equal(["b", "a"], results.Select(static item => item.QueryId));
		Assert.Equal(
			["second", "first"],
			results.Select(static item => item.Value));
	}

	[Fact]
	public async Task QueryDomPropertiesAsync_MissingIdentity_Throws()
	{
		var stored = Identity("/html/body/div", "id");
		var missing = Identity("/html/body/span", "id");
		var session = new WebRuntimeIndexedDomQuerySession(
			Index(1, Captured(stored, "stored")));

		await Assert.ThrowsAsync<InvalidDataException>(
			() => session.QueryDomPropertiesAsync(
				[Request("missing", missing)]).AsTask());
	}

	[Fact]
	public async Task ReplaceIndex_AtomicallyExposesNewRevision()
	{
		var identity = Identity("/html/body/div", "id");
		var session = new WebRuntimeIndexedDomQuerySession(
			Index(1, Captured(identity, "old")));

		session.ReplaceIndex(Index(2, Captured(identity, "new")));
		var results = await session.QueryDomPropertiesAsync(
			[Request("id", identity)]);

		Assert.Equal(2, session.CurrentIndex.Revision);
		Assert.Equal("new", Assert.Single(results).Value);
	}

	[Fact]
	public void ReplaceIndex_WithStaleRevision_Throws()
	{
		var identity = Identity("/html/body/div", "id");
		var session = new WebRuntimeIndexedDomQuerySession(
			Index(2, Captured(identity, "current")));

		Assert.Throws<InvalidOperationException>(
			() => session.ReplaceIndex(
				Index(2, Captured(identity, "stale"))));
	}

	[Fact]
	public void IndexBuilder_WithPendingIdentity_RejectsBuild()
	{
		var first = Identity("/html/body/div[1]", "id");
		var second = Identity("/html/body/div[2]", "id");
		var builder = Builder(first, second);
		builder.SetCaptured(
			first,
			"first",
			WebRuntimeDomValueSource.DirectConstant);

		Assert.Throws<InvalidDataException>(() => builder.Build());
		Assert.Equal(1, builder.CompletedCount);
		Assert.Equal(1, builder.PendingCount);
	}

	[Fact]
	public void IndexBuilder_WithDuplicateResult_RejectsSecondWrite()
	{
		var identity = Identity("/html/body/div", "id");
		var builder = Builder(identity);
		builder.SetConfirmedAbsent(identity, "absent");

		Assert.Throws<InvalidOperationException>(
			() => builder.SetConfirmedAbsent(identity, "again"));
	}

	[Fact]
	public void IndexBuilder_WithIdentityOutsidePlan_RejectsWrite()
	{
		var planned = Identity("/html/body/div", "id");
		var outside = Identity("/html/body/span", "id");
		var builder = Builder(planned);

		Assert.Throws<InvalidDataException>(
			() => builder.SetSourceUnsupported(outside, "unsupported"));
	}

	[Fact]
	public void IndexBuilder_WithCompleteExplicitResults_Builds()
	{
		var captured = Identity("/html/body/div[1]", "id");
		var absent = Identity("/html/body/div[2]", "id");
		var unsupported = Identity("/html/body/div[3]", "id");
		var builder = Builder(captured, absent, unsupported);
		builder.SetCaptured(
			captured,
			"app",
			WebRuntimeDomValueSource.DirectConstant);
		builder.SetConfirmedAbsent(absent, "attribute absent");
		builder.SetSourceUnsupported(unsupported, "source unsupported");

		var index = builder.Build();

		Assert.Equal(3, index.PropertyCount);
		Assert.Equal(0, builder.PendingCount);
	}

	[Fact]
	public async Task LiveSession_ReadsWholeBatchAndPublishesIndex()
	{
		var first = Identity("/html/body/div[1]", "id");
		var second = Identity("/html/body/div[2]", "id");
		var reader = new TestEvidenceReader(
			Captured(first, "first"),
			Captured(second, "second"));
		var session = new WebRuntimeLiveDomQuerySession(reader);

		var results = await session.QueryDomPropertiesAsync(
			[
				Request("first", first),
				Request("second", second)
			]);

		Assert.Equal(1, reader.ReadCount);
		Assert.Equal(2, results.Count);
		Assert.Equal(2, session.LastIndex?.PropertyCount);
	}

	[Fact]
	public async Task LiveSession_DirectMode_ReturnsValidatedResultsWithoutIndex()
	{
		var first = Identity("/html/body/div[1]", "id");
		var second = Identity("/html/body/div[2]", "id");
		var reader = new TestEvidenceReader(
			Captured(first, "first"),
			Captured(second, "second"));
		var session = new WebRuntimeLiveDomQuerySession(
			reader,
			retainQueryIndex: false);

		var results = await session.QueryDomPropertiesAsync(
			[
				Request("first", first),
				Request("second", second)
			]);

		Assert.Equal(["first", "second"],
			results.Select(static result => result.Value));
		Assert.Null(session.LastIndex);
		Assert.Equal(1, reader.ReadCount);
	}

	[Fact]
	public async Task LiveSession_WithIncompleteReaderResult_RejectsIndex()
	{
		var first = Identity("/html/body/div[1]", "id");
		var second = Identity("/html/body/div[2]", "id");
		var reader = new TestEvidenceReader(Captured(first, "first"));
		var session = new WebRuntimeLiveDomQuerySession(reader);

		await Assert.ThrowsAsync<InvalidDataException>(
			() => session.QueryDomPropertiesAsync(
				[
					Request("first", first),
					Request("second", second)
				]).AsTask());

		Assert.Null(session.LastIndex);
	}

	private static WebRuntimeDomQueryIndex Index(
		long revision,
		params WebRuntimeDomIndexedProperty[] properties) =>
		new(
			revision,
			new Uri("https://www.doubao.com/"),
			DateTimeOffset.UtcNow,
			properties);

	private static WebRuntimeDomIndexedProperty Captured(
		WebRuntimeDomPropertyIdentity identity,
		string value) =>
		new(
			identity,
			WebRuntimeDomPropertyStatus.Captured,
			value,
			WebRuntimeDomValueSource.DirectConstant,
			WebRuntimeDomLinkKind.None,
			string.Empty,
			"test");

	private static WebRuntimeDomPropertyIdentity Identity(
		string xpath,
		string propertyName) =>
		new(
			"document",
			xpath,
			"div",
			propertyName,
			propertyName,
			WebRuntimeDomOwnerKind.Attribute,
			WebRuntimeDomSlotCategory.DataOrganization,
			WebRuntimeDomEvidenceKind.Attributes,
			WebRuntimeDomDataSlot.Initialization);

	private static WebRuntimeDomPropertyRequest Request(
		string queryId,
		WebRuntimeDomPropertyIdentity identity) =>
		new(
			queryId,
			identity.DocumentScope,
			identity.XPath,
			identity.TagName,
			identity.PropertyName,
			identity.ReflectedPropertyName,
			identity.OwnerKind,
			identity.Category,
			identity.EvidenceKind,
			identity.Slot);

	private static WebRuntimeDomQueryIndexBuilder Builder(
		params WebRuntimeDomPropertyIdentity[] identities) =>
		new(
			1,
			new Uri("https://www.doubao.com/"),
			DateTimeOffset.UtcNow,
			identities.Select((identity, index) =>
				Request(index.ToString(
					System.Globalization.CultureInfo.InvariantCulture),
					identity)));

	private sealed class TestEvidenceReader(
		params WebRuntimeDomIndexedProperty[] properties)
		: IWebRuntimeDomEvidenceReader
	{
		public Uri CurrentPageUrl { get; } =
			new("https://www.doubao.com/");

		public int ReadCount { get; private set; }

		public ValueTask<IReadOnlyList<WebRuntimeDomIndexedProperty>>
			ReadDomPropertiesAsync(
				IReadOnlyList<WebRuntimeDomPropertyRequest> requests,
				CancellationToken cancellationToken = default)
		{
			ReadCount++;
			return ValueTask.FromResult<
				IReadOnlyList<WebRuntimeDomIndexedProperty>>(properties);
		}
	}
}
