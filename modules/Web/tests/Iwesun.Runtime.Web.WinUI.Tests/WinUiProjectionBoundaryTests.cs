using System.Reflection;
using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.WinUI.Tests;

public sealed class WinUiProjectionBoundaryTests
{
	[Fact]
	public void RuntimeWinUiAssembly_HasNoConsumerProjectDependency()
	{
		var assembly = typeof(global::Iwesun.Runtime.Web.WinUI.WinUiHtmlRuntimeSession)
			.Assembly;
		var forbidden = assembly.GetReferencedAssemblies()
			.Select(static item => item.Name ?? string.Empty)
			.Where(static name =>
				name.Contains("Doubao", StringComparison.OrdinalIgnoreCase))
			.ToArray();

		Assert.Empty(forbidden);
		Assert.All(
			assembly.GetTypes(),
			static type => Assert.DoesNotContain(
				"Doubao",
				type.FullName ?? type.Name,
				StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void StrongFactory_HasConcreteDispatchForEveryStandardElementType()
	{
		var factoryType = typeof(
			global::Iwesun.Runtime.Web.WinUI.WinUiXamlElementObjectFactory);
		var dispatchTypes = factoryType
			.GetMethods(BindingFlags.Instance | BindingFlags.Public)
			.Where(static method => method.Name == "CreateElement")
			.Select(static method => method.GetParameters())
			.Where(static parameters => parameters.Length == 2)
			.Select(static parameters => parameters[0].ParameterType)
			.ToHashSet();
		var mapping = new DomElementMapping(
			"projection-contract",
			"/html",
			null,
			[],
			null,
			null);
		var standardTypes = HtmlDomElementTypeCatalog.HtmlTags
			.Concat(HtmlDomElementTypeCatalog.SvgTags)
			.Select(tag => HtmlDomElementTypeCatalog.Create(tag, mapping).GetType())
			.ToHashSet();

		var missing = standardTypes
			.Except(dispatchTypes)
			.Select(static type => type.FullName ?? type.Name)
			.OrderBy(static name => name, StringComparer.Ordinal)
			.ToArray();

		Assert.Empty(missing);
	}

	[Fact]
	public void CoreArchitectureAudit_PassesForEveryStandardHtmlType()
	{
		var mapping = new DomElementMapping(
			"projection-contract",
			"/html",
			null,
			[],
			null,
			null);
		var failures = HtmlDomElementTypeCatalog.HtmlTags
			.Select(tag => HtmlDomElementTypeCatalog.Create(tag, mapping))
			.Cast<HtmlDomElementDefinition>()
			.Select(HtmlDomElementArchitectureAudit.Inspect)
			.Where(static audit => !audit.Passed)
			.Select(static audit => audit.TagName)
			.ToArray();

		Assert.Empty(failures);
	}

	[Theory]
	[InlineData("Grid.Row")]
	[InlineData("Grid.Column")]
	[InlineData("HtmlCssBoxGrid.FlexGrow")]
	[InlineData("HtmlCssBoxGrid.GridAreaExpression")]
	[InlineData("Width")]
	[InlineData("Margin")]
	[InlineData("HorizontalAlignment")]
	[InlineData("HtmlPosition.Right")]
	[InlineData("HtmlTransform.Value")]
	public void FilteredHost_ParentLayoutAttributesRemainOnOuterCarrier(
		string attributeName)
	{
		Assert.True(
			global::Iwesun.Runtime.Web.WinUI.WinUiXamlElementObjectFactory
				.IsFilteredHostLayoutCarrierAttribute(attributeName));
	}

	[Theory]
	[InlineData("Background")]
	[InlineData("Foreground")]
	[InlineData("Text")]
	[InlineData("Padding")]
	[InlineData("FontSize")]
	public void FilteredHost_VisualAttributesRemainOnInnerStrongElement(
		string attributeName)
	{
		Assert.False(
			global::Iwesun.Runtime.Web.WinUI.WinUiXamlElementObjectFactory
				.IsFilteredHostLayoutCarrierAttribute(attributeName));
	}
}
