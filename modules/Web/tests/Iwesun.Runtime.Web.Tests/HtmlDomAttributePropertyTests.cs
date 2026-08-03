using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class HtmlDomAttributePropertyTests
{
	[Theory]
	[InlineData("li", "value", HtmlAttributeValueSyntax.Integer)]
	[InlineData("input", "value", HtmlAttributeValueSyntax.Text)]
	[InlineData("meter", "value", HtmlAttributeValueSyntax.FloatingPoint)]
	[InlineData("input", "type", HtmlAttributeValueSyntax.Enumeration)]
	[InlineData("link", "type", HtmlAttributeValueSyntax.MimeType)]
	[InlineData("iframe", "src", HtmlAttributeValueSyntax.Url)]
	[InlineData("div", "hidden", HtmlAttributeValueSyntax.BooleanOrKeyword)]
	public void Definition_WhenAttributeMeaningDependsOnElement_UsesContext(
		string tagName,
		string attributeName,
		HtmlAttributeValueSyntax expected)
	{
		var definition = HtmlAttributeDefinitionCatalog.GetRequired(
			attributeName,
			tagName);

		Assert.Equal(expected, definition.ValueSyntax);
	}

	[Fact]
	public void Definition_WhenAttributeIsSecuritySensitive_RequiresManualReview()
	{
		var definition = HtmlAttributeDefinitionCatalog.GetRequired(
			"sandbox",
			"iframe");

		Assert.True(definition.IsSecuritySensitive);
		Assert.Equal(
			HtmlAttributeXamlStrategy.ManualReview,
			definition.XamlStrategy);
	}
}
