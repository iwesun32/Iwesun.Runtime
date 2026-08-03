using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

/// <summary>
/// 验证委托函数对槽位属性的处理是否完备
/// </summary>
public sealed class SlotProcessingTests
{
	[Fact]
	public void DomPropertyDataSlot_HasThreeValues()
	{
		// 验证枚举有三个槽位
		var slots = Enum.GetValues<DomPropertyDataSlot>();
		Assert.Equal(3, slots.Length);
		Assert.Contains(DomPropertyDataSlot.Initialization, slots);
		Assert.Contains(DomPropertyDataSlot.Link, slots);
		Assert.Contains(DomPropertyDataSlot.Runtime, slots);
	}

	[Fact]
	public void XamlPropertyDataSlot_HasThreeValues()
	{
		var slots = Enum.GetValues<XamlPropertyDataSlot>();
		Assert.Equal(3, slots.Length);
		Assert.Contains(XamlPropertyDataSlot.Initialization, slots);
		Assert.Contains(XamlPropertyDataSlot.Link, slots);
		Assert.Contains(XamlPropertyDataSlot.Runtime, slots);
	}

	[Fact]
	public void StringProperty_DomSlots_ContainsAllThreeSlots()
	{
		var prop = new DomElementStringProperty("test", ElementDataOrganizationSlotKind.Attribute);

		Assert.Equal(3, prop.DomSlots.Count);
		Assert.Contains(DomPropertyDataSlot.Initialization, prop.DomSlots);
		Assert.Contains(DomPropertyDataSlot.Link, prop.DomSlots);
		Assert.Contains(DomPropertyDataSlot.Runtime, prop.DomSlots);
	}

	[Fact]
	public void StringProperty_XamlSlots_ContainsAllThreeSlots()
	{
		var prop = new DomElementStringProperty("test", ElementDataOrganizationSlotKind.Attribute);

		Assert.Equal(3, prop.XamlSlots.Count);
		Assert.Contains(XamlPropertyDataSlot.Initialization, prop.XamlSlots);
		Assert.Contains(XamlPropertyDataSlot.Link, prop.XamlSlots);
		Assert.Contains(XamlPropertyDataSlot.Runtime, prop.XamlSlots);
	}

	[Fact]
	public void RuntimeProperty_DomSlots_ContainsAllThreeSlots()
	{
		var prop = new DomElementRuntimeProperty("rect.x", ElementSlotCategory.Space);

		Assert.Equal(3, prop.DomSlots.Count);
		Assert.Contains(DomPropertyDataSlot.Initialization, prop.DomSlots);
		Assert.Contains(DomPropertyDataSlot.Link, prop.DomSlots);
		Assert.Contains(DomPropertyDataSlot.Runtime, prop.DomSlots);
	}

	[Fact]
	public async Task FillAsync_QueriesAllThreeSlots()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var queriedSlots = new List<(string Name, DomPropertyDataSlot Slot)>();

		await element.DomFillAsync((scope, xpath, name, slot) =>
		{
			queriedSlots.Add((name, slot));
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
		});

