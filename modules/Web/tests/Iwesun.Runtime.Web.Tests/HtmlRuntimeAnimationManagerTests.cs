using Xunit;
using Iwesun.Runtime.Web;

namespace Iwesun.Runtime.WebView2.Tests;

public sealed class HtmlRuntimeAnimationManagerTests
{
	[Fact]
	public void Observe_StructuredTimeline_PreservesLinkRuntimeAndTarget()
	{
		var runtime = new HtmlRuntimeDesignRuntime();
		runtime.Observe(Captured(
			WebRuntimeDomDataSlot.Link,
			"css:test.css#animation-name",
			WebRuntimeDomValueSource.LinkedCalculation,
			WebRuntimeDomLinkKind.CssExpression,
			"css:test.css#animation-name"));
		var timeline =
			"{\"schema\":\"iwesun.web.animations/1\","
			+ "\"animations\":[{\"animationName\":\"pulse\","
			+ "\"currentTime\":12.5,\"keyframes\":[]}]}";
		runtime.Observe(Captured(
			WebRuntimeDomDataSlot.Runtime,
			timeline,
			WebRuntimeDomValueSource.DirectConstant,
			WebRuntimeDomLinkKind.None,
			string.Empty));

		var binding = Assert.Single(runtime.Animations.Bindings);
		Assert.Equal("css:test.css#animation-name", binding.Link?.LinkIdentity);
		Assert.Equal(timeline, binding.Runtime?.Value);
		Assert.Equal("iwesun.web.animations/1", binding.Timeline?.Schema);
		Assert.Single(binding.Timeline?.Animations ?? []);

		var target = new object();
		runtime.Animations.RegisterXamlTarget(binding, target);
		Assert.Same(target, Assert.Single(runtime.Animations.XamlTargets).Target);
	}

	private static WebRuntimeDomIndexedProperty Captured(
		WebRuntimeDomDataSlot slot,
		string value,
		WebRuntimeDomValueSource source,
		WebRuntimeDomLinkKind linkKind,
		string linkIdentity) =>
		new(
			new(
				"document",
				"/html/body/div",
				"div",
				"effect.animations",
				"RuntimeProperties",
				WebRuntimeDomOwnerKind.RuntimeProperty,
				WebRuntimeDomSlotCategory.Effect,
				WebRuntimeDomEvidenceKind.Animations,
				slot),
			WebRuntimeDomPropertyStatus.Captured,
			value,
			source,
			linkKind,
			linkIdentity,
			"test animation evidence");
}
