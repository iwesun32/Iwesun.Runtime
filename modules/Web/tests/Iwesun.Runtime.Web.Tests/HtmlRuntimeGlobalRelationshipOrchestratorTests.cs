using Iwesun.Runtime.Web;
using Xunit;

namespace Iwesun.Runtime.Web.Tests;

public sealed class HtmlRuntimeGlobalRelationshipOrchestratorTests
{
	[Fact]
	public async Task ResolveDom_SeparatesUiResourcesFromBusinessInputs()
	{
		var tree = DomElementTreeBuilder.Build(
			"document",
			[
				new("document", "/html", "html", null, ["/html/body"], null, null),
				new(
					"document",
					"/html/body",
					"body",
					"/html",
					["/html/body/img", "/html/body/input"],
					null,
					null),
				new(
					"document",
					"/html/body/img",
					"img",
					"/html/body",
					[],
					null,
					"/html/body/input"),
				new(
					"document",
					"/html/body/input",
					"input",
					"/html/body",
					[],
					"/html/body/img",
					null)
			]);
		var businessText = "业务正文";
		DomPropertyQueryDelegate query = (_, xpath, name, slot) =>
			ValueTask.FromResult(
				slot == DomPropertyDataSlot.Link
					? DomPropertyQueryResult.ConfirmedAbsent("No link fixture.")
					: (xpath, name) switch
					{
						("/html/body/img", "src") =>
							DomPropertyQueryResult.DirectConstant("ui/icon.png"),
						("/html/body/input", "placeholder") =>
							DomPropertyQueryResult.DirectConstant("请输入"),
						("/html/body/input", "content.value") =>
							DomPropertyQueryResult.DirectConstant(businessText),
						_ => DomPropertyQueryResult.ConfirmedAbsent("No fixture value.")
					});
		await tree.HtmlRootElement.DomFillAsync(query);

		var orchestrator = new HtmlRuntimeGlobalRelationshipOrchestrator();
		var graph = orchestrator.ResolveDom(
			tree.DocumentRoots,
			new HtmlRuntimeDesignRuntime());

		Assert.Contains(graph.BusinessInputs, static binding =>
			binding.Element.TagName == "img"
			&& binding.Source.Name == "src"
			&& binding.ContentKind == HtmlRuntimeDataContentKind.Graphic);
		Assert.Contains(graph.UiResources, static binding =>
			binding.Source.Name == "placeholder"
			&& binding.ContentKind == HtmlRuntimeDataContentKind.Text);
		Assert.Contains(graph.BusinessInputs, static binding =>
			binding.Source.Name == "content.value"
			&& binding.Value == "业务正文"
			&& binding.ContentKind == HtmlRuntimeDataContentKind.FormState);
		Assert.DoesNotContain(graph.UiResources, static binding =>
			binding.Source.Name == "src");

		var firstDynamicValue = Assert.Single(graph.BusinessInputs.Where(
			static binding => binding.Source.Name == "content.value"))
			.DynamicValue;
		businessText = "刷新后的业务正文";
		await tree.HtmlRootElement.DomFillAsync(query);
		var refreshed = orchestrator.ResolveDom(
			tree.DocumentRoots,
			new HtmlRuntimeDesignRuntime());
		var refreshedBinding = Assert.Single(refreshed.BusinessInputs.Where(
			static binding => binding.Source.Name == "content.value"));
		Assert.Same(firstDynamicValue, refreshedBinding.DynamicValue);
		Assert.Equal("刷新后的业务正文", firstDynamicValue.Value);
		Assert.Equal(2, firstDynamicValue.Revision);
		var programRevision = orchestrator.UpdateBusinessInput(
			"document",
			"/html/body/input",
			"content.value",
			"程序输入正文");
		Assert.Equal(3, programRevision);
		Assert.Equal("程序输入正文", firstDynamicValue.Value);
		Assert.Throws<InvalidOperationException>(() =>
			orchestrator.UpdateBusinessInput(
				"document",
				"/html/body/input",
				"placeholder",
				"禁止覆盖 UI 资源"));
	}
}
