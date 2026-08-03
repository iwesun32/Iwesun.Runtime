using Iwesun.Runtime.Web;
using Iwesun.Runtime.WebView2;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class HtmlDocumentRootFrameAttachmentTests
{
	[Fact]
	public async Task CdpTreeThroughXamlBuild_AttachesSameOriginIframeByImmutableIdentity()
	{
		var snapshot = await new WebRuntimeCdpDomTreeReader(
			new SameOriginFrameDevToolsSession(),
			static () => 23).ReadAsync();
		var tree = DomElementTreeBuilder.Build(
			"document",
			snapshot.Elements.Select(static node =>
				new DomElementConstructionNode(
					node.DocumentScope,
					node.XPath,
					node.TagName,
					node.ParentXPath,
					node.ChildXPaths,
					node.LeftSiblingXPath,
					node.RightSiblingXPath)
				{
					NodeId = node.NodeId,
					BackendNodeId = node.BackendNodeId,
					AttributeNames = node.AttributeNames,
					AttributeValues = node.AttributeValues
				}).ToArray());
		var root = new TestDocumentRoot();
		root.MountElementTree(tree.HtmlRootElement, tree.DocumentRoots);
		await root.PrepareDomFillAsync();
		foreach (var documentRoot in tree.DocumentRoots)
			await documentRoot.DomFillAsync();
		var factory = new RecordingFactory();

		_ = root.BuildXamlObjectTrees(factory);

		Assert.Equal(23, snapshot.Revision);
		Assert.Equal(2, snapshot.Documents.Count);
		var attachment = Assert.Single(factory.FrameAttachments);
		Assert.Equal(5, attachment.ParentPlan.SourceElement?.NodeId);
		Assert.Equal(8, attachment.ChildPlan.SourceElement?.NodeId);
		Assert.Equal(
			"/html/body/iframe",
			attachment.ChildPlan.SourceElement?.DocumentScope);
	}

	[Fact]
	public async Task BuildXamlObjectTrees_AttachesIframeDocumentToOwnerContent()
	{
		var tree = DomElementTreeBuilder.Build(
			"document",
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
					["/html/body/iframe"],
					null,
					null),
				new(
					"document",
					"/html/body/iframe",
					"iframe",
					"/html/body",
					[],
					null,
					null),
				new(
					"/html/body/iframe",
					"/html",
					"html",
					null,
					["/html/body"],
					null,
					null),
				new(
					"/html/body/iframe",
					"/html/body",
					"body",
					"/html",
					[],
					null,
					null)
			]);
		var root = new TestDocumentRoot();
		root.MountElementTree(tree.HtmlRootElement, tree.DocumentRoots);
		await root.PrepareDomFillAsync();
		foreach (var documentRoot in tree.DocumentRoots)
			await documentRoot.DomFillAsync();
		var factory = new RecordingFactory();

		var results = root.BuildXamlObjectTrees(factory);

		Assert.Equal(2, results.Count);
		var attachment = Assert.Single(factory.FrameAttachments);
		Assert.Equal("iframe", attachment.ParentPlan.SourceElement?.TagName);
		Assert.Equal("html", attachment.ChildPlan.SourceElement?.TagName);
		Assert.Equal(
			ElementXamlChildPlacementKind.Content,
			attachment.Placement);
	}

	private sealed class TestDocumentRoot()
		: HtmlDocumentRoot("document")
	{
		protected override ValueTask<IReadOnlyList<DomPropertyQueryResult>>
			QueryDomPropertiesAsync(
				IReadOnlyList<DomPropertyQueryContext> contexts,
				CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			IReadOnlyList<DomPropertyQueryResult> results = contexts
				.Select(static _ => DomPropertyQueryResult.ConfirmedAbsent(
					"Not required by this construction test."))
				.ToArray();
			return ValueTask.FromResult(results);
		}
	}

	private sealed class RecordingFactory : TestXamlElementObjectFactory
	{
		public List<XamlElementObjectAttachmentContext> FrameAttachments
		{
			get;
		} = [];

		protected override object CreateElementCore(XamlElementObjectPlan plan) =>
			new object();

		public override void FillElementProperties(
			XamlElementObjectPropertyFillContext context)
		{
		}

		public override void AttachChild(XamlElementObjectAttachmentContext context)
		{
			if (context.ParentPlan.SourceElement?.TagName == "iframe"
				&& context.ChildPlan.SourceElement?.DocumentScope
					== "/html/body/iframe")
			{
				FrameAttachments.Add(context);
			}
		}

		public override void ApplyGridTracks(
			object element,
			IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
			IReadOnlyList<XamlGridTrackDefinition> columnDefinitions)
		{
		}
	}

	private sealed class SameOriginFrameDevToolsSession :
		IWebRuntimeDevToolsSession
	{
		public string CurrentUrl => "https://example.test/";

		public Task<string> CallDevToolsProtocolMethodAsync(
			string method,
			string parametersJson,
			CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			Assert.Equal("DOM.getDocument", method);
			return Task.FromResult(
				"""
				{"root":{"nodeId":1,"backendNodeId":101,"nodeType":9,
				"nodeName":"#document","documentURL":"https://example.test/",
				"children":[{"nodeId":2,"backendNodeId":102,"nodeType":1,
				"nodeName":"HTML","localName":"html","attributes":[],"children":[
				{"nodeId":3,"backendNodeId":103,"nodeType":1,"nodeName":"BODY",
				"localName":"body","attributes":[],"children":[
				{"nodeId":5,"backendNodeId":105,"nodeType":1,"nodeName":"IFRAME",
				"localName":"iframe","attributes":["src","/frame"],"children":[],
				"contentDocument":{"nodeId":7,"backendNodeId":107,"nodeType":9,
				"nodeName":"#document","documentURL":"https://example.test/frame",
				"children":[{"nodeId":8,"backendNodeId":108,"nodeType":1,
				"nodeName":"HTML","localName":"html","attributes":[],"children":[
				{"nodeId":9,"backendNodeId":109,"nodeType":1,"nodeName":"BODY",
				"localName":"body","attributes":[],"children":[]}] }]}}]}]}]}}
				""");
		}
	}
}
