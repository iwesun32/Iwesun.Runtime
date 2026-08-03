using Xunit;

namespace Iwesun.Runtime.WebView2.Tests;

public sealed class WebRuntimeCdpDomTreeReaderTests
{
	[Fact]
	public async Task ReadAsync_BuildsImmutableCdpIdentitiesAndAttributes()
	{
		var session = new RecordingDevToolsSession();
		var reader = new WebRuntimeCdpDomTreeReader(
			session,
			static () => 41);

		var snapshot = await reader.ReadAsync();

		Assert.Equal(41, snapshot.Revision);
		Assert.Equal(1, session.CallCount);
		Assert.Equal("DOM.getDocument", session.LastMethod);
		Assert.Equal(4, snapshot.Elements.Count);
		Assert.Equal(2, snapshot.Documents.Count);
		var html = Assert.Single(snapshot.Elements.Where(static element =>
			element.DocumentScope == "document"
			&& element.XPath == "/html"));
		Assert.Equal(2, html.NodeId);
		Assert.Equal(102, html.BackendNodeId);
		Assert.Equal("en", html.AttributeValues["lang"]);
		var body = Assert.Single(snapshot.Elements.Where(static element =>
			element.DocumentScope == "document"
			&& element.XPath == "/html/body"));
		Assert.Equal("hello", body.OwnText);
		Assert.Equal("hello", body.TextContent);
		Assert.Equal("hello", html.TextContent);
		Assert.Same(html.TextContent, body.TextContent);
		var frameRoot = Assert.Single(snapshot.Elements.Where(static element =>
			element.DocumentScope == "/html/body/iframe"
			&& element.XPath == "/html"));
		Assert.Equal(8, frameRoot.NodeId);
		Assert.Equal(108, frameRoot.BackendNodeId);
	}

	private sealed class RecordingDevToolsSession :
		IWebRuntimeDevToolsSession
	{
		public string CurrentUrl => "https://example.test/";

		public int CallCount { get; private set; }

		public string LastMethod { get; private set; } = string.Empty;

		public Task<string> CallDevToolsProtocolMethodAsync(
			string method,
			string parametersJson,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			CallCount++;
			LastMethod = method;
			return Task.FromResult(
				"""
				{
				  "root": {
				    "nodeId": 1,
				    "backendNodeId": 101,
				    "nodeType": 9,
				    "nodeName": "#document",
				    "documentURL": "https://example.test/",
				    "children": [
				      {
				        "nodeId": 2,
				        "backendNodeId": 102,
				        "nodeType": 1,
				        "nodeName": "HTML",
				        "localName": "html",
				        "attributes": ["lang", "en"],
				        "children": [
				          {
				            "nodeId": 3,
				            "backendNodeId": 103,
				            "nodeType": 1,
				            "nodeName": "BODY",
				            "localName": "body",
				            "attributes": [],
				            "children": [
				              {
				                "nodeId": 4,
				                "backendNodeId": 104,
				                "nodeType": 3,
				                "nodeName": "#text",
				                "nodeValue": "hello"
				              },
				              {
				                "nodeId": 5,
				                "backendNodeId": 105,
				                "nodeType": 1,
				                "nodeName": "IFRAME",
				                "localName": "iframe",
				                "attributes": ["src", "/frame"],
				                "contentDocument": {
				                  "nodeId": 7,
				                  "backendNodeId": 107,
				                  "nodeType": 9,
				                  "nodeName": "#document",
				                  "documentURL": "https://example.test/frame",
				                  "children": [
				                    {
				                      "nodeId": 8,
				                      "backendNodeId": 108,
				                      "nodeType": 1,
				                      "nodeName": "HTML",
				                      "localName": "html",
				                      "attributes": [],
				                      "children": []
				                    }
				                  ]
				                },
				                "children": []
				              }
				            ]
				          }
				        ]
				      }
				    ]
				  }
				}
				""");
		}
	}
}
