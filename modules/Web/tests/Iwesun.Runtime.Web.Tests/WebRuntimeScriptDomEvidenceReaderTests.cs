using Xunit;
using Iwesun.Runtime.Web;

namespace Iwesun.Runtime.WebView2.Tests;

public sealed class WebRuntimeScriptDomEvidenceReaderTests
{
	[Fact]
	public async Task ReadDomPropertiesAsync_BatchesAllDocumentScopesOnce()
	{
		var scripts = new RecordingScriptSession();
		var tree = new WebRuntimeDomTreeSnapshot(
			7,
			new Uri("https://example.test/page"),
			DateTimeOffset.UtcNow,
			[
				Node("document", "/html"),
				Node("/html/body/iframe", "/html")
			],
			[
				new(
					"document",
					new Uri("https://example.test/page"),
					null),
				new(
					"/html/body/iframe",
					new Uri("https://example.test/frame"),
					0)
			]);
		var reader = new WebRuntimeScriptDomEvidenceReader(
			scripts,
			() => tree);
		var requests = new[]
		{
			Request("0", "document", "style.width"),
			Request("1", "document", "content.ownText"),
			Request("2", "document", "id"),
			Request("3", "/html/body/iframe", "style.height"),
			Request("4", "/html/body/iframe", "value")
		};

		var results = await reader.ReadDomPropertiesAsync(requests);

		Assert.Equal(5, results.Count);
		Assert.Equal(1, scripts.TopCalls);
		Assert.Equal(0, scripts.FrameCalls);
		Assert.All(
			results,
			static result => Assert.Equal(
				WebRuntimeDomPropertyStatus.Captured,
				result.Status));
		Assert.Equal("top-0", results[0].Value);
		Assert.Equal("cross-document-4", results[4].Value);
		Assert.Equal(new Uri("https://example.test/page"), reader.CurrentPageUrl);
	}

	[Fact]
	public async Task ReadDomPropertiesAsync_DoesNotOverlayTreeOwnTextInPropertyReader()
	{
		var scripts = new OwnTextScriptSession();
		var tree = new WebRuntimeDomTreeSnapshot(
			8,
			new Uri("https://example.test/page"),
			DateTimeOffset.UtcNow,
			[
				new("document", "/html", "html", null, [], null, null)
				{
					OwnText = "captured text"
				}
			],
			[
				new(
					"document",
					new Uri("https://example.test/page"),
					null)
			]);
		var reader = new WebRuntimeScriptDomEvidenceReader(
			scripts,
			() => tree);
		var request = Request("0", "document", "content.ownText") with
		{
			Slot = WebRuntimeDomDataSlot.Initialization
		};

		var result = Assert.Single(
			await reader.ReadDomPropertiesAsync([request]));

		Assert.Equal(WebRuntimeDomPropertyStatus.ConfirmedAbsent, result.Status);
		Assert.Equal(string.Empty, result.Value);
	}

	[Fact]
	public async Task QueryDomPropertyAsync_ReadsOneStrongIdentityDirectly()
	{
		var scripts = new SingleScriptSession();
		var tree = new WebRuntimeDomTreeSnapshot(
			9,
			new Uri("https://example.test/page"),
			DateTimeOffset.UtcNow,
			[Node("document", "/html")],
			[
				new(
					"document",
					new Uri("https://example.test/page"),
					null)
			]);
		var reader = new WebRuntimeScriptDomEvidenceReader(
			scripts,
			() => tree);
		IWebRuntimeSingleDomPropertyQuerySession session =
			new WebRuntimeLiveDomQuerySession(reader);
		var request = Request("single", "document", "style.width");

		var result = await session.QueryDomPropertyAsync(request);

		Assert.Equal("single", result.QueryId);
		Assert.Equal(WebRuntimeDomPropertyStatus.Captured, result.Status);
		Assert.Equal("320px", result.Value);
		Assert.Equal(1, scripts.Calls);
	}

	[Fact]
	public async Task ReadDomPropertiesAsync_DoesNotComposeTreeTextInPropertyReader()
	{
		var scripts = new OwnTextScriptSession();
		var tree = new WebRuntimeDomTreeSnapshot(
			10,
			new Uri("https://example.test/page"),
			DateTimeOffset.UtcNow,
			[
				new(
					"document",
					"/html",
					"html",
					null,
					["/html/body"],
					null,
					null),
				new(
					"document",
					"/html/body",
					"body",
					"/html",
					["/html/body/span[1]", "/html/body/span[2]"],
					null,
					null),
				new(
					"document",
					"/html/body/span[1]",
					"span",
					"/html/body",
					[],
					null,
					"/html/body/span[2]")
				{
					OwnText = "first"
				},
				new(
					"document",
					"/html/body/span[2]",
					"span",
					"/html/body",
					[],
					"/html/body/span[1]",
					null)
				{
					OwnText = " second"
				}
			],
			[
				new(
					"document",
					new Uri("https://example.test/page"),
					null)
			]);
		var reader = new WebRuntimeScriptDomEvidenceReader(
			scripts,
			() => tree);
		var request = Request("0", "document", "content.textContent") with
		{
			XPath = "/html/body",
			TagName = "body",
			Category = WebRuntimeDomSlotCategory.DataOrganization,
			EvidenceKind = WebRuntimeDomEvidenceKind.TextContent,
			Slot = WebRuntimeDomDataSlot.Initialization
		};

		var result = Assert.Single(
			await reader.ReadDomPropertiesAsync([request]));

		Assert.Equal(WebRuntimeDomPropertyStatus.ConfirmedAbsent, result.Status);
		Assert.Equal(string.Empty, result.Value);
		Assert.Equal(WebRuntimeDomValueSource.Unspecified, result.ValueSource);
	}

