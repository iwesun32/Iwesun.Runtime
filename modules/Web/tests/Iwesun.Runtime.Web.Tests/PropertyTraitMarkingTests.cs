using Iwesun.Runtime.Web;
using System.Reflection;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

/// <summary>
/// 验证反射特性标记是否正确：
/// - HTML 属性必须标记 IsFillRequired = true（需要从网页填充真实数据）
/// - 管理属性/设计属性不应标记 IsFillRequired（类自身管理，不需要填充）
/// </summary>
public sealed class PropertyTraitMarkingTests
{
	[Theory]
	[InlineData(typeof(HtmlButtonDomElement))]
	[InlineData(typeof(HtmlInputDomElement))]
	[InlineData(typeof(HtmlAnchorDomElement))]
	[InlineData(typeof(HtmlDivDomElement))]
	[InlineData(typeof(HtmlVideoDomElement))]
	[InlineData(typeof(HtmlImageDomElement))]
	[InlineData(typeof(HtmlTableHeaderCellDomElement))]
	public void HtmlDefinedProperties_HaveIsFillRequired(Type elementType)
	{
		var traits = ElementPropertyTraitsReflector.GetAttributes(elementType);

		// 所有标记为 IsHtmlDefinedProperty 的属性都应该标记 IsFillRequired
		var htmlProperties = traits
			.Where(t => t.IsHtmlDefinedProperty)
			.ToList();

		Assert.NotEmpty(htmlProperties);

		var missingFillRequired = htmlProperties
			.Where(t => !t.IsFillRequired)
			.Select(t => t.PropertyName)
			.ToArray();

		Assert.True(
			missingFillRequired.Length == 0,
			$"以下 HTML 属性缺少 IsFillRequired 标记: {string.Join(", ", missingFillRequired)}");
	}

	[Theory]
	[InlineData(typeof(HtmlButtonDomElement))]
	[InlineData(typeof(HtmlInputDomElement))]
	[InlineData(typeof(HtmlAnchorDomElement))]
	[InlineData(typeof(HtmlDivDomElement))]
	public void HtmlDefinedProperties_HaveIsXamlFillRequired(Type elementType)
	{
		var traits = ElementPropertyTraitsReflector.GetAttributes(elementType);

		// 所有标记为 IsHtmlDefinedProperty 的属性都应该标记 IsXamlFillRequired
		var htmlProperties = traits
			.Where(t => t.IsHtmlDefinedProperty)
			.ToList();

		var missingXamlFillRequired = htmlProperties
			.Where(t => !t.IsXamlFillRequired)
			.Select(t => t.PropertyName)
			.ToArray();

		Assert.True(
			missingXamlFillRequired.Length == 0,
			$"以下 HTML 属性缺少 IsXamlFillRequired 标记: {string.Join(", ", missingXamlFillRequired)}");
	}

	[Theory]
	[InlineData(typeof(HtmlButtonDomElement))]
	[InlineData(typeof(HtmlInputDomElement))]
	[InlineData(typeof(HtmlAnchorDomElement))]
	[InlineData(typeof(HtmlDivDomElement))]
	public void HtmlDefinedProperties_HaveIsAuditRequired(Type elementType)
	{
		var traits = ElementPropertyTraitsReflector.GetAttributes(elementType);

		// 所有标记为 IsHtmlDefinedProperty 的属性都应该标记 IsAuditRequired
		var htmlProperties = traits
			.Where(t => t.IsHtmlDefinedProperty)
			.ToList();

		var missingAuditRequired = htmlProperties
			.Where(t => !t.IsAuditRequired)
			.Select(t => t.PropertyName)
			.ToArray();

		Assert.True(
			missingAuditRequired.Length == 0,
			$"以下 HTML 属性缺少 IsAuditRequired 标记: {string.Join(", ", missingAuditRequired)}");
	}

	[Theory]
	[InlineData(typeof(HtmlButtonDomElement))]
	[InlineData(typeof(HtmlInputDomElement))]
	[InlineData(typeof(HtmlAnchorDomElement))]
	[InlineData(typeof(HtmlDivDomElement))]
	public void HtmlDefinedProperties_HaveIsSlottedPropertyOrCollection(Type elementType)
	{
		var traits = ElementPropertyTraitsReflector.GetAttributes(elementType);

		// 所有标记为 IsHtmlDefinedProperty 的属性都应该标记 IsSlottedProperty 或 IsSlottedPropertyCollection
		var htmlProperties = traits
			.Where(t => t.IsHtmlDefinedProperty)
			.ToList();

		var missingSlotted = htmlProperties
			.Where(t => !t.IsSlottedProperty && !t.IsSlottedPropertyCollection)
			.Select(t => t.PropertyName)
			.ToArray();

		Assert.True(
			missingSlotted.Length == 0,
			$"以下 HTML 属性缺少 IsSlottedProperty/IsSlottedPropertyCollection 标记: {string.Join(", ", missingSlotted)}");
	}

