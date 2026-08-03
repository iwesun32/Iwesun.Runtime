using Microsoft.UI.Xaml;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Target-side WinUI dependency properties for CSS semantics without a native
/// one-to-one WinUI property. The live layout/style engines consume these
/// values after installation. Audit reads only the installed target property;
/// it never falls back to source DOM evidence.
/// </summary>
internal static class WinUiAppliedCssRuntimeProperties
{
	private static readonly IReadOnlyDictionary<string, DependencyProperty>
		Properties = new Dictionary<string, DependencyProperty>(
			StringComparer.Ordinal)
		{
			["style.display"] = Register("Display"),
			["style.opacity"] = Register("Opacity"),
			["style.position"] = Register("Position"),
			["style.width"] = Register("Width"),
			["style.height"] = Register("Height"),
			["style.minWidth"] = Register("MinWidth"),
			["style.maxWidth"] = Register("MaxWidth"),
			["style.minHeight"] = Register("MinHeight"),
			["style.maxHeight"] = Register("MaxHeight"),
			["style.boxSizing"] = Register("BoxSizing"),
			["style.top"] = Register("Top"),
			["style.right"] = Register("Right"),
			["style.bottom"] = Register("Bottom"),
			["style.left"] = Register("Left"),
			["style.inset"] = Register("Inset"),
			["style.marginTop"] = Register("MarginTop"),
			["style.marginRight"] = Register("MarginRight"),
			["style.marginBottom"] = Register("MarginBottom"),
			["style.marginLeft"] = Register("MarginLeft"),
			["style.paddingTop"] = Register("PaddingTop"),
			["style.paddingRight"] = Register("PaddingRight"),
			["style.paddingBottom"] = Register("PaddingBottom"),
			["style.paddingLeft"] = Register("PaddingLeft"),
			["style.borderTopColor"] = Register("BorderTopColor"),
			["style.borderRightColor"] = Register("BorderRightColor"),
			["style.borderBottomColor"] = Register("BorderBottomColor"),
			["style.borderLeftColor"] = Register("BorderLeftColor"),
			["style.borderTopLeftRadius"] =
				Register("BorderTopLeftRadius"),
			["style.borderTopRightRadius"] =
				Register("BorderTopRightRadius"),
			["style.borderBottomRightRadius"] =
				Register("BorderBottomRightRadius"),
			["style.borderBottomLeftRadius"] =
				Register("BorderBottomLeftRadius"),
			["style.fill"] = Register("Fill"),
			["style.stroke"] = Register("Stroke"),
			["style.strokeWidth"] = Register("StrokeWidth"),
			["style.transformOrigin"] = Register("TransformOrigin"),
			["style.transitionProperty"] = Register("TransitionProperty"),
			["style.transitionDuration"] = Register("TransitionDuration"),
			["style.transitionTimingFunction"] =
				Register("TransitionTimingFunction"),
			["style.transitionDelay"] = Register("TransitionDelay"),
			["style.flex"] = Register("Flex"),
			["style.flexDirection"] = Register("FlexDirection"),
			["style.flexWrap"] = Register("FlexWrap"),
			["style.flexGrow"] = Register("FlexGrow"),
			["style.flexShrink"] = Register("FlexShrink"),
			["style.flexBasis"] = Register("FlexBasis"),
			["style.justifyContent"] = Register("JustifyContent"),
			["style.alignContent"] = Register("AlignContent"),
			["style.alignItems"] = Register("AlignItems"),
			["style.alignSelf"] = Register("AlignSelf"),
			["style.rowGap"] = Register("RowGap"),
			["style.columnGap"] = Register("ColumnGap"),
			["style.order"] = Register("Order"),
			["style.gridTemplateColumns"] =
				Register("GridTemplateColumns"),
			["style.gridTemplateRows"] = Register("GridTemplateRows"),
			["style.gridAutoColumns"] = Register("GridAutoColumns"),
			["style.gridAutoRows"] = Register("GridAutoRows"),
			["style.gridAutoFlow"] = Register("GridAutoFlow"),
			["style.overflowX"] = Register("OverflowX"),
			["style.overflowY"] = Register("OverflowY"),
			["style.objectFit"] = Register("ObjectFit"),
			["style.whiteSpace"] = Register("WhiteSpace"),
			["style.pointerEvents"] = Register("PointerEvents"),
			["style.transform"] = Register("Transform")
		};

	internal static IEnumerable<string> PropertyNames =>
		Properties.Keys;

	internal static void Set(
		DependencyObject target,
		string propertyName,
		string value)
	{
		ArgumentNullException.ThrowIfNull(target);
		target.SetValue(Resolve(propertyName), value);
	}

	internal static bool TryGet(
		DependencyObject target,
		string propertyName,
		out string value)
	{
		ArgumentNullException.ThrowIfNull(target);
		if (!Properties.TryGetValue(propertyName, out var property))
		{
			value = string.Empty;
			return false;
		}
		value = target.GetValue(property) as string ?? string.Empty;
		return value.Length != 0;
	}

	private static DependencyProperty Resolve(string propertyName) =>
		Properties.TryGetValue(propertyName, out var property)
			? property
			: throw new ArgumentOutOfRangeException(
				nameof(propertyName),
				propertyName,
				"Unsupported target-side CSS runtime property.");

	private static DependencyProperty Register(string name) =>
		DependencyProperty.RegisterAttached(
			name,
			typeof(string),
			typeof(WinUiAppliedCssRuntimeProperties),
			new PropertyMetadata(string.Empty));
}
