using Xunit;

namespace Iwesun.Runtime.Web;

public sealed class HtmlRuntimeViewportTests
{
	[Fact]
	public void ScaleCssPixelToRuntime_UsesSeparateRuntimeAndCssViewports()
	{
		var runtime = new HtmlRuntimeDesignRuntime();
		runtime.Layout.ConfigureDesignViewport(1920, 1280);
		runtime.Layout.ApplyRuntimeViewport(3840, 2020);
		runtime.Layout.ApplyCssRuntimeViewport(1920, 1010);

		Assert.Equal(2496, runtime.Layout.ScaleCssPixelToRuntime(1248, vertical: false));
		Assert.Equal(112, runtime.Layout.ScaleCssPixelToRuntime(56, vertical: true));
		Assert.Equal(2, runtime.Layout.ScaleCssPixelToRuntime(1, vertical: false));
	}

	[Fact]
	public void ScaleCssPixelToRuntime_WithoutBothRuntimeViewports_IsIdentity()
	{
		var runtime = new HtmlRuntimeDesignRuntime();

		Assert.Equal(17, runtime.Layout.ScaleCssPixelToRuntime(17, vertical: false));
		Assert.Equal(23, runtime.Layout.ScaleCssPixelToRuntime(23, vertical: true));
	}

	[Theory]
	[InlineData("Width", "auto", HtmlRuntimeDimensionOverrideKind.AutomaticSize)]
	[InlineData("Height", "AUTO", HtmlRuntimeDimensionOverrideKind.AutomaticSize)]
	[InlineData("MinWidth", "auto", HtmlRuntimeDimensionOverrideKind.ZeroMinimum)]
	[InlineData("MinHeight", "auto", HtmlRuntimeDimensionOverrideKind.ZeroMinimum)]
	[InlineData("MaxWidth", "none", HtmlRuntimeDimensionOverrideKind.UnboundedMaximum)]
	[InlineData("MaxHeight", "NONE", HtmlRuntimeDimensionOverrideKind.UnboundedMaximum)]
	[InlineData("FontSize", "auto", HtmlRuntimeDimensionOverrideKind.Unsupported)]
	public void RuntimeDimensionKeyword_ProducesExplicitOverride(
		string targetProperty,
		string cssValue,
		HtmlRuntimeDimensionOverrideKind expected)
	{
		Assert.Equal(
			expected,
			HtmlRuntimeDimensionOverride.Resolve(targetProperty, cssValue));
	}
}
