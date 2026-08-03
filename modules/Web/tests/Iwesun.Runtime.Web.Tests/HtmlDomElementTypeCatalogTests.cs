using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class HtmlDomElementTypeCatalogTests
{
	[Fact]
	public void Create_WhenTagIsMigrated_ReturnsConcreteType()
	{
		var element = HtmlDomElementTypeCatalog.Create(
			"nav",
			Mapping("/html/body/nav"));

		Assert.IsType<HtmlNavDomElement>(element);
		Assert.True(HtmlDomElementTypeCatalog.MatchesRegisteredType(element));
	}

	[Fact]
	public void Create_WhenStandardTagIsMigrated_ReturnsElementSpecificType()
	{
		var element = HtmlDomElementTypeCatalog.Create(
			"button",
			Mapping("/html/body/button"));

		Assert.IsType<HtmlButtonDomElement>(element);
		Assert.Equal("button", element.TagName);
	}

	[Fact]
	public void AuditCompleteness_WhenHtmlAndSvgAreMigrated_Passes()
	{
		var result = HtmlDomElementTypeCatalog.AuditCompleteness();

		Assert.True(result.Passed);
		Assert.Equal(113, result.RegisteredHtmlTypes);
		Assert.Equal(14, result.RegisteredSvgTypes);
		Assert.Empty(result.MissingHtmlTags);
		Assert.Empty(result.MissingSvgTags);
	}

	[Fact]
	public void Create_WhenSvgTagIsMigrated_ReturnsSvgSpecificType()
	{
		var element = HtmlDomElementTypeCatalog.Create(
			"clipPath",
			Mapping("/html/body/svg/defs/clipPath"));

		Assert.IsType<SvgClipPathDomElement>(element);
		Assert.Equal(ElementNamespace.Svg, element.ElementNamespace);
		Assert.True(HtmlDomElementTypeCatalog.MatchesRegisteredType(element));
	}

	[Theory]
	[InlineData("nav", DomElementQuerySpecialization.Layout)]
	[InlineData("span", DomElementQuerySpecialization.Text)]
	[InlineData("a", DomElementQuerySpecialization.Interactive)]
	[InlineData("input", DomElementQuerySpecialization.FormControl)]
	[InlineData("video", DomElementQuerySpecialization.Media)]
	[InlineData("meta", DomElementQuerySpecialization.Metadata)]
	[InlineData("path", DomElementQuerySpecialization.Svg)]
	public void Create_AssignsInheritedDedicatedQuerySpecialization(
		string tag,
		DomElementQuerySpecialization expected)
	{
		var element = HtmlDomElementTypeCatalog.Create(
			tag,
			Mapping($"/html/body/{tag}"));

		Assert.Equal(expected, element.QuerySpecialization);
	}

	private static DomElementMapping Mapping(string xpath) =>
		new("document", xpath, null, [], null, null);
}
