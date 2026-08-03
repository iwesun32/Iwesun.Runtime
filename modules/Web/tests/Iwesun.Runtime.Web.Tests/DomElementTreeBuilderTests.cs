using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class DomElementTreeBuilderTests
{
	[Fact]
	public void Build_PreservesSameAbsoluteXPathAcrossDocumentScopes()
	{
		var result = DomElementTreeBuilder.Build(
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

		Assert.Equal(2, result.DocumentRoots.Count);
		Assert.Equal(5, result.Elements.Count);
		Assert.Equal("document", result.HtmlRootElement.DocumentScope);
		Assert.Equal("/html", result.HtmlRootElement.XPath);
		Assert.All(
			result.DocumentRoots,
			static root => Assert.IsType<HtmlRootDomElement>(root));
		Assert.All(
			result.Elements.OfType<HtmlDomElementDefinition>(),
			static element =>
			{
				Assert.Empty(element.RuntimeProperties);
				Assert.Equal(4, element.RuntimeGeometry.Count);
			});
	}

	[Fact]
	public void Build_RejectsNestedDocumentWithoutIframeOwner()
	{
		var exception = Assert.Throws<InvalidDataException>(() =>
			DomElementTreeBuilder.Build(
				"document",
				[
					new(
						"document",
						"/html",
						"html",
						null,
						[],
						null,
						null),
					new(
						"/html/body/iframe",
						"/html",
						"html",
						null,
						[],
						null,
						null)
				]));

		Assert.Contains(
			"has no matching embedding owner",
			exception.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Build_AttachesAuthorShadowScopeToItsHostElement()
	{
		var result = DomElementTreeBuilder.Build(
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
					["/html/body/div"],
					null,
					null),
				new(
					"document",
					"/html/body/div",
					"div",
					"/html/body",
					[],
					null,
					null),
				new(
					"/html/body/div#shadow-root",
					"/span",
					"span",
					null,
					[],
					null,
					null)
			]);

		var shadowRoot = Assert.Single(result.DocumentRoots.Where(root =>
			root.DocumentScope == "/html/body/div#shadow-root"));
		Assert.Equal("div", shadowRoot.EmbeddingOwner?.TagName);
		Assert.Equal("/html/body/div", shadowRoot.EmbeddingOwner?.XPath);
		Assert.Equal(
			DomEmbeddingOwnerKind.ShadowHost,
			shadowRoot.EmbeddingOwnerKind);
	}

	[Fact]
	public void Build_RegistersCapturedExtensionAttributeNames()
	{
		var node = new DomElementConstructionNode(
			"document",
			"/html",
			"html",
			null,
			[],
			null,
			null)
		{
			AttributeNames = ["data-live-id"]
		};

		var result = DomElementTreeBuilder.Build("document", [node]);
		var html = Assert.IsType<HtmlRootDomElement>(result.HtmlRootElement);

		Assert.Contains(
			html.ExtensionAttributes,
			static attribute => attribute.Name == "data-live-id");
	}

	[Fact]
	public void Build_PreservesImmutableCdpNodeIdentities()
	{
		var node = new DomElementConstructionNode(
			"document",
			"/html",
			"html",
			null,
			[],
			null,
			null)
		{
			NodeId = 17,
			BackendNodeId = 29
		};

		var result = DomElementTreeBuilder.Build("document", [node]);

		Assert.Equal(17, result.HtmlRootElement.NodeId);
		Assert.Equal(29, result.HtmlRootElement.BackendNodeId);
		var nodeIdProperty = typeof(DomElement).GetProperty(
			nameof(DomElement.NodeId))
			?? throw new InvalidOperationException("NodeId property is missing.");
		var backendNodeIdProperty = typeof(DomElement).GetProperty(
			nameof(DomElement.BackendNodeId))
			?? throw new InvalidOperationException(
				"BackendNodeId property is missing.");
		Assert.False(nodeIdProperty.CanWrite);
		Assert.False(backendNodeIdProperty.CanWrite);
	}

	[Fact]
	public void Build_RejectsMissingParentChildBacklink()
	{
		var nodes = new[]
		{
			new DomElementConstructionNode(
				"document",
				"/html",
				"html",
				null,
				[],
				null,
				null),
			new DomElementConstructionNode(
				"document",
				"/html/body",
				"body",
				"/html",
				[],
				null,
				null)
		};

		var exception = Assert.Throws<InvalidDataException>(
			() => DomElementTreeBuilder.Build("document", nodes));
		Assert.Contains("relationship", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Build_MaximumHierarchyLevelCreatesOnlyRequestedLevels()
	{
		var result = DomElementTreeBuilder.Build(
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
					["/html/body/div"],
					null,
					null),
				new(
					"document",
					"/html/body/div",
					"div",
					"/html/body",
					[],
					null,
					null)
			],
			maximumHierarchyLevel: 2);

		Assert.Equal(2, result.Elements.Count);
		Assert.Equal(2, result.HtmlRootElement.MaximumHierarchyLevel);
		var body = Assert.Single(result.HtmlRootElement.Children);
		Assert.Equal(2, body.HierarchyLevel);
		Assert.Equal(2, body.EffectiveMaximumHierarchyLevel);
		Assert.Empty(body.Children);
	}

	[Fact]
	public void Build_MaximumHierarchyLevelContinuesThroughIframeOwner()
	{
		var result = DomElementTreeBuilder.Build(
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
			],
			maximumHierarchyLevel: 4);

		Assert.Equal(4, result.Elements.Count);
		var nestedRoot = Assert.Single(result.DocumentRoots.Where(root =>
			root.DocumentScope == "/html/body/iframe"));
		Assert.Equal(4, nestedRoot.HierarchyLevel);
		Assert.Equal("iframe", nestedRoot.EmbeddingOwner?.TagName);
		Assert.Equal(
			DomEmbeddingOwnerKind.Iframe,
			nestedRoot.EmbeddingOwnerKind);
		Assert.Empty(nestedRoot.Children);
	}
}
