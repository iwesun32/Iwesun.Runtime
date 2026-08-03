using Iwesun.Runtime.Web;
using System.Reflection;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

/// <summary>
/// 验证反射属性遍历是否能正确获取继承链上的所有属性
/// </summary>
public sealed class ReflectionInheritanceTests
{
	[Fact]
	public void GetAttributes_ForButtonElement_IncludesGlobalAndSpecificProperties()
	{
		var element = new HtmlButtonDomElement(Mapping("/html/body/button"));
		var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());

		// 全局属性（来自 HtmlDomElementDefinition 基类）
		Assert.Contains(traits, t => t.PropertyName == "Id");
		Assert.Contains(traits, t => t.PropertyName == "Class");
		Assert.Contains(traits, t => t.PropertyName == "Style");
		Assert.Contains(traits, t => t.PropertyName == "Title");
		Assert.Contains(traits, t => t.PropertyName == "Hidden");
		Assert.Contains(traits, t => t.PropertyName == "TabIndex");
		Assert.Contains(traits, t => t.PropertyName == "Role");

		// 按钮专属属性（来自 HtmlButtonDomElement）
		Assert.Contains(traits, t => t.PropertyName == "Disabled");
		Assert.Contains(traits, t => t.PropertyName == "Form");
		Assert.Contains(traits, t => t.PropertyName == "Name");
		Assert.Contains(traits, t => t.PropertyName == "Type");
		Assert.Contains(traits, t => t.PropertyName == "Value");
		Assert.Contains(traits, t => t.PropertyName == "Command");
		Assert.Contains(traits, t => t.PropertyName == "FormAction");

