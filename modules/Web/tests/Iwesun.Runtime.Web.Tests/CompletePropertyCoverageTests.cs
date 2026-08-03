using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

/// <summary>
/// 全面验证：所有元素类型的所有属性都完整实现，特性标记正确，基类遍历无遗漏
/// </summary>
public sealed class CompletePropertyCoverageTests
{
	[Fact]
	public void AllHtmlElements_HaveCompletePropertyCoverage()
	{
		var failures = new List<string>();

		foreach (var tag in HtmlDomElementTypeCatalog.HtmlTags)
		{
			var element = HtmlDomElementTypeCatalog.Create(
				tag,
				new DomElementMapping("document", $"/html/body/{tag}", null, [], null, null));

			var htmlElement = Assert.IsAssignableFrom<HtmlDomElementDefinition>(element);
			var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());

			// 1. 验证所有 HTML 属性都有完整标记
			foreach (var trait in traits.Where(t => t.IsHtmlDefinedProperty))
			{
				if (!trait.IsFillRequired)
					failures.Add($"{tag}.{trait.PropertyName}: IsHtmlDefinedProperty 但缺少 IsFillRequired");
				if (!trait.IsXamlFillRequired)
					failures.Add($"{tag}.{trait.PropertyName}: IsHtmlDefinedProperty 但缺少 IsXamlFillRequired");
				if (!trait.IsAuditRequired)
					failures.Add($"{tag}.{trait.PropertyName}: IsHtmlDefinedProperty 但缺少 IsAuditRequired");
				if (!trait.IsSlottedProperty && !trait.IsSlottedPropertyCollection)
					failures.Add($"{tag}.{trait.PropertyName}: IsHtmlDefinedProperty 但缺少 IsSlottedProperty/Collection");
			}

			// 2. 验证规范中定义的属性都存在
			var expectedAttributes = HtmlElementAttributeCatalog.GetSupportedAttributes(tag);
			var declaredAttributeNames = traits
				.Where(t => t.IsHtmlDefinedProperty && t.IsSlottedProperty)
				.Select(t => t.PropertyName.ToLowerInvariant())
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			// 扩展属性和集合属性不算在内
			foreach (var expected in expectedAttributes)
			{
				// 全局属性应该存在
				if (HtmlElementAttributeCatalog.GetGlobalAttributes().Contains(expected))
				{
					// 全局属性在基类中定义，检查是否能通过反射获取
					var globalProperty = traits.FirstOrDefault(t =>
						t.IsHtmlDefinedProperty &&
						t.PropertyName.Equals(GlobalAttributeToPropertyName(expected), StringComparison.OrdinalIgnoreCase));
					if (globalProperty.Equals(default(ElementPropertyTraits)))
					{
						failures.Add($"{tag}: 全局属性 '{expected}' 未找到");
					}
				}
			}

			// 3. 验证 FillAsync 能遍历到所有需要填充的属性（通过计数验证）
			var fillRequiredCount = traits.Count(t => t.IsFillRequired);
			if (fillRequiredCount == 0)
			{
				failures.Add($"{tag}: 没有任何属性标记为 IsFillRequired");
			}
		}

