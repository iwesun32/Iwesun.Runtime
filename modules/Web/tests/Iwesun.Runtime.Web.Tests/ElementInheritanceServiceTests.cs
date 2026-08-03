using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class ElementInheritanceServiceTests
{
	[Fact]
	public void Resolve_PublishesViewportSizeAndInheritedStyles()
	{
		var service = new ElementInheritanceService();
		service.Publish(new(
			"root",
			null,
			new ElementViewport(3840, 2160),
			new ElementSize(3840, 2080),
			[
				new(
					"font-size",
					"16px",
					PropertyValueKind.Length,
					PropertyInheritanceKind.Inherited),
				new(
					"background-color",
					"#ffffff",
					PropertyValueKind.Color,
					PropertyInheritanceKind.NotInherited)
			]));
		var childLink = service.Publish(new(
			"child",
			"root",
			new ElementViewport(3840, 2160),
			new ElementSize(960, 2080),
			[
				new(
					"color",
					"#111111",
					PropertyValueKind.Color,
					PropertyInheritanceKind.Inherited)
			]));

		var resolved = service.Resolve(childLink);

		Assert.Equal(new ElementViewport(3840, 2160), resolved.Viewport);
		Assert.Equal(new ElementSize(960, 2080), resolved.ContainerSize);
		Assert.Equal("16px", resolved.Styles["font-size"].Value);
		Assert.Equal("#111111", resolved.Styles["color"].Value);
		Assert.DoesNotContain("background-color", resolved.Styles);
	}

	[Fact]
	public async Task Element_LinksToPublishedInheritanceData()
	{
		var service = new ElementInheritanceService();
		var link = service.Publish(new(
			"desktop-4k",
			null,
			new ElementViewport(3840, 2160),
			new ElementSize(3840, 2080)));
		var element = HtmlDomElementTypeCatalog.Create(
			"main",
			new(
				"document",
				"/html/body/main",
				null,
				[],
				null,
				null));

		await element.DomFillAsync((_, _, name, slot) =>
			ValueTask.FromResult(
				name == "service.globalLayout"
					&& slot == DomPropertyDataSlot.Link
					? DomPropertyQueryResult.Captured(
						link.ContextId,
						ElementPropertyValueSource.LinkedCalculation,
						new(
							ElementPropertyLinkKind.LayoutExpression,
							link.ContextId))
					: DomPropertyQueryResult.ConfirmedAbsent("Not requested.")));

		Assert.Equal("desktop-4k", element.GlobalLayoutService.SourceLink.Description);
		Assert.Equal(3840, service.Resolve(link).Viewport.Width);
	}
}
