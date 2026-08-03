using Xunit;

namespace Iwesun.Runtime.Web;

public sealed class StrongCssLayoutPlanTests
{
	[Fact]
	public async Task GridPlan_ResolvesDensePlacementAndNamedNegativeLines()
	{
		var parent = new HtmlDivDomElement(Map(
			"/html/body/div", null,
			["/html/body/div/span[1]", "/html/body/div/span[2]", "/html/body/div/span[3]"]));
		var first = new HtmlSpanDomElement(Map("/html/body/div/span[1]", parent.XPath, []));
		var second = new HtmlSpanDomElement(Map("/html/body/div/span[2]", parent.XPath, []));
		var third = new HtmlSpanDomElement(Map("/html/body/div/span[3]", parent.XPath, []));
		parent.AddChild(first);
		parent.AddChild(second);
		parent.AddChild(third);
		Add(parent, "style.display", "style.gridTemplateColumns", "style.gridAutoRows", "style.gridAutoFlow");
		Add(first, "style.gridColumn");
		Add(second, "style.gridColumn");
		await FillAsync(parent,
			("style.display", "grid"),
			("style.gridTemplateColumns", "[start lane] 30px [middle lane] 30px [last lane] 30px [end]"),
			("style.gridAutoRows", "20px"),
			("style.gridAutoFlow", "row dense"));
		await FillAsync(first, ("style.gridColumn", "lane 1 / lane -1"));
		await FillAsync(second, ("style.gridColumn", "span 2"));
		await FillAsync(third);

		var root = Assert.IsType<Node>(Assert.Single(
			parent.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal(3, root.Columns.Count);
		Assert.Equal(2, root.Rows.Count);
		Assert.All(root.Columns, static definition =>
		{
			var length = Assert.IsType<LayoutLength.Constant>(definition.StrongLength);
			Assert.Equal(LayoutLengthUnit.CssPixel, length.Unit);
		});
		Assert.Equal((0, 0, 2), Cell(first));
		Assert.Equal((1, 0, 2), Cell(second));
		Assert.Equal((0, 2, 1), Cell(third));
	}

	[Fact]
	public async Task FlexPlan_CreatesStrongWrappedLinesWithoutApplicationRewrite()
	{
		var parent = new HtmlArticleDomElement(Map(
			"/html/body/article", null,
			["/html/body/article/span[1]", "/html/body/article/span[2]", "/html/body/article/span[3]"]));
		var children = Enumerable.Range(1, 3)
			.Select(index => new HtmlSpanDomElement(Map(
				$"/html/body/article/span[{index}]", parent.XPath, [])))
			.ToArray();
		foreach (var child in children)
		{
			parent.AddChild(child);
			Add(child, "style.flexBasis", "style.height");
		}
		Add(parent, "style.display", "style.flexDirection", "style.flexWrap", "style.width", "style.height", "style.columnGap", "style.rowGap", "style.alignContent");
		await FillAsync(parent,
			("style.display", "flex"),
			("style.flexDirection", "row"),
			("style.flexWrap", "wrap"),
			("style.width", "120px"),
			("style.height", "100px"),
			("style.columnGap", "10px"),
			("style.rowGap", "5px"),
			("style.alignContent", "space-between"));
		foreach (var child in children)
			await FillAsync(child, ("style.flexBasis", "50px"), ("style.height", "20px"));
		Assert.All(children, static child => Assert.Equal(
			"50px",
			child.RuntimeProperties.Single(static property =>
				property.Name == "style.flexBasis").SourceInitialization.Value));

		var root = Assert.IsType<Node>(Assert.Single(
			parent.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal(["80", "20"], root.Rows.Select(static row => row.Length));
		Assert.Equal(2, root.Children.Count);
		Assert.Equal("HtmlCssHorizontalFlexLineGrid", root.Children[0].ElementName);
		Assert.Equal(2, root.Children[0].Children.Count);
		Assert.Single(root.Children[1].Children);
	}

	[Fact]
	public async Task FlexPlan_IncludesMainAxisMarginsInOuterTrackSizing()
	{
		var parent = new HtmlDivDomElement(Map(
			"/html/body/div", null,
			["/html/body/div/span[1]", "/html/body/div/span[2]"]));
		var first = new HtmlSpanDomElement(Map(
			"/html/body/div/span[1]", parent.XPath, []));
		var second = new HtmlSpanDomElement(Map(
			"/html/body/div/span[2]", parent.XPath, []));
		parent.AddChild(first);
		parent.AddChild(second);
		Add(parent, "style.display", "style.flexDirection", "style.flexWrap", "style.height");
		foreach (var child in new[] { first, second })
		{
			Add(
				child,
				"style.height",
				"style.flexGrow",
				"style.marginTop",
				"style.marginBottom");
		}
		await FillAsync(
			parent,
			("style.display", "flex"),
			("style.flexDirection", "column"),
			("style.flexWrap", "nowrap"),
			("style.height", "100px"));
		await FillAsync(
			first,
			("style.height", "20px"),
			("style.flexGrow", "0"),
			("style.marginTop", "5px"),
			("style.marginBottom", "7px"));
		await FillAsync(
			second,
			("style.height", "20px"),
			("style.flexGrow", "1"),
			("style.marginTop", "3px"),
			("style.marginBottom", "4px"));

		var root = Assert.IsType<Node>(Assert.Single(
			parent.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal(["32", "68"], root.Rows.Select(static row => row.Length));
	}

	[Fact]
	public async Task FlexPlan_UsesBorderBoxContentExtentForSpaceDistribution()
	{
		var parent = new HtmlDivDomElement(Map(
			"/html/body/div", null,
			["/html/body/div/span[1]", "/html/body/div/span[2]"]));
		var first = new HtmlSpanDomElement(Map(
			"/html/body/div/span[1]", parent.XPath, []));
		var second = new HtmlSpanDomElement(Map(
			"/html/body/div/span[2]", parent.XPath, []));
		parent.AddChild(first);
		parent.AddChild(second);
		Add(
			parent,
			"style.display",
			"style.flexDirection",
			"style.flexWrap",
			"style.width",
			"style.boxSizing",
			"style.paddingLeft",
			"style.paddingRight",
			"style.justifyContent");
		foreach (var child in new[] { first, second })
			Add(child, "style.width", "style.flexGrow");
		await FillAsync(
			parent,
			("style.display", "flex"),
			("style.flexDirection", "row"),
			("style.flexWrap", "nowrap"),
			("style.width", "100px"),
			("style.boxSizing", "border-box"),
			("style.paddingLeft", "10px"),
			("style.paddingRight", "10px"),
			("style.justifyContent", "space-between"));
		await FillAsync(first, ("style.width", "20px"), ("style.flexGrow", "0"));
		await FillAsync(second, ("style.width", "20px"), ("style.flexGrow", "0"));

		var root = Assert.IsType<Node>(Assert.Single(
			parent.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal(["60", "20"], root.Columns.Select(static column => column.Length));
	}

	[Fact]
	public async Task FlexPlan_ColumnReversePlacesSpaceBetweenPhysicalRows()
	{
		var parent = new HtmlDivDomElement(Map(
			"/html/body/div", null,
			["/html/body/div/div[1]", "/html/body/div/div[2]"]));
		var first = new HtmlDivDomElement(Map(
			"/html/body/div/div[1]", parent.XPath, []));
		var second = new HtmlDivDomElement(Map(
			"/html/body/div/div[2]", parent.XPath, []));
		parent.AddChild(first);
		parent.AddChild(second);
		Add(
			parent,
			"style.display",
			"style.flexDirection",
			"style.flexWrap",
			"style.height",
			"style.boxSizing",
			"style.paddingTop",
			"style.paddingBottom",
			"style.justifyContent");
		foreach (var child in new[] { first, second })
			Add(child, "style.height", "style.flexGrow");
		await FillAsync(
			parent,
			("style.display", "flex"),
			("style.flexDirection", "column-reverse"),
			("style.flexWrap", "nowrap"),
			("style.height", "100px"),
			("style.boxSizing", "border-box"),
			("style.paddingTop", "10px"),
			("style.paddingBottom", "10px"),
			("style.justifyContent", "space-between"));
		await FillAsync(first, ("style.height", "36px"), ("style.flexGrow", "0"));
		await FillAsync(second, ("style.height", "30px"), ("style.flexGrow", "0"));

		var root = Assert.IsType<Node>(Assert.Single(
			parent.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal(["44", "36"], root.Rows.Select(static row => row.Length));
		Assert.Equal("1", ((Node)first.XamlElement!).Attributes["Grid.Row"]);
		Assert.Equal("0", ((Node)second.XamlElement!).Attributes["Grid.Row"]);
	}

	[Fact]
	public async Task FlexPlan_DisplayNoneChildDoesNotCreateTrack()
	{
		var parent = new HtmlDivDomElement(Map(
			"/html/body/div", null,
			["/html/body/div/div[1]", "/html/body/div/div[2]"]));
		var visible = new HtmlDivDomElement(Map(
			"/html/body/div/div[1]", parent.XPath, []));
		var hidden = new HtmlDivDomElement(Map(
			"/html/body/div/div[2]", parent.XPath, []));
		parent.AddChild(visible);
		parent.AddChild(hidden);
		Add(parent, "style.display", "style.flexDirection", "style.flexWrap");
		Add(visible, "style.display", "style.width", "style.flexGrow");
		Add(hidden, "style.display", "style.width", "style.flexGrow");
		await FillAsync(
			parent,
			("style.display", "flex"),
			("style.flexDirection", "row"),
			("style.flexWrap", "nowrap"));
		await FillAsync(
			visible,
			("style.display", "block"),
			("style.width", "20px"),
			("style.flexGrow", "0"));
		await FillAsync(
			hidden,
			("style.display", "none"),
			("style.width", "30px"),
			("style.flexGrow", "0"));
		Assert.Equal(
			"none",
			hidden.RuntimeProperties.Single(static property =>
				property.Name == "style.display").SourceInitialization.Value);

		var root = Assert.IsType<Node>(Assert.Single(
			parent.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal(["20"], root.Columns.Select(static column => column.Length));
		Assert.Equal("0", ((Node)visible.XamlElement!).Attributes["Grid.Column"]);
		Assert.DoesNotContain(
			"Grid.Column",
			((Node)hidden.XamlElement!).Attributes.Keys);
	}

	[Fact]
	public async Task FlexPlan_CarriesCenteredMainAxisOffsetInStrongChildPlacement()
	{
		var parent = new HtmlDivDomElement(Map(
			"/html/body/div", null, ["/html/body/div/span"]));
		var child = new HtmlSpanDomElement(Map(
			"/html/body/div/span", parent.XPath, []));
		parent.AddChild(child);
		Add(
			parent,
			"style.display",
			"style.flexDirection",
			"style.flexWrap",
			"style.height",
			"style.justifyContent");
		Add(child, "style.position", "style.height", "style.flexGrow");
		await FillAsync(
			parent,
			("style.display", "flex"),
			("style.flexDirection", "column"),
			("style.flexWrap", "nowrap"),
			("style.height", "100px"),
			("style.justifyContent", "center"));
		await FillAsync(
			child,
			("style.position", "static"),
			("style.height", "20px"),
			("style.flexGrow", "0"));

		parent.BuildXamlObjectTree(new Factory());

		var offset = Assert.IsType<LayoutLength.Constant>(
			child.XamlObjectNode!.Plan.LayoutPlacement!.MainAxisOffset);
		Assert.Equal(40, offset.Value);
		Assert.Equal(LayoutLengthUnit.CssPixel, offset.Unit);
	}

	[Fact]
	public async Task SvgRootPlan_UsesStrongViewportAndContentBoxOuterSize()
	{
		var svg = new SvgRootDomElement(Map("/html/body/svg", null, []));
		foreach (var name in new[]
		{
			"style.width", "style.height", "style.boxSizing",
			"style.paddingTop", "style.paddingRight",
			"style.paddingBottom", "style.paddingLeft",
			"style.borderTopWidth", "style.borderRightWidth",
			"style.borderBottomWidth", "style.borderLeftWidth"
		})
		{
			svg.AddRuntimeProperty(name, ElementSlotCategory.Style);
		}
		await FillAsync(
			svg,
			("style.width", "18px"),
			("style.height", "18px"),
			("style.boxSizing", "content-box"),
			("style.paddingTop", "2px"),
			("style.paddingRight", "2px"),
			("style.paddingBottom", "2px"),
			("style.paddingLeft", "2px"),
			("style.borderTopWidth", "0px"),
			("style.borderRightWidth", "0px"),
			("style.borderBottomWidth", "0px"),
			("style.borderLeftWidth", "0px"));

		var root = Assert.IsType<Node>(Assert.Single(
			svg.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal("HtmlSvgViewport", root.ElementName);
		Assert.Equal("22", root.Attributes["Width"]);
		Assert.Equal("22", root.Attributes["Height"]);
		Assert.Equal("2,2,2,2", root.Attributes["Padding"]);
	}

	[Fact]
	public async Task SvgPathPlan_PreservesGeometryOwnedIntrinsicDimensions()
	{
		var path = new SvgPathDomElement(Map("/html/body/svg/path", null, []));
		path.AddRuntimeProperty("style.width", ElementSlotCategory.Style);
		path.AddRuntimeProperty("style.height", ElementSlotCategory.Style);
		await FillAsync(
			path,
			("d", "M1 1 L17 1 L17 17 Z"),
			("style.width", "18px"),
			("style.height", "18px"));

		var root = Assert.IsType<Node>(Assert.Single(
			path.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal("Path", root.ElementName);
		Assert.Equal("M1 1 L17 1 L17 17 Z", root.Attributes["Data"]);
		Assert.DoesNotContain("Width", root.Attributes.Keys);
		Assert.DoesNotContain("Height", root.Attributes.Keys);
	}

	[Fact]
	public async Task BlockPlan_AllocatesCollapsedAdjacentVerticalMargins()
	{
		var parent = new HtmlDivDomElement(Map(
			"/html/body/div", null,
			["/html/body/div/div[1]", "/html/body/div/div[2]"]));
		var first = new HtmlDivDomElement(Map(
			"/html/body/div/div[1]", parent.XPath, []));
		var second = new HtmlDivDomElement(Map(
			"/html/body/div/div[2]", parent.XPath, []));
		parent.AddChild(first);
		parent.AddChild(second);
		Add(parent, "style.display");
		foreach (var child in new[] { first, second })
			Add(child, "style.height", "style.marginTop", "style.marginBottom");
		await FillAsync(parent, ("style.display", "block"));
		await FillAsync(
			first,
			("style.height", "114px"),
			("style.marginTop", "0px"),
			("style.marginBottom", "22px"));
		await FillAsync(
			second,
			("style.height", "994px"),
			("style.marginTop", "0px"),
			("style.marginBottom", "0px"));

		var root = Assert.IsType<Node>(Assert.Single(
			parent.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal(["136", "994"], root.Rows.Select(static row => row.Length));
	}

	[Fact]
	public async Task BlockPlan_CollapsesRatherThanAddsAdjacentPositiveMargins()
	{
		var parent = new HtmlDivDomElement(Map(
			"/html/body/div", null,
			["/html/body/div/div[1]", "/html/body/div/div[2]"]));
		var first = new HtmlDivDomElement(Map(
			"/html/body/div/div[1]", parent.XPath, []));
		var second = new HtmlDivDomElement(Map(
			"/html/body/div/div[2]", parent.XPath, []));
		parent.AddChild(first);
		parent.AddChild(second);
		Add(parent, "style.display");
		foreach (var child in new[] { first, second })
			Add(child, "style.height", "style.marginTop", "style.marginBottom");
		await FillAsync(parent, ("style.display", "block"));
		await FillAsync(
			first,
			("style.height", "100px"),
			("style.marginTop", "0px"),
			("style.marginBottom", "20px"));
		await FillAsync(
			second,
			("style.height", "100px"),
			("style.marginTop", "10px"),
			("style.marginBottom", "0px"));

		var root = Assert.IsType<Node>(Assert.Single(
			parent.BuildXamlObjectTree(new Factory()).RootObjects));

		// The next child contributes its own 10px top Margin. The preceding
		// row contributes the remaining 10px, yielding CSS max(20px, 10px).
		Assert.Equal(["110", "100"], root.Rows.Select(static row => row.Length));
	}

	[Fact]
	public async Task FormControlPlan_InheritsStrongCssBoxModelFromItsElementFamily()
	{
		var button = new HtmlButtonDomElement(Map("/html/body/button", null, []));
		Add(
			button,
			"style.display",
			"style.boxSizing",
			"style.width",
			"style.height",
			"style.paddingTop",
			"style.paddingRight",
			"style.paddingBottom",
			"style.paddingLeft",
			"style.borderTopWidth",
			"style.borderRightWidth",
			"style.borderBottomWidth",
			"style.borderLeftWidth");
		await FillAsync(
			button,
			("style.display", "flex"),
			("style.boxSizing", "border-box"),
			("style.width", "26px"),
			("style.height", "26px"),
			("style.paddingTop", "4px"),
			("style.paddingRight", "4px"),
			("style.paddingBottom", "4px"),
			("style.paddingLeft", "4px"),
			("style.borderTopWidth", "0px"),
			("style.borderRightWidth", "0px"),
			("style.borderBottomWidth", "0px"),
			("style.borderLeftWidth", "0px"));

		var node = Assert.IsType<Node>(Assert.Single(
			button.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal("HtmlInteractiveFlexPanel", node.ElementName);
		Assert.Equal("4,4,4,4", node.Attributes["Padding"]);
		Assert.Equal("0,0,0,0", node.Attributes["BorderThickness"]);
		var padding = Assert.Single(
			button.XamlObjectNode!.Plan.InitializationAttributes,
			static attribute => attribute.Name == "Padding");
		Assert.Equal(XamlCompositeValueKind.Thickness, padding.CompositeValueKind);
	}

	[Fact]
	public async Task ButtonFlexPlan_AppliesGapAndExcludesAbsoluteChildrenFromTracks()
	{
		var button = new HtmlButtonDomElement(Map(
			"/html/body/button",
			null,
			[
				"/html/body/button/svg",
				"/html/body/button/div[1]",
				"/html/body/button/div[2]"
			]));
		var svg = new SvgRootDomElement(Map(
			"/html/body/button/svg", button.XPath, []));
		var absolute = new HtmlDivDomElement(Map(
			"/html/body/button/div[1]", button.XPath, []));
		var trailing = new HtmlDivDomElement(Map(
			"/html/body/button/div[2]", button.XPath, []));
		button.AddChild(svg);
		button.AddChild(absolute);
		button.AddChild(trailing);
		Add(
			button,
			"style.display",
			"style.flexDirection",
			"style.width",
			"style.boxSizing",
			"style.paddingLeft",
			"style.paddingRight",
			"style.columnGap");
		foreach (var child in new DomElement[] { svg, absolute, trailing })
		{
			if (child is HtmlDomElementDefinition html)
				Add(html, "style.position", "style.width", "style.boxSizing", "style.flexGrow", "style.flexShrink", "style.right");
			else if (child is SvgDomElementDefinition vector)
			{
				foreach (var name in new[] { "style.position", "style.width", "style.boxSizing", "style.flexGrow", "style.flexShrink" })
					vector.AddRuntimeProperty(name, ElementSlotCategory.Style);
			}
		}
		await FillAsync(
			button,
			("style.display", "flex"),
			("style.flexDirection", "row"),
			("style.width", "39.38281px"),
			("style.boxSizing", "border-box"),
			("style.paddingLeft", "4px"),
			("style.paddingRight", "4px"),
			("style.columnGap", "4px"));
		await FillAsync(svg, ("style.position", "static"), ("style.width", "18px"), ("style.boxSizing", "border-box"), ("style.flexGrow", "1"), ("style.flexShrink", "1"));
		await FillAsync(absolute, ("style.position", "absolute"), ("style.width", "38px"), ("style.boxSizing", "border-box"), ("style.right", "0px"));
		await FillAsync(trailing, ("style.position", "static"), ("style.width", "9.38281px"), ("style.boxSizing", "border-box"), ("style.flexGrow", "0"), ("style.flexShrink", "1"));

		var node = Assert.IsType<Node>(Assert.Single(
			button.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal(["22", "9.38281"], node.Columns.Select(static column => column.Length));
		Assert.Equal("0", Assert.IsType<Node>(svg.XamlElement).Attributes["Grid.Column"]);
		Assert.False(Assert.IsType<Node>(absolute.XamlElement).Attributes.ContainsKey("Grid.Column"));
		Assert.Equal("1", Assert.IsType<Node>(trailing.XamlElement).Attributes["Grid.Column"]);
		Assert.Equal(
			XamlElementContainingBlockKind.PaddingBox,
			absolute.XamlObjectNode!.Plan.LayoutPlacement!.ContainingBlock);
		var right = Assert.IsType<LayoutLength.Constant>(
			absolute.XamlObjectNode.Plan.LayoutPlacement.Right);
		Assert.Equal(0, right.Value);
		Assert.Equal(LayoutLengthUnit.CssPixel, right.Unit);
	}

	[Fact]
	public async Task ButtonFlexPlan_DisplayNoneChildIsCollapsedAndHasNoTrack()
	{
		var button = new HtmlButtonDomElement(Map(
			"/html/body/button", null,
			["/html/body/button/div[1]", "/html/body/button/div[2]"]));
		var visible = new HtmlDivDomElement(Map(
			"/html/body/button/div[1]", button.XPath, []));
		var hidden = new HtmlDivDomElement(Map(
			"/html/body/button/div[2]", button.XPath, []));
		button.AddChild(visible);
		button.AddChild(hidden);
		Add(button, "style.display", "style.flexDirection", "style.width");
		Add(visible, "style.display", "style.width", "style.flexGrow");
		Add(hidden, "style.display", "style.width", "style.flexGrow");
		await FillAsync(
			button,
			("style.display", "flex"),
			("style.flexDirection", "row"),
			("style.width", "50px"));
		await FillAsync(
			visible,
			("style.display", "block"),
			("style.width", "20px"),
			("style.flexGrow", "0"));
		await FillAsync(
			hidden,
			("style.display", "none"),
			("style.width", "30px"),
			("style.flexGrow", "0"));

		var node = Assert.IsType<Node>(Assert.Single(
			button.BuildXamlObjectTree(new Factory()).RootObjects));

		Assert.Equal(["20"], node.Columns.Select(static column => column.Length));
		Assert.Equal("0", Assert.IsType<Node>(visible.XamlElement).Attributes["Grid.Column"]);
		var hiddenNode = Assert.IsType<Node>(hidden.XamlElement);
		Assert.DoesNotContain("Grid.Column", hiddenNode.Attributes.Keys);
		Assert.Equal("Collapsed", hiddenNode.Attributes["Visibility"]);
	}

	[Fact]
	public async Task BlockPlan_LeftAlignsAChildWithDefiniteWidth()
	{
		var parent = new HtmlDivDomElement(Map(
			"/html/body/div", null, ["/html/body/div/div"]));
		var child = new HtmlDivDomElement(Map(
			"/html/body/div/div", parent.XPath, []));
		parent.AddChild(child);
		Add(parent, "style.display");
		Add(child, "style.position", "style.width", "style.marginLeft", "style.marginRight");
		await FillAsync(parent, ("style.display", "block"));
		await FillAsync(
			child,
			("style.position", "static"),
			("style.width", "0px"),
			("style.marginLeft", "0px"),
			("style.marginRight", "0px"));

		parent.BuildXamlObjectTree(new Factory());

		Assert.Equal(
			XamlElementCrossAlignment.Near,
			child.XamlObjectNode!.Plan.LayoutPlacement!.CrossAlignment);
	}

	[Fact]
	public async Task BlockPlan_DoesNotReAddDimensionsOwnedByParentLayout()
	{
		var html = new HtmlRootDomElement(Map(
			"/html", null, ["/html/body"]));
		var body = new HtmlBodyDomElement(Map(
			"/html/body", html.XPath, ["/html/body/div"]));
		var child = new HtmlDivDomElement(Map(
			"/html/body/div", body.XPath, []));
		html.AddChild(body);
		body.AddChild(child);
		foreach (var element in new HtmlDomElementDefinition[] { html, body, child })
			Add(element, "style.width", "style.height");
		await FillAsync(html, ("style.width", "1920px"), ("style.height", "1010px"));
		await FillAsync(body, ("style.width", "1920px"), ("style.height", "1010px"));
		await FillAsync(child, ("style.width", "1920px"), ("style.height", "1010px"));

		var root = Assert.IsType<Node>(Assert.Single(
			html.BuildXamlObjectTree(new Factory()).RootObjects));
		var bodyNode = Assert.Single(root.Children);
		var childNode = Assert.Single(bodyNode.Children);

		Assert.False(root.Attributes.ContainsKey("Width"));
		Assert.False(root.Attributes.ContainsKey("Height"));
		Assert.False(bodyNode.Attributes.ContainsKey("Width"));
		Assert.False(bodyNode.Attributes.ContainsKey("Height"));
		Assert.False(childNode.Attributes.ContainsKey("Width"));
		Assert.False(childNode.Attributes.ContainsKey("Height"));
		Assert.Single(bodyNode.Rows);
	}

	private static (int Row, int Column, int ColumnSpan) Cell(DomElement element)
	{
		var node = Assert.IsType<Node>(element.XamlElement);
		return (
			int.Parse(node.Attributes["Grid.Row"], System.Globalization.CultureInfo.InvariantCulture),
			int.Parse(node.Attributes["Grid.Column"], System.Globalization.CultureInfo.InvariantCulture),
			node.Attributes.TryGetValue("Grid.ColumnSpan", out var span)
				? int.Parse(span, System.Globalization.CultureInfo.InvariantCulture)
				: 1);
	}

	private static void Add(HtmlDomElementDefinition element, params string[] names)
	{
		foreach (var name in names)
			element.AddRuntimeProperty(name, ElementSlotCategory.Style);
	}

	private static async Task FillAsync(
		DomElement element,
		params (string Name, string Value)[] values)
	{
		var lookup = values.ToDictionary(static item => item.Name, static item => item.Value, StringComparer.Ordinal);
		await element.DomFillAsync((_, _, name, slot) => ValueTask.FromResult(
			slot == DomPropertyDataSlot.Initialization
				&& lookup.TryGetValue(name, out var value)
					? DomPropertyQueryResult.DirectConstant(value)
					: DomPropertyQueryResult.ConfirmedAbsent("Not declared by the test.")));
	}

	private static DomElementMapping Map(string xpath, string? parent, IReadOnlyList<string> children) =>
		new("document", xpath, parent, children, null, null);

	private sealed class Factory : TestXamlElementObjectFactory
	{
		protected override object CreateElementCore(XamlElementObjectPlan plan) =>
			new Node(plan.Mapping.ElementName);

		public override void FillElementProperties(XamlElementObjectPropertyFillContext context)
		{
			var node = (Node)context.Element;
			foreach (var attribute in context.Plan.InitializationAttributes)
				node.Attributes[attribute.Name] = attribute.Value;
		}

		public override void AttachChild(XamlElementObjectAttachmentContext context) =>
			((Node)context.Parent).Children.Add((Node)context.Child);

		public override void ApplyGridTracks(
			object element,
			IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
			IReadOnlyList<XamlGridTrackDefinition> columnDefinitions)
		{
			((Node)element).Rows.AddRange(rowDefinitions);
			((Node)element).Columns.AddRange(columnDefinitions);
		}
	}

	private sealed class Node(string elementName)
	{
		internal string ElementName { get; } = elementName;
		internal Dictionary<string, string> Attributes { get; } = new(StringComparer.Ordinal);
		internal List<Node> Children { get; } = [];
		internal List<XamlGridTrackDefinition> Rows { get; } = [];
		internal List<XamlGridTrackDefinition> Columns { get; } = [];
	}
}
