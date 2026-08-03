using System.Text;
using System.Reflection;
using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class DomElementXamlGenerationTests
{
	[Theory]
	[InlineData("style.colorScheme", ElementEvidenceKind.CssDeclarations | ElementEvidenceKind.ComputedStyles)]
	[InlineData("style.fill", ElementEvidenceKind.CssDeclarations | ElementEvidenceKind.ComputedStyles)]
	[InlineData("style.stroke", ElementEvidenceKind.CssDeclarations | ElementEvidenceKind.ComputedStyles)]
	[InlineData("style.textDecorationLine", ElementEvidenceKind.CssDeclarations | ElementEvidenceKind.ComputedStyles)]
	[InlineData("resource.references", ElementEvidenceKind.Resources)]
	public void RuntimePropertyCatalog_CapturedSchemaPropertiesHaveEvidenceSemantics(
		string propertyName,
		ElementEvidenceKind expectedEvidence)
	{
		var definition = Assert.Single(
			DomElementRuntimePropertyCatalog.Standard,
			property => property.Name == propertyName);

		Assert.Equal(expectedEvidence, definition.EvidenceKind);
		Assert.Equal(
			expectedEvidence,
			DomElementRuntimePropertyCatalog.ResolveEvidenceKind(propertyName));
	}

	[Fact]
	public void CreateXaml_EveryStandardElementOwnsItsConcreteTypeDecision()
	{
		var mapping = new DomElementMapping(
			"architecture-probe",
			"/probe",
			null,
			[],
			null,
			null);
		var tags = HtmlDomElementTypeCatalog.HtmlTags
			.Concat(HtmlDomElementTypeCatalog.SvgTags)
			.ToArray();

		Assert.Equal(127, tags.Length);
		foreach (var tag in tags)
		{
			var element = HtmlDomElementTypeCatalog.Create(tag, mapping);
			var method = element.GetType().GetMethod(
				"CreateXaml",
				BindingFlags.Instance | BindingFlags.NonPublic);

			Assert.NotNull(method);
			Assert.Equal(element.GetType(), method.DeclaringType);
			Assert.False(method.IsAbstract);
		}
	}

	[Fact]
	public void CreateXaml_HasNoCommonFallbackDecisionMethod()
	{
		var forbidden = new[]
		{
			"ResolveXamlElementMapping",
			"ResolveTypeDefinedXamlElementMapping"
		};
		foreach (var type in typeof(DomElement).Assembly.GetTypes())
		{
			foreach (var methodName in forbidden)
			{
				Assert.Null(type.GetMethod(
					methodName,
					BindingFlags.Instance
						| BindingFlags.Static
						| BindingFlags.Public
						| BindingFlags.NonPublic
						| BindingFlags.DeclaredOnly));
			}
		}
	}

	[Fact]
	public void ToXaml_UsesInheritedElementImplementations_NotExternalTranslator()
	{
		var assembly = typeof(DomElement).Assembly;

		Assert.Null(assembly.GetType(
			"Iwesun.Runtime.Web.DomElementXamlAttributeTranslator",
			throwOnError: false));
		Assert.Equal(
			typeof(HtmlLayoutDomElementDefinition),
			DeclaredBuildXamlAttributes(typeof(HtmlDivDomElement)).DeclaringType);
		Assert.Equal(
			typeof(HtmlTextVisualDomElementDefinition),
			DeclaredBuildXamlAttributes(typeof(HtmlSpanDomElement)).DeclaringType);
		Assert.Equal(
			typeof(HtmlInputDomElement),
			DeclaredBuildXamlAttributes(typeof(HtmlInputDomElement)).DeclaringType);
		Assert.Equal(
			typeof(SvgPathDomElement),
			DeclaredBuildXamlAttributes(typeof(SvgPathDomElement)).DeclaringType);
	}

	[Fact]
	public async Task ToXaml_UsesInitializationDesignAndOmitsNullValues()
	{
		var element = Create("/html/body/div", null, []);
		await element.XamlFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "design.alignment"
				&& slot == XamlPropertyDataSlot.Initialization
					? XamlPropertyQueryResult.DirectConstant("Left")
					: XamlPropertyQueryResult.ConfirmedAbsent("Not specified.")));
		using var output = new StringWriter();

		var xaml = element.ToXaml(output);

		Assert.Contains("HorizontalAlignment=\"Left\"", xaml, StringComparison.Ordinal);
		Assert.Contains(
			"Tag=\"document::/html/body/div\"",
			xaml,
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_DoesNotUseRuntimeAbsoluteValueAsInitialization()
	{
		var element = Create("/html/body/div", null, []);
		await element.XamlFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "runtime.x"
				&& slot == XamlPropertyDataSlot.Initialization
					? XamlPropertyQueryResult.DirectConstant("640")
					: XamlPropertyQueryResult.ConfirmedAbsent("Not specified.")));
		using var output = new StringWriter();

		var xaml = element.ToXaml(output);

		Assert.DoesNotContain("Canvas.Left", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public void ToXaml_AllowsWholeNodeOwnNodeAndChildrenOverrides()
	{
		var parent = Create(
			"/html/body/div",
			null,
			["/html/body/div/div"]);
		var child = Create(
			"/html/body/div/div",
			"/html/body/div",
			[]);
		parent.AddChild(child);
		using var output = new StringWriter();

		var xaml = parent.ToXaml(output);

		Assert.Equal(1, parent.PublicToXamlCalls);
		Assert.Equal(1, parent.SubtreeToXamlCalls);
		Assert.Equal(1, parent.OwnXamlCalls);
		Assert.Equal(1, parent.ChildrenXamlCalls);
		Assert.Equal(1, child.SubtreeToXamlCalls);
		Assert.Equal(1, child.OwnXamlCalls);
		Assert.Equal(1, child.ChildrenXamlCalls);
		Assert.Equal(2, Count(xaml, "<Grid"));
	}

	[Fact]
	public async Task ToXaml_AfterDomFill_EmitsAndRecordsXamlInitialization()
	{
		var parent = new HtmlDivDomElement(new(
			"document",
			"/html/body",
			null,
			["/html/body/div"],
			null,
			null));
		var element = new HtmlDivDomElement(new(
			"document",
			"/html/body/div",
			"/html/body",
			[],
			null,
			null));
		parent.AddChild(element);
		element.AddRuntimeProperty("style.width", ElementSlotCategory.Style);
		await element.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "style.width" && slot != DomPropertyDataSlot.Link
					? DomPropertyQueryResult.DirectConstant("320px")
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));
		using var output = new StringWriter();

		var xaml = parent.ToXaml(output);

		Assert.Contains("Width=\"320\"", xaml, StringComparison.Ordinal);
		var width = Assert.Single(element.RuntimeProperties);
		Assert.True(width.XamlInitialization.IsSet);
		Assert.Equal("320", width.XamlInitialization.Value);
		Assert.False(width.XamlRuntime.IsSet);
	}

	[Fact]
	public async Task ToXaml_FlexPanel_UsesGridTracksAndChildIndex()
	{
		var element = new HtmlDivDomElement(new(
			"document",
			"/html/body/div",
			null,
			["/html/body/div/div"],
			null,
			null));
		var child = new HtmlDivDomElement(new(
			"document",
			"/html/body/div/div",
			"/html/body/div",
			[],
			null,
			null));
		element.AddChild(child);
		element.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		element.AddRuntimeProperty("style.flexDirection", ElementSlotCategory.Style);
		element.AddRuntimeProperty("style.flexWrap", ElementSlotCategory.Style);
		child.AddRuntimeProperty("style.flexGrow", ElementSlotCategory.Style);
		child.AddRuntimeProperty("style.minWidth", ElementSlotCategory.Style);
		await element.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Link
					? DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					: DomPropertyQueryResult.DirectConstant(name switch
					{
						"style.display" => "flex",
						"style.flexDirection" => "row",
						"style.flexWrap" => "nowrap",
						"style.flexGrow" => "1",
						"style.minWidth" => "400px",
						_ => string.Empty
					})));
		using var output = new StringWriter();

		var xaml = element.ToXaml(output);

		Assert.StartsWith("<HtmlCssBoxGrid", xaml, StringComparison.Ordinal);
		Assert.Contains("<Grid.ColumnDefinitions>", xaml, StringComparison.Ordinal);
		Assert.Contains(
			"<ColumnDefinition Width=\"*\" MinWidth=\"400\" />",
			xaml,
			StringComparison.Ordinal);
		Assert.DoesNotContain("MinColumn", xaml, StringComparison.Ordinal);
		Assert.Contains("Grid.Column=\"0\"", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_FlexRuntimeDefaultWrap_UsesComputedChildTracks()
	{
		var element = new HtmlDivDomElement(new(
			"document",
			"/html/body/div",
			null,
			["/html/body/div/div[1]", "/html/body/div/div[2]"],
			null,
			null));
		var first = new HtmlDivDomElement(new(
			"document",
			"/html/body/div/div[1]",
			"/html/body/div",
			[],
			null,
			null));
		var second = new HtmlDivDomElement(new(
			"document",
			"/html/body/div/div[2]",
			"/html/body/div",
			[],
			null,
			null));
		element.AddChild(first);
		element.AddChild(second);
		element.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		element.AddRuntimeProperty("style.flexWrap", ElementSlotCategory.Style);
		element.AddRuntimeProperty("style.flexDirection", ElementSlotCategory.Style);
		first.AddRuntimeProperty("style.width", ElementSlotCategory.Style);
		second.AddRuntimeProperty("style.width", ElementSlotCategory.Style);
		await element.DomFillAsync((_, xpath, name, slot) =>
			ValueTask.FromResult(
				slot switch
				{
					DomPropertyDataSlot.Link =>
						DomPropertyQueryResult.ConfirmedAbsent("Not specified."),
					DomPropertyDataSlot.Initialization
						when name == "style.display" =>
						DomPropertyQueryResult.DirectConstant("flex"),
					DomPropertyDataSlot.Initialization
						when xpath.EndsWith("div[1]", StringComparison.Ordinal)
							&& name == "style.width" =>
						DomPropertyQueryResult.DirectConstant("var(--width)"),
					DomPropertyDataSlot.Runtime
						when name == "style.flexWrap" =>
						DomPropertyQueryResult.DirectConstant("nowrap"),
					DomPropertyDataSlot.Runtime
						when name == "style.flexDirection" =>
						DomPropertyQueryResult.DirectConstant("row"),
					DomPropertyDataSlot.Runtime
						when xpath.EndsWith("div[1]", StringComparison.Ordinal)
							&& name == "style.width" =>
						DomPropertyQueryResult.DirectConstant("300px"),
					DomPropertyDataSlot.Runtime
						when xpath.EndsWith("div[2]", StringComparison.Ordinal)
							&& name == "style.width" =>
						DomPropertyQueryResult.DirectConstant("700px"),
					_ => DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
				}));

		var xaml = element.ToXaml(new StringWriter());

		Assert.Contains("<ColumnDefinition Width=\"300\"", xaml, StringComparison.Ordinal);
		Assert.Contains("<ColumnDefinition Width=\"700\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Grid.Column=\"0\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Grid.Column=\"1\"", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task GetXamlElementMapping_RelativeHeightBlock_DoesNotCollapseToStackPanel()
	{
		var root = new HtmlDivDomElement(new(
			"document",
			"/html/body",
			null,
			["/html/body/div"],
			null,
			null));
		var container = new HtmlDivDomElement(new(
			"document",
			"/html/body/div",
			"/html/body",
			["/html/body/div/div"],
			null,
			null));
		var child = new HtmlDivDomElement(new(
			"document",
			"/html/body/div/div",
			"/html/body/div",
			[],
			null,
			null));
		root.AddChild(container);
		container.AddChild(child);
		container.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		container.AddRuntimeProperty("style.height", ElementSlotCategory.Style);
		child.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		await root.DomFillAsync((_, xpath, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Link
					? DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					: DomPropertyQueryResult.DirectConstant(
						(xpath, name) switch
						{
							("/html/body/div", "style.display") => "block",
							("/html/body/div", "style.height") => "100%",
							("/html/body/div/div", "style.display") => "block",
							_ => string.Empty
						})));

		var mapping = container.GetXamlElementMapping();

		Assert.Equal("HtmlCssBoxGrid", mapping.ElementName);
		Assert.Equal(
			XamlElementMappingKind.BlockFlow,
			mapping.Kind);
		Assert.True(mapping.RequiresRuntimeLayoutContract);
	}

	[Fact]
	public async Task GetXamlElementMapping_BlockWithRelativeChild_DoesNotUseContentSizing()
	{
		var root = new HtmlDivDomElement(new(
			"document", "/html/body", null, ["/html/body/div"], null, null));
		var container = new HtmlDivDomElement(new(
			"document",
			"/html/body/div",
			"/html/body",
			["/html/body/div/div"],
			null,
			null));
		var child = new HtmlDivDomElement(new(
			"document",
			"/html/body/div/div",
			"/html/body/div",
			[],
			null,
			null));
		root.AddChild(container);
		container.AddChild(child);
		container.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		child.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		child.AddRuntimeProperty("style.height", ElementSlotCategory.Style);
		await root.DomFillAsync((_, xpath, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Link
					? DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					: DomPropertyQueryResult.DirectConstant(
						(xpath, name) switch
						{
							("/html/body/div", "style.display") => "block",
							("/html/body/div/div", "style.display") => "block",
							("/html/body/div/div", "style.height") => "100%",
							_ => string.Empty
						})));

		var mapping = container.GetXamlElementMapping();

		Assert.Equal(XamlElementMappingKind.BlockFlow, mapping.Kind);
	}

	[Fact]
	public async Task ToXaml_ContainerAndTextRules_DoNotLeakAcrossFamilies()
	{
		var container = new HtmlDivDomElement(new(
			"document", "/html/body/div", null, [], null, null));
		container.AddRuntimeProperty("style.backgroundColor", ElementSlotCategory.Style);
		container.AddRuntimeProperty("style.color", ElementSlotCategory.Style);
		await container.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant(
						name == "style.backgroundColor" ? "rgb(1, 2, 3)" : "#112233")
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));

		var text = new HtmlSpanDomElement(new(
			"document", "/html/body/span", null, [], null, null));
		text.AddRuntimeProperty("style.backgroundColor", ElementSlotCategory.Style);
		text.AddRuntimeProperty("style.color", ElementSlotCategory.Style);
		await text.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant(
						name == "style.color" ? "#112233" : "rgb(1, 2, 3)")
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));

		var containerXaml = container.ToXaml(new StringWriter());
		var textXaml = text.ToXaml(new StringWriter());

		Assert.Contains("Background=\"#FF010203\"", containerXaml, StringComparison.Ordinal);
		Assert.DoesNotContain("Foreground=", containerXaml, StringComparison.Ordinal);
		Assert.Contains("Foreground=\"#112233\"", textXaml, StringComparison.Ordinal);
		Assert.DoesNotContain("Background=", textXaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_CssColorKeywordDefersToRuntimeLink()
	{
		var text = new HtmlSpanDomElement(new(
			"document", "/html/body/span", null, [], null, null));
		text.AddRuntimeProperty("style.color", ElementSlotCategory.Style);
		await text.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "style.color" && slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant("currentColor")
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));

		var xaml = text.ToXaml(new StringWriter());

		Assert.DoesNotContain("Foreground=", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_HyperlinkButtonOmitsUnsupportedTextAlignment()
	{
		var link = new HtmlAnchorDomElement(new(
			"document", "/html/body/a", null, [], null, null));
		link.AddRuntimeProperty("style.textAlign", ElementSlotCategory.Style);
		await link.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Initialization
					&& name is "href" or "style.textAlign"
					? DomPropertyQueryResult.DirectConstant(
						name == "href"
							? "https://example.test/"
							: "center")
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));

		var xaml = link.ToXaml(new StringWriter());

		Assert.StartsWith("<HyperlinkButton", xaml, StringComparison.Ordinal);
		Assert.DoesNotContain("TextAlignment=", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_ContentControlWrapsCompositeChildrenInOneContentRoot()
	{
		var link = new HtmlAnchorDomElement(new(
			"document",
			"/html/body/a",
			null,
			["/html/body/a/span[1]", "/html/body/a/span[2]"],
			null,
			null));
		link.AddChild(new HtmlSpanDomElement(new(
			"document", "/html/body/a/span[1]", "/html/body/a", [], null, null)));
		link.AddChild(new HtmlSpanDomElement(new(
			"document", "/html/body/a/span[2]", "/html/body/a", [], null, null)));
		await link.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "href"
					&& slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant(
						"https://example.test/")
					: DomPropertyQueryResult.ConfirmedAbsent(
						"Not specified.")));

		var xaml = link.ToXaml(new StringWriter());

		Assert.Contains("<HyperlinkButton", xaml, StringComparison.Ordinal);
		Assert.Contains("<Grid>", xaml, StringComparison.Ordinal);
		Assert.Equal(2, Count(xaml, "<TextBlock"));
		Assert.Contains("</Grid>", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_PhrasingContainerWithControlChildUsesInlineFlowPanel()
	{
		var span = new HtmlSpanDomElement(new(
			"document",
			"/html/body/span",
			null,
			["/html/body/span/button"],
			null,
			null));
		span.AddRuntimeProperty("content.ownText", ElementSlotCategory.DataOrganization);
		span.AddChild(new HtmlButtonDomElement(new(
			"document", "/html/body/span/button", "/html/body/span", [], null, null)));
		await span.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "content.ownText" && slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant("Before")
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));

		var xaml = span.ToXaml(new StringWriter());

		Assert.StartsWith("<HtmlInlineFlowPanel", xaml, StringComparison.Ordinal);
		Assert.DoesNotContain("Orientation=", xaml, StringComparison.Ordinal);
		Assert.Contains(
			"<TextBlock Text=\"Before\" Tag=\"generated-own-text:document::/html/body/span\" />",
			xaml,
			StringComparison.Ordinal);
		Assert.Contains("<HtmlFormButton", xaml, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"<TextBlock",
			xaml[(xaml.IndexOf(
				"<HtmlFormButton",
				StringComparison.Ordinal))..],
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_InputOwnsPlaceholderAndReadOnlyTranslation()
	{
		var input = new HtmlInputDomElement(new(
			"document", "/html/body/input", null, [], null, null));
		await input.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot != DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					: name switch
					{
						"placeholder" => DomPropertyQueryResult.DirectConstant("Search"),
						"readonly" => DomPropertyQueryResult.DirectConstant(string.Empty),
						_ => DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					}));

		var xaml = input.ToXaml(new StringWriter());

		Assert.Contains("PlaceholderText=\"Search\"", xaml, StringComparison.Ordinal);
		Assert.Contains("IsReadOnly=\"True\"", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_TextFamilyProjectsOwnText()
	{
		var text = new HtmlSpanDomElement(new(
			"document", "/html/body/span", null, [], null, null));
		text.AddRuntimeProperty("content.ownText", ElementSlotCategory.DataOrganization);
		await text.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "content.ownText" && slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant("Runtime text")
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));

		var xaml = text.ToXaml(new StringWriter());

		Assert.Contains("Text=\"Runtime text\"", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_BlockSpanProjectsOwnTextIntoStrongCssBox()
	{
		var text = new HtmlSpanDomElement(new(
			"document", "/html/body/span", null, [], null, null));
		text.AddRuntimeProperty("content.ownText", ElementSlotCategory.DataOrganization);
		text.AddRuntimeProperty("style.display", ElementSlotCategory.Style);
		await text.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(name switch
			{
				"content.ownText" when slot is DomPropertyDataSlot.Initialization
					or DomPropertyDataSlot.Runtime =>
					DomPropertyQueryResult.DirectConstant("Block span text"),
				"style.display" when slot is DomPropertyDataSlot.Initialization
					or DomPropertyDataSlot.Runtime =>
					DomPropertyQueryResult.DirectConstant("block"),
				_ => DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
			}));

		var xaml = text.ToXaml(new StringWriter());

		Assert.StartsWith("<HtmlCssBoxGrid", xaml, StringComparison.Ordinal);
		Assert.Contains("Text=\"Block span text\"", xaml, StringComparison.Ordinal);
		Assert.Contains(
			"Tag=\"generated-own-text:document::/html/body/span\"",
			xaml,
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_LayoutFamilyProjectsDirectTextAsChild()
	{
		var container = new HtmlDivDomElement(new(
			"document", "/html/body/div", null, [], null, null));
		container.AddRuntimeProperty("content.ownText", ElementSlotCategory.DataOrganization);
		await container.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "content.ownText" && slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant("Container text")
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));

		var xaml = container.ToXaml(new StringWriter());

		Assert.Contains(
			"<TextBlock Text=\"Container text\"",
			xaml,
			StringComparison.Ordinal);
		Assert.Contains(
			"Tag=\"generated-own-text:document::/html/body/div\"",
			xaml,
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_ButtonFamilyProjectsLeafContent()
	{
		var button = new HtmlButtonDomElement(new(
			"document", "/html/body/button", null, [], null, null));
		button.AddRuntimeProperty("content.ownText", ElementSlotCategory.DataOrganization);
		await button.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "content.ownText" && slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant("Run")
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));

		var xaml = button.ToXaml(new StringWriter());

		Assert.Contains("Content=\"Run\"", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_InputPrefersRuntimeStateValueAndPlaceholder()
	{
		var input = new HtmlInputDomElement(new(
			"document", "/html/body/input", null, [], null, null));
		input.AddRuntimeProperty("content.value", ElementSlotCategory.DataOrganization);
		input.AddRuntimeProperty("content.placeholder", ElementSlotCategory.DataOrganization);
		await input.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Runtime
					? name switch
					{
						"content.value" =>
							DomPropertyQueryResult.DirectConstant("Live"),
						"content.placeholder" =>
							DomPropertyQueryResult.DirectConstant("Live hint"),
						_ => DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					}
					: slot == DomPropertyDataSlot.Initialization
						? name switch
						{
							"value" =>
								DomPropertyQueryResult.DirectConstant("Static"),
							"placeholder" =>
								DomPropertyQueryResult.DirectConstant(
									"Static hint"),
							_ => DomPropertyQueryResult.ConfirmedAbsent(
								"Not specified.")
						}
						: DomPropertyQueryResult.ConfirmedAbsent(
							"Not specified.")));

		var xaml = input.ToXaml(new StringWriter());

		Assert.Contains("Text=\"Live\"", xaml, StringComparison.Ordinal);
		Assert.Contains("PlaceholderText=\"Live hint\"", xaml, StringComparison.Ordinal);
		Assert.DoesNotContain("Text=\"Static\"", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_InputProjectsLiveConstraintValidationState()
	{
		var input = new HtmlInputDomElement(new(
			"document", "/html/body/input", null, [], null, null));
		foreach (var name in new[]
		{
			"state.valid",
			"state.willValidate",
			"state.validationMessage"
		})
		{
			input.AddRuntimeProperty(
				name,
				ElementSlotCategory.DataOrganization);
		}
		await input.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Runtime
					? name switch
					{
						"state.valid" =>
							DomPropertyQueryResult.DirectConstant("false"),
						"state.willValidate" =>
							DomPropertyQueryResult.DirectConstant("true"),
						"state.validationMessage" =>
							DomPropertyQueryResult.DirectConstant("Required"),
						_ => DomPropertyQueryResult.ConfirmedAbsent(
							"Not specified.")
					}
					: DomPropertyQueryResult.ConfirmedAbsent(
						"Not specified.")));

		var xaml = input.ToXaml(new StringWriter());

		Assert.Contains(
			"HtmlValidation.IsValid=\"false\"",
			xaml,
			StringComparison.Ordinal);
		Assert.Contains(
			"HtmlValidation.WillValidate=\"true\"",
			xaml,
			StringComparison.Ordinal);
		Assert.Contains(
			"HtmlValidation.ValidationMessage=\"Required\"",
			xaml,
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_SvgPathOwnsGeometryTranslation()
	{
		var path = new SvgPathDomElement(new(
			"document", "/html/body/svg/path", null, [], null, null));
		await path.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot != DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					: name switch
					{
						"d" => DomPropertyQueryResult.DirectConstant("M0,0 L1,1"),
						"fill" => DomPropertyQueryResult.DirectConstant("#010203"),
						_ => DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					}));

		var xaml = path.ToXaml(new StringWriter());

		Assert.StartsWith("<Path", xaml, StringComparison.Ordinal);
		Assert.Contains("Data=\"M0,0 L1,1\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Fill=\"#010203\"", xaml, StringComparison.Ordinal);
		Assert.DoesNotContain("Background=", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public void XamlExecutionCatalog_ResolvesGenericGeometryAndLayoutTargets()
	{
		var geometry = DomXamlPropertyExecutionCatalog.Resolve("rect.width");
		var layout = DomXamlPropertyExecutionCatalog.Resolve("style.flexDirection");

		Assert.Equal(XamlPropertyExecutionKind.RuntimeGeometry, geometry.Kind);
		Assert.False(geometry.SupportsInitialization);
		Assert.True(geometry.SupportsRuntime);
		Assert.Equal(XamlPropertyExecutionKind.ContainerLayout, layout.Kind);
		Assert.Equal("Panel.Layout", layout.TargetProperty);
	}

	[Theory]
	[InlineData("details", "open")]
	[InlineData("dialog", "open")]
	[InlineData("input", "checked")]
	[InlineData("option", "selected")]
	[InlineData("input", "value")]
	public void XamlExecutionCatalog_InteractiveHtmlStateIsExplicitlyOneWay(
		string tagName,
		string attributeName)
	{
		var descriptor = DomXamlPropertyExecutionCatalog.ResolveHtmlAttribute(
			attributeName,
			HtmlAttributeDefinitionCatalog.GetRequired(attributeName, tagName));

		Assert.Equal(
			DomXamlSynchronizationDirection.DomToXaml,
			descriptor.SynchronizationDirection);
		Assert.True(descriptor.TargetCanMutateLocally);
		Assert.False(descriptor.SupportsDomWriteBack);
	}

	[Theory]
	[InlineData(XamlControlDataTargetKind.Text)]
	[InlineData(XamlControlDataTargetKind.SelectedItem)]
	[InlineData(XamlControlDataTargetKind.SelectedValue)]
	[InlineData(XamlControlDataTargetKind.Value)]
	[InlineData(XamlControlDataTargetKind.IsChecked)]
	public void XamlExecutionCatalog_InteractiveControlDataHasNoImplicitDomWriteBack(
		XamlControlDataTargetKind targetKind)
	{
		var descriptor = DomXamlPropertyExecutionCatalog.Resolve(
			"state.probe",
			targetKind);

		Assert.Equal(
			DomXamlSynchronizationDirection.DomToXaml,
			descriptor.SynchronizationDirection);
		Assert.True(descriptor.TargetCanMutateLocally);
		Assert.False(descriptor.SupportsDomWriteBack);
	}

	[Fact]
	public void XamlExecutionCatalog_StaticResourceIsNotLocallyMutable()
	{
		var descriptor = DomXamlPropertyExecutionCatalog.Resolve(
			"resource.image",
			XamlControlDataTargetKind.Source);

		Assert.Equal(
			DomXamlSynchronizationDirection.DomToXaml,
			descriptor.SynchronizationDirection);
		Assert.False(descriptor.TargetCanMutateLocally);
		Assert.False(descriptor.SupportsDomWriteBack);
	}

	[Fact]
	public async Task XamlFill_QueryEngineReceivesPropertyOwnedExecutionContext()
	{
		var element = new HtmlDivDomElement(new(
			"document", "/html/body/div", null, [], null, null));
		element.AddRuntimeProperty("style.width", ElementSlotCategory.Style);
		await element.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "style.width" && slot != DomPropertyDataSlot.Link
					? DomPropertyQueryResult.DirectConstant("320px")
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));
		_ = element.ToXaml(new StringWriter());
		var engine = new RecordingXamlPropertyQueryEngine();

		await element.XamlFillAsync(engine);

		var contexts = engine.Contexts
			.Where(static context => context.PropertyName == "style.width")
			.ToArray();
		Assert.Equal(3, contexts.Length);
		Assert.All(contexts, static context =>
		{
			Assert.Equal("div", context.TagName);
			Assert.Equal("FrameworkElement.Width", context.Execution.TargetProperty);
			Assert.Equal("320px", context.Source.Initialization.Value);
			Assert.Equal("320px", context.Source.Runtime.Value);
		});
		var initialization = contexts.Single(static context =>
			context.Slot == XamlPropertyDataSlot.Initialization);
		Assert.True(initialization.Target.Initialization.IsSet);
		Assert.Equal("320", initialization.Target.Initialization.Value);
	}

	[Fact]
	public async Task ToXaml_AllRegisteredVisualElementsRecordGeneratedInitializationOnItsOwner()
	{
		var tags = HtmlDomElementTypeCatalog.HtmlTags
			.Concat(HtmlDomElementTypeCatalog.SvgTags)
			.Distinct(StringComparer.OrdinalIgnoreCase);
		foreach (var tag in tags)
		{
			var element = HtmlDomElementTypeCatalog.Create(
				tag,
				new("document", $"/probe/{tag}", null, [], null, null));
			if (element.XamlSupport is
				XamlConversionSupport.Unsupported
					or XamlConversionSupport.NonVisual)
			{
				continue;
			}
			DomElementRuntimeProperty width;
			switch (element)
			{
				case HtmlDomElementDefinition html:
					html.AddRuntimeProperty(
						"style.width",
						ElementSlotCategory.Style);
					width = html.RuntimeProperties.Single();
					break;
				case SvgDomElementDefinition svg:
					svg.AddRuntimeProperty(
						"style.width",
						ElementSlotCategory.Style);
					width = svg.RuntimeProperties.Single();
					break;
				default:
					throw new InvalidOperationException(
						$"Unexpected registered element type {element.GetType().Name}.");
			}
			await element.DomFillAsync((_, _, name, slot) =>
				ValueTask.FromResult(
					name == "style.width"
						&& slot == DomPropertyDataSlot.Initialization
							? DomPropertyQueryResult.DirectConstant("320px")
							: DomPropertyQueryResult.ConfirmedAbsent(
								"Not specified.")));
			_ = element.ToXaml(new StringWriter());

			Assert.True(
				width.XamlInitialization.IsSet,
				$"{tag} did not record its generated XAML initialization.");
			Assert.Equal("320", width.XamlInitialization.Value);
			Assert.False(width.XamlRuntime.IsSet);
		}
	}

	[Theory]
	[InlineData("text", "TextBox")]
	[InlineData("search", "TextBox")]
	[InlineData("tel", "TextBox")]
	[InlineData("url", "TextBox")]
	[InlineData("email", "TextBox")]
	[InlineData("password", "PasswordBox")]
	[InlineData("date", "HtmlDateInputControl")]
	[InlineData("month", "HtmlMonthInputControl")]
	[InlineData("week", "HtmlWeekInputControl")]
	[InlineData("time", "HtmlTimeInputControl")]
	[InlineData("datetime-local", "HtmlDateTimeLocalInputControl")]
	[InlineData("number", "NumberBox")]
	[InlineData("range", "Slider")]
	[InlineData("color", "ColorPicker")]
	[InlineData("checkbox", "CheckBox")]
	[InlineData("radio", "RadioButton")]
	[InlineData("file", "HtmlFileInputControl")]
	[InlineData("submit", "HtmlFormButton")]
	[InlineData("image", "HtmlImageSubmitButton")]
	[InlineData("reset", "HtmlFormButton")]
	[InlineData("button", "HtmlFormButton")]
	[InlineData("hidden", "ContentControl")]
	public async Task CreateXaml_InputTypeState_UsesStrongTarget(
		string inputType,
		string expectedElementName)
	{
		var input = new HtmlInputDomElement(new(
			"document", "/html/body/input", null, [], null, null));
		await input.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "type" && slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant(inputType)
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));

		Assert.Equal(
			HtmlXamlSemanticState.ParseInputType(inputType),
			input.InputType);
		Assert.Equal(expectedElementName, input.GetXamlElementMapping().ElementName);
		Assert.Equal(expectedElementName, input.XamlElementName);
	}

	[Theory]
	[InlineData("date", "Value=\"2026-07-29\"", " Text=")]
	[InlineData("month", "Value=\"2026-07\"", " Text=")]
	[InlineData("week", "Value=\"2026-W31\"", " Text=")]
	[InlineData("time", "Value=\"12:30\"", " Text=")]
	[InlineData("datetime-local", "Value=\"2026-07-29T12:30\"", " Text=")]
	[InlineData("file", "Accept=\"image/*\"", " Text=")]
	[InlineData("image", "Source=\"https://example.test/submit.png\"", " Text=")]
	public async Task ToXaml_InputSpecializedState_UsesOnlyTargetProperties(
		string inputType,
		string expected,
		string unexpected)
	{
		var input = new HtmlInputDomElement(new(
			"document", "/html/body/input", null, [], null, null));
		await input.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot != DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					: name == "type"
						? DomPropertyQueryResult.DirectConstant(inputType)
						: name == "value"
							? DomPropertyQueryResult.DirectConstant(
								inputType switch
								{
									"date" => "2026-07-29",
									"month" => "2026-07",
									"week" => "2026-W31",
									"time" => "12:30",
									"datetime-local" => "2026-07-29T12:30",
									_ => string.Empty
								})
							: name == "accept" && inputType == "file"
								? DomPropertyQueryResult.DirectConstant("image/*")
								: name == "src" && inputType == "image"
									? DomPropertyQueryResult.DirectConstant(
										"https://example.test/submit.png")
									: DomPropertyQueryResult.ConfirmedAbsent(
										"Not specified.")));

		var xaml = input.ToXaml(new StringWriter());

		Assert.Contains(expected, xaml, StringComparison.Ordinal);
		Assert.DoesNotContain(unexpected, xaml, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("password", "IsHitTestVisible=\"False\"")]
	[InlineData("date", "IsReadOnly=\"True\"")]
	[InlineData("month", "IsReadOnly=\"True\"")]
	[InlineData("week", "IsReadOnly=\"True\"")]
	[InlineData("time", "IsReadOnly=\"True\"")]
	[InlineData("datetime-local", "IsReadOnly=\"True\"")]
	public async Task ToXaml_ReadOnlyInputState_UsesStrongTargetBehavior(
		string inputType,
		string expected)
	{
		var input = new HtmlInputDomElement(new(
			"document", "/html/body/input", null, [], null, null));
		await input.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot != DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					: name == "type"
						? DomPropertyQueryResult.DirectConstant(inputType)
						: name == "readonly"
							? DomPropertyQueryResult.DirectConstant(string.Empty)
							: DomPropertyQueryResult.ConfirmedAbsent(
								"Not specified.")));

		var xaml = input.ToXaml(new StringWriter());

		Assert.Contains(expected, xaml, StringComparison.Ordinal);
		Assert.DoesNotContain("IsEnabled=\"False\"", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_QuoteAddsGeneratedQuotationMarks()
	{
		var quote = new HtmlQuoteDomElement(new(
			"document", "/html/body/q", null, [], null, null));
		quote.AddRuntimeProperty(
			"content.ownText",
			ElementSlotCategory.DataOrganization);
		await quote.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "content.ownText"
					&& slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant("quoted")
					: DomPropertyQueryResult.ConfirmedAbsent(
						"Not specified.")));

		var xaml = quote.ToXaml(new StringWriter());

		Assert.Contains("Text=\"“quoted”\"", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_TextUsesComputedUaStyleWhenNoAuthoredRuleExists()
	{
		var strong = new HtmlStrongDomElement(new(
			"document", "/html/body/strong", null, [], null, null));
		strong.AddRuntimeProperty(
			"content.ownText",
			ElementSlotCategory.DataOrganization);
		strong.AddRuntimeProperty(
			"style.fontWeight",
			ElementSlotCategory.Style);
		await strong.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "content.ownText"
					&& slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant("important")
					: name == "style.fontWeight"
						&& slot == DomPropertyDataSlot.Runtime
						? DomPropertyQueryResult.DirectConstant("700")
						: DomPropertyQueryResult.ConfirmedAbsent(
							"Not specified.")));

		var xaml = strong.ToXaml(new StringWriter());

		Assert.Contains("FontWeight=\"Bold\"", xaml, StringComparison.Ordinal);
		Assert.Contains("Text=\"important\"", xaml, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ToXaml_CanvasProjectsTrackedRuntimeCommandStream()
	{
		var canvas = new HtmlCanvasDomElement(new(
			"document", "/html/body/canvas", null, [], null, null));
		canvas.AddRuntimeProperty(
			"resource.canvasCommandStream",
			ElementSlotCategory.DataOrganization);
		const string commands =
			"""[{"name":"fillRect","args":[1,2,3,4],"sequence":0}]""";
		await canvas.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "resource.canvasCommandStream"
					&& slot == DomPropertyDataSlot.Runtime
					? DomPropertyQueryResult.DirectConstant(commands)
					: DomPropertyQueryResult.ConfirmedAbsent(
						"Not specified.")));

		var xaml = canvas.ToXaml(new StringWriter());

		Assert.Contains(
			"CommandStream=\"[{&quot;name&quot;:&quot;fillRect&quot;",
			xaml,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("body")]
	[InlineData("header")]
	[InlineData("footer")]
	[InlineData("main")]
	[InlineData("nav")]
	[InlineData("section")]
	[InlineData("article")]
	[InlineData("aside")]
	[InlineData("address")]
	[InlineData("figure")]
	[InlineData("div")]
	[InlineData("hgroup")]
	[InlineData("blockquote")]
	[InlineData("search")]
	[InlineData("slot")]
	[InlineData("form")]
	public async Task CreateXaml_LayoutElementUsesOwnFormattingContext(
		string tagName)
	{
		foreach (var scenario in new[]
		{
			(Display: "block", Direction: "row",
				Kind: XamlElementMappingKind.BlockFlow),
			(Display: "flex", Direction: "row",
				Kind: XamlElementMappingKind.FlexLayout),
			(Display: "grid", Direction: "row",
				Kind: XamlElementMappingKind.GridLayout)
		})
		{
			var element = Assert.IsAssignableFrom<HtmlDomElementDefinition>(
				HtmlDomElementTypeCatalog.Create(
					tagName,
					new(
						"document",
						$"/html/body/{tagName}",
						null,
						[],
						null,
						null)));
			element.AddRuntimeProperty(
				"style.display",
				DomElementRuntimePropertyCatalog.Standard.Single(
					static property =>
						property.Name == "style.display").Category);
			element.AddRuntimeProperty(
				"style.flexDirection",
				DomElementRuntimePropertyCatalog.Standard.Single(
					static property =>
						property.Name == "style.flexDirection").Category);
			await element.DomFillAsync((_, _, name, slot) =>
				ValueTask.FromResult(
					slot != DomPropertyDataSlot.Runtime
						? DomPropertyQueryResult.ConfirmedAbsent(
							"Not specified.")
						: name == "style.display"
							? DomPropertyQueryResult.DirectConstant(
								scenario.Display)
							: name == "style.flexDirection"
								? DomPropertyQueryResult.DirectConstant(
									scenario.Direction)
								: DomPropertyQueryResult.ConfirmedAbsent(
									"Not specified.")));

			var display = Assert.Single(
				element.RuntimeProperties,
				static property => property.Name == "style.display");
			Assert.Equal(scenario.Display, display.SourceRuntime.Value);
			Assert.Equal(
				scenario.Kind,
				element.GetXamlElementMapping().Kind);
		}
	}

	[Theory]
	[InlineData(false, null, "ComboBox", HtmlSelectPresentationState.DropDownSingleSelection)]
	[InlineData(false, "4", "ListView", HtmlSelectPresentationState.VisibleSingleSelectionList)]
	[InlineData(true, null, "ListView", HtmlSelectPresentationState.VisibleMultipleSelectionList)]
	public async Task CreateXaml_SelectState_UsesStrongTarget(
		bool multiple,
		string? size,
		string expectedElementName,
		HtmlSelectPresentationState expectedPresentation)
	{
		var select = new HtmlSelectDomElement(new(
			"document", "/html/body/select", null, [], null, null));
		await select.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot != DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					: name == "multiple" && multiple
						? DomPropertyQueryResult.DirectConstant(string.Empty)
						: name == "size" && size is not null
							? DomPropertyQueryResult.DirectConstant(size)
							: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));

		Assert.Equal(expectedPresentation, select.Presentation);
		Assert.Equal(expectedElementName, select.GetXamlElementMapping().ElementName);
	}

	[Fact]
	public async Task BuildResolvedXamlAttributes_HtmlCssBoxUsesStrongGapProperties()
	{
		var element = new HtmlDivDomElement(new(
			"document", "/html/body/div", null, [], null, null));
		foreach (var name in new[]
		{
			"style.display",
			"style.rowGap",
			"style.columnGap"
		})
		{
			element.AddRuntimeProperty(name, ElementSlotCategory.Style);
		}
		await element.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Link
					? DomPropertyQueryResult.ConfirmedAbsent("Not specified.")
					: DomPropertyQueryResult.DirectConstant(name switch
					{
						"style.display" => "grid",
						"style.rowGap" => "8px",
						"style.columnGap" => "12px",
						_ => string.Empty
					})));

		var attributes = element.BuildResolvedXamlAttributes();

		Assert.Contains(attributes, static item =>
			item.Name == "RowGap" && item.Value == "8");
		Assert.Contains(attributes, static item =>
			item.Name == "ColumnGap" && item.Value == "12");
		Assert.DoesNotContain(attributes, static item =>
			item.Name is "RowSpacing" or "ColumnSpacing");
	}

	[Fact]
	public async Task BuildResolvedXamlAttributes_CssBoxThicknessPreservesAllFourSources()
	{
		var element = new HtmlDivDomElement(new(
			"document", "/html/body/div", null, [], null, null));
		foreach (var name in new[]
		{
			"style.paddingTop",
			"style.paddingRight",
			"style.paddingBottom",
			"style.paddingLeft"
		})
		{
			element.AddRuntimeProperty(name, ElementSlotCategory.Style);
		}
		await element.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Initialization
					? DomPropertyQueryResult.DirectConstant(name switch
					{
						"style.paddingTop" => "1px",
						"style.paddingRight" => "2px",
						"style.paddingBottom" => "3px",
						"style.paddingLeft" => "4px",
						_ => string.Empty
					})
					: DomPropertyQueryResult.ConfirmedAbsent("Not specified.")));

		var padding = Assert.Single(
			element.BuildResolvedXamlAttributes(),
			static attribute => attribute.Name == "Padding");

		Assert.Equal("4,1,2,3", padding.Value);
		Assert.Equal(
			[
				"style.paddingLeft",
				"style.paddingTop",
				"style.paddingRight",
				"style.paddingBottom"
			],
			padding.Sources.Select(static source => source.Name));
		Assert.All(
			padding.Sources,
			source =>
			{
				var runtimeProperty =
					Assert.IsType<DomElementRuntimeProperty>(source);
				Assert.True(runtimeProperty.XamlInitialization.IsSet);
				Assert.Equal(
					"4,1,2,3",
					runtimeProperty.XamlInitialization.Value);
			});
	}

	private static TestDomElement Create(
		string xpath,
		string? parentXPath,
		IReadOnlyList<string> childXPaths) =>
		new(new(
			"document",
			xpath,
			parentXPath,
			childXPaths,
			null,
			null));

	private static MethodInfo DeclaredBuildXamlAttributes(Type type) =>
		type.GetMethod(
			"BuildXamlAttributes",
			BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new InvalidOperationException(
			$"{type.FullName} lacks BuildXamlAttributes.");

	private static int Count(string value, string text)
	{
		var count = 0;
		for (var index = 0;
			(index = value.IndexOf(text, index, StringComparison.Ordinal)) >= 0;
			index += text.Length)
		{
			count++;
		}
		return count;
	}

	private sealed class RecordingXamlPropertyQueryEngine :
		IXamlPropertyQueryEngine
	{
		public List<XamlPropertyQueryContext> Contexts { get; } = [];

		public ValueTask<XamlPropertyQueryResult> QueryAsync(
			XamlPropertyQueryContext context,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Contexts.Add(context);
			return ValueTask.FromResult(
				XamlPropertyQueryResult.ConfirmedAbsent("Recorded."));
		}
	}

	private sealed class TestDomElement(DomElementMapping mapping) :
		HtmlContainerDomElementDefinition(mapping, "test-element")
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
			new(XamlElementObjectType.Grid, XamlElementMappingKind.ConservativeContainer, true, "XAML generation test container.");

		[ElementProperty(
			PropertyValueKind.Enumeration,
			PropertyValueStage.CssInitialization,
			CoordinateMode = PropertyCoordinateMode.Relative,
			Translation = PropertyTranslationKind.Semantic,
			IsSlottedProperty = true,
			IsXamlFillRequired = true,
			IsXamlOutputProperty = true,
			TargetProperty = "HorizontalAlignment")]
		public DomElementStringProperty Alignment { get; } =
			new("design.alignment", ElementDataOrganizationSlotKind.Custom);

		[ElementProperty(
			PropertyValueKind.Coordinate,
			PropertyValueStage.DomRuntime,
			CoordinateMode = PropertyCoordinateMode.Absolute,
			ReferenceSpace = PropertyReferenceSpace.Viewport,
			Translation = PropertyTranslationKind.RuntimeCalculated,
			IsSlottedProperty = true,
			IsXamlFillRequired = true,
			IsXamlOutputProperty = true,
			TargetProperty = "Canvas.Left")]
		public DomElementStringProperty RuntimeX { get; } =
			new("runtime.x", ElementDataOrganizationSlotKind.Custom);

		[ElementProperty(
			PropertyValueKind.Text,
			PropertyValueStage.HtmlInitialization,
			Translation = PropertyTranslationKind.Direct,
			IsSlottedProperty = true,
			IsXamlFillRequired = true,
			IsXamlOutputProperty = true,
			TargetProperty = "Tag")]
		public DomElementStringProperty OptionalTag { get; } =
			new("optional.tag", ElementDataOrganizationSlotKind.Custom);

		public int PublicToXamlCalls { get; private set; }
		public int SubtreeToXamlCalls { get; private set; }
		public int OwnXamlCalls { get; private set; }
		public int ChildrenXamlCalls { get; private set; }

		public override string ToXaml(TextWriter output)
		{
			PublicToXamlCalls++;
			return base.ToXaml(output);
		}

		protected override void ToXaml(
			StringBuilder output,
			int depth,
			bool isDocumentRoot)
		{
			SubtreeToXamlCalls++;
			base.ToXaml(output, depth, isDocumentRoot);
		}

		protected override void WriteOwnXaml(
			StringBuilder output,
			int depth,
			bool isDocumentRoot)
		{
			OwnXamlCalls++;
			base.WriteOwnXaml(output, depth, isDocumentRoot);
		}

		protected override void WriteChildrenXaml(
			StringBuilder output,
			int depth,
			bool isDocumentRoot)
		{
			ChildrenXamlCalls++;
			base.WriteChildrenXaml(output, depth, isDocumentRoot);
		}
	}
}
