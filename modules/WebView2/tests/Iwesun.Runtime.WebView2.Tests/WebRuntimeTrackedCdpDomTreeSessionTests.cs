using System.Collections.Concurrent;
using Xunit;

namespace Iwesun.Runtime.WebView2.Tests;

public sealed class WebRuntimeTrackedCdpDomTreeSessionTests
{
	[Fact]
	public async Task ResolveXPath_UsesTrackedTreeAndCdpNodeId()
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		var html = await access.GetOuterHtmlAsync("/html/body/main");

		Assert.Equal("<main id=\"content\"></main>", html);
		var call = Assert.Single(devTools.Calls.Where(static item =>
			item.Method == "DOM.getOuterHTML"));
		Assert.Contains("\"nodeId\":4", call.ParametersJson);
		Assert.DoesNotContain("script", call.Method, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task AttributeEvent_PublishesOneNewAtomicTreeRevision()
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		await tree.InitializeAsync();
		var first = tree.Current!;

		devTools.Raise(
			"DOM.attributeModified",
			"""{"nodeId":4,"name":"class","value":"ready"}""");

		var second = tree.Current!;
		var node = second.ResolveXPath("document", "/html/body/main");
		Assert.True(second.Revision > first.Revision);
		Assert.Equal("ready", node.Attributes["class"]);
		Assert.Equal(second.Revision, tree.Current!.Revision);
	}

	[Fact]
	public async Task Click_UsesVisibleQuadAndVerifiedHitBeforeMouseInput()
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		var result = await access.ClickAsync("//main[@id='content']");

