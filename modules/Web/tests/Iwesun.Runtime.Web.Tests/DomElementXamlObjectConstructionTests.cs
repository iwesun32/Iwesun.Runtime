using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class DomElementXamlObjectConstructionTests
{
	[Fact]
	public async Task BuildXamlObjectTree_CreatesAndConnectsDirectChildren()
	{
		var parent = new HtmlDivDomElement(
			Mapping("/html/body/div", null, ["/html/body/div/span"]));
		var child = new HtmlSpanDomElement(
			Mapping(
				"/html/body/div/span",
				"/html/body/div",
				[]));
		parent.AddChild(child);
		await FillInitializationsAsync(parent);
		var factory = new TestXamlObjectFactory();

		var result = parent.BuildXamlObjectTree(factory);

		var root = Assert.IsType<TestXamlObject>(Assert.Single(result.RootObjects));
		Assert.Same(root, parent.XamlElement);
		var childObject = Assert.Single(root.Children);
		Assert.Same(childObject, child.XamlElement);
		Assert.Same(root, parent.XamlObjectNode?.Element);
		Assert.Same(childObject, child.XamlObjectNode?.Element);
		Assert.Single(parent.XamlOwnedObjects);
		Assert.Single(child.XamlOwnedObjects);
		Assert.Equal(
			XamlElementContentProjectionKind.DirectChildren,
			parent.XamlObjectNode?.Plan.ContentProjection);
		Assert.Equal(
			ElementXamlChildPlacementKind.DirectChildren,
			Assert.Single(factory.Attachments).Placement);
	}

	[Fact]
	public async Task BuildXamlObjectTree_LayoutTextCreatesSyntheticTextBlock()
	{
		var element = new HtmlDivDomElement(
			Mapping("/html/body/div", null, []));
		element.AddRuntimeProperty(
			"content.ownText",
			ElementSlotCategory.DataOrganization);
		await FillOwnTextAsync(element, "Container text");
		var factory = new TestXamlObjectFactory();

		var result = element.BuildXamlObjectTree(factory);

		var root = Assert.IsType<TestXamlObject>(Assert.Single(result.RootObjects));
		var text = Assert.Single(root.Children);
		Assert.Equal("TextBlock", text.ElementName);
		Assert.Equal("Container text", text.Attributes["Text"]);
		Assert.Null(text.SourceElement);
		Assert.Equal(2, element.XamlOwnedObjects.Count);
		Assert.Contains(root, element.XamlOwnedObjects);
		Assert.Contains(text, element.XamlOwnedObjects);
		Assert.Equal(
			XamlElementContentProjectionKind.GeneratedContent,
			element.XamlObjectNode?.Plan.ContentProjection);
	}

	[Fact]
	public async Task BuildXamlObjectTree_BlockOwnTextPreservesCapturedElementOrder()
	{
		var parent = new HtmlDivDomElement(
			Mapping(
				"/html/body/div",
				null,
				["/html/body/div/div"],
				0));
		var child = new HtmlDivDomElement(
			Mapping("/html/body/div/div", parent.XPath, []));
		parent.AddChild(child);
		parent.AddRuntimeProperty(
			"content.ownText",
			ElementSlotCategory.DataOrganization);
		await FillOwnTextAsync(parent, "Before child");
		await FillInitializationsAsync(child);

		var result = parent.BuildXamlObjectTree(new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(Assert.Single(result.RootObjects));
		Assert.Equal(["TextBlock", "HtmlCssBoxGrid"], root.Children
			.Select(static item => item.ElementName));
		Assert.Equal("0", root.Children[0].Attributes["Grid.Row"]);
		Assert.Equal("1", root.Children[1].Attributes["Grid.Row"]);
		Assert.Equal(["Auto", "Auto"], root.RowDefinitions
			.Select(static item => item.Length));
	}

	[Fact]
	public async Task BuildXamlObjectTree_ContentChildrenUseOneGridWrapper()
	{
		var link = new HtmlAnchorDomElement(
			Mapping(
				"/html/body/a",
				null,
				[
					"/html/body/a/span[1]",
					"/html/body/a/span[2]"
				]));
		link.AddChild(new HtmlSpanDomElement(
			Mapping("/html/body/a/span[1]", "/html/body/a", [])));
		link.AddChild(new HtmlSpanDomElement(
			Mapping("/html/body/a/span[2]", "/html/body/a", [])));
		await FillInitializationsAsync(
			link,
			("href", "https://example.test/"));
		var factory = new TestXamlObjectFactory();

		var result = link.BuildXamlObjectTree(factory);

		var root = Assert.IsType<TestXamlObject>(Assert.Single(result.RootObjects));
		var wrapper = Assert.Single(root.Children);
		Assert.Equal("Grid", wrapper.ElementName);
		Assert.Equal(2, wrapper.Children.Count);
		Assert.Contains(wrapper, link.XamlOwnedObjects);
		Assert.Equal(
			XamlElementContentProjectionKind.Content,
			link.XamlObjectNode?.Plan.ContentProjection);
		Assert.Equal(
			ElementXamlChildPlacementKind.Content,
			Assert.Single(factory.Attachments.Where(
				attachment => ReferenceEquals(
					attachment.Parent,
					root))).Placement);
		Assert.All(
			factory.Attachments.Where(
				attachment => ReferenceEquals(
					attachment.Parent,
					wrapper)),
			attachment => Assert.Equal(
				ElementXamlChildPlacementKind.DirectChildren,
				attachment.Placement));
	}

	[Fact]
	public async Task BuildXamlObjectTree_CompositePhrasingPreservesRuntimeOrder()
	{
		var span = new HtmlSpanDomElement(
			Mapping(
				"/html/body/span",
				null,
				["/html/body/span/button"]));
		span.AddRuntimeProperty(
			"content.ownText",
			ElementSlotCategory.DataOrganization);
		span.AddChild(new HtmlButtonDomElement(
			Mapping(
				"/html/body/span/button",
				"/html/body/span",
				[])));
		await FillOwnTextAsync(span, "Before");
		var factory = new TestXamlObjectFactory();

		var result = span.BuildXamlObjectTree(factory);

		var root = Assert.IsType<TestXamlObject>(Assert.Single(result.RootObjects));
		Assert.Equal("HtmlInlineFlowPanel", root.ElementName);
		Assert.Equal(["TextBlock", "HtmlFormButton"], root.Children
			.Select(static child => child.ElementName));
		Assert.All(
			factory.Attachments,
			attachment => Assert.Equal(
				ElementXamlChildPlacementKind.DirectChildren,
				attachment.Placement));
	}

	[Fact]
	public async Task BuildXamlObjectTree_NonVisualParentPassesThroughVisualChildren()
	{
		var head = new HtmlHeadDomElement(
			Mapping(
				"/html/head",
				null,
				["/html/head/span"]));
		var child = new HtmlSpanDomElement(
			Mapping("/html/head/span", "/html/head", []));
		head.AddChild(child);
		await FillInitializationsAsync(head);
		var factory = new TestXamlObjectFactory();

		var result = head.BuildXamlObjectTree(factory);

		var root = Assert.IsType<TestXamlObject>(Assert.Single(result.RootObjects));
		Assert.Null(head.XamlElement);
		Assert.Same(root, child.XamlElement);
		Assert.Empty(factory.Attachments);
	}

	[Fact]
	public void HtmlDocumentRoot_MaximumHierarchyLevelDefaultsToUnlimited()
	{
		var htmlRoot = new TestHtmlDocumentRoot();

		Assert.Null(htmlRoot.MaximumHierarchyLevel);
		Assert.NotNull(typeof(HtmlDocumentRoot).GetProperty(
			nameof(HtmlDocumentRoot.MaximumHierarchyLevel)));
	}

	[Fact]
	public async Task BuildXamlObjectTree_ActiveDepthLimitStopsEveryDeeperProjection()
	{
		var rootElement = new HtmlDivDomElement(
			Mapping("/html/body/div", null, ["/html/body/div/div"]));
		var child = new HtmlDivDomElement(
			Mapping(
				"/html/body/div/div",
				"/html/body/div",
				["/html/body/div/div/span"]));
		var grandchild = new HtmlSpanDomElement(
			Mapping(
				"/html/body/div/div/span",
				"/html/body/div/div",
				[]));
		rootElement.AddChild(child);
		child.AddChild(grandchild);
		var htmlRoot = new TestHtmlDocumentRoot
		{
			MaximumHierarchyLevel = 2
		};
		htmlRoot.MountElementTree(rootElement, [rootElement]);
		await FillInitializationsAsync(rootElement);
		Assert.Equal(1, rootElement.HierarchyLevel);
		Assert.Equal(2, child.HierarchyLevel);
		Assert.Equal(3, grandchild.HierarchyLevel);

		var result = rootElement.BuildXamlObjectTree(
			new TestXamlObjectFactory());

		var rootObject = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		var childObject = Assert.Single(rootObject.Children);
		Assert.Empty(childObject.Children);
		Assert.NotNull(rootElement.XamlElement);
		Assert.NotNull(child.XamlElement);
		Assert.Null(grandchild.XamlElement);
	}

	[Fact]
	public async Task BuildXamlObjectTree_OptionGroupFlattensLabelAndOptions()
	{
		var select = new HtmlSelectDomElement(
			Mapping(
				"/html/body/select",
				null,
				["/html/body/select/optgroup"]));
		var group = new HtmlOptionGroupDomElement(
			Mapping(
				"/html/body/select/optgroup",
				"/html/body/select",
				["/html/body/select/optgroup/option"]));
		var option = new HtmlOptionDomElement(
			Mapping(
				"/html/body/select/optgroup/option",
				"/html/body/select/optgroup",
				[]));
		select.AddChild(group);
		group.AddChild(option);
		await FillInitializationsAsync(select);
		await FillInitializationsAsync(group, ("label", "Group A"));
		await FillInitializationsAsync(option, ("label", "Choice"));

		var result = select.BuildXamlObjectTree(
			new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		Assert.Equal("ComboBox", root.ElementName);
		Assert.Equal(["TextBlock", "ComboBoxItem"], root.Children
			.Select(static child => child.ElementName));
		Assert.Equal("Group A", root.Children[0].Attributes["Text"]);
		Assert.Equal("Choice", root.Children[1].Attributes["Content"]);
	}

	[Fact]
	public async Task BuildXamlObjectTree_ImageUsesResolvedRuntimeResource()
	{
		var image = new HtmlImageDomElement(
			Mapping("/html/body/img", null, []));
		await FillInitializationsAsync(
			image,
			("src", "/assets/avatar.png"),
			("resource.imageSourceUrl", "https://example.test/assets/avatar.png"));

		var result = image.BuildXamlObjectTree(
			new TestXamlObjectFactory());

		var target = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		Assert.Equal("Image", target.ElementName);
		Assert.Equal(
			"https://example.test/assets/avatar.png",
			target.Attributes["Source"]);
	}

	[Fact]
	public async Task BuildXamlObjectTree_ImageMapCreatesImageAndHotspotOverlay()
	{
		var rootElement = new HtmlDivDomElement(
			Mapping(
				"/html/body/div",
				null,
				["/html/body/div/map", "/html/body/div/img"]));
		var map = new HtmlImageMapDomElement(
			Mapping(
				"/html/body/div/map",
				"/html/body/div",
				["/html/body/div/map/area"]));
		var area = new HtmlImageMapAreaDomElement(
			Mapping(
				"/html/body/div/map/area",
				"/html/body/div/map",
				[]));
		var image = new HtmlImageDomElement(
			Mapping(
				"/html/body/div/img",
				"/html/body/div",
				[]));
		rootElement.AddChild(map);
		rootElement.AddChild(image);
		map.AddChild(area);
		await FillInitializationsAsync(rootElement);
		await FillInitializationsAsync(map, ("name", "diagram"));
		await FillInitializationsAsync(
			area,
			("alt", "Open detail"),
			("shape", "rect"),
			("coords", "10,20,110,70"));
		await FillInitializationsAsync(
			image,
			("usemap", "#diagram"),
			("src", "https://example.test/diagram.png"));

		var result = rootElement.BuildXamlObjectTree(
			new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		var composite = Assert.Single(root.Children);
		Assert.Equal("HtmlImageMapComposite", composite.ElementName);
		Assert.Equal(["Image", "HtmlImageMapOverlay"], composite.Children
			.Select(static child => child.ElementName));
		var hotspot = Assert.Single(composite.Children[1].Children);
		Assert.Equal("HtmlImageMapHotspot", hotspot.ElementName);
		Assert.Equal("rect", hotspot.Attributes["Shape"]);
		Assert.Equal("10,20,110,70", hotspot.Attributes["Coordinates"]);
	}

	[Fact]
	public async Task BuildXamlObjectTree_RubySeparatesBaseAndAnnotation()
	{
		var ruby = new HtmlRubyDomElement(
			Mapping(
				"/html/body/ruby",
				null,
				[
					"/html/body/ruby/span",
					"/html/body/ruby/rp[1]",
					"/html/body/ruby/rt",
					"/html/body/ruby/rp[2]"
				]));
		var baseText = new HtmlSpanDomElement(
			Mapping(
				"/html/body/ruby/span",
				"/html/body/ruby",
				[]));
		var openFallback = new HtmlRubyParenthesisDomElement(
			Mapping(
				"/html/body/ruby/rp[1]",
				"/html/body/ruby",
				[]));
		var annotation = new HtmlRubyTextDomElement(
			Mapping(
				"/html/body/ruby/rt",
				"/html/body/ruby",
				[]));
		var closeFallback = new HtmlRubyParenthesisDomElement(
			Mapping(
				"/html/body/ruby/rp[2]",
				"/html/body/ruby",
				[]));
		ruby.AddChild(baseText);
		ruby.AddChild(openFallback);
		ruby.AddChild(annotation);
		ruby.AddChild(closeFallback);
		baseText.AddRuntimeProperty(
			"content.ownText",
			ElementSlotCategory.DataOrganization);
		openFallback.AddRuntimeProperty(
			"content.ownText",
			ElementSlotCategory.DataOrganization);
		annotation.AddRuntimeProperty(
			"content.ownText",
			ElementSlotCategory.DataOrganization);
		closeFallback.AddRuntimeProperty(
			"content.ownText",
			ElementSlotCategory.DataOrganization);
		await FillInitializationsAsync(ruby);
		await FillOwnTextAsync(baseText, "漢");
		await FillOwnTextAsync(openFallback, "(");
		await FillOwnTextAsync(annotation, "かん");
		await FillOwnTextAsync(closeFallback, ")");

		var result = ruby.BuildXamlObjectTree(
			new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		Assert.Equal("HtmlRubyPanel", root.ElementName);
		Assert.Equal(
			["TextBlock", "HtmlRubyAnnotationTextBlock"],
			root.Children.Select(static child => child.ElementName));
	}

	private static async Task FillOwnTextAsync(
		DomElement element,
		string text)
	{
		await element.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "content.ownText"
					&& slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant(text)
						: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));
	}

	[Fact]
	public async Task BuildXamlObjectTree_HiddenInputRetainsCollapsedFormState()
	{
		var input = new HtmlInputDomElement(
			Mapping("/html/body/input", null, []));
		await FillInitializationsAsync(
			input,
			("type", "hidden"),
			("name", "csrf"),
			("value", "token-value"),
			("form", "message-form"));

		var result = input.BuildXamlObjectTree(
			new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		Assert.Equal("ContentControl", root.ElementName);
		Assert.Equal("Collapsed", root.Attributes["Visibility"]);
		Assert.Equal(
			"token-value",
			root.Attributes["HtmlFormState.InitialValue"]);
		Assert.Equal(
			"message-form",
			root.Attributes["HtmlFormState.FormOwnerId"]);
		Assert.Same(root, input.XamlElement);
	}

	[Fact]
	public void BuildXamlObjectTree_InputBeforeDomFill_FailsInsteadOfGuessingText()
	{
		var input = new HtmlInputDomElement(
			Mapping("/html/body/input", null, []));

		var exception = Assert.Throws<InvalidOperationException>(() =>
			input.BuildXamlObjectTree(new TestXamlObjectFactory()));

		Assert.Contains(
			"must complete DOM Fill",
			exception.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task BuildXamlObjectTree_InputListUsesMountedDatalistSource()
	{
		var rootElement = new HtmlDivDomElement(
			Mapping(
				"/html/body/div",
				null,
				["/html/body/div/input", "/html/body/div/datalist"]));
		var input = new HtmlInputDomElement(
			Mapping(
				"/html/body/div/input",
				"/html/body/div",
				[]));
		var datalist = new HtmlDataListDomElement(
			Mapping(
				"/html/body/div/datalist",
				"/html/body/div",
				["/html/body/div/datalist/option"]));
		var option = new HtmlOptionDomElement(
			Mapping(
				"/html/body/div/datalist/option",
				"/html/body/div/datalist",
				[]));
		rootElement.AddChild(input);
		rootElement.AddChild(datalist);
		datalist.AddChild(option);
		var htmlRoot = new TestHtmlDocumentRoot();
		htmlRoot.MountElementTree(rootElement, [rootElement]);
		await FillInitializationsAsync(rootElement);
		await FillInitializationsAsync(
			input,
			("type", "text"),
			("list", "cities"));
		await FillInitializationsAsync(datalist, ("id", "cities"));
		await FillInitializationsAsync(option, ("value", "Shanghai"));

		var result = rootElement.BuildXamlObjectTree(
			new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		var editor = Assert.Single(root.Children);
		Assert.Equal("HtmlDatalistInputControl", editor.ElementName);
		Assert.Equal("Shanghai", editor.Attributes["SuggestionValues"]);
		Assert.Null(datalist.XamlElement);
	}

	[Theory]
	[InlineData("bdi", "HtmlBidiIsolationTextBlock")]
	[InlineData("bdo", "HtmlBidiOverrideTextBlock")]
	public async Task BuildXamlObjectTree_BidiLeafUsesDedicatedType(
		string tagName,
		string expectedType)
	{
		var element = Assert.IsAssignableFrom<HtmlDomElementDefinition>(
			HtmlDomElementTypeCatalog.Create(
				tagName,
				Mapping($"/html/body/{tagName}", null, [])));
		element.AddRuntimeProperty(
			"content.ownText",
			ElementSlotCategory.DataOrganization);
		await FillOwnTextAsync(element, "abc");

		var result = element.BuildXamlObjectTree(
			new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		Assert.Equal(expectedType, root.ElementName);
		Assert.Equal("abc", root.Attributes["Text"]);
	}

	[Fact]
	public async Task BuildXamlObjectTree_VideoCarriesTimedTextTrack()
	{
		var video = new HtmlVideoDomElement(
			Mapping(
				"/html/body/video",
				null,
				["/html/body/video/track"]));
		var track = new HtmlTrackDomElement(
			Mapping(
				"/html/body/video/track",
				"/html/body/video",
				[]));
		video.AddChild(track);
		await FillInitializationsAsync(
			video,
			("src", "https://example.test/movie.mp4"),
			("controls", string.Empty));
		await FillInitializationsAsync(
			track,
			("src", "https://example.test/captions.vtt"),
			("kind", "captions"),
			("srclang", "en"),
			("label", "English"),
			("default", string.Empty));

		var result = video.BuildXamlObjectTree(
			new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		Assert.Equal("HtmlMediaElementControl", root.ElementName);
		Assert.Contains(
			"https://example.test/captions.vtt",
			root.Attributes["TrackSources"],
			StringComparison.Ordinal);
		Assert.Null(track.XamlElement);
	}

	[Fact]
	public async Task BuildXamlObjectTree_TableConsumesColumnMetadataAsTracks()
	{
		var table = new HtmlTableDomElement(
			Mapping(
				"/html/body/table",
				null,
				["/html/body/table/colgroup"]));
		var group = new HtmlTableColumnGroupDomElement(
			Mapping(
				"/html/body/table/colgroup",
				"/html/body/table",
				[
					"/html/body/table/colgroup/col[1]",
					"/html/body/table/colgroup/col[2]"
				]));
		var first = new HtmlTableColumnDomElement(
			Mapping(
				"/html/body/table/colgroup/col[1]",
				"/html/body/table/colgroup",
				[]));
		var second = new HtmlTableColumnDomElement(
			Mapping(
				"/html/body/table/colgroup/col[2]",
				"/html/body/table/colgroup",
				[]));
		table.AddChild(group);
		group.AddChild(first);
		group.AddChild(second);
		var htmlRoot = new TestHtmlDocumentRoot();
		htmlRoot.SetStyle(group, "style.width", "25%");
		htmlRoot.SetStyle(first, "style.width", "200px");
		htmlRoot.MountElementTree(table, [table]);
		await FillInitializationsAsync(table);
		await FillInitializationsAsync(group);
		await FillInitializationsAsync(
			first,
			("span", "2"));
		await FillInitializationsAsync(second);
		Assert.Empty(group.RuntimeProperties);
		Assert.Empty(first.RuntimeProperties);

		var result = table.BuildXamlObjectTree(
			new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		Assert.Equal(
			["200", "200", "25*"],
			root.ColumnDefinitions.Select(static item => item.Length));
		Assert.Null(group.XamlElement);
		Assert.Null(first.XamlElement);
		Assert.Null(second.XamlElement);
	}

	[Fact]
	public async Task BuildXamlObjectTree_BlockFlowKeepsStickyAndExcludesOutOfFlowTracks()
	{
		var parent = new HtmlDivDomElement(
			Mapping(
				"/html/body/div",
				null,
				[
					"/html/body/div/div[1]",
					"/html/body/div/div[2]",
					"/html/body/div/div[3]"
				]));
		var normal = new HtmlDivDomElement(
			Mapping("/html/body/div/div[1]", parent.XPath, []));
		var absolute = new HtmlDivDomElement(
			Mapping("/html/body/div/div[2]", parent.XPath, []));
		var sticky = new HtmlDivDomElement(
			Mapping("/html/body/div/div[3]", parent.XPath, []));
		parent.AddChild(normal);
		parent.AddChild(absolute);
		parent.AddChild(sticky);
		foreach (var child in new[] { normal, absolute, sticky })
		{
			child.AddRuntimeProperty("style.position", ElementSlotCategory.Style);
			child.AddRuntimeProperty("style.height", ElementSlotCategory.Style);
		}
		await FillInitializationsAsync(parent);
		await FillInitializationsAsync(
			normal,
			("style.position", "static"),
			("style.height", "10px"));
		await FillInitializationsAsync(
			absolute,
			("style.position", "absolute"),
			("style.height", "20px"));
		await FillInitializationsAsync(
			sticky,
			("style.position", "sticky"),
			("style.height", "30px"));

		var result = parent.BuildXamlObjectTree(new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		Assert.Equal(
			["10", "30"],
			root.RowDefinitions.Select(static definition => definition.Length));
		var normalObject = Assert.IsType<TestXamlObject>(normal.XamlElement);
		var absoluteObject = Assert.IsType<TestXamlObject>(absolute.XamlElement);
		var stickyObject = Assert.IsType<TestXamlObject>(sticky.XamlElement);
		Assert.Equal("0", normalObject.Attributes["Grid.Row"]);
		Assert.DoesNotContain("Grid.Row", absoluteObject.Attributes.Keys);
		Assert.Equal("1", stickyObject.Attributes["Grid.Row"]);
	}

	[Fact]
	public async Task BuildXamlObjectTree_GridTrackLineNameGroupsDoNotCreateTracks()
	{
		var parent = new HtmlDivDomElement(
			Mapping(
				"/html/body/div",
				null,
				["/html/body/div/span"]));
		var child = new HtmlSpanDomElement(
			Mapping(
				"/html/body/div/span",
				parent.XPath,
				[]));
		parent.AddChild(child);
		parent.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		parent.AddRuntimeProperty(
			"style.gridTemplateColumns",
			ElementSlotCategory.Style);
		await FillInitializationsAsync(
			parent,
			("style.display", "grid"),
			(
				"style.gridTemplateColumns",
				"[start lane] 30px [middle lane] 40px [end lane] 50px [finish]"));
		await FillInitializationsAsync(child);

		var result = parent.BuildXamlObjectTree(new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		Assert.Equal(
			["30", "40", "50"],
			root.ColumnDefinitions.Select(static definition => definition.Length));
	}

	[Fact]
	public async Task BuildXamlObjectTree_NestedNumericRepeatPreservesEveryTrack()
	{
		var parent = new HtmlDivDomElement(
			Mapping("/html/body/div", null, []));
		parent.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		parent.AddRuntimeProperty(
			"style.gridTemplateColumns",
			ElementSlotCategory.Style);
		await FillInitializationsAsync(
			parent,
			("style.display", "grid"),
			(
				"style.gridTemplateColumns",
				"repeat(2, [lane] minmax(20px, 1fr) [edge])"));

		var result = parent.BuildXamlObjectTree(new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		Assert.Equal(
			["*", "*"],
			root.ColumnDefinitions.Select(static definition => definition.Length));
		Assert.All(
			root.ColumnDefinitions,
			static definition =>
			{
				Assert.Equal("20", definition.Minimum);
				Assert.Equal("*", definition.Maximum);
			});
	}

	[Fact]
	public async Task BuildXamlObjectTree_AutoFillUsesRuntimeGeometryWithoutPersistingItAsInitialization()
	{
		var parent = new HtmlDivDomElement(
			Mapping("/html/body/div", null, []));
		parent.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		parent.AddRuntimeProperty("style.gridTemplateColumns", ElementSlotCategory.Style);
		parent.AddRuntimeProperty("style.columnGap", ElementSlotCategory.Style);
		parent.AddRuntimeProperty("rect.width", ElementSlotCategory.Space);
		await parent.DomFillAsync((_, _, name, slot) =>
		{
			var value = (name, slot) switch
			{
				("style.display", DomPropertyDataSlot.Initialization) => "grid",
				("style.gridTemplateColumns", DomPropertyDataSlot.Initialization) =>
					"repeat(auto-fill, minmax(100px, 1fr))",
				("style.columnGap", DomPropertyDataSlot.Initialization) => "10px",
				("rect.width", DomPropertyDataSlot.Runtime) => "250",
				_ => null
			};
			return ValueTask.FromResult(value is null
				? DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
				: DomPropertyQueryResult.DirectConstant(value));
		});

		var result = parent.BuildXamlObjectTree(new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(Assert.Single(result.RootObjects));
		Assert.Equal(["*", "*"], root.ColumnDefinitions.Select(static track => track.Length));
		Assert.False(parent.RuntimeProperties.Single(static property =>
			property.Name == "rect.width").SourceInitialization.IsSet);
		Assert.Equal("250", parent.RuntimeProperties.Single(static property =>
			property.Name == "rect.width").SourceRuntime.Value);
	}

	[Fact]
	public async Task BuildXamlObjectTree_FlexTracksFollowCssOrder()
	{
		var parent = new HtmlDivDomElement(
			Mapping(
				"/html/body/div",
				null,
				[
					"/html/body/div/div[1]",
					"/html/body/div/div[2]",
					"/html/body/div/div[3]"
				]));
		var first = new HtmlDivDomElement(
			Mapping("/html/body/div/div[1]", parent.XPath, []));
		var second = new HtmlDivDomElement(
			Mapping("/html/body/div/div[2]", parent.XPath, []));
		var absolute = new HtmlDivDomElement(
			Mapping("/html/body/div/div[3]", parent.XPath, []));
		parent.AddChild(first);
		parent.AddChild(second);
		parent.AddChild(absolute);
		parent.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		parent.AddRuntimeProperty("style.flexDirection", ElementSlotCategory.Style);
		parent.AddRuntimeProperty("style.flexWrap", ElementSlotCategory.Style);
		foreach (var child in new[] { first, second, absolute })
		{
			child.AddRuntimeProperty("style.order", ElementSlotCategory.Style);
			child.AddRuntimeProperty("style.width", ElementSlotCategory.Style);
			child.AddRuntimeProperty("style.position", ElementSlotCategory.Style);
		}
		await FillInitializationsAsync(
			parent,
			("style.display", "flex"),
			("style.flexDirection", "row"),
			("style.flexWrap", "nowrap"));
		await FillInitializationsAsync(
			first,
			("style.order", "2"),
			("style.position", "static"),
			("style.width", "100px"));
		await FillInitializationsAsync(
			second,
			("style.order", "-1"),
			("style.position", "static"),
			("style.width", "200px"));
		await FillInitializationsAsync(
			absolute,
			("style.order", "-10"),
			("style.position", "absolute"),
			("style.width", "300px"));

		var result = parent.BuildXamlObjectTree(new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(
			Assert.Single(result.RootObjects));
		Assert.Equal(
			["200", "100"],
			root.ColumnDefinitions.Select(static definition => definition.Length));
		var firstObject = Assert.IsType<TestXamlObject>(first.XamlElement);
		var secondObject = Assert.IsType<TestXamlObject>(second.XamlElement);
		var absoluteObject = Assert.IsType<TestXamlObject>(absolute.XamlElement);
		Assert.Equal("1", firstObject.Attributes["Grid.Column"]);
		Assert.Equal("0", secondObject.Attributes["Grid.Column"]);
		Assert.DoesNotContain("Grid.Column", absoluteObject.Attributes.Keys);
	}

	[Fact]
	public async Task BuildXamlObjectTree_FlexTrackUsesSvgContentBoxOuterWidth()
	{
		var parent = new HtmlDivDomElement(
			Mapping("/html/body/div", null, ["/html/body/div/svg"]));
		var svg = new SvgRootDomElement(
			Mapping("/html/body/div/svg", parent.XPath, []));
		parent.AddChild(svg);
		parent.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		parent.AddRuntimeProperty("style.flexDirection", ElementSlotCategory.Style);
		parent.AddRuntimeProperty("style.flexWrap", ElementSlotCategory.Style);
		foreach (var name in new[]
		{
			"style.width",
			"style.boxSizing",
			"style.paddingLeft",
			"style.paddingRight"
		})
		{
			svg.AddRuntimeProperty(name, ElementSlotCategory.Style);
		}
		await FillInitializationsAsync(
			parent,
			("style.display", "flex"),
			("style.flexDirection", "row"),
			("style.flexWrap", "nowrap"));
		await FillInitializationsAsync(
			svg,
			("style.width", "18px"),
			("style.boxSizing", "content-box"),
			("style.paddingLeft", "2px"),
			("style.paddingRight", "2px"));

		var result = parent.BuildXamlObjectTree(new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(Assert.Single(result.RootObjects));
		Assert.Equal(
			["22"],
			root.ColumnDefinitions.Select(static definition => definition.Length));
	}

	[Fact]
	public async Task BuildXamlObjectTree_ScrollBlockUsesComputedChildHeight()
	{
		var parent = new HtmlDivDomElement(
			Mapping("/html/body/div", null, ["/html/body/div/div"]));
		var child = new HtmlDivDomElement(
			Mapping("/html/body/div/div", parent.XPath, []));
		parent.AddChild(child);
		parent.AddRuntimeProperty("style.overflowY", ElementSlotCategory.Style);
		child.AddRuntimeProperty("style.height", ElementSlotCategory.Style);
		await FillInitializationsAsync(parent, ("style.overflowY", "auto"));
		await child.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult((name, slot) switch
			{
				("style.height", DomPropertyDataSlot.Initialization) =>
					DomPropertyQueryResult.DirectConstant("100%"),
				("style.height", DomPropertyDataSlot.Runtime) =>
					DomPropertyQueryResult.DirectConstant("994px"),
				_ => DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
			}));

		var result = parent.BuildXamlObjectTree(new TestXamlObjectFactory());

		var root = Assert.IsType<TestXamlObject>(Assert.Single(result.RootObjects));
		Assert.Equal(
			["994"],
			root.RowDefinitions.Select(static definition => definition.Length));
	}

	[Fact]
	public async Task BuildXamlObjectTree_DefiniteFlexCrossSizeUsesNearPlacement()
	{
		var parent = new HtmlDivDomElement(
			Mapping("/html/body/div", null, ["/html/body/div/div"]));
		var child = new HtmlDivDomElement(
			Mapping("/html/body/div/div", parent.XPath, []));
		parent.AddChild(child);
		foreach (var name in new[]
		{
			"style.display",
			"style.flexDirection",
			"style.flexWrap",
			"style.alignItems"
		})
		{
			parent.AddRuntimeProperty(name, ElementSlotCategory.Style);
		}
		child.AddRuntimeProperty("style.width", ElementSlotCategory.Style);
		await FillInitializationsAsync(
			parent,
			("style.display", "flex"),
			("style.flexDirection", "column"),
			("style.flexWrap", "nowrap"),
			("style.alignItems", "stretch"));
		await FillInitializationsAsync(child, ("style.width", "663px"));

		var result = parent.BuildXamlObjectTree(new TestXamlObjectFactory());

		var childPlan = Assert.Single(Assert.Single(result.Roots).Children).Plan;
		var placement = Assert.IsType<XamlElementLayoutPlacement>(
			childPlan.LayoutPlacement);
		Assert.False(placement.IsOutOfFlow);
		Assert.True(placement.HasDefiniteCrossSize);
		Assert.Equal(XamlElementCrossAxis.Horizontal, placement.CrossAxis);
		Assert.Equal(XamlElementCrossAlignment.Near, placement.CrossAlignment);
	}

	private static async Task FillInitializationsAsync(
		DomElement element,
		params (string Name, string Value)[] values)
	{
		var map = values.ToDictionary(
			static item => item.Name,
			static item => item.Value,
			StringComparer.Ordinal);
		await element.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Initialization
					&& map.TryGetValue(name, out var value)
						? DomPropertyQueryResult.DirectConstant(value)
						: DomPropertyQueryResult.ConfirmedAbsent(
							"Not specified.")));
	}

	private static DomElementMapping Mapping(
		string xpath,
		string? parentXPath,
		IReadOnlyList<string> childXPaths,
		int ownTextElementInsertionIndex = -1) =>
		new(
			"document",
			xpath,
			parentXPath,
			childXPaths,
			null,
			null,
			"",
			"",
			0,
			0,
			ownTextElementInsertionIndex);

	private sealed class TestXamlObjectFactory
		: TestXamlElementObjectFactory
	{
		public List<XamlElementObjectAttachmentContext> Attachments { get; } =
			[];

		protected override object CreateElementCore(
			XamlElementObjectPlan plan) =>
			new TestXamlObject(
				plan.Mapping.ElementName,
				plan.SourceElement);

		public override void FillElementProperties(
			XamlElementObjectPropertyFillContext context)
		{
			var target = (TestXamlObject)context.Element;
			foreach (var attribute in context.Plan.InitializationAttributes)
				target.Attributes.Add(attribute.Name, attribute.Value);
		}

		public override void AttachChild(
			XamlElementObjectAttachmentContext context)
		{
			Attachments.Add(context);
			((TestXamlObject)context.Parent).Children.Add(
				(TestXamlObject)context.Child);
		}

		public override void ApplyGridTracks(
			object element,
			IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
			IReadOnlyList<XamlGridTrackDefinition> columnDefinitions)
		{
			var target = (TestXamlObject)element;
			target.RowDefinitions.AddRange(rowDefinitions);
			target.ColumnDefinitions.AddRange(columnDefinitions);
		}
	}

	private sealed class TestXamlObject(
		string elementName,
		DomElement? sourceElement)
	{
		public string ElementName { get; } = elementName;

		public DomElement? SourceElement { get; } = sourceElement;

		public Dictionary<string, string> Attributes { get; } =
			new(StringComparer.Ordinal);

		public List<TestXamlObject> Children { get; } = [];

		public List<XamlGridTrackDefinition> RowDefinitions { get; } = [];

		public List<XamlGridTrackDefinition> ColumnDefinitions { get; } = [];
	}

	private sealed class TestHtmlDocumentRoot : HtmlDocumentRoot
	{
		private readonly Dictionary<(string XPath, string Property), string>
			_styles = new();

		internal TestHtmlDocumentRoot() : base("document")
		{
		}

		internal void SetStyle(
			DomElement element,
			string propertyName,
			string value) =>
			_styles[(element.XPath, propertyName)] = value;

		public override string? ResolveGlobalStyleValue(
			DomElement element,
			string propertyName) =>
			_styles.GetValueOrDefault((element.XPath, propertyName));

		protected override ValueTask<IReadOnlyList<DomPropertyQueryResult>>
			QueryDomPropertiesAsync(
				IReadOnlyList<DomPropertyQueryContext> contexts,
				CancellationToken cancellationToken) =>
			throw new NotSupportedException();
	}
}