		// 管理属性（来自 DomElement 根类）
		Assert.Contains(traits, t => t.PropertyName == "DocumentScope");
		Assert.Contains(traits, t => t.PropertyName == "XPath");
		Assert.Contains(traits, t => t.PropertyName == "Parent");
		Assert.Contains(traits, t => t.PropertyName == "Children");
	}

	[Fact]
	public void GetAttributes_ForInputElement_IncludesAllInheritedProperties()
	{
		var element = new HtmlInputDomElement(Mapping("/html/body/input"));
		var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());

		// 全局属性
		Assert.Contains(traits, t => t.PropertyName == "Id");
		Assert.Contains(traits, t => t.PropertyName == "Class");

		// input 专属属性
		Assert.Contains(traits, t => t.PropertyName == "Accept");
		Assert.Contains(traits, t => t.PropertyName == "Alt");
		Assert.Contains(traits, t => t.PropertyName == "Checked");
		Assert.Contains(traits, t => t.PropertyName == "Disabled");
		Assert.Contains(traits, t => t.PropertyName == "Form");
		Assert.Contains(traits, t => t.PropertyName == "Name");
		Assert.Contains(traits, t => t.PropertyName == "Placeholder");
		Assert.Contains(traits, t => t.PropertyName == "ReadOnly");
		Assert.Contains(traits, t => t.PropertyName == "Required");
		Assert.Contains(traits, t => t.PropertyName == "Type");
		Assert.Contains(traits, t => t.PropertyName == "Value");
	}

	[Fact]
	public void GetAttributes_ForAnchorElement_IncludesHyperlinkBaseProperties()
	{
		var element = new HtmlAnchorDomElement(Mapping("/html/body/a"));
		var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());

		// 全局属性
		Assert.Contains(traits, t => t.PropertyName == "Id");

		// 超链接属性（在 HtmlAnchorDomElement 中声明，继承自 HtmlHyperlinkDomElementDefinition 语义）
		Assert.Contains(traits, t => t.PropertyName == "Href");
		Assert.Contains(traits, t => t.PropertyName == "Target");
		Assert.Contains(traits, t => t.PropertyName == "Download");
		Assert.Contains(traits, t => t.PropertyName == "Rel");
		Assert.Contains(traits, t => t.PropertyName == "HrefLang");
		Assert.Contains(traits, t => t.PropertyName == "Type");
		Assert.Contains(traits, t => t.PropertyName == "ReferrerPolicy");
	}

	[Fact]
	public void GetAttributes_ForTableHeaderCell_IncludesTableCellBaseProperties()
	{
		var element = new HtmlTableHeaderCellDomElement(Mapping("/html/body/table/tr/th"));
		var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());

		// 全局属性
		Assert.Contains(traits, t => t.PropertyName == "Id");

		// 表格单元格属性（在 HtmlTableHeaderCellDomElement 中声明）
		Assert.Contains(traits, t => t.PropertyName == "ColumnSpan");
		Assert.Contains(traits, t => t.PropertyName == "RowSpan");
		Assert.Contains(traits, t => t.PropertyName == "Headers");

		// th 专属属性
		Assert.Contains(traits, t => t.PropertyName == "Scope");
		Assert.Contains(traits, t => t.PropertyName == "Abbreviation");
	}

	[Fact]
	public void GetAttributes_ForVideoElement_IncludesMediaProperties()
	{
		var element = new HtmlVideoDomElement(Mapping("/html/body/video"));
		var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());

		// 全局属性
		Assert.Contains(traits, t => t.PropertyName == "Id");

		// video 媒体属性
		Assert.Contains(traits, t => t.PropertyName == "Source");
		Assert.Contains(traits, t => t.PropertyName == "CrossOrigin");
		Assert.Contains(traits, t => t.PropertyName == "Poster");
		Assert.Contains(traits, t => t.PropertyName == "Preload");
		Assert.Contains(traits, t => t.PropertyName == "AutoPlay");
		Assert.Contains(traits, t => t.PropertyName == "Loop");
		Assert.Contains(traits, t => t.PropertyName == "Muted");
		Assert.Contains(traits, t => t.PropertyName == "Controls");
		Assert.Contains(traits, t => t.PropertyName == "Width");
		Assert.Contains(traits, t => t.PropertyName == "Height");
	}

	[Fact]
	public void GetAttributes_ForGenericDiv_OnlyHasGlobalProperties()
	{
		var element = new HtmlDivDomElement(Mapping("/html/body/div"));
		var traits = ElementPropertyTraitsReflector.GetAttributes(element.GetType());

		// div 没有专属属性，只有全局属性和管理属性
		var htmlPropertyNames = traits
			.Where(t => t.IsHtmlDefinedProperty)
			.Select(t => t.PropertyName)
			.ToArray();

		// 全局属性应该存在
		Assert.Contains("Id", htmlPropertyNames);
		Assert.Contains("Class", htmlPropertyNames);
		Assert.Contains("Style", htmlPropertyNames);

		// div 不应该有表单/媒体属性
		Assert.DoesNotContain("Disabled", htmlPropertyNames);
		Assert.DoesNotContain("Href", htmlPropertyNames);
		Assert.DoesNotContain("Source", htmlPropertyNames);
	}

	[Fact]
	public void Audit_OnButtonElement_TraversesAllInheritedProperties()
	{
		var element = new HtmlButtonDomElement(Mapping("/html/body/button"));

		// Audit 方法内部使用 ElementPropertyTraitsReflector.GetAttributes(GetType())
		// 如果继承属性能被正确遍历，Audit 应该能正常执行而不会抛出异常
		var report = element.Audit();

		// 报告应该包含元素标识
		Assert.Contains("/html/body/button", report.ElementIdentity);
	}

	[Fact]
	public void CommonHtmlPropertyFamilies_AreExpressedBySharedContracts()
	{
		Assert.IsAssignableFrom<IHtmlHyperlinkCommonProperties>(
			new HtmlAnchorDomElement(Mapping("/html/body/a")));
		Assert.IsAssignableFrom<IHtmlHyperlinkCommonProperties>(
			new HtmlImageMapAreaDomElement(Mapping("/html/body/area")));
		Assert.IsAssignableFrom<IHtmlMediaCommonProperties>(
			new HtmlAudioDomElement(Mapping("/html/body/audio")));
		Assert.IsAssignableFrom<IHtmlMediaCommonProperties>(
			new HtmlVideoDomElement(Mapping("/html/body/video")));
		Assert.IsAssignableFrom<IHtmlModificationCommonProperties>(
			new HtmlDeletedTextDomElement(Mapping("/html/body/del")));
		Assert.IsAssignableFrom<IHtmlModificationCommonProperties>(
			new HtmlInsertedTextDomElement(Mapping("/html/body/ins")));
		Assert.IsAssignableFrom<IHtmlQuoteCommonProperties>(
			new HtmlBlockQuoteDomElement(Mapping("/html/body/blockquote")));
		Assert.IsAssignableFrom<IHtmlQuoteCommonProperties>(
			new HtmlQuoteDomElement(Mapping("/html/body/q")));
		Assert.IsAssignableFrom<IHtmlTableCellCommonProperties>(
			new HtmlTableCellDomElement(Mapping("/html/body/table/tr/td")));
		Assert.IsAssignableFrom<IHtmlTableCellCommonProperties>(
			new HtmlTableHeaderCellDomElement(Mapping("/html/body/table/tr/th")));
		Assert.IsAssignableFrom<IHtmlSubmitterCommonProperties>(
			new HtmlButtonDomElement(Mapping("/html/body/button")));
		Assert.IsAssignableFrom<IHtmlSubmitterCommonProperties>(
			new HtmlInputDomElement(Mapping("/html/body/input")));
	}

	[Fact]
	public void CommonHtmlProperties_AreOwnedByBaseClasses_NotConcreteElements()
	{
		var cases = new (Type ElementType, string[] PropertyNames)[]
		{
			(typeof(HtmlAnchorDomElement),
				["Href", "Target", "Download", "Ping", "Rel", "ReferrerPolicy"]),
			(typeof(HtmlImageMapAreaDomElement),
				["Href", "Target", "Download", "Ping", "Rel", "ReferrerPolicy"]),
			(typeof(HtmlAudioDomElement),
				["Source", "CrossOrigin", "Preload", "AutoPlay", "Loop", "Muted", "Controls", "Loading"]),
			(typeof(HtmlVideoDomElement),
				["Source", "CrossOrigin", "Preload", "AutoPlay", "Loop", "Muted", "Controls", "Loading"]),
			(typeof(HtmlDeletedTextDomElement), ["Cite", "DateTime"]),
			(typeof(HtmlInsertedTextDomElement), ["Cite", "DateTime"]),
			(typeof(HtmlBlockQuoteDomElement), ["Cite"]),
			(typeof(HtmlQuoteDomElement), ["Cite"]),
			(typeof(HtmlTableCellDomElement), ["ColumnSpan", "RowSpan", "Headers"]),
			(typeof(HtmlTableHeaderCellDomElement), ["ColumnSpan", "RowSpan", "Headers"]),
			(typeof(HtmlButtonDomElement),
				["Disabled", "Form", "Name", "FormAction", "FormEncodingType", "FormMethod", "FormNoValidate", "FormTarget"]),
			(typeof(HtmlInputDomElement),
				["Disabled", "Form", "Name", "FormAction", "FormEncodingType", "FormMethod", "FormNoValidate", "FormTarget"]),
			(typeof(HtmlTextAreaDomElement), ["Disabled", "Form", "Name"]),
			(typeof(HtmlSelectDomElement), ["Disabled", "Form", "Name"]),
			(typeof(HtmlFieldSetDomElement), ["Disabled", "Form", "Name"]),
			(typeof(HtmlOutputDomElement), ["Form", "Name"]),
			(typeof(HtmlObjectDomElement), ["Form", "Name"])
		};

		foreach (var item in cases)
		{
			var concreteProperties = item.ElementType.GetProperties(
				BindingFlags.Instance
					| BindingFlags.Public
					| BindingFlags.DeclaredOnly);
			foreach (var propertyName in item.PropertyNames)
			{
				Assert.DoesNotContain(
					concreteProperties,
					property => property.Name == propertyName);
				var inheritedProperty = item.ElementType.GetProperty(propertyName);
				Assert.NotNull(inheritedProperty);
				Assert.NotEqual(
					item.ElementType,
					inheritedProperty.DeclaringType);
			}
		}
	}

	private static DomElementMapping Mapping(string xpath) =>
		new("document", xpath, null, [], null, null);
}