		Assert.Equal(4, result.NodeId);
		Assert.Equal(20, result.InputX);
		Assert.Equal(30, result.InputY);
		Assert.Equal(4, result.HitNodeId);
		Assert.Equal(104, result.HitBackendNodeId);
		var clickCalls = devTools.Calls.Where(static call =>
			call.Method is "Page.bringToFront"
				or "DOM.scrollIntoViewIfNeeded"
				or "DOM.getContentQuads"
				or "Page.getLayoutMetrics"
				or "DOM.getNodeForLocation"
				or "Input.dispatchMouseEvent").ToArray();
		Assert.Equal(
			[
				"Page.bringToFront",
				"DOM.scrollIntoViewIfNeeded",
				"DOM.getContentQuads",
				"Page.getLayoutMetrics",
				"DOM.getNodeForLocation",
				"Input.dispatchMouseEvent",
				"DOM.getNodeForLocation",
				"Input.dispatchMouseEvent",
				"Input.dispatchMouseEvent"
			],
			clickCalls.Select(static call => call.Method));
		Assert.Contains("\"backendNodeId\":104", clickCalls[1].ParametersJson);
		Assert.Contains("\"backendNodeId\":104", clickCalls[2].ParametersJson);
		Assert.Contains("\"type\":\"mouseMoved\"", clickCalls[5].ParametersJson);
		Assert.Contains("\"type\":\"mousePressed\"", clickCalls[7].ParametersJson);
		Assert.Contains("\"type\":\"mouseReleased\"", clickCalls[8].ParametersJson);
		Assert.DoesNotContain(devTools.Calls, static call =>
			call.Method is "Runtime.evaluate" or "Runtime.callFunctionOn");
	}

	[Fact]
	public async Task Click_WhenScrollFails_DoesNotReadBoxOrDispatchInput()
	{
		var devTools = new RecordingEventSession
		{
			FailMethod = "DOM.scrollIntoViewIfNeeded"
		};
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => access.ClickAsync("/html/body/main").AsTask());

		Assert.Contains(devTools.Calls, static call =>
			call.Method == "DOM.scrollIntoViewIfNeeded"
			&& call.ParametersJson.Contains(
				"\"backendNodeId\":104",
				StringComparison.Ordinal));
		Assert.DoesNotContain(devTools.Calls, static call =>
			call.Method is "DOM.getContentQuads"
				or "Page.getLayoutMetrics"
				or "DOM.getNodeForLocation"
				or "Input.dispatchMouseEvent"
				or "Runtime.evaluate"
				or "Runtime.callFunctionOn");
	}

	[Fact]
	public async Task Click_WhenSemanticNodeHasNoHit_UsesNearestVisibleAncestor()
	{
		var devTools = new RecordingEventSession
		{
			HitNodeId = 3,
			HitBackendNodeId = 103
		};
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		var result = await access.ClickAsync("/html/body/main");

		Assert.Equal(4, result.NodeId);
		Assert.Equal(3, result.HitNodeId);
		Assert.Equal(103, result.HitBackendNodeId);
		Assert.Contains(devTools.Calls, static call =>
			call.Method == "DOM.getNodeForLocation");
		Assert.Contains(devTools.Calls, static call =>
			call.Method == "Input.dispatchMouseEvent");
	}

	[Fact]
	public async Task Click_ClipsContentQuadToCssViewport()
	{
		var devTools = new RecordingEventSession
		{
			ContentQuadsJson =
				"""{"quads":[[-100,10,20,10,20,30,-100,30]]}"""
		};
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		var result = await access.ClickAsync("/html/body/main");

		Assert.Equal(10, result.InputX);
		Assert.Equal(20, result.InputY);
	}

	[Fact]
	public async Task Click_WhenPageIsScrolled_ConvertsPageQuadToViewportPoint()
	{
		var devTools = new RecordingEventSession
		{
			ContentQuadsJson =
				"""{"quads":[[210,320,230,320,230,340,210,340]]}""",
			LayoutMetricsJson =
				"""
				{"cssLayoutViewport":{
				  "pageX":200,"pageY":300,
				  "clientWidth":100,"clientHeight":80}}
				"""
		};
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		var result = await access.ClickAsync("/html/body/main");

		Assert.Equal(20, result.InputX);
		Assert.Equal(30, result.InputY);
	}

	[Fact]
	public async Task Click_WhenQuadCenterIsCovered_UsesVerifiedInnerPoint()
	{
		var devTools = new RecordingEventSession
		{
			CenterHitUsesAncestor = true
		};
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		var result = await access.ClickAsync("/html/body/main");

		Assert.NotEqual(20, result.InputX);
		Assert.NotEqual(30, result.InputY);
		Assert.Equal(4, result.HitNodeId);
	}

	[Theory]
	[InlineData("//main[@id='content']")]
	[InlineData("/html/body/main[@id=\"content\"]")]
	[InlineData("//*[@id='content']")]
	public async Task ResolveXPath_NavigatesCommonXPathSubsetWithoutScript(
		string xpath)
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);

		var node = await tree.ResolveXPathAsync(xpath);

		Assert.Equal(4, node.NodeId);
		Assert.DoesNotContain(devTools.Calls, static call =>
			call.Method.Contains("Runtime.", StringComparison.Ordinal)
			|| call.Method.Contains("script", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public async Task ResolveNodeId_ReusesRevisionLocalXPathResult()
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);

		var first = await tree.ResolveNodeIdAsync("//main[@id='content']");
		var second = await tree.ResolveNodeIdAsync("//main[@id='content']");

		Assert.Equal(4, first);
		Assert.Equal(first, second);
		Assert.Equal(1, devTools.Calls.Count(static call =>
			call.Method == "DOM.getDocument"));
	}

	[Fact]
	public async Task Tree_IsLinkedBeforeAuxiliaryIndexesArePublished()
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		await tree.InitializeAsync();

		var index = tree.Current!;
		var html = index.ResolveXPath("document", "/html");
		var body = Assert.Single(html.Children);
		var main = Assert.Single(body.Children);

		Assert.Null(html.Parent);
		Assert.Same(html, body.Parent);
		Assert.Same(body, main.Parent);
		Assert.Same(main, index.ResolveXPath(
			"document",
			"//main[@id='content']"));
		Assert.True(index.TryGetNode(main.NodeId, out var indexed));
		Assert.Same(main, indexed);
	}

	[Fact]
	public async Task StructuralEvent_RebuildsBeforeNextXPathAccess()
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		await tree.InitializeAsync();
		devTools.DocumentVersion = 2;

		devTools.Raise("DOM.documentUpdated", "{}");
		var node = await tree.ResolveXPathAsync("/html/body/section");

		Assert.Equal(5, node.NodeId);
		Assert.True(devTools.Calls.Count(static item =>
			item.Method == "DOM.getDocument") >= 2);
	}

	[Theory]
	[InlineData("Page.navigatedWithinDocument")]
	[InlineData("Page.frameNavigated")]
	public async Task NavigationEvent_RebuildsBeforeNextXPathAccess(
		string eventName)
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		await tree.InitializeAsync();
		devTools.DocumentVersion = 2;

		devTools.Raise(
			eventName,
			"""{"frameId":"main","url":"https://example.test/chat/2"}""");
		var node = await tree.ResolveXPathAsync("/html/body/section");

		Assert.Equal(5, node.NodeId);
		Assert.Contains(devTools.Calls, static item =>
			item.Method == "Page.enable");
		Assert.True(devTools.Calls.Count(static item =>
			item.Method == "DOM.getDocument") >= 2);
	}

	[Fact]
	public async Task Refresh_DoesNotExposeOldTreeBeforeNewRevisionIsPublished()
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		await tree.InitializeAsync();
		devTools.DocumentVersion = 2;
		var block = devTools.BlockNextDocumentRead();

		tree.Invalidate();
		await block.Started.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Null(tree.Current);
		block.Release();
		var snapshot = await tree.ReadDomTreeAsync();
		Assert.Equal(5, tree.Current!.ResolveNodeId(
			"document",
			"/html/body/section"));
		Assert.Equal(tree.Current.Revision, snapshot.Revision);
	}

	[Fact]
	public async Task ExplicitRefresh_WhenInvalidatedDuringRead_RetriesUntilPublished()
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		await tree.InitializeAsync();
		devTools.DocumentVersion = 2;
		var block = devTools.BlockNextDocumentRead();

		var refresh = tree.RefreshAsync().AsTask();
		await block.Started.WaitAsync(TimeSpan.FromSeconds(5));
		devTools.Raise("DOM.documentUpdated", "{}");
		block.Release();
		await refresh;

		Assert.NotNull(tree.Current);
		Assert.Equal(5, tree.Current!.ResolveNodeId(
			"document",
			"/html/body/section"));
		Assert.True(devTools.Calls.Count(static item =>
			item.Method == "DOM.getDocument") >= 3);
	}

	[Fact]
	public async Task LiveLookup_WhenInvalidatedDuringRecovery_PublishesBeforeUse()
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		await tree.InitializeAsync();
		devTools.DocumentVersion = 2;
		var block = devTools.BlockNextDocumentRead();
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		var focus = access.FocusAsync("/html/body/section").AsTask();
		await block.Started.WaitAsync(TimeSpan.FromSeconds(5));
		devTools.Raise("DOM.documentUpdated", "{}");
		block.Release();
		var result = await focus;

		Assert.Equal(5, result.NodeId);
		Assert.NotNull(tree.Current);
		Assert.True(devTools.Calls.Count(static item =>
			item.Method == "DOM.getDocument") >= 3);
	}

	[Theory]
	[InlineData("The CDP DOM tree has no current revision.")]
	[InlineData("The tracked CDP DOM tree refresh produced no current tree.")]
	public void InvalidatedCurrentTree_IsClassifiedAsExpiredNodeIdentity(
		string message)
	{
		var method = typeof(WebRuntimeTrackedCdpDomTreeSession).GetMethod(
			"IsInvalidNodeIdentityException",
			System.Reflection.BindingFlags.Static
				| System.Reflection.BindingFlags.NonPublic);
		Assert.NotNull(method);
		Assert.True((bool)method.Invoke(
			null,
			[new InvalidOperationException(message)])!);
	}

	[Fact]
	public async Task LiveLookup_WhenDomEventWasMissed_RefreshesMismatchedTree()
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		await tree.InitializeAsync();
		devTools.DocumentVersion = 2;
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		var result = await access.FocusAsync("/html/body/section");

		Assert.Equal(5, result.NodeId);
		Assert.Equal(205, result.BackendNodeId);
		Assert.True(devTools.Calls.Count(static item =>
			item.Method == "DOM.getDocument") >= 2);
		Assert.DoesNotContain(devTools.Calls, static item =>
			item.Method == "DOM.performSearch");
	}

	[Fact]
	public async Task LiveOperation_WhenNodeIdExpires_RefreshesAndRetriesOnce()
	{
		var devTools = new RecordingEventSession
		{
			FailOnceMethod = "DOM.getOuterHTML",
			FailureMessage = "Could not find node with given id"
		};
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		var html = await access.GetOuterHtmlAsync("/html/body/main");

		Assert.Equal("<main id=\"content\"></main>", html);
		Assert.Equal(2, devTools.Calls.Count(static item =>
			item.Method == "DOM.getOuterHTML"));
		Assert.True(devTools.Calls.Count(static item =>
			item.Method == "DOM.getDocument") >= 2);
	}

	[Fact]
	public async Task LiveResolution_UsesTrackedScopedIdentityWithoutGlobalSearch()
	{
		var devTools = new RecordingEventSession();
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		var attributes = await access.GetAttributesAsync("/html/body/main");

		Assert.Empty(attributes);
		Assert.DoesNotContain(devTools.Calls, static item =>
			item.Method is "DOM.performSearch" or "DOM.describeNode");
	}

	[Fact]
	public async Task LiveOperation_WhenNodeIdRemainsExpired_RetriesOnlyOnce()
	{
		var devTools = new RecordingEventSession
		{
			FailMethod = "DOM.getOuterHTML",
			FailureMessage = "Invalid node id"
		};
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		await Assert.ThrowsAsync<InvalidOperationException>(
			() => access.GetOuterHtmlAsync("/html/body/main").AsTask());

		Assert.Equal(2, devTools.Calls.Count(static item =>
			item.Method == "DOM.getOuterHTML"));
	}

	[Fact]
	public async Task LiveLookup_WhenXPathIsAbsent_DoesNotRunGlobalSearch()
	{
		var devTools = new RecordingEventSession
		{
			SearchResultCount = 0
		};
		await using var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		var access = new WebRuntimeCdpDomAccess(devTools, tree);

		await Assert.ThrowsAsync<KeyNotFoundException>(
			() => access.GetAttributesAsync("/html/body/missing").AsTask());

		Assert.DoesNotContain(devTools.Calls, static item =>
			item.Method is "DOM.performSearch" or "DOM.discardSearchResults");
		Assert.DoesNotContain(devTools.Calls, static item =>
			item.Method == "DOM.getAttributes");
	}

	[Fact]
	public async Task Dispose_DetachesEveryCdpEvent()
	{
		var devTools = new RecordingEventSession();
		var tree = new WebRuntimeTrackedCdpDomTreeSession(devTools);
		await tree.InitializeAsync();
		Assert.True(devTools.SubscriptionCount > 0);

		await tree.DisposeAsync();

		Assert.Equal(0, devTools.SubscriptionCount);
	}

	private sealed class RecordingEventSession : IWebRuntimeDevToolsSession
	{
		private readonly ConcurrentDictionary<string,
			List<EventHandler<WebRuntimeDevToolsProtocolEventArgs>>> _handlers =
			new(StringComparer.Ordinal);

		public string CurrentUrl => "https://example.test/";

		public int DocumentVersion { get; set; } = 1;

		public string? FailMethod { get; init; }

		public string? FailOnceMethod { get; init; }

		public string FailureMessage { get; init; } =
			"Injected CDP failure.";

		public int SearchResultCount { get; init; } = 1;

		private int _failOnceObserved;
		private DocumentReadBlock? _documentReadBlock;

		public string ContentQuadsJson { get; init; } =
			"""{"quads":[[10,20,30,20,30,40,10,40]]}""";

		public string LayoutMetricsJson { get; init; } =
			"""{"cssLayoutViewport":{"clientWidth":100,"clientHeight":80}}""";

		public int HitNodeId { get; init; } = 4;

		public int HitBackendNodeId { get; init; } = 104;

		public bool CenterHitUsesAncestor { get; init; }

		public List<(string Method, string ParametersJson)> Calls { get; } = [];

		public int SubscriptionCount =>
			_handlers.Values.Sum(static handlers => handlers.Count);

		public (Task Started, Action Release) BlockNextDocumentRead()
		{
			var block = new DocumentReadBlock();
			if (Interlocked.CompareExchange(
					ref _documentReadBlock,
					block,
					null) is not null)
			{
				throw new InvalidOperationException(
					"A document read is already blocked.");
			}
			return (
				block.Started.Task,
				() => block.Release.TrySetResult());
		}

		public Task<string> CallDevToolsProtocolMethodAsync(
			string method,
			string parametersJson,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			lock (Calls)
				Calls.Add((method, parametersJson));
			if (string.Equals(method, FailMethod, StringComparison.Ordinal))
			{
				throw new InvalidOperationException(FailureMessage);
			}
			if (string.Equals(method, FailOnceMethod, StringComparison.Ordinal)
				&& Interlocked.Exchange(ref _failOnceObserved, 1) == 0)
			{
				throw new InvalidOperationException(FailureMessage);
			}
			if (method == "DOM.getDocument"
				&& Interlocked.Exchange(
					ref _documentReadBlock,
					null) is { } block)
			{
				return CompleteDocumentReadAsync(
					block,
					DocumentVersion,
					ct);
			}
			return Task.FromResult(method switch
			{
				"DOM.getDocument" => CreateDocument(DocumentVersion),
				"DOM.performSearch" =>
					$$"""{"searchId":"runtime-search","resultCount":{{SearchResultCount}}}""",
				"DOM.getSearchResults" => DocumentVersion == 1
					? """{"nodeIds":[4]}"""
					: """{"nodeIds":[5]}""",
				"DOM.describeNode" => DocumentVersion == 1
					? """{"node":{"nodeId":4,"backendNodeId":104}}"""
					: """{"node":{"nodeId":5,"backendNodeId":205}}""",
				"DOM.getOuterHTML" =>
					"""{"outerHTML":"<main id=\"content\"></main>"}""",
				"DOM.getContentQuads" => ContentQuadsJson,
				"Page.getLayoutMetrics" => LayoutMetricsJson,
				"DOM.getNodeForLocation" => CreateHit(parametersJson),
				_ => "{}"
			});
		}

		private static async Task<string> CompleteDocumentReadAsync(
			DocumentReadBlock block,
			int documentVersion,
			CancellationToken cancellationToken)
		{
			block.Started.TrySetResult();
			await block.Release.Task.WaitAsync(cancellationToken);
			return CreateDocument(documentVersion);
		}

		private string CreateHit(string parametersJson)
		{
			if (CenterHitUsesAncestor
				&& parametersJson.Contains("\"x\":20", StringComparison.Ordinal)
				&& parametersJson.Contains("\"y\":30", StringComparison.Ordinal))
			{
				return """{"nodeId":3,"backendNodeId":103}""";
			}
			return $$"""{"nodeId":{{HitNodeId}},"backendNodeId":{{HitBackendNodeId}}}""";
		}

		public IDisposable SubscribeDevToolsProtocolEvent(
			string eventName,
			EventHandler<WebRuntimeDevToolsProtocolEventArgs> handler)
		{
			var handlers = _handlers.GetOrAdd(eventName, static _ => []);
			lock (handlers)
				handlers.Add(handler);
			return new Subscription(handlers, handler);
		}

		public void Raise(string eventName, string json)
		{
			if (!_handlers.TryGetValue(eventName, out var handlers))
				return;
			EventHandler<WebRuntimeDevToolsProtocolEventArgs>[] snapshot;
			lock (handlers)
				snapshot = handlers.ToArray();
			foreach (var handler in snapshot)
				handler(this, new(json));
		}

		private static string CreateDocument(int version) =>
			version == 1
				? """
				  {
				    "root": {
				      "nodeId": 1, "backendNodeId": 101, "nodeType": 9,
				      "nodeName": "#document",
				      "documentURL": "https://example.test/",
				      "children": [{
				        "nodeId": 2, "backendNodeId": 102, "nodeType": 1,
				        "nodeName": "HTML", "localName": "html",
				        "children": [{
				          "nodeId": 3, "backendNodeId": 103, "nodeType": 1,
				          "nodeName": "BODY", "localName": "body",
				          "children": [{
				            "nodeId": 4, "backendNodeId": 104, "nodeType": 1,
				            "nodeName": "MAIN", "localName": "main",
				            "attributes": ["id", "content"], "children": []
				          }]
				        }]
				      }]
				    }
				  }
				  """
				: """
				  {
				    "root": {
				      "nodeId": 1, "backendNodeId": 201, "nodeType": 9,
				      "nodeName": "#document",
				      "documentURL": "https://example.test/",
				      "children": [{
				        "nodeId": 2, "backendNodeId": 202, "nodeType": 1,
				        "nodeName": "HTML", "localName": "html",
				        "children": [{
				          "nodeId": 3, "backendNodeId": 203, "nodeType": 1,
				          "nodeName": "BODY", "localName": "body",
				          "children": [{
				            "nodeId": 5, "backendNodeId": 205, "nodeType": 1,
				            "nodeName": "SECTION", "localName": "section",
				            "attributes": [], "children": []
				          }]
				        }]
				      }]
				    }
				  }
				  """;

		private sealed class Subscription(
			List<EventHandler<WebRuntimeDevToolsProtocolEventArgs>> handlers,
			EventHandler<WebRuntimeDevToolsProtocolEventArgs> handler)
			: IDisposable
		{
			private List<EventHandler<WebRuntimeDevToolsProtocolEventArgs>>?
				_handlers = handlers;
			private EventHandler<WebRuntimeDevToolsProtocolEventArgs>? _handler =
				handler;

			public void Dispose()
			{
				var handlers = Interlocked.Exchange(ref _handlers, null);
				var handler = Interlocked.Exchange(ref _handler, null);
				if (handlers is null || handler is null)
					return;
				lock (handlers)
					handlers.Remove(handler);
			}
		}

		private sealed class DocumentReadBlock
		{
			public TaskCompletionSource Started { get; } = new(
				TaskCreationOptions.RunContinuationsAsynchronously);

			public TaskCompletionSource Release { get; } = new(
				TaskCreationOptions.RunContinuationsAsynchronously);
		}
	}
}
