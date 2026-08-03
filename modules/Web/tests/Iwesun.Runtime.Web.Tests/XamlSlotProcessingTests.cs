using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

/// <summary>
/// 验证 XAML 端槽位属性处理是否完备
/// </summary>
public sealed class XamlSlotProcessingTests
{
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
	public async Task XamlFillAsync_QueriesAllThreeSlots()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var queriedSlots = new List<(string Name, XamlPropertyDataSlot Slot)>();

		await element.XamlFillAsync((scope, xpath, name, slot) =>
		{
			queriedSlots.Add((name, slot));
			return ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent(""));
		});

		// 验证 Id 属性的三个 XAML 槽位都被查询了
		var idSlots = queriedSlots.Where(s => s.Name == "id").ToList();
		Assert.Equal(3, idSlots.Count);
		Assert.Contains(idSlots, s => s.Slot == XamlPropertyDataSlot.Initialization);
		Assert.Contains(idSlots, s => s.Slot == XamlPropertyDataSlot.Link);
		Assert.Contains(idSlots, s => s.Slot == XamlPropertyDataSlot.Runtime);
	}

	[Fact]
	public async Task XamlFillAsync_InitializationSlot_SetsXamlInitialization()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await element.XamlFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "id" && slot == XamlPropertyDataSlot.Initialization)
				return ValueTask.FromResult(XamlPropertyQueryResult.DirectConstant("test-id"));
			return ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.True(element.Id.XamlInitialization.IsSet);
		Assert.Equal("test-id", element.Id.XamlInitialization.Value);
		Assert.False(element.Id.XamlLink.IsSet);
		Assert.False(element.Id.XamlRuntime.IsSet);
	}

	[Fact]
	public async Task XamlFillAsync_LinkSlot_SetsXamlLink()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var link = new ElementPropertyLink(ElementPropertyLinkKind.XamlBinding, "{Binding Width}");

		await element.XamlFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "id" && slot == XamlPropertyDataSlot.Link)
				return ValueTask.FromResult(XamlPropertyQueryResult.Captured(
					"", ElementPropertyValueSource.LinkedCalculation, link));
			return ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.True(element.Id.XamlLink.IsSet);
		Assert.Equal(ElementPropertyLinkKind.XamlBinding, element.Id.XamlLink.Kind);
	}

	[Fact]
	public async Task XamlFillAsync_RuntimeSlot_SetsXamlRuntime()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await element.XamlFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "id" && slot == XamlPropertyDataSlot.Runtime)
				return ValueTask.FromResult(XamlPropertyQueryResult.DirectConstant("runtime-value"));
			return ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.True(element.Id.XamlRuntime.IsSet);
		Assert.Equal("runtime-value", element.Id.XamlRuntime.Value);
	}

	[Fact]
	public async Task XamlFillAsync_AllThreeSlotsFilled()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var link = new ElementPropertyLink(ElementPropertyLinkKind.XamlBinding, "{Binding}");

		await element.XamlFillAsync((scope, xpath, name, slot) =>
		{
			if (name != "id") return ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent(""));
			return slot switch
			{
				XamlPropertyDataSlot.Initialization => ValueTask.FromResult(XamlPropertyQueryResult.DirectConstant("init")),
				XamlPropertyDataSlot.Link => ValueTask.FromResult(XamlPropertyQueryResult.Captured(
					"", ElementPropertyValueSource.LinkedCalculation, link)),
				XamlPropertyDataSlot.Runtime => ValueTask.FromResult(XamlPropertyQueryResult.DirectConstant("runtime")),
				_ => ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent(""))
			};
		});

		Assert.Equal("init", element.Id.XamlInitialization.Value);
		Assert.True(element.Id.XamlLink.IsSet);
		Assert.Equal("runtime", element.Id.XamlRuntime.Value);
	}

	[Fact]
	public async Task XamlFillAsync_ConfirmedAbsent_DoesNotSetSlot()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await element.XamlFillAsync((scope, xpath, name, slot) =>
			ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent("not present")));

		Assert.False(element.Id.XamlInitialization.IsSet);
		Assert.False(element.Id.XamlLink.IsSet);
		Assert.False(element.Id.XamlRuntime.IsSet);
	}

	[Fact]
	public async Task XamlFillAsync_ConfirmedAbsentAfterCapture_ClearsPriorSlot()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await element.XamlFillAsync((scope, xpath, name, slot) =>
			ValueTask.FromResult(
				name == "id" && slot == XamlPropertyDataSlot.Initialization
					? XamlPropertyQueryResult.DirectConstant("stale-id")
					: XamlPropertyQueryResult.ConfirmedAbsent("not present")));
		Assert.True(element.Id.XamlInitialization.IsSet);

		await element.XamlFillAsync((scope, xpath, name, slot) =>
			ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent("removed")));

		Assert.False(element.Id.XamlInitialization.IsSet);
	}

	[Fact]
	public void AuditSlots_WhenOnlyXamlHasValue_ReportsUnexpectedXamlValue()
	{
		var property = new DomElementStringProperty(
			"id",
			ElementDataOrganizationSlotKind.Attribute);
		property.ApplyXamlQueryResult(
			XamlPropertyDataSlot.Initialization,
			XamlPropertyQueryResult.DirectConstant("unexpected"));

		var result = property.AuditSlots();

		Assert.Equal(
			ElementSlotFeatureAuditStatus.UnexpectedXamlValue,
			result.Initialization.Status);
		Assert.False(result.Passed);
	}

	[Fact]
	public async Task XamlFillAsync_TargetUnsupported_DoesNotSetSlot()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		var fill = await element.XamlFillAsync((scope, xpath, name, slot) =>
			ValueTask.FromResult(XamlPropertyQueryResult.TargetUnsupported("not supported")));

		Assert.False(element.Id.XamlInitialization.IsSet);
		Assert.NotEmpty(fill.Slots);
		Assert.All(
			fill.Slots,
			static trace => Assert.Equal(
				XamlPropertyQueryStatus.TargetUnsupported,
				trace.QueryStatus));
	}

	[Fact]
	public async Task XamlFillAsync_DirectConstant_DoesNotRequireLink()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await element.XamlFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "id" && slot == XamlPropertyDataSlot.Initialization)
				return ValueTask.FromResult(XamlPropertyQueryResult.DirectConstant("value"));
			return ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.Equal("value", element.Id.XamlInitialization.Value);
	}

	[Fact]
	public async Task XamlFillAsync_RecursivelyFillsChildren()
	{
		var parent = new HtmlDivDomElement(Mapping("/html/body/div"));
		var child = new HtmlSpanDomElement(Mapping("/html/body/div/span"));
		parent.AddChild(child);

		var queriedElements = new HashSet<string>();

		await parent.XamlFillAsync((scope, xpath, name, slot) =>
		{
			queriedElements.Add(xpath);
			return ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.Contains("/html/body/div", queriedElements);
		Assert.Contains("/html/body/div/span", queriedElements);
	}

	[Fact]
	public async Task XamlFillAsync_WithEngine_UsesEngine()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var engine = new TestXamlQueryEngine();
		engine.SetResponse("id", XamlPropertyDataSlot.Initialization, "engine-value");
		element.XamlQueryEngine = engine;

		await element.XamlFillAsync();

		Assert.Equal("engine-value", element.Id.XamlInitialization.Value);
		Assert.True(engine.QueryCount > 0);
	}

	[Fact]
	public async Task XamlFillAsync_WithAssignedEngine_PreservesExecutionAndControlContext()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var xamlElement = new object();
		element.XamlElement = xamlElement;
		var engine = new TestXamlQueryEngine();
		engine.SetResponse("rect.x", XamlPropertyDataSlot.Runtime, "10");
		element.XamlQueryEngine = engine;

		await element.XamlFillAsync();

		var context = Assert.Single(
			engine.Contexts.Where(static item =>
				item.PropertyName == "rect.x"
					&& item.Slot == XamlPropertyDataSlot.Runtime));
		Assert.Same(element, context.Element);
		Assert.Same(xamlElement, context.XamlElement);
		Assert.Equal("RuntimeGeometry", context.ReflectedPropertyName);
		Assert.Equal(ElementSlotOwnerKind.RuntimeProperty, context.OwnerKind);
		Assert.Equal(
			XamlPropertyExecutionKind.RuntimeGeometry,
			context.Execution.Kind);
		Assert.True(element.RuntimeGeometry.X.XamlRuntime.IsSet);
		Assert.Equal("10", element.RuntimeGeometry.X.XamlRuntime.Value);
		Assert.False(element.RuntimeGeometry.X.XamlInitialization.IsSet);
		Assert.False(element.RuntimeGeometry.X.XamlLink.IsSet);
	}

	[Fact]
	public async Task XamlFillAsync_WithoutEngine_ThrowsWhenNoDelegate()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));

		await Assert.ThrowsAsync<InvalidOperationException>(() => element.XamlFillAsync().AsTask());
	}

	[Fact]
	public async Task XamlFillAsync_ExtensionAttributes_AreAlsoFilled()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		element.AddExtensionAttribute("data-custom");

		var foundCustom = false;

		await element.XamlFillAsync((scope, xpath, name, slot) =>
		{
			if (name == "data-custom")
			{
				foundCustom = true;
				if (slot == XamlPropertyDataSlot.Initialization)
					return ValueTask.FromResult(XamlPropertyQueryResult.DirectConstant("custom-value"));
			}
			return ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent(""));
		});

		Assert.True(foundCustom, "扩展属性 data-custom 应该被查询");
	}

	private static DomElementMapping Mapping(string xpath) =>
		new("document", xpath, null, [], null, null);

	private sealed class TestXamlQueryEngine : IXamlPropertyQueryEngine
	{
		private readonly Dictionary<(string Name, XamlPropertyDataSlot Slot), string> _responses = new();
		public int QueryCount { get; private set; }
		public List<XamlPropertyQueryContext> Contexts { get; } = [];

		public void SetResponse(string propertyName, XamlPropertyDataSlot slot, string value)
		{
			_responses[(propertyName, slot)] = value;
		}

		public ValueTask<XamlPropertyQueryResult> QueryAsync(
			XamlPropertyQueryContext context,
			CancellationToken cancellationToken = default)
		{
			QueryCount++;
			Contexts.Add(context);
			if (_responses.TryGetValue((context.PropertyName, context.Slot), out var value))
			{
				return ValueTask.FromResult(XamlPropertyQueryResult.DirectConstant(value));
			}
			return ValueTask.FromResult(XamlPropertyQueryResult.ConfirmedAbsent("not set"));
		}
	}
}
