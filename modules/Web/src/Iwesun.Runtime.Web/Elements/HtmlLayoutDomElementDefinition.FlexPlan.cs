using System.Globalization;

namespace Iwesun.Runtime.Web;

public abstract partial class HtmlLayoutDomElementDefinition
{
	protected override LayoutLength? ResolveXamlFlexMainAxisOffset(
		DomElement child,
		bool vertical)
	{
		var children = OrderedFlexChildren();
		if (children.Length == 0)
			return null;
		var visualFirst = IsReverseFlex() ? children[^1] : children[0];
		if (!ReferenceEquals(visualFirst, child))
			return null;
		var extent = ResolveFlexContentExtent(vertical);
		if (extent <= 0)
			return null;
		var gap = ParseGridPixels(RuntimeValue(
			this,
			vertical ? "style.rowGap" : "style.columnGap"));
		var bases = children
			.Select(candidate => ResolveFlexBase(candidate, vertical))
			.ToArray();
		var margins = children
			.Select(candidate => ResolveFlexMainMargins(candidate, vertical))
			.ToArray();
		var grow = children
			.Select(candidate => ResolveFlexFactor(candidate, "style.flexGrow", 0))
			.ToArray();
		var available = Math.Max(
			0,
			extent - gap * Math.Max(0, children.Length - 1) - margins.Sum());
		var free = available - bases.Sum();
		if (free > 0 && grow.Sum() > 0)
			return null;
		var occupied = bases.Sum() + margins.Sum()
			+ gap * Math.Max(0, children.Length - 1);
		var remainder = Math.Max(0, extent - occupied);
		var justify = (RuntimeValue(this, "style.justifyContent")
			?? "flex-start").Trim().ToLowerInvariant();
		var leading = justify switch
		{
			"flex-end" or "end" => remainder,
			"center" => remainder / 2,
			"space-around" => remainder / (2 * children.Length),
			"space-evenly" => remainder / (children.Length + 1),
			_ => 0
		};
		return leading <= 0
			? null
			: new LayoutLength.Constant(
				leading,
				LayoutLengthUnit.CssPixel);
	}

	protected override bool TryBuildXamlWrappedFlexLayout(
		out bool vertical,
		out IReadOnlyList<XamlGridTrackDefinition> lineDefinitions,
		out IReadOnlyList<IReadOnlyList<DomElement>> lines)
	{
		var display = RuntimeValue(this, "style.display");
		var wrap = RuntimeValue(this, "style.flexWrap") ?? "nowrap";
		if (display is not ("flex" or "inline-flex")
			|| wrap.Equals("nowrap", StringComparison.OrdinalIgnoreCase))
		{
			vertical = false;
			lineDefinitions = [];
			lines = [];
			return false;
		}
		vertical = RuntimeValue(this, "style.flexDirection")?.StartsWith(
			"column",
			StringComparison.OrdinalIgnoreCase) == true;
		var ordered = OrderedFlexChildren();
		var mainExtent = ParseGridPixels(RuntimeValue(
			this,
			vertical ? "style.height" : "style.width"));
		var mainGap = ParseGridPixels(RuntimeValue(
			this,
			vertical ? "style.rowGap" : "style.columnGap"));
		var builtLines = new List<IReadOnlyList<DomElement>>();
		var current = new List<DomElement>();
		var currentExtent = 0d;
		foreach (var child in ordered)
		{
			var childExtent = ResolveFlexBase(child, vertical);
			var required = current.Count == 0 ? childExtent : mainGap + childExtent;
			if (current.Count > 0 && mainExtent > 0 && currentExtent + required > mainExtent)
			{
				builtLines.Add(current.ToArray());
				current = [];
				currentExtent = 0;
				required = childExtent;
			}
			current.Add(child);
			currentExtent += required;
		}
		if (current.Count > 0)
			builtLines.Add(current.ToArray());
		if (wrap.Equals("wrap-reverse", StringComparison.OrdinalIgnoreCase))
			builtLines.Reverse();
		lines = builtLines;
		lineDefinitions = BuildFlexLineCrossTracks(builtLines, vertical);
		return builtLines.Count > 0;
	}

