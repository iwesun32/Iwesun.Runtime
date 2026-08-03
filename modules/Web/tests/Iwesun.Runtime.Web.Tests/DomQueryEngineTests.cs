using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

/// <summary>
/// 验证 DOM/XAML 查询引擎和委托机制
/// </summary>
public sealed class DomQueryEngineTests
{
	[Fact]
	public void DomQueryEngine_CanBeSetAndRetrieved()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var engine = new TestDomQueryEngine();

		element.DomQueryEngine = engine;

		Assert.Same(engine, element.DomQueryEngine);
	}

	[Fact]
	public void XamlQueryEngine_CanBeSetAndRetrieved()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var engine = new TestXamlQueryEngine();

		element.XamlQueryEngine = engine;

		Assert.Same(engine, element.XamlQueryEngine);
	}

	[Fact]
	public async Task DomFillAsync_WithEngine_UsesEngine()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var engine = new TestDomQueryEngine();
		engine.SetResponse("id", DomPropertyDataSlot.Initialization, "test-id");
		element.DomQueryEngine = engine;

		var result = await element.DomFillAsync();

		Assert.Equal("test-id", element.Id.SourceInitialization.Value);
		Assert.True(engine.QueryCount > 0);
	}

	[Fact]
	public async Task DomFillAsync_UsesOwnerQueryName_NotClrPropertyCasing()
	{
		var input = new HtmlInputDomElement(Mapping("/html/body/input"));
		var inputEngine = new TestDomQueryEngine();
		input.DomQueryEngine = inputEngine;

		await input.DomFillAsync();

		var autoCompleteRequests = inputEngine.Contexts
			.Where(static context =>
				context.ReflectedPropertyName == "AutoComplete")
			.ToArray();
		Assert.NotEmpty(autoCompleteRequests);
		Assert.All(
			autoCompleteRequests,
			static context => Assert.Equal(
				"autocomplete",
				context.PropertyName));

		var svg = new SvgRootDomElement(Mapping("/html/body/svg"));
		var svgEngine = new TestDomQueryEngine();
		svg.DomQueryEngine = svgEngine;

		await svg.DomFillAsync();

		var viewBoxRequests = svgEngine.Contexts
			.Where(static context =>
				context.ReflectedPropertyName == "ViewBox")
			.ToArray();
		Assert.NotEmpty(viewBoxRequests);
		Assert.All(
			viewBoxRequests,
			static context => Assert.Equal("viewBox", context.PropertyName));
	}

	[Fact]
	public void ContentQueryScript_UsesDedicatedOwnTextSemantics()
	{
		var script = WebView2DomPropertyFiller.BuildContentQueryScript(
			"document",
			"/html/body/div",
			"ownText");

		Assert.Contains("node.childNodes", script, StringComparison.Ordinal);
		Assert.Contains("Node.TEXT_NODE", script, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"node['content.ownText']",
			script,
			StringComparison.Ordinal);
	}

	[Fact]
	public void ScopedNodeLookup_TraversesNestedIframeDocuments()
	{
		var script = WebView2DomPropertyFiller.BuildScopedNodeLookupScript(
			"/html/body/iframe[1]::/html/body/iframe[2]",
			"/html/body/div[1]");

		Assert.Contains(
			"__scope.split('::')",
			script,
			StringComparison.Ordinal);
		Assert.Contains(
			"__frameOwner?.contentDocument",
			script,
			StringComparison.Ordinal);
		Assert.Contains(
			"'/html/body/div[1]'",
			script,
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task DomFillAsync_WithoutEngine_ThrowsWhenNoDelegate()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await Assert.ThrowsAsync<InvalidOperationException>(() => element.DomFillAsync().AsTask());
	}

	[Fact]
	public async Task DomFillAsync_WithExternalDelegate_Works()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		var result = await element.DomFillAsync((scope, xpath, name, slot) =>
		{
			// name 是 HTML 属性名（如 "id"），不是 CLR 属性名（如 "Id"）
			if (name == "id" && slot == DomPropertyDataSlot.Initialization)
				return ValueTask.FromResult(DomPropertyQueryResult.DirectConstant("external-id"));
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent("not requested"));
		});

		Assert.Equal("external-id", element.Id.SourceInitialization.Value);
	}

	[Fact]
	public async Task Subclass_CanOverrideDefaultDelegate()
	{
		// 使用 HtmlLayoutDomElementDefinition（非密封）测试子类重写
		var element = new TestLayoutElement(Mapping("/html/body/test"));

		// TestLayoutElement 重写了 CreateDefaultDomQueryDelegate，返回内置委托
		var result = await element.DomFillAsync();

		Assert.Equal("overridden-id", element.Id.SourceInitialization.Value);
		Assert.True(element.DefaultDelegateCalled);
	}

	[Fact]
	public void CreateDefaultDomQueryDelegate_ReturnsNullWhenNoEngine()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		var del = element.CallCreateDefaultDomQueryDelegate();

		Assert.Null(del);
	}

	[Fact]
	public void CreateDefaultDomQueryDelegate_ReturnsDelegateWhenEngineSet()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		element.DomQueryEngine = new TestDomQueryEngine();

		var del = element.CallCreateDefaultDomQueryDelegate();

		Assert.NotNull(del);
	}

	[Fact]
	public async Task FillAsync_FillsAllThreeSlots()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var engine = new TestDomQueryEngine();
		engine.SetResponse("id", DomPropertyDataSlot.Initialization, "init-value");
		engine.SetResponse("id", DomPropertyDataSlot.Link, "link-desc", isLink: true);
		engine.SetResponse("id", DomPropertyDataSlot.Runtime, "runtime-value");
		element.DomQueryEngine = engine;

		await element.DomFillAsync();

		Assert.True(element.Id.SourceInitialization.IsSet);
		Assert.Equal("init-value", element.Id.SourceInitialization.Value);
		Assert.True(element.Id.SourceLink.IsSet);
		Assert.Equal("link-desc", element.Id.SourceLink.Description);
		Assert.True(element.Id.SourceRuntime.IsSet);
		Assert.Equal("runtime-value", element.Id.SourceRuntime.Value);
	}

	[Fact]
	public async Task DomFillAsync_WithMountedHtmlRoot_UsesBatchedRootApi()
	{
		var parent = new HtmlDivDomElement(Mapping("/html/body/div"));
		var child = new HtmlSpanDomElement(
			new(
				"document",
				"/html/body/div/span",
				"/html/body/div",
				[],
				null,
				null));
		parent.AddChild(child);
		var root = new TestHtmlDocumentRoot();
		root.MountElementTree(parent, [parent]);

		var result = await parent.DomFillAsync();

		Assert.Same(root, parent.HtmlRoot);
		Assert.Same(root, child.HtmlRoot);
		Assert.Equal("live-parent", parent.Id.SourceInitialization.Value);
		Assert.Equal("live-child", child.Id.SourceInitialization.Value);
		Assert.Equal(2, root.BatchCount);
		Assert.Single(result.Children);
		Assert.All(
			result.Slots.Concat(result.Children[0].Slots),
			static trace =>
			{
				Assert.False(string.IsNullOrWhiteSpace(
					trace.ReflectedPropertyName));
				Assert.NotNull(trace.OwnerKind);
				Assert.NotEqual(
					ElementSlotCategory.Unspecified,
					trace.Category);
				Assert.NotEqual(
					ElementEvidenceKind.None,
					trace.EvidenceKind);
			});
	}

	[Fact]
	public void AddChild_AfterRootMount_PropagatesHtmlRoot()
	{
		var parent = new HtmlDivDomElement(Mapping("/html/body/div"));
		var root = new TestHtmlDocumentRoot();
		root.MountElementTree(parent, [parent]);
		var child = new HtmlSpanDomElement(
			new(
				"document",
				"/html/body/div/span",
				"/html/body/div",
				[],
				null,
				null));

		parent.AddChild(child);

		Assert.Same(root, child.HtmlRoot);
	}

	[Fact]
	public async Task DomFillOwnAsync_FillsRepresentativeElementWithoutTraversingChildren()
	{
		var parent = new HtmlDivDomElement(Mapping("/html/body/div"));
		var child = new HtmlSpanDomElement(
			new(
				"document",
				"/html/body/div/span",
				"/html/body/div",
				[],
				null,
				null));
		parent.AddChild(child);
		var root = new TestHtmlDocumentRoot();
		root.MountElementTree(parent, [parent]);

		var result = await parent.DomFillOwnAsync();

		Assert.Empty(result.Children);
		Assert.Equal("live-parent", parent.Id.SourceInitialization.Value);
		Assert.False(child.Id.SourceInitialization.IsSet);
		Assert.All(
			result.Slots,
			static trace => Assert.NotNull(trace.OwnerKind));
	}

	[Fact]
	public async Task DomFillOwnAsync_UsesDedicatedRuntimeGeometrySlots()
	{
		var tree = DomElementTreeBuilder.Build(
			"document",
			[
				new(
					"document",
					"/html",
					"html",
					null,
					[],
					null,
					null)
			]);
		var element = tree.HtmlRootElement;
		var root = new TestHtmlDocumentRoot();
		root.MountElementTree(element, [element]);

		var result = await element.DomFillOwnAsync();
		var runtime = result.Slots
			.Where(static trace =>
				trace.OwnerKind == ElementSlotOwnerKind.RuntimeProperty)
			.GroupBy(static trace => trace.PropertyName, StringComparer.Ordinal)
			.ToDictionary(
				static group => group.Key,
				static group => group.First().EvidenceKind,
				StringComparer.Ordinal);

		Assert.Equal(4, runtime.Count);
		Assert.Equal(
			ElementEvidenceKind.LocalLayout
				| ElementEvidenceKind.DomRuntimeGeometry,
			runtime["rect.width"]);
		Assert.All(element.RuntimeGeometry, static geometry =>
		{
			Assert.Equal(
				[DomPropertyDataSlot.Runtime],
				geometry.DomSlots);
			Assert.Equal(
				[XamlPropertyDataSlot.Runtime],
				geometry.XamlSlots);
			Assert.True(geometry.SourceRuntime.IsSet);
			Assert.False(geometry.XamlRuntime.IsSet);
		});
		Assert.DoesNotContain(runtime.Keys, static name =>
			name.StartsWith("style.", StringComparison.Ordinal));
	}

	[Fact]
	public async Task PrepareDomFillAsync_QueriesEntireMountedTreeOnce()
	{
		var parent = new HtmlDivDomElement(Mapping("/html/body/div"));
		var child = new HtmlSpanDomElement(
			new(
				"document",
				"/html/body/div/span",
				"/html/body/div",
				[],
				null,
				null));
		parent.AddChild(child);
		var root = new TestHtmlDocumentRoot();
		root.MountElementTree(parent, [parent]);

		await root.PrepareDomFillAsync();
		var result = await parent.DomFillAsync();

		Assert.Equal(1, root.BatchCount);
		Assert.Equal("live-parent", parent.Id.SourceInitialization.Value);
		Assert.Equal("live-child", child.Id.SourceInitialization.Value);
		Assert.Single(result.Children);
	}

	[Fact]
	public async Task PrepareDomFillAsync_MountsSameRevisionTextOnRootSnapshot()
	{
		var tree = DomElementTreeBuilder.Build(
			"document",
			[
				new DomElementConstructionNode(
				"document",
				"/html/body/div",
				"div",
				null,
				[],
				null,
				null)
				{
					OwnText = "own text",
					TextContent = "own text child text"
				}
			]);
		var element = Assert.IsType<HtmlDivDomElement>(
			tree.HtmlRootElement);
		var root = new TestHtmlDocumentRoot();
		root.MountElementTree(element, [element]);

		await root.PrepareDomFillAsync();

		var snapshot = Assert.IsType<DomEvidenceSnapshot>(
			root.EvidenceSnapshot);
		Assert.True(snapshot.Revision > 0);
		Assert.True(snapshot.Count > 0);
		var ownText = snapshot.Entries.Where(static entry =>
			entry.Context.PropertyName == "content.ownText"
			&& entry.Context.Slot == DomPropertyDataSlot.Initialization)
			.ToArray();
		Assert.Single(ownText);
		Assert.Equal(
			ElementSlotOwnerKind.DataSource,
			ownText[0].Context.OwnerKind);
		Assert.All(ownText, static entry =>
			Assert.Equal("own text", entry.Result.Value));
		Assert.Equal(
			ElementPropertyValueSource.DirectConstant,
			ownText[0].Result.ValueSource);
		Assert.DoesNotContain(
			root.QueriedContexts,
			static context =>
				context.Slot == DomPropertyDataSlot.Initialization
				&& (context.PropertyName == "content.ownText"
					|| context.PropertyName == "content.textContent"));
	}

	private static DomElementMapping Mapping(string xpath) =>
		new("document", xpath, null, [], null, null);

	private sealed class TestDomQueryEngine : IDomPropertyQueryEngine
	{
		private readonly Dictionary<(string Name, DomPropertyDataSlot Slot), (string Value, bool IsLink)> _responses = new();
		public int QueryCount { get; private set; }
		public List<DomPropertyQueryContext> Contexts { get; } = [];

		public void SetResponse(string propertyName, DomPropertyDataSlot slot, string value, bool isLink = false)
		{
			_responses[(propertyName, slot)] = (value, isLink);
		}

		public ValueTask<DomPropertyQueryResult> QueryAsync(
			DomPropertyQueryContext context,
			CancellationToken cancellationToken = default)
		{
			QueryCount++;
			Contexts.Add(context);
			if (_responses.TryGetValue((context.PropertyName, context.Slot), out var response))
			{
				if (response.IsLink)
				{
					return ValueTask.FromResult(DomPropertyQueryResult.Captured(
						response.Value,
						ElementPropertyValueSource.LinkedCalculation,
						new ElementPropertyLink(ElementPropertyLinkKind.CustomString, response.Value)));
				}
				return ValueTask.FromResult(DomPropertyQueryResult.DirectConstant(response.Value));
			}
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent("not set"));
		}
	}

	private sealed class TestXamlQueryEngine : IXamlPropertyQueryEngine
	{
		public int QueryCount { get; private set; }

		public ValueTask<XamlPropertyQueryResult> QueryAsync(
			XamlPropertyQueryContext context,
			CancellationToken cancellationToken = default)
		{
			QueryCount++;
			return ValueTask.FromResult(XamlPropertyQueryResult.DirectConstant("test"));
		}
	}

	private sealed class TestHtmlDocumentRoot()
		: HtmlDocumentRoot("document")
	{
		public int BatchCount { get; private set; }

		public List<DomPropertyQueryContext> QueriedContexts { get; } = [];

		protected override ValueTask<IReadOnlyList<DomPropertyQueryResult>>
			QueryDomPropertiesAsync(
				IReadOnlyList<DomPropertyQueryContext> contexts,
				CancellationToken cancellationToken)
		{
			BatchCount++;
			QueriedContexts.AddRange(contexts);
			IReadOnlyList<DomPropertyQueryResult> results = contexts
				.Select(static context =>
					context.PropertyName.StartsWith(
						"rect.",
						StringComparison.Ordinal)
						&& context.Slot == DomPropertyDataSlot.Runtime
						? DomPropertyQueryResult.DirectConstant(
							context.PropertyName switch
							{
								"rect.x" => "10",
								"rect.y" => "20",
								"rect.width" => "300",
								"rect.height" => "200",
								_ => throw new InvalidOperationException()
							})
						: context.PropertyName == "id"
						&& context.Slot == DomPropertyDataSlot.Initialization
						? DomPropertyQueryResult.DirectConstant(
							context.XPath.EndsWith(
								"/span",
								StringComparison.Ordinal)
								? "live-child"
								: "live-parent")
						: DomPropertyQueryResult.ConfirmedAbsent("not set"))
				.ToArray();
			return ValueTask.FromResult(results);
		}
	}

	private sealed class TestLayoutElement : HtmlLayoutDomElementDefinition
	{
		public bool DefaultDelegateCalled { get; private set; }

		public TestLayoutElement(DomElementMapping mapping) : base(
			mapping, "test", ElementCategory.LayoutContainer,
			ElementContentModel.Flow, XamlConversionSupport.Composite,
			ElementDefaultDisplay.Block, ElementXamlChildPlacementKind.DirectChildren)
		{ }

		internal override object CreateXamlObject(
			IXamlElementObjectFactory factory,
			XamlElementObjectPlan plan) =>
			factory.CreateSyntheticElement(new(plan));

		internal override void FillXamlObjectProperties(
			IXamlElementObjectFactory factory,
			object xamlElement,
			XamlElementObjectPlan plan) =>
			factory.FillElementProperties(new(xamlElement, plan));

		protected override IReadOnlyList<DomElementDataSource>
			CreateDataSources() => [];

		protected override XamlElementMappingDecision CreateXaml() =>
			new(XamlElementObjectType.Grid, XamlElementMappingKind.ConservativeContainer, true, "DOM query test container.");

		protected override DomPropertyQueryDelegate? CreateDefaultDomQueryDelegate()
		{
			DefaultDelegateCalled = true;
			return (scope, xpath, name, slot) =>
			{
				if (name == "id" && slot == DomPropertyDataSlot.Initialization)
					return ValueTask.FromResult(DomPropertyQueryResult.DirectConstant("overridden-id"));
				return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
			};
		}
	}
}

internal static class DomElementTestExtensions
{
	public static DomPropertyQueryDelegate? CallCreateDefaultDomQueryDelegate(this DomElement element)
	{
		var method = typeof(DomElement).GetMethod(
			"CreateDefaultDomQueryDelegate",
			System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		return method?.Invoke(element, null) as DomPropertyQueryDelegate;
	}
}