	[Fact]
	public void ManagementProperties_DoNotRequireFill()
	{
		var traits = ElementPropertyTraitsReflector.GetAttributes(typeof(HtmlDivDomElement));

		// 管理属性（树关系、XPath 等）不应该标记 IsFillRequired
		var managementProperties = traits
			.Where(t => t.IsManagementProperty || t.IsObjectTreeRelationshipProperty || t.IsDomMappingProperty)
			.ToList();

		Assert.NotEmpty(managementProperties);

		var incorrectlyMarkedForFill = managementProperties
			.Where(t => t.IsFillRequired)
			.Select(t => t.PropertyName)
			.ToArray();

		Assert.True(
			incorrectlyMarkedForFill.Length == 0,
			$"以下管理属性错误标记了 IsFillRequired: {string.Join(", ", incorrectlyMarkedForFill)}");
	}

	[Fact]
	public void DesignProperties_DoNotRequireFill()
	{
		var traits = ElementPropertyTraitsReflector.GetAttributes(typeof(HtmlDivDomElement));

		// 类型固有设计属性（TagName, Category 等）不应该标记 IsFillRequired
		var designProperties = traits
			.Where(t => t.IsElementDesignProperty)
			.ToList();

		Assert.NotEmpty(designProperties);

		var incorrectlyMarkedForFill = designProperties
			.Where(t => t.IsFillRequired)
			.Select(t => t.PropertyName)
			.ToArray();

		Assert.True(
			incorrectlyMarkedForFill.Length == 0,
			$"以下设计属性错误标记了 IsFillRequired: {string.Join(", ", incorrectlyMarkedForFill)}");
	}

	[Fact]
	public void RuntimeDerivedProperties_DoNotRequireFill()
	{
		var traits = ElementPropertyTraitsReflector.GetAttributes(typeof(HtmlDivDomElement));

		// 运行时派生属性（Parent, Children, Siblings 等）不应该标记 IsFillRequired
		var runtimeProperties = traits
			.Where(t => t.IsRuntimeDerivedProperty)
			.ToList();

		Assert.NotEmpty(runtimeProperties);

		var incorrectlyMarkedForFill = runtimeProperties
			.Where(t => t.IsFillRequired)
			.Select(t => t.PropertyName)
			.ToArray();

		Assert.True(
			incorrectlyMarkedForFill.Length == 0,
			$"以下运行时派生属性错误标记了 IsFillRequired: {string.Join(", ", incorrectlyMarkedForFill)}");
	}

	[Fact]
	public void GlobalHtmlProperties_AllHaveCorrectMarking()
	{
		var traits = ElementPropertyTraitsReflector.GetAttributes(typeof(HtmlDivDomElement));

		// 36 个全局 HTML 属性应该全部存在且正确标记
		var expectedGlobalProperties = new[]
		{
			"Id", "Class", "Style", "Title", "Language", "Direction", "Hidden",
			"TabIndex", "AccessKey", "ContentEditable", "Draggable", "SpellCheck",
			"Translate", "Role", "Slot", "Part", "ExportParts", "Popover", "Inert",
			"InputMode", "EnterKeyHint", "AutoCapitalize", "AutoCorrect", "AutoFocus",
			"HeadingOffset", "HeadingReset", "Is", "WritingSuggestions", "Nonce",
			"ItemId", "ItemProp", "ItemRef", "ItemScope", "ItemType"
		};

		foreach (var propName in expectedGlobalProperties)
		{
			var trait = traits.First(t => t.PropertyName == propName);
			Assert.True(trait.IsHtmlDefinedProperty, $"{propName} 应该标记 IsHtmlDefinedProperty");
			Assert.True(trait.IsSlottedProperty || trait.IsSlottedPropertyCollection, $"{propName} 应该标记 IsSlottedProperty 或 IsSlottedPropertyCollection");
			Assert.True(trait.IsFillRequired, $"{propName} 应该标记 IsFillRequired");
			Assert.True(trait.IsXamlFillRequired, $"{propName} 应该标记 IsXamlFillRequired");
			Assert.True(trait.IsAuditRequired, $"{propName} 应该标记 IsAuditRequired");
		}
	}

