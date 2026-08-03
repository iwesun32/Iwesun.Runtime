using Xunit;
using Iwesun.Runtime.Web;

namespace Iwesun.Runtime.WebView2.Tests;

public sealed class WebRuntimeSnapshotDomTreeReaderTests
{
	[Fact]
	public async Task ReadAsync_ReturnsTypedTopAndIframeTreesWithLocalXpaths()
	{
		var reader = new WebRuntimeSnapshotDomTreeReader(
			new SnapshotScriptSession(),
			static () => 42);
		var session = new WebRuntimeLiveDomTreeSession(reader);

		var snapshot = await session.ReadDomTreeAsync();

		Assert.Equal(42, snapshot.Revision);
		Assert.Equal(5, snapshot.Elements.Count);
		Assert.Contains(
			snapshot.Elements,
			static node => node.DocumentScope == "document"
				&& node.XPath == "/html/body/iframe"
				&& node.OwnText == "attachment");
		Assert.Contains(
			snapshot.Elements,
			static node => node.DocumentScope == "/html/body/iframe"
				&& node.XPath == "/html"
				&& node.ParentXPath is null);
		Assert.Collection(
			snapshot.Documents,
			static document =>
			{
				Assert.Equal("document", document.DocumentScope);
				Assert.Null(document.FrameIndex);
			},
			static document =>
			{
				Assert.Equal("/html/body/iframe", document.DocumentScope);
				Assert.Equal(0, document.FrameIndex);
			});
		Assert.Same(snapshot, session.LastSnapshot);
	}

	private sealed class SnapshotScriptSession : IWebRuntimeScriptSession
	{
		public Task<string> EvaluateStringAsync(
			string expression,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			Assert.Equal(WebRuntimeDomSnapshot.CaptureExpression, expression);
			return Task.FromResult(SnapshotJson);
		}

		public Task<string> EvaluateStringInFrameAsync(
			string sourceUrlContains,
			string expression,
			CancellationToken ct) =>
			throw new NotSupportedException();
	}

	private const string SnapshotJson = """
		{
		  "schema": "iwesun.webview2.dom-snapshot/1.0",
		  "capturedAt": "2026-07-29T00:00:00+00:00",
		  "top": {
		    "url": "https://example.test/page",
		    "documentElement": {
		      "nodeType": 1,
		      "nodeName": "HTML",
		      "localName": "html",
		      "path": "/html",
		      "childNodes": [
		        {
		          "nodeType": 1,
		          "nodeName": "BODY",
		          "localName": "body",
		          "path": "/html/body",
		          "childNodes": [
		            {
		              "nodeType": 1,
		              "nodeName": "IFRAME",
		              "localName": "iframe",
		              "path": "/html/body/iframe",
		              "childNodes": [
		                {
		                  "nodeType": 3,
		                  "nodeName": "#text",
		                  "path": "/html/body/iframe/text()[1]",
		                  "nodeValue": "attachment"
		                }
		              ],
		              "contentDocument": {
		                "url": "https://example.test/frame",
		                "documentElement": {
		                  "nodeType": 1,
		                  "nodeName": "HTML",
		                  "localName": "html",
		                  "path": "/html",
		                  "childNodes": [
		                    {
		                      "nodeType": 1,
		                      "nodeName": "BODY",
		                      "localName": "body",
		                      "path": "/html/body",
		                      "childNodes": []
		                    }
		                  ]
		                }
		              }
		            }
		          ]
		        }
		      ]
		    }
		  }
		}
		""";
}