		// 验证 Id 属性的三个槽位都被查询了
		var idSlots = queriedSlots.Where(s => s.Name == "id").ToList();
		Assert.Equal(3, idSlots.Count);
		Assert.Contains(idSlots, s => s.Slot == DomPropertyDataSlot.Initialization);
		Assert.Contains(idSlots, s => s.Slot == DomPropertyDataSlot.Link);
		Assert.Contains(idSlots, s => s.Slot == DomPropertyDataSlot.Runtime);
	}

	[Fact]
	public async Task FillAsync_InitializationSlot_SetsSourceInitialization()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await element.DomFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "id" && slot == DomPropertyDataSlot.Initialization)
				return ValueTask.FromResult(DomPropertyQueryResult.DirectConstant("test-id"));
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.True(element.Id.SourceInitialization.IsSet);
		Assert.Equal("test-id", element.Id.SourceInitialization.Value);
		Assert.False(element.Id.SourceLink.IsSet);
		Assert.False(element.Id.SourceRuntime.IsSet);
	}

	[Fact]
	public async Task FillAsync_LinkSlot_SetsSourceLink()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var link = new ElementPropertyLink(ElementPropertyLinkKind.CssExpression, "var(--color)");

		await element.DomFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "id" && slot == DomPropertyDataSlot.Link)
				return ValueTask.FromResult(DomPropertyQueryResult.Captured(
					"", ElementPropertyValueSource.LinkedCalculation, link));
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.True(element.Id.SourceLink.IsSet);
		Assert.Equal(ElementPropertyLinkKind.CssExpression, element.Id.SourceLink.Kind);
		Assert.Equal("var(--color)", element.Id.SourceLink.Description);
	}

	[Fact]
	public async Task FillAsync_RuntimeSlot_SetsSourceRuntime()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await element.DomFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "id" && slot == DomPropertyDataSlot.Runtime)
				return ValueTask.FromResult(DomPropertyQueryResult.DirectConstant("runtime-id"));
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.True(element.Id.SourceRuntime.IsSet);
		Assert.Equal("runtime-id", element.Id.SourceRuntime.Value);
	}

	[Fact]
	public async Task FillAsync_AllThreeSlotsFilled_AllValuesPresent()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var link = new ElementPropertyLink(ElementPropertyLinkKind.CssExpression, "var(--color)");

		await element.DomFillAsync((scope, xpath, name, slot) =>
		{
			if (name != "id") return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
			return slot switch
			{
				DomPropertyDataSlot.Initialization => ValueTask.FromResult(DomPropertyQueryResult.DirectConstant("init")),
				DomPropertyDataSlot.Link => ValueTask.FromResult(DomPropertyQueryResult.Captured(
					"", ElementPropertyValueSource.LinkedCalculation, link)),
				DomPropertyDataSlot.Runtime => ValueTask.FromResult(DomPropertyQueryResult.DirectConstant("runtime")),
				_ => ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""))
			};
		});

		Assert.Equal("init", element.Id.SourceInitialization.Value);
		Assert.Equal(
			ElementPropertyValueSource.DirectConstant,
			element.Id.SourceInitializationValueSource);
		Assert.True(element.Id.SourceLink.IsSet);
		Assert.Equal(
			ElementPropertyValueSource.LinkedCalculation,
			element.Id.SourceLinkValueSource);
		Assert.Equal("runtime", element.Id.SourceRuntime.Value);
		Assert.Equal(
			ElementPropertyValueSource.DirectConstant,
			element.Id.SourceRuntimeValueSource);
	}

	[Fact]
	public async Task FillAsync_LinkedValueWithoutPersistedLink_FailsConsistencyGate()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var link = new ElementPropertyLink(
			ElementPropertyLinkKind.CssExpression,
			"var(--id)");

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			element.DomFillAsync((scope, xpath, name, slot) =>
				ValueTask.FromResult(
					name == "id" && slot == DomPropertyDataSlot.Initialization
						? DomPropertyQueryResult.Captured(
							"linked-id",
							ElementPropertyValueSource.LinkedCalculation,
							link)
						: DomPropertyQueryResult.ConfirmedAbsent("missing link")))
				.AsTask());
	}

	[Fact]
	public async Task FillAsync_ConfirmedAbsent_DoesNotSetSlot()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await element.DomFillAsync((scope, xpath, name, slot) =>
			ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent("not present")));

		Assert.False(element.Id.SourceInitialization.IsSet);
		Assert.False(element.Id.SourceLink.IsSet);
		Assert.False(element.Id.SourceRuntime.IsSet);
	}

	[Fact]
	public async Task FillAsync_ConfirmedAbsentAfterCapture_ClearsPriorSlot()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await element.DomFillAsync((scope, xpath, name, slot) =>
			ValueTask.FromResult(
				name == "id" && slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant("stale-id")
					: DomPropertyQueryResult.ConfirmedAbsent("not present")));
		Assert.True(element.Id.SourceInitialization.IsSet);

		await element.DomFillAsync((scope, xpath, name, slot) =>
			ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent("removed")));

		Assert.False(element.Id.SourceInitialization.IsSet);
	}

	[Fact]
	public async Task DomFillAsync_WithEngine_DistinguishesSameQueryNameByStrongIdentity()
	{
		var element = new HtmlAnchorDomElement(Mapping("/html/body/a"));
		var engine = new RecordingDomQueryEngine();

		await element.DomFillAsync(engine);

		var hrefContexts = engine.Contexts
			.Where(static context => context.PropertyName == "href")
			.ToArray();
		Assert.Contains(
			hrefContexts,
			static context =>
				context.ReflectedPropertyName == "Href"
					&& context.OwnerKind == ElementSlotOwnerKind.Attribute);
		Assert.Contains(
			hrefContexts,
			static context =>
				context.ReflectedPropertyName == "DataSources"
					&& context.OwnerKind == ElementSlotOwnerKind.DataSource);
	}

	[Fact]
	public async Task FillAsync_SourceUnsupported_DoesNotSetSlot()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await element.DomFillAsync((scope, xpath, name, slot) =>
			ValueTask.FromResult(DomPropertyQueryResult.SourceUnsupported("not supported")));

		Assert.False(element.Id.SourceInitialization.IsSet);
	}

	[Fact]
	public async Task FillAsync_ValueSourceDirectConstant_DoesNotRequireLink()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		// DirectConstant 不需要 Link，不应该抛异常
		await element.DomFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "id" && slot == DomPropertyDataSlot.Initialization)
				return ValueTask.FromResult(DomPropertyQueryResult.DirectConstant("value"));
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.Equal("value", element.Id.SourceInitialization.Value);
	}

	[Fact]
	public async Task FillAsync_ValueSourceContainerAutomaticLayout_RequiresContainerLayoutLink()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		// ContainerAutomaticLayout 必须携带 ContainerLayout 类型的 Link
		var binding = new ContainerLayoutBinding(
			ContainerLayoutMechanism.NormalFlow,
			new LayoutContainerReference(LayoutContainerReferenceKind.ContainingBlock, LayoutReferenceBox.ContentBox),
			new ContainerSizeConstraint(ElementSpaceSlotKind.Width,
				new LayoutLength.Percentage(100, LayoutPercentageBasis.ContainingBlockWidth)),
			"width:100%");

		await element.DomFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "id" && slot == DomPropertyDataSlot.Link)
				return ValueTask.FromResult(DomPropertyQueryResult.Captured(
					"", ElementPropertyValueSource.ContainerAutomaticLayout,
					ElementPropertyLink.FromContainerLayout(binding)));
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.True(element.Id.SourceLink.IsSet);
		Assert.Equal(ElementPropertyLinkKind.ContainerLayout, element.Id.SourceLink.Kind);
	}

	[Fact]
	public async Task FillAsync_RecursivelyFillsChildren()
	{
		var parent = new HtmlDivDomElement(Mapping("/html/body/div"));
		var child = new HtmlSpanDomElement(Mapping("/html/body/div/span"));
		parent.AddChild(child);

		var queriedElements = new HashSet<string>();

		await parent.DomFillAsync((scope, xpath, name, slot) =>
		{
			queriedElements.Add(xpath);
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
		});

		// 父元素和子元素都应该被查询
		Assert.Contains("/html/body/div", queriedElements);
		Assert.Contains("/html/body/div/span", queriedElements);
	}

	[Fact]
	public async Task FillAsync_ExtensionAttributes_AreAlsoFilled()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		element.AddExtensionAttribute("data-custom");

		var foundCustom = false;

		await element.DomFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "data-custom")
			{
				foundCustom = true;
				if (slot == DomPropertyDataSlot.Initialization)
					return ValueTask.FromResult(DomPropertyQueryResult.DirectConstant("custom-value"));
			}
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.True(foundCustom, "扩展属性 data-custom 应该被查询");
		var extAttr = element.ExtensionAttributes.First(a => a.Name == "data-custom");
		Assert.Equal("custom-value", extAttr.SourceInitialization.Value);
	}

	[Fact]
	public async Task FillAsync_RuntimeProperties_AreAlsoFilled()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		element.AddRuntimeProperty("rect.x", ElementSlotCategory.Space);

		var foundRectX = false;

		await element.DomFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "rect.x")
			{
				foundRectX = true;
				if (slot == DomPropertyDataSlot.Runtime)
					return ValueTask.FromResult(DomPropertyQueryResult.DirectConstant("100"));
			}
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.True(foundRectX, "运行时属性 rect.x 应该被查询");
	}

	[Fact]
	public async Task FillAsync_EventsCollection_IsFilled()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var capturedEvent = new CapturedDomElementEvent(
			"event.click.0", DomEventKind.Click, "click");
		element.Events.Add(capturedEvent);

		var foundEvent = false;

		await element.DomFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "event.click.0")
			{
				foundEvent = true;
			}
			return ValueTask.FromResult(DomPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.True(foundEvent, "事件属性应该被查询");
	}

	private static DomElementMapping Mapping(string xpath) =>
		new("document", xpath, null, [], null, null);

	private sealed class RecordingDomQueryEngine : IDomPropertyQueryEngine
	{
		public List<DomPropertyQueryContext> Contexts { get; } = [];

		public ValueTask<DomPropertyQueryResult> QueryAsync(
			DomPropertyQueryContext context,
			CancellationToken cancellationToken = default)
		{
			Contexts.Add(context);
			return ValueTask.FromResult(
				DomPropertyQueryResult.ConfirmedAbsent("recorded"));
		}
	}
}