	[Fact]
	public void EventCollections_AreMarkedForFill()
	{
		var traits = ElementPropertyTraitsReflector.GetAttributes(typeof(HtmlButtonDomElement));

		var eventProperty = traits.First(t => t.PropertyName == "Events");
		Assert.True(eventProperty.IsEventCollectionProperty);
		Assert.True(eventProperty.IsFillRequired);
		Assert.True(eventProperty.IsSlottedPropertyCollection);
	}

	[Fact]
	public void ExtensionAttributes_AreMarkedForFill()
	{
		var traits = ElementPropertyTraitsReflector.GetAttributes(typeof(HtmlDivDomElement));

		var extensionProperty = traits.First(t => t.PropertyName == "ExtensionAttributes");
		Assert.True(extensionProperty.IsSlottedPropertyCollection);
		Assert.True(extensionProperty.IsFillRequired);
	}

	[Fact]
	public void RuntimeProperties_AreMarkedForFill()
	{
		var traits = ElementPropertyTraitsReflector.GetAttributes(typeof(HtmlDivDomElement));

		var runtimeProperty = traits.First(t => t.PropertyName == "RuntimeProperties");
		Assert.True(runtimeProperty.IsSlottedPropertyCollection);
		Assert.True(runtimeProperty.IsFillRequired);
	}

	[Fact]
	public void AllStandardHtmlElements_HaveConsistentPropertyMarking()
	{
		var failures = new List<string>();

		foreach (var tag in HtmlDomElementTypeCatalog.HtmlTags)
		{
			var element = HtmlDomElementTypeCatalog.Create(
				tag,
				new DomElementMapping("document", $"/html/body/{tag}", null, [], null, null));

			var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());

			// 检查所有 HTML 属性都有完整标记
			foreach (var trait in traits.Where(t => t.IsHtmlDefinedProperty))
			{
				if (!trait.IsFillRequired)
					failures.Add($"{tag}.{trait.PropertyName}: missing IsFillRequired");
				if (!trait.IsXamlFillRequired)
					failures.Add($"{tag}.{trait.PropertyName}: missing IsXamlFillRequired");
				if (!trait.IsAuditRequired)
					failures.Add($"{tag}.{trait.PropertyName}: missing IsAuditRequired");
				if (!trait.IsSlottedProperty && !trait.IsSlottedPropertyCollection)
					failures.Add($"{tag}.{trait.PropertyName}: missing IsSlottedProperty/IsSlottedPropertyCollection");
			}
		}

		Assert.True(
			failures.Count == 0,
			$"属性标记不一致:\n{string.Join("\n", failures)}");
	}

	[Fact]
	public void OwnerResolvedTraits_UseExplicitUnspecifiedStaticRoutingSentinels()
	{
		var traits = ElementPropertyTraitsReflector
			.GetAttributes(typeof(HtmlDivDomElement))
			.Where(static trait => trait.IsFillRequired)
			.ToArray();

		Assert.All(traits, static trait =>
		{
			Assert.True(trait.UsesOwnerResolvedTraversal);
			Assert.Equal(ElementEvidenceKind.None, trait.FillEvidenceKind);
			Assert.Equal(ElementFillSlot.Unspecified, trait.FillSlot);
			Assert.Equal(ElementSlotCategory.Unspecified, trait.FillCategory);
			Assert.Empty(trait.FillQueryName);
		});
	}

	[Fact]
	public void ValidateTraversalContract_PublicSlotOwnerWithoutTrait_Throws()
	{
		var element = new MissingTraitElement(
			new DomElementMapping(
				"document",
				"/html/body/missing-trait",
				null,
				[],
				null,
				null));

		var exception = Assert.Throws<InvalidOperationException>(
			element.ValidateTraversalContract);

		Assert.Contains("Rogue", exception.Message, StringComparison.Ordinal);
	}

	private sealed class MissingTraitElement(DomElementMapping mapping) :
		HtmlContainerDomElementDefinition(mapping, "div")
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
			new(XamlElementObjectType.Grid, XamlElementMappingKind.ConservativeContainer, true, "Missing-trait test container.");

		public DomElementStringProperty Rogue { get; } =
			new("rogue", ElementDataOrganizationSlotKind.Custom);
	}
}