	private bool TryBuildWrappedFlexChildPlans(
		out IReadOnlyList<XamlElementObjectPlan> wrappedPlans)
	{
		if (!TryBuildXamlWrappedFlexLayout(
			out var vertical,
			out _,
			out var lines))
		{
			wrappedPlans = [];
			return false;
		}
		var result = new List<XamlElementObjectPlan>(lines.Count);
		for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
		{
			var line = lines[lineIndex];
			var childPlans = new List<XamlElementObjectPlan>();
			for (var childIndex = 0; childIndex < line.Count; childIndex++)
			{
				var visualIndex = IsReverseFlex()
					? line.Count - childIndex - 1
					: childIndex;
				foreach (var childPlan in line[childIndex].BuildXamlObjectPlans())
				{
					var attributes = childPlan.InitializationAttributes
						.Where(attribute => attribute.Name is not ("Grid.Row" or "Grid.Column"))
						.Append(new(
							vertical ? "Grid.Row" : "Grid.Column",
							visualIndex.ToString(CultureInfo.InvariantCulture),
							null))
						.ToArray();
					childPlans.Add(childPlan with { InitializationAttributes = attributes });
				}
			}
			var lineTracks = BuildStrongNoWrapFlexTracks(line, vertical);
			var lineAttribute = new GeneratedXamlAttribute(
				vertical ? "Grid.Column" : "Grid.Row",
				lineIndex.ToString(CultureInfo.InvariantCulture),
				null);
			result.Add(new(
				null,
				new(
					vertical
						? XamlElementObjectType.HtmlCssVerticalFlexLineGrid
						: XamlElementObjectType.HtmlCssHorizontalFlexLineGrid,
					XamlElementMappingKind.FlexLayout,
					RequiresRuntimeLayoutContract: true,
					"A strong synthetic CSS flex line."),
				[lineAttribute],
				ElementXamlChildPlacementKind.DirectChildren,
				vertical ? lineTracks : [],
				vertical ? [] : lineTracks,
				childPlans,
				$"CSS flex line {lineIndex} for {DocumentScope}::{XPath}.",
				this,
				XamlElementContentProjectionKind.Composite));
		}
		wrappedPlans = result;
		return true;
	}

	private IReadOnlyList<XamlGridTrackDefinition> BuildFlexLineCrossTracks(
		IReadOnlyList<IReadOnlyList<DomElement>> lines,
		bool vertical)
	{
		if (lines.Count == 0)
			return [];
		var crossExtent = ParseGridPixels(RuntimeValue(
			this,
			vertical ? "style.width" : "style.height"));
		var crossGap = ParseGridPixels(RuntimeValue(
			this,
			vertical ? "style.columnGap" : "style.rowGap"));
		var lengths = lines.Select(line => line
			.Select(child => ParseGridPixels(RuntimeValue(
				child,
				vertical ? "style.width" : "style.height")))
			.DefaultIfEmpty(0)
			.Max()).ToArray();
		if (crossExtent > 0)
		{
			var occupied = lengths.Sum() + crossGap * Math.Max(0, lengths.Length - 1);
			DistributeFlexSpacing(
				lengths,
				crossGap,
				Math.Max(0, crossExtent - occupied),
				RuntimeValue(this, "style.alignContent") ?? "stretch");
		}
		return lengths.Select(static length => new XamlGridTrackDefinition(
			length.ToString("R", CultureInfo.InvariantCulture),
			null,
			null)).ToArray();
	}

	private IReadOnlyList<XamlGridTrackDefinition> BuildStrongNoWrapFlexTracks(
		IReadOnlyList<DomElement> children,
		bool vertical)
	{
		if (children.Count == 0)
			return [];
		var extent = ResolveFlexContentExtent(vertical);
		if (extent <= 0)
			return children.Select(child => ResolveFlexTrack(child, vertical)).ToArray();

		var gap = ParseGridPixels(RuntimeValue(
			this,
			vertical ? "style.rowGap" : "style.columnGap"));
		var bases = children.Select(child => ResolveFlexBase(child, vertical)).ToArray();
		var margins = children.Select(child => ResolveFlexMainMargins(child, vertical)).ToArray();
		var grow = children.Select(child => ResolveFlexFactor(child, "style.flexGrow", 0)).ToArray();
		var shrink = children.Select(child => ResolveFlexFactor(child, "style.flexShrink", 1)).ToArray();
		var availableForItems = Math.Max(
			0,
			extent
				- gap * Math.Max(0, children.Count - 1)
				- margins.Sum());
		var free = availableForItems - bases.Sum();
		var lengths = bases.ToArray();
		if (free > 0 && grow.Sum() > 0)
		{
			var totalGrow = grow.Sum();
			for (var index = 0; index < lengths.Length; index++)
				lengths[index] += free * grow[index] / totalGrow;
		}
		else if (free < 0)
		{
			var weights = shrink.Select((factor, index) => factor * bases[index]).ToArray();
			var totalWeight = weights.Sum();
			if (totalWeight > 0)
				for (var index = 0; index < lengths.Length; index++)
					lengths[index] = Math.Max(0, lengths[index] + free * weights[index] / totalWeight);
		}

		for (var index = 0; index < lengths.Length; index++)
			lengths[index] += margins[index];
		var occupied = lengths.Sum() + gap * Math.Max(0, lengths.Length - 1);
		var remainder = Math.Max(0, extent - occupied);
		DistributeFlexSpacing(
			lengths,
			gap,
			remainder,
			RuntimeValue(this, "style.justifyContent") ?? "flex-start",
			IsReverseFlex());
		return lengths.Select(static length => new XamlGridTrackDefinition(
			length.ToString("R", CultureInfo.InvariantCulture),
			null,
			null)).ToArray();
	}

