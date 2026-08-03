using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class XamlColorNormalizationTests
{
	[Theory]
	[InlineData("rgb(17, 34, 51)", "#FF112233")]
	[InlineData("rgba(17, 34, 51, 0.5)", "#80112233")]
	[InlineData("rgb(17 34 51 / 50%)", "#80112233")]
	[InlineData("#1234", "#44112233")]
	[InlineData("#11223344", "#44112233")]
	[InlineData("#112233", "#112233")]
	public void NormalizeXamlColor_ConvertsCssColorToXamlArgb(
		string value,
		string expected)
	{
		Assert.Equal(expected, TestLayoutElement.NormalizeColor(value));
	}

	private sealed class TestLayoutElement(DomElementMapping mapping)
		: HtmlLayoutDomElementDefinition(
			mapping,
			"test",
			ElementCategory.LayoutContainer,
			ElementContentModel.Flow,
			XamlConversionSupport.Composite,
			ElementDefaultDisplay.Block,
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
			new(XamlElementObjectType.Grid, XamlElementMappingKind.ConservativeContainer, true, "Color normalization test container.");

		internal static string? NormalizeColor(string value) =>
			NormalizeXamlColor(value);
	}
}