	private static WebRuntimeDomPropertyRequest Request(
		string id,
		string scope,
		string propertyName) =>
		new(
			id,
			scope,
			"/html",
			"html",
			propertyName,
			"RuntimeProperties",
			WebRuntimeDomOwnerKind.RuntimeProperty,
			WebRuntimeDomSlotCategory.Style,
			WebRuntimeDomEvidenceKind.ComputedStyles,
			WebRuntimeDomDataSlot.Runtime);

	private static WebRuntimeDomTreeElement Node(
		string scope,
		string xpath) =>
		new(scope, xpath, "html", null, [], null, null);

	private sealed class RecordingScriptSession :
		IWebRuntimeScriptSession,
		IWebRuntimeIndexedFrameScriptSession,
		IWebRuntimeDocumentScopeScriptSession
	{
		public int TopCalls { get; private set; }

		public int FrameCalls { get; private set; }

		public Task<string> EvaluateStringAsync(
			string expression,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			TopCalls++;
			Assert.Contains("\"queryId\":\"0\"", expression, StringComparison.Ordinal);
			Assert.Contains("\"queryId\":\"2\"", expression, StringComparison.Ordinal);
			Assert.Contains("\"queryId\":\"3\"", expression, StringComparison.Ordinal);
			Assert.Contains(
				"\"documentScope\":\"/html/body/iframe\"",
				expression,
				StringComparison.Ordinal);
			Assert.DoesNotContain("__XPATH__", expression, StringComparison.Ordinal);
			Assert.DoesNotContain(
				"__PROPERTY_NAME__",
				expression,
				StringComparison.Ordinal);
			return Task.FromResult(
				Results(
					("0", "top-0"),
					("1", "top-1"),
					("2", "top-2"),
					("3", "cross-document-3"),
					("4", "cross-document-4")));
		}

		public Task<string> EvaluateStringInFrameAsync(
			int frameIndex,
			string expression,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			FrameCalls++;
			Assert.Equal(0, frameIndex);
			Assert.Contains("\"queryId\":\"3\"", expression, StringComparison.Ordinal);
			return Task.FromResult(Results(("3", "frame-3"), ("4", "frame-4")));
		}

		public Task<string> EvaluateStringInFrameAsync(
			string sourceUrlContains,
			string expression,
			CancellationToken ct) =>
			throw new NotSupportedException();

		public Task<string> EvaluateStringInDocumentAsync(
			string documentScope,
			Uri documentUri,
			string expression,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			FrameCalls++;
			Assert.Equal("/html/body/iframe", documentScope);
			Assert.Equal(
				new Uri("https://example.test/frame"),
				documentUri);
			Assert.Contains(
				"\"queryId\":\"3\"",
				expression,
				StringComparison.Ordinal);
			return Task.FromResult(
				Results(("3", "frame-3"), ("4", "frame-4")));
		}

		private static string Results(
			params (string QueryId, string Value)[] values) =>
			System.Text.Json.JsonSerializer.Serialize(values.Select(
				static value => new
				{
					queryId = value.QueryId,
					status = 0,
					value = value.Value,
					description = "live"
				}));
	}

	private sealed class OwnTextScriptSession : IWebRuntimeScriptSession
	{
		public Task<string> EvaluateStringAsync(
			string expression,
			CancellationToken ct) =>
			Task.FromResult(
				"""[{"queryId":"0","status":1,"value":"","description":"absent"}]""");

		public Task<string> EvaluateStringInFrameAsync(
			string sourceUrlContains,
			string expression,
			CancellationToken ct) =>
			throw new NotSupportedException();
	}

	private sealed class SingleScriptSession : IWebRuntimeScriptSession
	{
		public int Calls { get; private set; }

		public Task<string> EvaluateStringAsync(
			string expression,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			Calls++;
			Assert.Contains(
				"const path = '/html';",
				expression,
				StringComparison.Ordinal);
			Assert.DoesNotContain(
				"\"queryId\":",
				expression,
				StringComparison.Ordinal);
			return Task.FromResult(
				"0\u001f320px\u001flive\u001f0\u001f0\u001f");
		}

		public Task<string> EvaluateStringInFrameAsync(
			string sourceUrlContains,
			string expression,
			CancellationToken ct) =>
			throw new NotSupportedException();
	}
}