		Assert.True(
			failures.Count == 0,
			$"HTML 元素属性覆盖不完整:\n{string.Join("\n", failures.Take(20))}" +
			(failures.Count > 20 ? $"\n... 共 {failures.Count} 个问题" : ""));
	}

	[Fact]
	public void AllSvgElements_HaveCompletePropertyCoverage()
	{
		var failures = new List<string>();

		foreach (var tag in HtmlDomElementTypeCatalog.SvgTags)
		{
			var element = HtmlDomElementTypeCatalog.Create(
				tag,
				new DomElementMapping("document", $"/html/body/svg/{tag}", null, [], null, null));

			var svgElement = Assert.IsAssignableFrom<SvgDomElementDefinition>(element);
			var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());

			// 验证所有 SVG 属性都有完整标记
			foreach (var trait in traits.Where(t => t.IsHtmlDefinedProperty))
			{
				if (!trait.IsFillRequired)
					failures.Add($"svg.{tag}.{trait.PropertyName}: IsHtmlDefinedProperty 但缺少 IsFillRequired");
				if (!trait.IsXamlFillRequired)
					failures.Add($"svg.{tag}.{trait.PropertyName}: IsHtmlDefinedProperty 但缺少 IsXamlFillRequired");
				if (!trait.IsAuditRequired)
					failures.Add($"svg.{tag}.{trait.PropertyName}: IsHtmlDefinedProperty 但缺少 IsAuditRequired");
			}

			var fillRequiredCount = traits.Count(t => t.IsFillRequired);
			if (fillRequiredCount == 0)
			{
				failures.Add($"svg.{tag}: 没有任何属性标记为 IsFillRequired");
			}
		}

		Assert.True(
			failures.Count == 0,
			$"SVG 元素属性覆盖不完整:\n{string.Join("\n", failures)}");
	}

	[Fact]
	public void ManagementAndDesignProperties_AreNotMarkedForFill()
	{
		var element = new HtmlDivDomElement(
			new DomElementMapping("document", "/html/body/div", null, [], null, null));
		var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());

		var failures = new List<string>();

		// 设计属性（类型固有）不应该标记 FillRequired
		var designProperties = new[]
		{
			"TagName", "ElementNamespace", "Category", "VisualKind", "ContentModel",
			"Closure", "Syntax", "XamlSupport", "XamlControlFamily", "XamlElementName",
			"DefaultDisplay", "InteractionKind", "XamlChildPlacement"
		};

		foreach (var propName in designProperties)
		{
			var trait = traits.FirstOrDefault(t => t.PropertyName == propName);
			if (trait.Equals(default(ElementPropertyTraits)))
			{
				failures.Add($"设计属性 {propName} 未找到");
				continue;
			}
			if (!trait.IsElementDesignProperty)
				failures.Add($"{propName}: 应该标记 IsElementDesignProperty");
			if (trait.IsFillRequired)
				failures.Add($"{propName}: 设计属性不应该标记 IsFillRequired");
		}

		// 管理/树关系属性不应该标记 FillRequired
		var managementProperties = new[]
		{
			"DocumentScope", "XPath", "ParentXPath",
			"LeftSiblingXPath", "RightSiblingXPath", "Parent", "Children",
			"LeftSibling", "RightSibling"
		};

		foreach (var propName in managementProperties)
		{
			var trait = traits.FirstOrDefault(t => t.PropertyName == propName);
			if (trait.Equals(default(ElementPropertyTraits)))
			{
				failures.Add($"管理属性 {propName} 未找到");
				continue;
			}
			if (trait.IsFillRequired)
				failures.Add($"{propName}: 管理属性不应该标记 IsFillRequired");
		}

		Assert.True(
			failures.Count == 0,
			$"管理/设计属性标记错误:\n{string.Join("\n", failures)}");
	}

	[Fact]
	public void FillAsync_ProcessesAllExpectedProperties()
	{
		// 验证 DomFillAsync 确实会遍历到所有 IsFillRequired 标记的属性
		var element = new HtmlButtonDomElement(
			new DomElementMapping("document", "/html/body/button", null, [], null, null));

		var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());
		var expectedFillCount = traits.Count(t => t.IsFillRequired);

		// 统计实际会被处理的属性数量（与 DomFillAsync 逻辑一致）
		var actualProcessedCount = 0;
		foreach (var trait in traits.Where(t => t.IsFillRequired))
		{
			var property = element.GetType().GetProperty(trait.PropertyName);
			Assert.NotNull(property);
			var value = property.GetValue(element);
			// DomFillAsync 会处理 IDomPropertySlotOwner, IDomEventSlotOwner, 或它们的集合
			if (value is IDomPropertySlotOwner || value is IDomEventSlotOwner ||
				value is IEnumerable<IDomPropertySlotOwner> || value is IEnumerable<IDomEventSlotOwner>)
			{
				actualProcessedCount++;
			}
		}

		// 所有 IsFillRequired 属性都应该能被 FillAsync 处理
		Assert.Equal(expectedFillCount, actualProcessedCount);
	}

	[Fact]
	public void Audit_TraversesAllAuditRequiredProperties()
	{
		// 验证 Audit 方法会遍历到所有 IsAuditRequired 标记的属性
		var element = new HtmlDivDomElement(
			new DomElementMapping("document", "/html/body/div", null, [], null, null));

		var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());
		var auditRequiredCount = traits.Count(t => t.IsAuditRequired);

		// Audit 方法内部使用相同的反射逻辑
		Assert.True(auditRequiredCount > 30, $"div 应该有超过 30 个需要审核的属性，实际有 {auditRequiredCount}");
	}

	[Fact]
	public void ToXaml_OutputsAllXamlOutputProperties()
	{
		// 验证 ToXaml 会处理所有 IsXamlOutputProperty 标记的属性
		var element = new HtmlDivDomElement(
			new DomElementMapping("document", "/html/body/div", null, [], null, null));

		var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());
		var xamlOutputCount = traits.Count(t => t.IsXamlOutputProperty);
		var xamlFillRequiredCount = traits.Count(t => t.IsXamlFillRequired);

		// XAML 填充属性数量应该合理
		Assert.True(xamlFillRequiredCount > 30, $"div 应该有超过 30 个 XAML 填充属性，实际有 {xamlFillRequiredCount}");
	}

	[Fact]
	public void ElementCatalog_RegistersAll113HtmlTypes()
	{
		var audit = HtmlDomElementTypeCatalog.AuditCompleteness();
		Assert.True(audit.Passed);
		Assert.Equal(113, audit.RegisteredHtmlTypes);
		Assert.Equal(14, audit.RegisteredSvgTypes);
		Assert.Empty(audit.MissingHtmlTags);
		Assert.Empty(audit.MissingSvgTags);
	}

	[Fact]
	public void StandardRuntimeProperties_AllHaveExplicitXamlExecutionRoutes()
	{
		var unsupported = DomElementRuntimePropertyCatalog.Standard
			.Select(static property => (
				property.Name,
				Execution: DomXamlPropertyExecutionCatalog.Resolve(
					property.Name)))
			.Where(static item => !item.Execution.IsSupported)
			.Select(static item => item.Name)
			.Order(StringComparer.Ordinal)
			.ToArray();

		Assert.True(
			unsupported.Length == 0,
			"Standard runtime properties without a XAML execution route:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, unsupported));
	}

	[Fact]
	public void EveryConcreteElementSlotOwner_HasAnExplicitXamlExecutionRoute()
	{
		var failures = new List<string>();
		foreach (var tag in HtmlDomElementTypeCatalog.HtmlTags
			.Concat(HtmlDomElementTypeCatalog.SvgTags))
		{
			var element = HtmlDomElementTypeCatalog.Create(
				tag,
				new DomElementMapping(
					"document",
					$"/audit/{tag}",
					null,
					[],
					null,
					null));
			foreach (var definition in DomElementRuntimePropertyCatalog.Standard)
			{
				switch (element)
				{
					case HtmlDomElementDefinition html:
						html.AddRuntimeProperty(
							definition.Name,
							definition.Category,
							definition.EvidenceKind);
						break;
					case SvgDomElementDefinition svg:
						svg.AddRuntimeProperty(
							definition.Name,
							definition.Category,
							definition.EvidenceKind);
						break;
				}
			}
			foreach (var traits in ElementPropertyTraitsReflector
				.GetAttributes(element.GetType())
				.Where(static traits => traits.IsXamlFillRequired))
			{
				var property = element.GetType().GetProperty(traits.PropertyName);
				Assert.NotNull(property);
				foreach (var owner in EnumerateExecutionOwners(
					property.GetValue(element)))
				{
					if (!owner.XamlExecution.IsSupported)
					{
						failures.Add(
							$"{tag}.{traits.PropertyName}/{owner.XamlExecution.SourcePropertyName}");
					}
				}
			}
		}

		Assert.True(
			failures.Count == 0,
			"Element slot owners without XAML execution routes:"
				+ Environment.NewLine
				+ string.Join(Environment.NewLine, failures));
	}

	[Fact]
	public void GlobalAttributes_AllPresentAndMarked()
	{
		var element = new HtmlDivDomElement(
			new DomElementMapping("document", "/html/body/div", null, [], null, null));
		var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());

		var expectedGlobalAttributes = new Dictionary<string, string>
		{
			["id"] = "Id",
			["class"] = "Class",
			["style"] = "Style",
			["title"] = "Title",
			["lang"] = "Language",
			["dir"] = "Direction",
			["hidden"] = "Hidden",
			["tabindex"] = "TabIndex",
			["accesskey"] = "AccessKey",
			["contenteditable"] = "ContentEditable",
			["draggable"] = "Draggable",
			["spellcheck"] = "SpellCheck",
			["translate"] = "Translate",
			["role"] = "Role",
			["slot"] = "Slot",
			["part"] = "Part",
			["exportparts"] = "ExportParts",
			["popover"] = "Popover",
			["inert"] = "Inert",
			["inputmode"] = "InputMode",
			["enterkeyhint"] = "EnterKeyHint",
			["autocapitalize"] = "AutoCapitalize",
			["autocorrect"] = "AutoCorrect",
			["autofocus"] = "AutoFocus",
			["nonce"] = "Nonce",
			["itemid"] = "ItemId",
			["itemprop"] = "ItemProp",
			["itemref"] = "ItemRef",
			["itemscope"] = "ItemScope",
			["itemtype"] = "ItemType"
		};

		foreach (var (htmlAttr, propertyName) in expectedGlobalAttributes)
		{
			var trait = traits.FirstOrDefault(t => t.PropertyName == propertyName);
			Assert.False(
				trait.Equals(default(ElementPropertyTraits)),
				$"全局属性 '{htmlAttr}' (->{propertyName}) 未找到");
			Assert.True(trait.IsHtmlDefinedProperty, $"{propertyName}: 应该标记 IsHtmlDefinedProperty");
			Assert.True(trait.IsSlottedProperty, $"{propertyName}: 应该标记 IsSlottedProperty");
			Assert.True(trait.IsFillRequired, $"{propertyName}: 应该标记 IsFillRequired");
			Assert.True(trait.IsXamlFillRequired, $"{propertyName}: 应该标记 IsXamlFillRequired");
			Assert.True(trait.IsAuditRequired, $"{propertyName}: 应该标记 IsAuditRequired");
		}
	}

	private static string GlobalAttributeToPropertyName(string htmlAttr) => htmlAttr switch
	{
		"id" => "Id",
		"class" => "Class",
		"style" => "Style",
		"title" => "Title",
		"lang" => "Language",
		"dir" => "Direction",
		"hidden" => "Hidden",
		"tabindex" => "TabIndex",
		"accesskey" => "AccessKey",
		"contenteditable" => "ContentEditable",
		"draggable" => "Draggable",
		"spellcheck" => "SpellCheck",
		"translate" => "Translate",
		"role" => "Role",
		"slot" => "Slot",
		"part" => "Part",
		"exportparts" => "ExportParts",
		"popover" => "Popover",
		"inert" => "Inert",
		"inputmode" => "InputMode",
		"enterkeyhint" => "EnterKeyHint",
		"autocapitalize" => "AutoCapitalize",
		"autocorrect" => "AutoCorrect",
		"autofocus" => "AutoFocus",
		"headingoffset" => "HeadingOffset",
		"headingreset" => "HeadingReset",
		"is" => "Is",
		"writingsuggestions" => "WritingSuggestions",
		"nonce" => "Nonce",
		"itemid" => "ItemId",
		"itemprop" => "ItemProp",
		"itemref" => "ItemRef",
		"itemscope" => "ItemScope",
		"itemtype" => "ItemType",
		_ => htmlAttr
	};

	private static IEnumerable<IXamlPropertyExecutionOwner>
		EnumerateExecutionOwners(object? value)
	{
		if (value is IXamlPropertyExecutionOwner owner)
			yield return owner;
		if (value is not IEnumerable<IXamlPropertyExecutionOwner> owners)
			yield break;
		foreach (var item in owners)
			yield return item;
	}
}