	private double ResolveFlexContentExtent(bool vertical)
	{
		var extent = ParseGridPixels(RuntimeValue(
			this,
			vertical ? "style.height" : "style.width"));
		if (extent <= 0)
			return extent;
		var properties = RuntimePropertiesFor(this).ToDictionary(
			static property => property.Name,
			StringComparer.Ordinal);
		if (ActiveValue(properties.GetValueOrDefault("style.boxSizing"))
			?.Equals("border-box", StringComparison.OrdinalIgnoreCase) != true)
		{
			return extent;
		}
		var chrome = ResolveBoxEdge(
				properties,
				vertical ? "style.paddingTop" : "style.paddingLeft",
				"style.padding")
			+ ResolveBoxEdge(
				properties,
				vertical ? "style.paddingBottom" : "style.paddingRight",
				"style.padding")
			+ ResolveBoxEdge(
				properties,
				vertical ? "style.borderTopWidth" : "style.borderLeftWidth",
				"style.border")
			+ ResolveBoxEdge(
				properties,
				vertical ? "style.borderBottomWidth" : "style.borderRightWidth",
				"style.border");
		return Math.Max(0, extent - chrome);
	}

	private static double ResolveFlexBase(DomElement child, bool vertical)
	{
		var properties = RuntimePropertiesFor(child).ToDictionary(
			static property => property.Name,
			StringComparer.Ordinal);
		var basis = RuntimeValue(child, "style.flexBasis");
		if (!string.IsNullOrWhiteSpace(basis)
			&& basis is not "auto" and not "content")
		{
			var basisLength = ParseGridPixels(basis);
			return AddContentBoxChrome(properties, basisLength, vertical);
		}
		var sizeName = vertical ? "style.height" : "style.width";
		if (properties.TryGetValue(sizeName, out var size)
			&& ResolveOuterBoxTrackLength(properties, size, vertical) is { } outer)
		{
			return ParseGridPixels(outer);
		}
		var geometry = RuntimeValue(child, vertical ? "rect.height" : "rect.width");
		return ParseGridPixels(geometry);
	}

	private static double AddContentBoxChrome(
		IReadOnlyDictionary<string, DomElementRuntimeProperty> properties,
		double length,
		bool vertical)
	{
		if (ActiveValue(properties.GetValueOrDefault("style.boxSizing"))
			?.Equals("border-box", StringComparison.OrdinalIgnoreCase) == true)
		{
			return length;
		}
		return length
			+ ResolveBoxEdge(
				properties,
				vertical ? "style.paddingTop" : "style.paddingLeft",
				"style.padding")
			+ ResolveBoxEdge(
				properties,
				vertical ? "style.paddingBottom" : "style.paddingRight",
				"style.padding")
			+ ResolveBoxEdge(
				properties,
				vertical ? "style.borderTopWidth" : "style.borderLeftWidth",
				"style.border")
			+ ResolveBoxEdge(
				properties,
				vertical ? "style.borderBottomWidth" : "style.borderRightWidth",
				"style.border");
	}

	private static double ResolveFlexMainMargins(DomElement child, bool vertical)
	{
		var properties = RuntimePropertiesFor(child).ToDictionary(
			static property => property.Name,
			StringComparer.Ordinal);
		var leading = ResolveBoxEdge(
			properties,
			vertical ? "style.marginTop" : "style.marginLeft",
			"style.margin");
		var trailing = ResolveBoxEdge(
			properties,
			vertical ? "style.marginBottom" : "style.marginRight",
			"style.margin");
		return leading + trailing;
	}

	private static double ResolveFlexFactor(
		DomElement child,
		string propertyName,
		double fallback) =>
		double.TryParse(
			RuntimeValue(child, propertyName),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var value)
			? Math.Max(0, value)
			: fallback;

	private static void DistributeFlexSpacing(
		double[] lengths,
		double gap,
		double remainder,
		string justifyContent,
		bool reverse = false)
	{
		if (lengths.Length == 0)
			return;
		var normalized = justifyContent.Trim().ToLowerInvariant();
		var between = gap;
		var leading = 0d;
		var trailing = 0d;
		switch (normalized)
		{
			case "flex-end":
			case "end":
				leading = remainder;
				break;
			case "center":
				leading = trailing = remainder / 2;
				break;
			case "space-between" when lengths.Length > 1:
				between += remainder / (lengths.Length - 1);
				break;
			case "space-around":
				between += remainder / lengths.Length;
				leading = trailing = remainder / (2 * lengths.Length);
				break;
			case "space-evenly":
				between += remainder / (lengths.Length + 1);
				leading = trailing = remainder / (lengths.Length + 1);
				break;
			default:
				trailing = remainder;
				break;
		}
		var physicalFirst = reverse ? lengths.Length - 1 : 0;
		var physicalLast = reverse ? 0 : lengths.Length - 1;
		lengths[physicalFirst] += leading;
		if (reverse)
		{
			for (var index = 1; index < lengths.Length; index++)
				lengths[index] += between;
		}
		else
		{
			for (var index = 0; index < lengths.Length - 1; index++)
				lengths[index] += between;
		}
		lengths[physicalLast] += trailing;
	}
}
