using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class DomElementTreeTests
{
	[Fact]
	public void AddChild_WhenMappingMatches_AssignsTreeRelationships()
	{
		var parent = Create("/html/body", null, ["/html/body/div"]);
		var child = Create("/html/body/div", "/html/body", []);

		parent.AddChild(child);

		Assert.Same(parent, child.Parent);
		Assert.Same(child, parent.Children[0]);
		Assert.Null(child.LeftSibling);
		Assert.Null(child.RightSibling);
	}

	[Fact]
	public void AddChild_WhenChildAlreadyAttached_RejectsSecondParent()
	{
		var first = Create("/html/body", null, ["/html/body/div"]);
		var second = Create("/html/main", null, ["/html/body/div"]);
		var child = Create("/html/body/div", "/html/body", []);
		first.AddChild(child);

		Assert.Throws<InvalidOperationException>(() => second.AddChild(child));
	}

	[Fact]
	public void AddChild_WhenCycleWouldBeCreated_RejectsCycle()
	{
		var root = Create("/html", null, ["/html/body"]);
		var child = Create("/html/body", "/html", []);
		root.AddChild(child);

		Assert.Throws<InvalidOperationException>(() => child.AddChild(root));
	}

	[Fact]
	public void HtmlGlobalProperties_DeclareCompletePipelineTraits()
	{
		var names = new[]
		{
			"Id", "Class", "Style", "Title", "Language",
			"Direction", "Hidden", "TabIndex", "Role"
		};

		foreach (var name in names)
		{
			var traits = ElementPropertyTraitsReflector.GetAttribute(
				typeof(TestElement),
				name);
			Assert.True(traits.IsHtmlDefinedProperty);
			Assert.True(traits.IsSlottedProperty);
			Assert.True(traits.IsFillRequired);
			Assert.True(traits.IsXamlFillRequired);
			Assert.True(traits.IsAuditRequired);
		}
	}

	[Fact]
	public async Task DomFillAsync_WhenRuntimePropertyIsDeclared_FillsEveryLiveSlot()
	{
		var element = Create("/html/body/div", null, []);
		element.AddRuntimeProperty("rect.x", ElementSlotCategory.Space);
		var requests = new List<(string Name, DomPropertyDataSlot Slot)>();

		await element.DomFillAsync((_, _, name, slot) =>
		{
			requests.Add((name, slot));
			return ValueTask.FromResult(
				name == "rect.x" && slot != DomPropertyDataSlot.Link
					? DomPropertyQueryResult.DirectConstant("42")
					: DomPropertyQueryResult.ConfirmedAbsent("Not present."));
		});

		var property = Assert.Single(element.RuntimeProperties);
		Assert.Equal("42", property.SourceInitialization.Value);
		Assert.Equal("42", property.SourceRuntime.Value);
		Assert.Contains(("rect.x", DomPropertyDataSlot.Initialization), requests);
		Assert.Contains(("rect.x", DomPropertyDataSlot.Link), requests);
		Assert.Contains(("rect.x", DomPropertyDataSlot.Runtime), requests);
	}

	[Fact]
	public async Task DomFillAsync_WhenCapturedEventIsDeclared_FillsEventCollection()
	{
		var element = Create("/html/body/div", null, []);
		var capturedEvent = new CapturedDomElementEvent(
			"event.click.0",
			DomEventKind.Click,
			"click");
		element.Events.Add(capturedEvent);

		await element.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "event.click.0"
					&& slot == DomPropertyDataSlot.Initialization
						? DomPropertyQueryResult.DirectConstant(
							"""{"useCapture":false,"passive":true,"once":false}""")
						: DomPropertyQueryResult.ConfirmedAbsent("Not present.")));

		Assert.True(capturedEvent.DomInitialization.IsSet);
		Assert.Equal(DomEventKind.Click, capturedEvent.DomInitialization.Value?.Kind);
		Assert.True(capturedEvent.DomInitialization.Value?.Passive);
	}

	[Fact]
	public async Task DomFillAsync_WhenElementDefinesControlDataSources_FillsTheirSlots()
	{
		var element = Assert.IsType<HtmlInputDomElement>(
			HtmlDomElementTypeCatalog.Create(
				"input",
				new("document", "/html/body/input", null, [], null, null)));
		var requests = new List<(string Name, DomPropertyDataSlot Slot)>();

		await element.DomFillAsync((_, _, name, slot) =>
		{
			requests.Add((name, slot));
			return ValueTask.FromResult(
				name == "content.value" && slot != DomPropertyDataSlot.Link
					? DomPropertyQueryResult.DirectConstant("typed value")
					: DomPropertyQueryResult.ConfirmedAbsent("Not present."));
		});

		var value = Assert.Single(
			element.DataSources,
			static source => source.Name == "content.value");
		Assert.Equal(DomDataSourceKind.FormValue, value.DomSourceKind);
		Assert.Equal(DomDataSourceDomain.BusinessInput, value.Domain);
		Assert.Equal(XamlControlDataTargetKind.Value, value.XamlTargetKind);
		Assert.Equal("typed value", value.SourceInitialization.Value);
		Assert.Equal("typed value", value.SourceRuntime.Value);
		Assert.Contains(("content.value", DomPropertyDataSlot.Initialization), requests);
		Assert.Contains(("content.value", DomPropertyDataSlot.Runtime), requests);
	}

	[Fact]
	public async Task DomFillAsync_WhenInlineHandlerExists_FillsTypedEventSlot()
	{
		Assert.True(
			HtmlEventHandlerCatalog.TryGet(
				"button",
				"onclick",
				out var definition));
		var element = Assert.IsType<HtmlButtonDomElement>(
			HtmlDomElementTypeCatalog.Create(
				"button",
				new("document", "/html/body/button", null, [], null, null)));
		var handler = new HtmlDomEventHandler(Assert.IsType<HtmlEventHandlerDefinition>(definition));
		element.Events.Add(handler);

		await element.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "handler.onclick"
					&& slot == DomPropertyDataSlot.Initialization
						? DomPropertyQueryResult.DirectConstant("doSomething()")
						: DomPropertyQueryResult.ConfirmedAbsent("Not present.")));

		Assert.True(handler.DomInitialization.IsSet);
		Assert.Equal(DomEventKind.Click, handler.DomInitialization.Value?.Kind);
		Assert.Equal(
			DomEventRegistrationKind.InlineAttribute,
			handler.DomInitialization.Value?.RegistrationKind);
		Assert.True(handler.DomLink.IsSet);
	}

	[Fact]
	public async Task DomFillAsync_WhenSvgPathIsUsed_FillsNamedCommonAndSpecificSlots()
	{
		var element = Assert.IsType<SvgPathDomElement>(
			HtmlDomElementTypeCatalog.Create(
				"path",
				new("document", "/html/body/svg/path", null, [], null, null)));

		await element.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot is DomPropertyDataSlot.Initialization or DomPropertyDataSlot.Runtime
					&& name is "id" or "fill" or "d" or "pathLength"
						? DomPropertyQueryResult.DirectConstant($"{name}-value")
						: DomPropertyQueryResult.ConfirmedAbsent("Not present.")));

		Assert.Equal("id-value", element.Id.SourceInitialization.Value);
		Assert.Equal("fill-value", element.Fill.SourceRuntime.Value);
		Assert.Equal("d-value", element.Data.SourceInitialization.Value);
		Assert.Equal("pathLength-value", element.PathLength.SourceRuntime.Value);
		Assert.True(SvgDomElementArchitectureAudit.Inspect(element).Passed);
	}

	private static TestElement Create(
		string xpath,
		string? parentXPath,
		IReadOnlyList<string> childXPaths) =>
		new(
			new(
				"document",
				xpath,
				parentXPath,
				childXPaths,
				null,
				null));

	private sealed class TestElement(DomElementMapping mapping) :
		HtmlDomElementDefinition(
			mapping,
			"div",
			ElementCategory.LayoutContainer,
			ElementVisualKind.LayoutContainer,
			ElementContentModel.Flow,
			ElementClosure.OpenContainer,
			ElementSyntax.Normal,
			XamlConversionSupport.Direct,
			XamlControlFamily.Panel,
			ElementDefaultDisplay.Block,
			ElementInteractionKind.None,
			ElementXamlChildPlacementKind.DirectChildren)
	{
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
			new(XamlElementObjectType.Grid, XamlElementMappingKind.ConservativeContainer, true, "Test tree container.");
	}
}
