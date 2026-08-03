using System.Globalization;
using System.Text;

namespace Iwesun.Runtime.Web;

public abstract partial class HtmlLayoutDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementCategory category,
	ElementContentModel contentModel,
	XamlConversionSupport xamlSupport,
	ElementDefaultDisplay defaultDisplay,
	ElementXamlChildPlacementKind childPlacement) :
	HtmlDomElementDefinition(
		mapping,
		tagName,
		category,
		ElementVisualKind.LayoutContainer,
		contentModel,
		ElementClosure.OpenContainer,
		ElementSyntax.Normal,
		xamlSupport,
		XamlControlFamily.Panel,
		defaultDisplay,
		ElementInteractionKind.None,
		childPlacement)
{
	public override DomElementQuerySpecialization QuerySpecialization =>
		DomElementQuerySpecialization.Layout;

	protected override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans()
	{
		if (TryBuildWrappedFlexChildPlans(out var wrappedPlans))
			return wrappedPlans;
		var plans = BuildDirectDomChildObjectPlans().ToList();
		var ownText = DataSource("content.ownText");
		var value = DataSourceValue("content.ownText");
		if (ownText is not null && !string.IsNullOrWhiteSpace(value))
		{
			var textPlan = CreateSyntheticXamlObjectPlan(
				XamlElementObjectType.TextBlock,
				[
					new("Text", value, ownText),
					new(
						"Tag",
						$"generated-own-text:{DocumentScope}::{XPath}",
						null)
				],
				ElementXamlChildPlacementKind.None,
				[],
				$"Generated own text for {DocumentScope}::{XPath}.");
			var insertionIndex = Math.Clamp(
				CapturedOwnTextElementInsertionIndex,
				0,
				plans.Count);
			plans.Insert(insertionIndex, textPlan);
		}
		if (CreateXaml().Kind == XamlElementMappingKind.BlockFlow)
		{
			var rowIndex = 0;
			for (var index = 0; index < plans.Count; index++)
			{
				if (plans[index].LayoutPlacement?.IsOutOfFlow == true
					|| plans[index].SourceElement is { } sourceElement
						&& !ParticipatesInNormalFlow(sourceElement))
				{
					plans[index] = plans[index] with
					{
						InitializationAttributes = plans[index]
							.InitializationAttributes
							.Where(static attribute =>
								attribute.Name != "Grid.Row")
							.ToArray()
					};
					continue;
				}
				plans[index] = plans[index] with
				{
					InitializationAttributes = plans[index]
						.InitializationAttributes
						.Where(static attribute => attribute.Name != "Grid.Row")
						.Append(new(
							"Grid.Row",
							rowIndex.ToString(CultureInfo.InvariantCulture),
							null))
						.ToArray()
				};
				rowIndex++;
			}
		}
		return plans;
	}

	protected override bool HasGeneratedXamlContent() =>
		!string.IsNullOrWhiteSpace(RuntimeInitialization("content.ownText"));

	protected override void WriteChildrenXaml(
		System.Text.StringBuilder output,
		int depth,
		bool isDocumentRoot)
	{
		base.WriteChildrenXaml(output, depth, isDocumentRoot);
		var ownText = DataSource("content.ownText");
		var value = DataSourceValue("content.ownText");
		if (ownText is not null && !string.IsNullOrWhiteSpace(value))
		{
			output.Append(' ', depth * 2)
				.Append("<TextBlock Text=\"")
				.Append(EscapeXamlAttribute(value))
				.Append("\" Tag=\"generated-own-text:")
				.Append(EscapeXamlAttribute($"{DocumentScope}::{XPath}"))
				.Append("\" />")
				.AppendLine();
		}
	}

	private static bool HasRelativeContainerSizing(DomElement element)
	{
		var runtimeProperties = element switch
		{
			HtmlDomElementDefinition html => html.RuntimeProperties,
			SvgDomElementDefinition svg => svg.RuntimeProperties,
			_ => []
		};
		foreach (var propertyName in RelativeContainerSizingProperties)
		{
			var property = runtimeProperties.FirstOrDefault(candidate =>
				candidate.Name.Equals(propertyName, StringComparison.Ordinal));
			var value = element.HtmlRoot?.ResolveGlobalStyleValue(
				element,
				propertyName,
				DomPropertyDataSlot.Initialization)
				?? (property?.SourceInitialization.IsSet == true
					? property.SourceInitialization.Value
					: null);
			if (string.IsNullOrWhiteSpace(value))
				continue;
			var normalized = value.Trim();
			if (normalized.EndsWith('%')
				|| RelativeLengthFunctions.Any(function =>
					normalized.Contains(
						function,
						StringComparison.OrdinalIgnoreCase))
				|| RelativeLengthUnits.Any(unit =>
					normalized.EndsWith(
						unit,
						StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
		}
		return false;
	}

	private static readonly string[] RelativeContainerSizingProperties =
	[
		"style.width",
		"style.height",
		"style.minWidth",
		"style.minHeight",
		"style.maxWidth",
		"style.maxHeight"
	];

	private static readonly string[] RelativeLengthFunctions =
	[
		"calc(",
		"min(",
		"max(",
		"clamp(",
		"var("
	];

	private static readonly string[] RelativeLengthUnits =
	[
		"vw",
		"vh",
		"vmin",
		"vmax",
		"svw",
		"svh",
		"lvw",
		"lvh",
		"dvw",
		"dvh"
	];

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		var mapping = CreateXaml();
		AddColor(attributes, "style.backgroundColor", "Background");
		AddThickness(attributes, "style.margin", "Margin");
		if (mapping.ElementName == "HtmlCssBoxGrid")
		{
			AddThickness(attributes, "style.padding", "Padding");
			AddThickness(attributes, "style.border", "BorderThickness", "Width");
			AddCornerRadius(attributes);
			AddUniformBorderColor(attributes);
			ExpandContentBoxDimensions(attributes);
		}
		if (mapping.ElementName == "HtmlCssBoxGrid")
		{
			AddLength(attributes, "style.rowGap", "RowGap");
			AddLength(attributes, "style.columnGap", "ColumnGap");
			AddLayoutValue(attributes, "style.display", "LayoutMode", static value =>
				value.Trim().ToLowerInvariant() switch
				{
					"flex" or "inline-flex" => "flex",
					"grid" or "inline-grid" => "grid",
					_ => "block"
				});
			AddLayoutValue(attributes, "style.flexDirection", "FlexDirection");
			AddLayoutValue(attributes, "style.flexWrap", "FlexWrap");
			AddLayoutValue(attributes, "style.justifyContent", "JustifyContent");
			AddLayoutValue(attributes, "style.alignContent", "AlignContent");
			AddLayoutValue(attributes, "style.alignItems", "AlignItems");
			AddLayoutValue(attributes, "style.gridAutoFlow", "GridAutoFlow");
			AddLayoutValue(attributes, "style.gridAutoRows", "GridAutoRows");
			AddLayoutValue(attributes, "style.gridAutoColumns", "GridAutoColumns");
			AddLayoutValue(attributes, "style.gridTemplateAreas", "GridTemplateAreas");
			AddLayoutValue(attributes, "style.gridTemplateRows", "GridTemplateRows");
			AddLayoutValue(attributes, "style.gridTemplateColumns", "GridTemplateColumns");
			AddLayoutValue(attributes, "style.lineHeight", "TextLineHeight", NormalizeXamlLength);
			AddLayoutValue(attributes, "style.fontStretch", "FontStretch", NormalizeFontStretch);
			AddLayoutValue(attributes, "style.textAlign", "TextAlignment", NormalizeTextAlignmentValue);
			AddLayoutValue(attributes, "style.textDecorationLine", "TextDecorations", NormalizeTextDecorations);
			AddLayoutValue(attributes, "style.whiteSpace", "WhiteSpace");
			AddLayoutValue(attributes, "style.textOverflow", "TextOverflow");
			AddLayoutValue(attributes, "style.direction", "FlowDirection", static value =>
				value.Trim().Equals("rtl", StringComparison.OrdinalIgnoreCase)
					? "RightToLeft"
					: "LeftToRight");
		}
		else if (mapping.ElementName is "Grid"
			or "HtmlTablePanel"
			or "HtmlTableSectionPanel"
			or "HtmlTableRowPanel")
		{
			AddLength(attributes, "style.rowGap", "RowSpacing");
			AddLength(attributes, "style.columnGap", "ColumnSpacing");
		}
		else if (mapping.ElementName == "StackPanel")
		{
			AddLength(attributes, "style.rowGap", "Spacing");
		}
		AddLayoutValue(attributes, "style.flexGrow", "HtmlCssBoxGrid.FlexGrow");
		AddLayoutValue(attributes, "style.flexShrink", "HtmlCssBoxGrid.FlexShrink");
		AddLayoutValue(attributes, "style.flexBasis", "HtmlCssBoxGrid.FlexBasis");
		AddLayoutValue(attributes, "style.alignSelf", "HtmlCssBoxGrid.AlignSelf");
		AddLayoutValue(attributes, "style.order", "HtmlCssBoxGrid.Order");
		AddLayoutValue(attributes, "style.gridRow", "HtmlCssBoxGrid.GridRowExpression");
		AddLayoutValue(attributes, "style.gridColumn", "HtmlCssBoxGrid.GridColumnExpression");
		AddLayoutValue(attributes, "style.gridArea", "HtmlCssBoxGrid.GridAreaExpression");
		RemoveDimensionsOwnedByLayout(attributes);
		if (Parent is null)
		{
			attributes["HorizontalAlignment"] = new(
				"HorizontalAlignment", "Stretch", null);
			attributes["VerticalAlignment"] = new(
				"VerticalAlignment", "Stretch", null);
		}
		return attributes.Values
			.OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
			.ToArray();
	}

	private void ExpandContentBoxDimensions(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		var boxSizing = RuntimeValue(this, "style.boxSizing");
		if (!string.Equals(boxSizing, "content-box", StringComparison.OrdinalIgnoreCase))
			return;
		ExpandContentBoxDimension(
			attributes,
			"Width",
			"style.width",
			["style.paddingLeft", "style.paddingRight", "style.borderLeftWidth", "style.borderRightWidth"]);
		ExpandContentBoxDimension(
			attributes,
			"Height",
			"style.height",
			["style.paddingTop", "style.paddingBottom", "style.borderTopWidth", "style.borderBottomWidth"]);
	}

	private void ExpandContentBoxDimension(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string targetName,
		string sizeName,
		IReadOnlyList<string> edgeNames)
	{
		var owners = new List<DomElementRuntimeProperty>();
		var size = Runtime(sizeName);
		if (size is null
			|| !TryReadXamlNumber(size, out var total))
			return;
		owners.Add(size);
		foreach (var edgeName in edgeNames)
		{
			var edge = Runtime(edgeName);
			if (edge is null || !TryReadXamlNumber(edge, out var value))
				return;
			owners.Add(edge);
			total += value;
		}
		attributes[targetName] = new(
			targetName,
			total.ToString("R", CultureInfo.InvariantCulture),
			size,
			owners);
	}

	private static bool TryReadXamlNumber(
		DomElementRuntimeProperty property,
		out double value) =>
		double.TryParse(
			NormalizeXamlLength(ActiveValue(property)),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out value);

	private void AddLayoutValue(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourceName,
		string targetName,
		Func<string, string?>? convert = null)
	{
		var property = Runtime(sourceName);
		if (property is null)
			return;
		var value = ActiveValue(property);
		if (value is not null && convert is not null)
			value = convert(value);
		SetXamlAttribute(attributes, targetName, value, property);
	}

	private static string? NormalizeFontStretch(string value) =>
		value.Trim().ToLowerInvariant() switch
		{
			"ultra-condensed" => "UltraCondensed",
			"extra-condensed" => "ExtraCondensed",
			"condensed" => "Condensed",
			"semi-condensed" => "SemiCondensed",
			"normal" => "Normal",
			"semi-expanded" => "SemiExpanded",
			"expanded" => "Expanded",
			"extra-expanded" => "ExtraExpanded",
			"ultra-expanded" => "UltraExpanded",
			_ => null
		};

	private static string? NormalizeTextAlignmentValue(string value) =>
		value.Trim().ToLowerInvariant() switch
		{
			"left" or "start" => "Left",
			"right" or "end" => "Right",
			"center" => "Center",
			"justify" or "justify-all" => "Justify",
			_ => null
		};

	private static string NormalizeTextDecorations(string value)
	{
		var normalized = value.Trim().ToLowerInvariant();
		var values = new List<string>();
		if (normalized.Contains("underline", StringComparison.Ordinal))
			values.Add("Underline");
		if (normalized.Contains("line-through", StringComparison.Ordinal))
			values.Add("Strikethrough");
		return values.Count == 0 ? "None" : string.Join(", ", values);
	}

	protected override bool TryBuildXamlFlexLayout(
		out bool vertical,
		out IReadOnlyList<XamlGridTrackDefinition> definitions)
	{
		var displayValue = RuntimeValue(this, "style.display");
		var wrapValue = RuntimeValue(this, "style.flexWrap") ?? "nowrap";
		if (displayValue is not ("flex" or "inline-flex")
			|| !wrapValue.Equals("nowrap", StringComparison.OrdinalIgnoreCase))
		{
			vertical = false;
			definitions = [];
			return false;
		}
		vertical = RuntimeValue(this, "style.flexDirection")?.StartsWith(
				"column",
				StringComparison.OrdinalIgnoreCase) == true;
		definitions = BuildStrongNoWrapFlexTracks(
			OrderedFlexChildren(),
			vertical);
		return definitions.Count > 0;
	}

	protected override bool TryBuildXamlGridLayout(
		out IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
		out IReadOnlyList<XamlGridTrackDefinition> columnDefinitions)
	{
		if (!TryBuildStrongGridPlan(out var plan))
		{
			rowDefinitions = [];
			columnDefinitions = [];
			return false;
		}
		rowDefinitions = plan.Rows;
		columnDefinitions = plan.Columns;
		return rowDefinitions.Count + columnDefinitions.Count > 0;
	}

	protected override bool TryBuildXamlBlockLayout(
		out IReadOnlyList<XamlGridTrackDefinition> definitions)
	{
		if (CreateXaml().Kind != XamlElementMappingKind.BlockFlow)
		{
			definitions = [];
			return false;
		}
		var tracks = BuildStrongBlockTracks(
			Children.Where(ParticipatesInNormalFlow).ToArray());
		if (!string.IsNullOrWhiteSpace(
			RuntimeInitialization("content.ownText")))
		{
			var sourceInsertionIndex = Math.Clamp(
				CapturedOwnTextElementInsertionIndex,
				0,
				Children.Count);
			var normalFlowInsertionIndex = Children
				.Take(sourceInsertionIndex)
				.Count(ParticipatesInNormalFlow);
			tracks.Insert(
				normalFlowInsertionIndex,
				new("Auto", null, null));
		}
		definitions = tracks;
		return definitions.Count > 0;
	}

	private static List<XamlGridTrackDefinition> BuildStrongBlockTracks(
		IReadOnlyList<DomElement> children)
	{
		var tracks = new List<XamlGridTrackDefinition>(children.Count);
		for (var index = 0; index < children.Count; index++)
		{
			var child = children[index];
			var currentBottom = ResolveBlockMargin(child, "style.marginBottom");
			var nextTop = index + 1 < children.Count
				? ResolveBlockMargin(children[index + 1], "style.marginTop")
				: 0;
			// WinUI keeps each child's declared Margin as runtime evidence. A Grid
			// row therefore needs only the part of the CSS collapsed adjoining
			// margin that is not already contributed by the next child's top
			// margin. This preserves CSS block-flow positions without replacing
			// the authored per-element margin values.
			var trailingAllocation = CollapseAdjacentBlockMargins(
				currentBottom,
				nextTop) - nextTop;
			tracks.Add(ResolveBlockTrack(child, trailingAllocation));
		}
		return tracks;
	}

	private static double ResolveBlockMargin(
		DomElement child,
		string propertyName)
	{
		var properties = RuntimePropertiesFor(child).ToDictionary(
			static property => property.Name,
			StringComparer.Ordinal);
		return ResolveBoxEdge(properties, propertyName, "style.margin");
	}

	private static double CollapseAdjacentBlockMargins(double first, double second)
	{
		if (first >= 0 && second >= 0)
			return Math.Max(first, second);
		if (first <= 0 && second <= 0)
			return Math.Min(first, second);
		return first + second;
	}

	private IReadOnlyList<XamlGridTrackDefinition> ParseGridTracks(
		string? expression,
		bool vertical)
	{
		if (string.IsNullOrWhiteSpace(expression)
			|| expression.Equals("none", StringComparison.OrdinalIgnoreCase))
		{
			return [];
		}
		var expanded = ExpandGridRepeats(expression, vertical);
		var tracks = new List<XamlGridTrackDefinition>();
		foreach (var token in SplitGridTrackTokens(expanded))
		{
			if (token.StartsWith("[", StringComparison.Ordinal)
				&& token.EndsWith("]", StringComparison.Ordinal))
			{
				continue;
			}
			if (token.StartsWith("minmax(", StringComparison.OrdinalIgnoreCase)
				&& token.EndsWith(")", StringComparison.Ordinal))
			{
				var parts = token[7..^1].Split(
					',',
					StringSplitOptions.TrimEntries);
				if (parts.Length == 2)
				{
					tracks.Add(new(
						NormalizeGridTrackLength(parts[1]),
						NormalizeXamlLength(parts[0]),
						NormalizeGridTrackLength(parts[1])));
					continue;
				}
			}
			tracks.Add(new(
				NormalizeGridTrackLength(token),
				null,
				null));
		}
		return tracks;
	}

	private string ExpandGridRepeats(string expression, bool vertical)
	{
		var result = new StringBuilder(expression.Length);
		for (var index = 0; index < expression.Length;)
		{
			if (!expression.AsSpan(index).StartsWith("repeat(", StringComparison.OrdinalIgnoreCase))
			{
				result.Append(expression[index++]);
				continue;
			}

			var close = FindMatchingGridParenthesis(expression, index + 6);
			if (close < 0)
				throw new InvalidOperationException($"Unbalanced CSS grid repeat(): {expression}");
			var argument = expression[(index + 7)..close];
			var comma = FindTopLevelGridComma(argument);
			if (comma < 0)
				throw new InvalidOperationException($"Invalid CSS grid repeat(): {expression[index..(close + 1)]}");
			var countText = argument[..comma].Trim();
			int count;
			if (countText is "auto-fill" or "auto-fit")
			{
				count = ResolveAutomaticGridRepeatCount(
					argument[(comma + 1)..].Trim(),
					vertical,
					countText == "auto-fit");
			}
			else if (!int.TryParse(
				countText,
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out count)
				|| count <= 0)
			{
				throw new InvalidOperationException(
					$"Invalid CSS grid repeat count '{countText}'.");
			}
			var body = ExpandGridRepeats(
				argument[(comma + 1)..].Trim(),
				vertical);
			result.Append(string.Join(' ', Enumerable.Repeat(body, count)));
			index = close + 1;
		}
		return result.ToString();
	}

	private int ResolveAutomaticGridRepeatCount(
		string body,
		bool vertical,
		bool fitToItems)
	{
		var available = ParseGridPixels(ActiveValue(Runtime(
			vertical ? "rect.height" : "rect.width")));
		var gap = ParseGridPixels(ActiveValue(Runtime(
			vertical ? "style.rowGap" : "style.columnGap")));
		var trackMinimums = SplitGridTrackTokens(body)
			.Where(static token => !token.StartsWith("[", StringComparison.Ordinal))
			.Select(ParseGridTrackMinimum)
			.ToArray();
		if (available <= 0 || trackMinimums.Length == 0
			|| trackMinimums.Any(static minimum => minimum <= 0))
		{
			throw new NotSupportedException(
				$"CSS grid repeat(auto-*, {body}) requires positive runtime geometry and fixed/minmax minimum track lengths.");
		}
		var groupExtent = trackMinimums.Sum() + gap * trackMinimums.Length;
		var count = Math.Max(1, (int)Math.Floor((available + gap) / groupExtent));
		if (fitToItems)
		{
			var itemCount = Children.Count(ParticipatesInNormalFlow);
			var requiredGroups = Math.Max(
				1,
				(int)Math.Ceiling(itemCount / (double)trackMinimums.Length));
			count = Math.Min(count, requiredGroups);
		}
		return count;
	}

	private static double ParseGridTrackMinimum(string token)
	{
		if (token.StartsWith("minmax(", StringComparison.OrdinalIgnoreCase)
			&& token.EndsWith(")", StringComparison.Ordinal))
		{
			var argument = token[7..^1];
			var comma = FindTopLevelGridComma(argument);
			return comma < 0 ? 0 : ParseGridPixels(argument[..comma]);
		}
		return ParseGridPixels(token);
	}

	private static double ParseGridPixels(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return 0;
		var candidate = value.Trim();
		if (candidate.EndsWith("px", StringComparison.OrdinalIgnoreCase))
			candidate = candidate[..^2];
		return double.TryParse(
			candidate,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var pixels)
			&& double.IsFinite(pixels)
			? Math.Max(0, pixels)
			: 0;
	}

	private static int FindMatchingGridParenthesis(string expression, int openIndex)
	{
		var depth = 0;
		for (var index = openIndex; index < expression.Length; index++)
		{
			if (expression[index] == '(')
				depth++;
			else if (expression[index] == ')' && --depth == 0)
				return index;
		}
		return -1;
	}

	private static int FindTopLevelGridComma(string expression)
	{
		var depth = 0;
		for (var index = 0; index < expression.Length; index++)
		{
			if (expression[index] == '(')
				depth++;
			else if (expression[index] == ')')
				depth--;
			else if (expression[index] == ',' && depth == 0)
				return index;
		}
		return -1;
	}

	private static IEnumerable<string> SplitGridTrackTokens(string expression)
	{
		var start = 0;
		var depth = 0;
		var bracketDepth = 0;
		for (var index = 0; index < expression.Length; index++)
		{
			var character = expression[index];
			if (character == '(')
				depth++;
			else if (character == ')')
				depth = Math.Max(0, depth - 1);
			else if (character == '[')
				bracketDepth++;
			else if (character == ']')
				bracketDepth = Math.Max(0, bracketDepth - 1);
			else if (char.IsWhiteSpace(character)
				&& depth == 0
				&& bracketDepth == 0)
			{
				if (index > start)
					yield return expression[start..index].Trim();
				start = index + 1;
			}
		}
		if (start < expression.Length)
			yield return expression[start..].Trim();
	}

	private static string NormalizeGridTrackLength(string value)
	{
		var normalized = value.Trim();
		if (normalized.Equals("auto", StringComparison.OrdinalIgnoreCase)
			|| normalized.Contains(
				"content",
				StringComparison.OrdinalIgnoreCase))
		{
			return "Auto";
		}
		if (normalized.EndsWith("fr", StringComparison.OrdinalIgnoreCase)
			&& double.TryParse(
				normalized[..^2],
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var fraction)
			&& fraction > 0)
		{
			return fraction == 1
				? "*"
				: $"{fraction.ToString("R", CultureInfo.InvariantCulture)}*";
		}
		return NormalizeXamlLength(normalized) ?? "Auto";
	}

	protected override bool TryResolveXamlLayoutTrackIndex(
		DomElement child,
		out bool vertical,
		out int index)
	{
		if (TryBuildXamlFlexLayout(out vertical, out var flexDefinitions))
		{
			var ordered = OrderedFlexChildren();
			var sourceIndex = Array.IndexOf(ordered, child);
			if (sourceIndex < 0)
			{
				index = -1;
				return false;
			}
			index = IsReverseFlex()
				? flexDefinitions.Count - sourceIndex - 1
				: sourceIndex;
			return true;
		}
		vertical = true;
		if (!TryBuildXamlBlockLayout(out _))
		{
			index = -1;
			return false;
		}
		var normalFlow = Children
			.Where(ParticipatesInNormalFlow)
			.ToArray();
		index = Array.IndexOf(normalFlow, child);
		return index >= 0;
	}

	private void AddColor(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourceName,
		string targetName)
	{
		var property = Runtime(sourceName);
		SetXamlAttribute(
			attributes,
			targetName,
			property is null ? null : NormalizeXamlColor(ActiveValue(property)),
			property);
	}

	private void AddLength(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourceName,
		string targetName)
	{
		var property = Runtime(sourceName);
		SetXamlAttribute(
			attributes,
			targetName,
			property is null ? null : NormalizeXamlLength(ActiveValue(property)),
			property);
	}

	private void AddThickness(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourcePrefix,
		string targetName,
		string sourceSuffix = "")
	{
		var top = Runtime(sourcePrefix + "Top" + sourceSuffix);
		var right = Runtime(sourcePrefix + "Right" + sourceSuffix);
		var bottom = Runtime(sourcePrefix + "Bottom" + sourceSuffix);
		var left = Runtime(sourcePrefix + "Left" + sourceSuffix);
		if (top is null || right is null || bottom is null || left is null)
			return;
		var values = new[]
		{
			NormalizeXamlLength(ActiveValue(left)),
			NormalizeXamlLength(ActiveValue(top)),
			NormalizeXamlLength(ActiveValue(right)),
			NormalizeXamlLength(ActiveValue(bottom))
		};
		if (values.Any(static value => value is null))
			return;
		SetCompositeXamlAttribute(
			attributes,
			targetName,
			string.Join(",", values),
			[left, top, right, bottom],
			XamlCompositeValueKind.Thickness);
	}

	private void AddCornerRadius(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		var topLeft = Runtime("style.borderTopLeftRadius");
		var topRight = Runtime("style.borderTopRightRadius");
		var bottomRight = Runtime("style.borderBottomRightRadius");
		var bottomLeft = Runtime("style.borderBottomLeftRadius");
		if (topLeft is null
			|| topRight is null
			|| bottomRight is null
			|| bottomLeft is null)
		{
			return;
		}
		var values = new[]
		{
			NormalizeXamlLength(ActiveValue(topLeft)),
			NormalizeXamlLength(ActiveValue(topRight)),
			NormalizeXamlLength(ActiveValue(bottomRight)),
			NormalizeXamlLength(ActiveValue(bottomLeft))
		};
		if (values.Any(static value => value is null))
			return;
		SetCompositeXamlAttribute(
			attributes,
			"CornerRadius",
			string.Join(",", values),
			[topLeft, topRight, bottomRight, bottomLeft],
			XamlCompositeValueKind.CornerRadius);
	}

	private void AddUniformBorderColor(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		var top = Runtime("style.borderTopColor");
		var right = Runtime("style.borderRightColor");
		var bottom = Runtime("style.borderBottomColor");
		var left = Runtime("style.borderLeftColor");
		if (top is null || right is null || bottom is null || left is null)
			return;
		var colors = new[]
		{
			NormalizeXamlColor(ActiveValue(top)),
			NormalizeXamlColor(ActiveValue(right)),
			NormalizeXamlColor(ActiveValue(bottom)),
			NormalizeXamlColor(ActiveValue(left))
		};
		if (colors.Any(static value => value is null)
			|| colors.Skip(1).Any(value =>
				!string.Equals(value, colors[0], StringComparison.Ordinal)))
		{
			return;
		}
		SetCompositeXamlAttribute(
			attributes,
			"BorderBrush",
			colors[0],
			[top, right, bottom, left],
			XamlCompositeValueKind.UniformValue);
	}

	private void RemoveDimensionsOwnedByLayout(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		if (Parent is null)
		{
			attributes.Remove("Width");
			attributes.Remove("Height");
			return;
		}
		if (TryGetParentXamlFlexLayout(out var vertical, out _))
		{
			var axis = vertical ? "Height" : "Width";
			attributes.Remove(axis);
			attributes.Remove("Min" + axis);
			attributes.Remove("Max" + axis);
		}
		RemoveDimensionEqualToParent(attributes, "Width", "style.width");
		RemoveDimensionEqualToParent(attributes, "Height", "style.height");
	}

	protected override bool ShouldEmitXamlRuntimeProperty(
		DomElementRuntimeProperty property)
	{
		var targetName = property.XamlExecution.MarkupAttributeName;
		if (targetName is not ("Width" or "Height" or "MinWidth"
			or "MinHeight" or "MaxWidth" or "MaxHeight"))
		{
			return base.ShouldEmitXamlRuntimeProperty(property);
		}
		if (Parent is null && targetName is "Width" or "Height")
			return false;
		if (TryGetParentXamlFlexLayout(out var vertical, out _))
		{
			var axis = vertical ? "Height" : "Width";
			if (targetName == axis
				|| targetName == "Min" + axis
				|| targetName == "Max" + axis)
			{
				return false;
			}
		}
		if (targetName is "Width" or "Height"
			&& IsRuntimeDimensionEqualToParent(property.Name))
		{
			return false;
		}
		return base.ShouldEmitXamlRuntimeProperty(property);
	}

	private bool IsRuntimeDimensionEqualToParent(string sourceName)
	{
		if (Parent is not HtmlDomElementDefinition parent)
			return false;
		var current = Runtime(sourceName);
		var parentProperty = parent.RuntimeProperties.FirstOrDefault(
			property => property.Name == sourceName)
			?? parent.HtmlRoot?.ResolveGlobalStyleProperty(parent, sourceName);
		var currentValue = current is null
			? null : NormalizeXamlLength(ActiveValue(current));
		var parentValue = parentProperty is null
			? null : NormalizeXamlLength(ActiveValue(parentProperty));
		return double.TryParse(
				currentValue,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var currentNumber)
			&& double.TryParse(
				parentValue,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var parentNumber)
			&& Math.Abs(currentNumber - parentNumber) <= .5;
	}

	private void RemoveDimensionEqualToParent(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string targetName,
		string sourceName)
	{
		if (Parent is not HtmlDomElementDefinition parent)
			return;
		var current = Runtime(sourceName);
		var parentProperty = parent.RuntimeProperties.FirstOrDefault(
			property => property.Name == sourceName)
			?? parent.HtmlRoot?.ResolveGlobalStyleProperty(parent, sourceName);
		var currentValue = current is null
			? null : NormalizeXamlLength(ActiveValue(current));
		var parentValue = parentProperty is null
			? null : NormalizeXamlLength(ActiveValue(parentProperty));
		if (double.TryParse(currentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
			&& double.TryParse(parentValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
			&& Math.Abs(a - b) <= .5)
		{
			attributes.Remove(targetName);
		}
	}

	private DomElementRuntimeProperty? Runtime(string name) =>
		RuntimeProperties.FirstOrDefault(property => property.Name == name)
		?? HtmlRoot?.ResolveGlobalStyleProperty(this, name);

	private static string? RuntimeValue(DomElement element, string name)
	{
		if (name.StartsWith("style.", StringComparison.Ordinal)
			|| name.StartsWith("effect.", StringComparison.Ordinal))
		{
			var global = (element.HtmlRoot?.ResolveGlobalStyleValue(
					element,
					name,
					DomPropertyDataSlot.Runtime)
				?? element.HtmlRoot?.ResolveGlobalStyleValue(
					element,
					name,
					DomPropertyDataSlot.Initialization))?
				.Trim()
				.ToLowerInvariant();
			if (!string.IsNullOrWhiteSpace(global))
				return global;
		}
		var property = RuntimePropertiesFor(element)
			.FirstOrDefault(candidate => candidate.Name == name);
		return ActiveValue(property)?.ToLowerInvariant();
	}

	private static IReadOnlyList<DomElementRuntimeProperty> RuntimePropertiesFor(
		DomElement element)
	{
		var properties = (element switch
		{
			HtmlDomElementDefinition html => html.RuntimeProperties,
			SvgDomElementDefinition svg => svg.RuntimeProperties,
			_ => []
		}).ToDictionary(
			static property => property.Name,
			StringComparer.Ordinal);
		if (element.HtmlRoot is not { } root)
			return properties.Values.ToArray();
		foreach (var name in LayoutTrackPropertyNames)
		{
			if (!properties.ContainsKey(name)
				&& root.ResolveGlobalStyleProperty(element, name) is { } property)
			{
				properties.Add(name, property);
			}
		}
		return properties.Values.ToArray();
	}

	private static readonly string[] LayoutTrackPropertyNames =
	[
		"style.boxSizing",
		"style.flexGrow",
		"style.width",
		"style.height",
		"style.minWidth",
		"style.maxWidth",
		"style.minHeight",
		"style.maxHeight",
		"style.padding",
		"style.paddingTop",
		"style.paddingRight",
		"style.paddingBottom",
		"style.paddingLeft",
		"style.margin",
		"style.marginTop",
		"style.marginRight",
		"style.marginBottom",
		"style.marginLeft",
		"style.border",
		"style.borderTopWidth",
		"style.borderRightWidth",
		"style.borderBottomWidth",
		"style.borderLeftWidth"
	];

	private static string? ActiveValue(
		DomElementRuntimeProperty? property)
	{
		if (property?.SourceRuntime.IsSet == true)
			return property.SourceRuntime.Value?.Trim();
		return property?.SourceInitialization.IsSet == true
			? property.SourceInitialization.Value?.Trim()
			: null;
	}

	private static bool IsOutOfFlow(DomElement child) =>
		RuntimeValue(child, "style.position")
			is "absolute" or "fixed";

	private static bool ParticipatesInNormalFlow(DomElement child) =>
		!IsOutOfFlow(child)
		&& !string.Equals(
			RuntimeValue(child, "style.display"),
			"none",
			StringComparison.OrdinalIgnoreCase);

	private DomElement[] OrderedFlexChildren() =>
		Children
			.Where(ParticipatesInNormalFlow)
			.Select(static (child, sourceIndex) => new
			{
				Child = child,
				SourceIndex = sourceIndex,
				Order = ResolveFlexOrder(child)
			})
			.OrderBy(static item => item.Order)
			.ThenBy(static item => item.SourceIndex)
			.Select(static item => item.Child)
			.ToArray();

	private bool IsReverseFlex() =>
		RuntimeValue(this, "style.flexDirection")
			?.EndsWith("-reverse", StringComparison.OrdinalIgnoreCase) == true;

	private static int ResolveFlexOrder(DomElement child) =>
		int.TryParse(
			RuntimeValue(child, "style.order"),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var order)
				? order
				: 0;

	private static bool IsNormalBlockChild(DomElement child)
	{
		if (IsOutOfFlow(child))
			return false;
		var display = RuntimeValue(child, "style.display");
		return display is null
			|| display is "block" or "flow-root" or "list-item"
				or "flex" or "grid" or "table";
	}

	private static bool IsScrollable(DomElement element) =>
		RuntimeValue(element, "style.overflowX") is "auto" or "scroll"
		|| RuntimeValue(element, "style.overflowY") is "auto" or "scroll";

	private static bool HasOwnText(DomElement element) =>
		!string.IsNullOrWhiteSpace(element.CapturedOwnText);

	private static XamlGridTrackDefinition ResolveFlexTrack(
		DomElement child,
		bool vertical)
	{
		var runtime = RuntimePropertiesFor(child).ToDictionary(
			static property => property.Name,
			StringComparer.Ordinal);
		if (runtime.Count == 0)
			return new("Auto", null, null);
		var length = "Auto";
		if (runtime.TryGetValue("style.flexGrow", out var grow)
			&& double.TryParse(
				ActiveValue(grow),
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var growth)
			&& growth > 0)
		{
			length = growth == 1
				? "*"
				: $"{growth.ToString("R", CultureInfo.InvariantCulture)}*";
		}
		else if (runtime.TryGetValue(
			vertical ? "style.height" : "style.width",
			out var size))
		{
			length = ResolveOuterBoxTrackLength(runtime, size, vertical)
				?? "Auto";
		}
		var axis = vertical ? "Height" : "Width";
		var minimum = runtime.TryGetValue($"style.min{axis}", out var min)
			? NormalizeXamlLength(ActiveValue(min))
			: null;
		var maximum = runtime.TryGetValue($"style.max{axis}", out var max)
			? NormalizeXamlLength(ActiveValue(max))
			: null;
		return new(length, minimum, maximum);
	}

	private static XamlGridTrackDefinition ResolveBlockTrack(
		DomElement child,
		double trailingMarginAllocation)
	{
		var runtime = RuntimePropertiesFor(child).ToDictionary(
			static property => property.Name,
			StringComparer.Ordinal);
		if (runtime.Count == 0)
			return new("Auto", null, null);
		var length = "Auto";
		if (runtime.TryGetValue("style.height", out var height))
		{
			var active = ActiveValue(height);
			if (active?.EndsWith('%') == true
				&& double.TryParse(
					active[..^1],
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out var percentage)
				&& percentage > 0)
			{
				length = $"{percentage.ToString("R", CultureInfo.InvariantCulture)}*";
			}
			else if (IsScrollable(child.Parent ?? child)
				&& NormalizeXamlLength(active) is { } runtimeLength)
			{
				length = runtimeLength;
			}
			else
			{
				length = NormalizeXamlLength(active)
					?? "Auto";
			}
		}
		if (trailingMarginAllocation != 0
			&& double.TryParse(
				length,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var concreteLength))
		{
			length = Math.Max(0, concreteLength + trailingMarginAllocation)
				.ToString("R", CultureInfo.InvariantCulture);
		}
		var minimum = runtime.TryGetValue("style.minHeight", out var min)
			? NormalizeXamlLength(ActiveValue(min))
			: null;
		var maximum = runtime.TryGetValue("style.maxHeight", out var max)
			? NormalizeXamlLength(ActiveValue(max))
			: null;
		return new(length, minimum, maximum);
	}

	private static string? ResolveOuterBoxTrackLength(
		IReadOnlyDictionary<string, DomElementRuntimeProperty> runtime,
		DomElementRuntimeProperty size,
		bool vertical)
	{
		var normalized = NormalizeXamlLength(ActiveValue(size));
		if (normalized is null
			|| !double.TryParse(
				normalized,
				NumberStyles.Float,
				CultureInfo.InvariantCulture,
				out var contentLength)
			|| ActiveValue(runtime.GetValueOrDefault("style.boxSizing"))
				?.Equals("border-box", StringComparison.OrdinalIgnoreCase) == true)
		{
			return normalized;
		}
		var leadingPadding = ResolveBoxEdge(
			runtime,
			vertical ? "style.paddingTop" : "style.paddingLeft",
			"style.padding");
		var trailingPadding = ResolveBoxEdge(
			runtime,
			vertical ? "style.paddingBottom" : "style.paddingRight",
			"style.padding");
		var leadingBorder = ResolveBoxEdge(
			runtime,
			vertical ? "style.borderTopWidth" : "style.borderLeftWidth",
			"style.border");
		var trailingBorder = ResolveBoxEdge(
			runtime,
			vertical ? "style.borderBottomWidth" : "style.borderRightWidth",
			"style.border");
		return (contentLength
			+ leadingPadding
			+ trailingPadding
			+ leadingBorder
			+ trailingBorder).ToString("R", CultureInfo.InvariantCulture);
	}

	private static double ResolveBoxEdge(
		IReadOnlyDictionary<string, DomElementRuntimeProperty> runtime,
		string edgeName,
		string shorthandName)
	{
		if (TryCssPixelValue(runtime.GetValueOrDefault(edgeName), out var edge))
			return edge;
		return TryCssPixelValue(
			runtime.GetValueOrDefault(shorthandName),
			out var shorthand)
				? shorthand
				: 0;
	}

	private static bool TryCssPixelValue(
		DomElementRuntimeProperty? property,
		out double value)
	{
		value = 0;
		var raw = ActiveValue(property);
		if (string.IsNullOrWhiteSpace(raw))
			return false;
		var token = raw.Trim().Split(
			[' ', ','],
			StringSplitOptions.RemoveEmptyEntries)[0];
		if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase))
			token = token[..^2];
		return double.TryParse(
			token,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out value);
	}
}

public abstract class HtmlTextVisualDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementCategory category,
	ElementVisualKind visualKind,
	ElementContentModel contentModel,
	ElementClosure closure,
	ElementSyntax syntax,
	XamlConversionSupport xamlSupport,
	XamlControlFamily controlFamily,
	ElementDefaultDisplay defaultDisplay,
	ElementInteractionKind interactionKind,
	ElementXamlChildPlacementKind childPlacement) :
	HtmlDomElementDefinition(
		mapping,
		tagName,
		category,
		visualKind,
		contentModel,
		closure,
		syntax,
		xamlSupport,
		controlFamily,
		defaultDisplay,
		interactionKind,
		childPlacement)
{
	public override DomElementQuerySpecialization QuerySpecialization =>
		DomElementQuerySpecialization.Text;

	protected bool TryBuildStrongTextVisualFlexLayout(
		out bool vertical,
		out IReadOnlyList<XamlGridTrackDefinition> definitions)
	{
		vertical = StrongFlexValue(this, "style.flexDirection")
			?.StartsWith("column", StringComparison.OrdinalIgnoreCase) == true;
		var verticalAxis = vertical;
		var children = OrderedStrongFlexChildren();
		if (children.Length == 0)
		{
			definitions = [];
			return false;
		}
		var extent = StrongFlexLength(StrongFlexValue(
			this,
			verticalAxis ? "style.height" : "style.width"));
		if (string.Equals(
			StrongFlexValue(this, "style.boxSizing"),
			"border-box",
			StringComparison.OrdinalIgnoreCase))
		{
			extent = Math.Max(
				0,
				extent
					- StrongFlexEdge(this, verticalAxis ? "style.paddingTop" : "style.paddingLeft")
					- StrongFlexEdge(this, verticalAxis ? "style.paddingBottom" : "style.paddingRight")
					- StrongFlexEdge(this, verticalAxis ? "style.borderTopWidth" : "style.borderLeftWidth")
					- StrongFlexEdge(this, verticalAxis ? "style.borderBottomWidth" : "style.borderRightWidth"));
		}
		var gap = StrongFlexLength(StrongFlexValue(
			this,
			verticalAxis ? "style.rowGap" : "style.columnGap"));
		var bases = children.Select(child => StrongFlexBase(child, verticalAxis)).ToArray();
		var margins = children.Select(child =>
			StrongFlexEdge(child, verticalAxis ? "style.marginTop" : "style.marginLeft")
			+ StrongFlexEdge(child, verticalAxis ? "style.marginBottom" : "style.marginRight"))
			.ToArray();
		var grow = children.Select(child => Math.Max(
			0,
			StrongFlexNumber(StrongFlexValue(child, "style.flexGrow"), 0))).ToArray();
		var shrink = children.Select(child => Math.Max(
			0,
			StrongFlexNumber(StrongFlexValue(child, "style.flexShrink"), 1))).ToArray();
		var lengths = bases.ToArray();
		if (extent > 0)
		{
			var available = Math.Max(
				0,
				extent - gap * Math.Max(0, children.Length - 1) - margins.Sum());
			var free = available - bases.Sum();
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
		}
		for (var index = 0; index < lengths.Length; index++)
		{
			lengths[index] += margins[index];
			if (index + 1 < lengths.Length)
				lengths[index] += gap;
		}
		definitions = children.Select((child, index) =>
		{
			var axis = verticalAxis ? "Height" : "Width";
			return new XamlGridTrackDefinition(
				lengths[index].ToString("R", CultureInfo.InvariantCulture),
				NormalizeXamlLength(StrongFlexValue(child, $"style.min{axis}")),
				NormalizeXamlLength(StrongFlexValue(child, $"style.max{axis}")));
		}).ToArray();
		return true;
	}

	protected bool TryResolveStrongTextVisualFlexTrackIndex(
		DomElement child,
		out bool vertical,
		out int index)
	{
		if (!TryBuildStrongTextVisualFlexLayout(out vertical, out var definitions))
		{
			index = -1;
			return false;
		}
		var ordered = OrderedStrongFlexChildren();
		index = Array.IndexOf(ordered, child);
		if (index < 0 || index >= definitions.Count)
			return false;
		if (StrongFlexValue(this, "style.flexDirection")
			?.EndsWith("-reverse", StringComparison.OrdinalIgnoreCase) == true)
		{
			index = definitions.Count - index - 1;
		}
		return true;
	}

	private DomElement[] OrderedStrongFlexChildren() =>
		Children
			.Where(static child => StrongFlexValue(child, "style.position")
				is not ("absolute" or "fixed")
				&& !string.Equals(
					StrongFlexValue(child, "style.display"),
					"none",
					StringComparison.OrdinalIgnoreCase))
			.Select(static (child, sourceIndex) => new
			{
				Child = child,
				SourceIndex = sourceIndex,
				Order = StrongFlexNumber(StrongFlexValue(child, "style.order"), 0)
			})
			.OrderBy(static item => item.Order)
			.ThenBy(static item => item.SourceIndex)
			.Select(static item => item.Child)
			.ToArray();

	private static double StrongFlexBase(DomElement child, bool vertical)
	{
		var basis = StrongFlexValue(child, "style.flexBasis");
		var result = !string.IsNullOrWhiteSpace(basis)
			&& basis is not ("auto" or "content")
				? StrongFlexLength(basis)
				: StrongFlexLength(StrongFlexValue(
					child,
					vertical ? "style.height" : "style.width"));
		if (string.Equals(
			StrongFlexValue(child, "style.boxSizing"),
			"border-box",
			StringComparison.OrdinalIgnoreCase))
			return result;
		return result
			+ StrongFlexEdge(child, vertical ? "style.paddingTop" : "style.paddingLeft")
			+ StrongFlexEdge(child, vertical ? "style.paddingBottom" : "style.paddingRight")
			+ StrongFlexEdge(child, vertical ? "style.borderTopWidth" : "style.borderLeftWidth")
			+ StrongFlexEdge(child, vertical ? "style.borderBottomWidth" : "style.borderRightWidth");
	}

	private static double StrongFlexEdge(DomElement element, string name) =>
		StrongFlexLength(StrongFlexValue(element, name));

	private static double StrongFlexLength(string? value)
	{
		var token = value?.Trim() ?? string.Empty;
		if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase))
			token = token[..^2];
		return StrongFlexNumber(token, 0);
	}

	private static double StrongFlexNumber(string? value, double fallback) =>
		double.TryParse(
			value,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var result)
			? result
			: fallback;

	private static string? StrongFlexValue(DomElement element, string name)
	{
		var global = element.HtmlRoot?.ResolveGlobalStyleValue(
				element,
				name,
				DomPropertyDataSlot.Runtime)
			?? element.HtmlRoot?.ResolveGlobalStyleValue(
				element,
				name,
				DomPropertyDataSlot.Initialization);
		if (!string.IsNullOrWhiteSpace(global))
			return global.Trim();
		var property = element switch
		{
			HtmlDomElementDefinition html => html.RuntimeProperties.FirstOrDefault(
				candidate => candidate.Name.Equals(name, StringComparison.Ordinal)),
			SvgDomElementDefinition svg => svg.RuntimeProperties.FirstOrDefault(
				candidate => candidate.Name.Equals(name, StringComparison.Ordinal)),
			_ => null
		};
		return property?.SourceRuntime.IsSet == true
			? property.SourceRuntime.Value?.Trim()
			: property?.SourceInitialization.IsSet == true
				? property.SourceInitialization.Value?.Trim()
				: null;
	}

	protected void AddStrongTextVisualControlBoxAttributes(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		AddStrongControlColor(attributes, "style.backgroundColor", "Background");
		AddStrongControlThickness(attributes, "style.margin", "Margin");
		AddStrongControlThickness(attributes, "style.padding", "Padding");
		AddStrongControlThickness(
			attributes,
			"style.border",
			"BorderThickness",
			"Width");
		AddStrongControlRadius(attributes);
		AddStrongControlBorderColor(attributes);
		ExpandStrongControlContentBox(attributes, "Width", "style.width",
			["style.paddingLeft", "style.paddingRight", "style.borderLeftWidth", "style.borderRightWidth"]);
		ExpandStrongControlContentBox(attributes, "Height", "style.height",
			["style.paddingTop", "style.paddingBottom", "style.borderTopWidth", "style.borderBottomWidth"]);
	}

	private void AddStrongControlColor(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourceName,
		string targetName)
	{
		var source = RuntimeProperty(sourceName);
		SetXamlAttribute(
			attributes,
			targetName,
			source is null ? null : NormalizeXamlColor(StrongControlValue(source)),
			source);
	}

	private void AddStrongControlThickness(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string prefix,
		string targetName,
		string suffix = "")
	{
		var top = RuntimeProperty(prefix + "Top" + suffix);
		var right = RuntimeProperty(prefix + "Right" + suffix);
		var bottom = RuntimeProperty(prefix + "Bottom" + suffix);
		var left = RuntimeProperty(prefix + "Left" + suffix);
		if (top is null || right is null || bottom is null || left is null)
			return;
		var values = new[]
		{
			NormalizeXamlLength(StrongControlValue(left)),
			NormalizeXamlLength(StrongControlValue(top)),
			NormalizeXamlLength(StrongControlValue(right)),
			NormalizeXamlLength(StrongControlValue(bottom))
		};
		if (values.Any(static value => value is null))
			return;
		SetCompositeXamlAttribute(
			attributes,
			targetName,
			string.Join(",", values),
			[left, top, right, bottom],
			XamlCompositeValueKind.Thickness);
	}

	private void AddStrongControlRadius(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		var sources = new[]
		{
			RuntimeProperty("style.borderTopLeftRadius"),
			RuntimeProperty("style.borderTopRightRadius"),
			RuntimeProperty("style.borderBottomRightRadius"),
			RuntimeProperty("style.borderBottomLeftRadius")
		};
		if (sources.Any(static source => source is null))
			return;
		var owners = sources.Cast<DomElementRuntimeProperty>().ToArray();
		var values = owners.Select(source =>
			NormalizeXamlLength(StrongControlValue(source))).ToArray();
		if (values.Any(static value => value is null))
			return;
		SetCompositeXamlAttribute(
			attributes,
			"CornerRadius",
			string.Join(",", values),
			owners,
			XamlCompositeValueKind.CornerRadius);
	}

	private void AddStrongControlBorderColor(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		var sources = new[]
		{
			RuntimeProperty("style.borderTopColor"),
			RuntimeProperty("style.borderRightColor"),
			RuntimeProperty("style.borderBottomColor"),
			RuntimeProperty("style.borderLeftColor")
		};
		if (sources.Any(static source => source is null))
			return;
		var owners = sources.Cast<DomElementRuntimeProperty>().ToArray();
		var values = owners.Select(source =>
			NormalizeXamlColor(StrongControlValue(source))).ToArray();
		if (values.Any(static value => value is null)
			|| values.Skip(1).Any(value =>
				!string.Equals(value, values[0], StringComparison.Ordinal)))
			return;
		SetCompositeXamlAttribute(
			attributes,
			"BorderBrush",
			values[0],
			owners,
			XamlCompositeValueKind.UniformValue);
	}

	private void ExpandStrongControlContentBox(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string targetName,
		string sizeName,
		IReadOnlyList<string> edgeNames)
	{
		if (!string.Equals(
			RuntimeValue("style.boxSizing"),
			"content-box",
			StringComparison.OrdinalIgnoreCase))
			return;
		var owners = new List<DomElementRuntimeProperty>();
		var size = RuntimeProperty(sizeName);
		if (!StrongControlNumber(size, out var total))
			return;
		owners.Add(size!);
		foreach (var edgeName in edgeNames)
		{
			var edge = RuntimeProperty(edgeName);
			if (!StrongControlNumber(edge, out var value))
				return;
			owners.Add(edge!);
			total += value;
		}
		SetCompositeXamlAttribute(
			attributes,
			targetName,
			total.ToString("R", CultureInfo.InvariantCulture),
			owners,
			XamlCompositeValueKind.SumLengths);
	}

	private static bool StrongControlNumber(
		DomElementRuntimeProperty? property,
		out double value) =>
		double.TryParse(
			NormalizeXamlLength(StrongControlValue(property)),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out value);

	private static string? StrongControlValue(DomElementRuntimeProperty? property) =>
		property?.SourceRuntime.IsSet == true
			? property.SourceRuntime.Value
			: property?.SourceInitialization.IsSet == true
				? property.SourceInitialization.Value
				: null;

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection()
	{
		var mapping = CreateXaml();
		if (mapping.ObjectType == XamlElementObjectType.HtmlCssBoxGrid)
		{
			return new(
				mapping,
				ElementXamlChildPlacementKind.DirectChildren,
				HasGeneratedXamlContent()
					? XamlElementContentProjectionKind.Composite
					: XamlElementContentProjectionKind.DirectChildren);
		}
		if (!IsCompositePhrasingContainer())
			return base.ResolveXamlObjectProjection();
		return new(
			mapping,
			ElementXamlChildPlacementKind.DirectChildren,
			HasGeneratedXamlContent()
				? XamlElementContentProjectionKind.Composite
				: XamlElementContentProjectionKind.DirectChildren);
	}

	protected override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans()
	{
		var directChildren = BuildDirectDomChildObjectPlans();
		var ownTextPlan = CreatePhrasingOwnTextPlan();
		if (IsCompositePhrasingContainer())
		{
			var plans = new List<XamlElementObjectPlan>();
			if (ownTextPlan is not null)
				plans.Add(ownTextPlan);
			plans.AddRange(directChildren);
			return plans;
		}
		var mapping = CreateXaml();
		if (mapping.ObjectType == XamlElementObjectType.HtmlCssBoxGrid
			&& ownTextPlan is not null)
		{
			ownTextPlan = ownTextPlan with
			{
				InitializationAttributes = ownTextPlan.InitializationAttributes
					.Where(static attribute => attribute.Name is "Text" or "Tag")
					.ToArray()
			};
			var plans = directChildren.ToList();
			plans.Insert(
				Math.Clamp(CapturedOwnTextElementInsertionIndex, 0, plans.Count),
				ownTextPlan);
			var vertical = mapping.Kind != XamlElementMappingKind.FlexLayout
				|| RuntimeValue("style.flexDirection")
					?.StartsWith("column", StringComparison.OrdinalIgnoreCase) == true;
			if (mapping.Kind is XamlElementMappingKind.BlockFlow
				or XamlElementMappingKind.FlexLayout)
			{
				var normalIndex = 0;
				for (var index = 0; index < plans.Count; index++)
				{
					if (plans[index].LayoutPlacement?.IsOutOfFlow == true)
						continue;
					var property = vertical ? "Grid.Row" : "Grid.Column";
					plans[index] = plans[index] with
					{
						InitializationAttributes = plans[index]
							.InitializationAttributes
							.Where(attribute => attribute.Name != property)
							.Append(new(
								property,
								normalIndex.ToString(CultureInfo.InvariantCulture),
								null))
							.ToArray()
					};
					normalIndex++;
				}
			}
			return plans;
		}
		if (XamlChildPlacement == ElementXamlChildPlacementKind.Content
			&& directChildren.Count != 0)
		{
			return
			[
				CreateSyntheticXamlObjectPlan(
					XamlElementObjectType.Grid,
					[],
					ElementXamlChildPlacementKind.DirectChildren,
					directChildren,
					$"Content wrapper for {DocumentScope}::{XPath}.")
			];
		}
		return directChildren;
	}

	private XamlElementObjectPlan? CreatePhrasingOwnTextPlan()
	{
		var ownText = DataSource("content.ownText");
		var value = DataSourceValue("content.ownText");
		if (ownText is null || string.IsNullOrWhiteSpace(value))
			return null;
		var textStyles = new Dictionary<string, GeneratedXamlAttribute>(
			StringComparer.Ordinal);
		AddTextStyles(textStyles, supportsTextAlignment: true);
		textStyles["Text"] = new("Text", value, ownText);
		textStyles["Tag"] = new(
			"Tag",
			$"generated-own-text:{DocumentScope}::{XPath}",
			null);
		return CreateSyntheticXamlObjectPlan(
			XamlElementObjectType.TextBlock,
			textStyles.Values
				.OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
				.ToArray(),
			ElementXamlChildPlacementKind.None,
			[],
			$"Composite text for {DocumentScope}::{XPath}.");
	}

	protected override bool HasGeneratedXamlContent() =>
		(IsCompositePhrasingContainer()
			|| CreateXaml().ObjectType == XamlElementObjectType.HtmlCssBoxGrid)
		&& !string.IsNullOrWhiteSpace(RuntimeInitialization("content.ownText"));

	protected override void WriteChildrenXaml(
		System.Text.StringBuilder output,
		int depth,
		bool isDocumentRoot)
	{
		if (IsCompositePhrasingContainer())
		{
			WriteDirectTextChild(output, depth);
			base.WriteChildrenXaml(output, depth, isDocumentRoot);
			return;
		}
		if (CreateXaml().ObjectType == XamlElementObjectType.HtmlCssBoxGrid)
		{
			WriteDirectTextChild(output, depth);
			base.WriteChildrenXaml(output, depth, isDocumentRoot);
			return;
		}
		if (XamlChildPlacement != ElementXamlChildPlacementKind.Content
			|| Children.Count == 0)
		{
			base.WriteChildrenXaml(output, depth, isDocumentRoot);
			return;
		}

		output.Append(' ', depth * 2)
			.Append("<Grid>")
			.AppendLine();
		base.WriteChildrenXaml(output, depth + 1, isDocumentRoot);
		output.Append(' ', depth * 2)
			.Append("</Grid>")
			.AppendLine();
	}

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		var mapping = CreateXaml();
		if (mapping.ElementName == "HtmlCssBoxGrid")
		{
			AddTextStyles(attributes, supportsTextAlignment: true);
			attributes.Remove("LineHeight");
			attributes.Remove("TextWrapping");
			attributes.Remove("TextTrimming");
			AddTextStyle(attributes, "style.lineHeight", "TextLineHeight", NormalizeXamlLength);
			AddTextStyle(attributes, "style.whiteSpace", "WhiteSpace");
			AddTextStyle(attributes, "style.textOverflow", "TextOverflow");
		}
		else if (mapping.ElementName is
			"Grid"
			or "HtmlInlineFlowPanel"
			or "HtmlInteractiveFlexPanel"
			or "HtmlFieldSetPanel"
			or "HtmlFormLabelPanel"
			or "HtmlBidiIsolationPanel"
			or "HtmlBidiOverridePanel"
			or "HtmlSubscriptPanel"
			or "HtmlSuperscriptPanel"
			or "HtmlRubyPanel"
			or "HtmlRubyAnnotationPanel"
			or "HtmlTablePanel"
			or "HtmlTableSectionPanel"
			or "HtmlTableRowPanel")
		{
			foreach (var textAttribute in new[]
			{
				"Foreground",
				"FontSize",
				"FontWeight",
				"FontStyle",
				"FontFamily",
				"FontStretch",
				"LineHeight",
				"TextAlignment",
				"TextDecorations",
				"TextWrapping",
				"TextTrimming"
			})
			{
				attributes.Remove(textAttribute);
			}
			AddTextStyle(
				attributes,
				"style.backgroundColor",
				"Background",
				NormalizeXamlColor);
		}
		else
		{
			AddTextStyles(
				attributes,
				SupportsTextAlignment(mapping.ElementName),
				SupportsTextBlockLayout(mapping.ElementName));
			if (mapping.ElementName == "TextBox")
			{
				AddTextStyle(attributes, "style.whiteSpace", "TextWrapping", static value =>
					value.Trim().ToLowerInvariant() is "pre-wrap" or "pre-line" or "normal" or "break-spaces"
						? "Wrap"
						: "NoWrap");
			}
			if (IsVerticalWritingMode())
			{
				attributes.Remove("LineHeight");
				attributes.Remove("TextWrapping");
				attributes.Remove("TextTrimming");
				AddTextStyle(
					attributes,
					"style.lineHeight",
					"TextLineHeight",
					NormalizeXamlLength);
				AddTextStyle(attributes, "style.whiteSpace", "WhiteSpace");
				AddTextStyle(attributes, "style.textOverflow", "TextOverflow");
			}
		}
		return attributes.Values
			.OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
			.ToArray();
	}

	private bool IsVerticalWritingMode()
	{
		var property = RuntimeProperty("style.writingMode");
		var value = property?.SourceRuntime.IsSet == true
			? property.SourceRuntime.Value
			: property?.SourceInitialization.IsSet == true
				? property.SourceInitialization.Value
				: null;
		return value?.Trim().StartsWith(
			"vertical-",
			StringComparison.OrdinalIgnoreCase) == true;
	}

	private void AddTextStyles(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		bool supportsTextAlignment,
		bool supportsTextBlockLayout = true)
	{
		AddTextStyle(attributes, "style.color", "Foreground", NormalizeXamlColor);
		AddTextStyle(attributes, "style.fontSize", "FontSize", NormalizeXamlLength);
		AddTextStyle(attributes, "style.fontWeight", "FontWeight", static value =>
			value switch
			{
				"400" => "Normal",
				"700" => "Bold",
				_ => value
			});
		AddTextStyle(attributes, "style.fontStyle", "FontStyle");
		AddTextStyle(attributes, "style.fontFamily", "FontFamily");
		AddTextStyle(attributes, "style.fontStretch", "FontStretch", NormalizeFontStretchValue);
		if (supportsTextBlockLayout)
		{
			AddTextStyle(attributes, "style.lineHeight", "LineHeight", NormalizeXamlLength);
			AddTextStyle(attributes, "style.textDecorationLine", "TextDecorations", NormalizeTextDecorationValue);
			AddTextStyle(attributes, "style.whiteSpace", "TextWrapping", static value =>
				value.Trim().ToLowerInvariant() is "pre-wrap" or "pre-line" or "normal" or "break-spaces"
					? "Wrap"
					: "NoWrap");
			AddTextStyle(attributes, "style.textOverflow", "TextTrimming", static value =>
				value.Trim().Equals("ellipsis", StringComparison.OrdinalIgnoreCase)
					? "CharacterEllipsis"
					: "None");
		}
		AddTextStyle(attributes, "style.direction", "FlowDirection", static value =>
			value.Trim().Equals("rtl", StringComparison.OrdinalIgnoreCase)
				? "RightToLeft"
				: "LeftToRight");
		if (supportsTextAlignment)
		{
			AddTextStyle(
				attributes,
				"style.textAlign",
				"TextAlignment",
				NormalizeTextAlignment);
		}
	}

	private static bool SupportsTextBlockLayout(string elementName) =>
		elementName is
			"TextBlock"
			or "HtmlBidiIsolationTextBlock"
			or "HtmlBidiOverrideTextBlock"
			or "HtmlSubscriptTextBlock"
			or "HtmlSuperscriptTextBlock"
			or "HtmlRubyAnnotationTextBlock";

	private static string? NormalizeFontStretchValue(string value) =>
		value.Trim().ToLowerInvariant() switch
		{
			"ultra-condensed" => "UltraCondensed",
			"extra-condensed" => "ExtraCondensed",
			"condensed" => "Condensed",
			"semi-condensed" => "SemiCondensed",
			"normal" => "Normal",
			"semi-expanded" => "SemiExpanded",
			"expanded" => "Expanded",
			"extra-expanded" => "ExtraExpanded",
			"ultra-expanded" => "UltraExpanded",
			_ => null
		};

	private static string NormalizeTextDecorationValue(string value)
	{
		var normalized = value.Trim().ToLowerInvariant();
		var values = new List<string>();
		if (normalized.Contains("underline", StringComparison.Ordinal))
			values.Add("Underline");
		if (normalized.Contains("line-through", StringComparison.Ordinal))
			values.Add("Strikethrough");
		return values.Count == 0 ? "None" : string.Join(", ", values);
	}

	private static string? NormalizeTextAlignment(string value)
	{
		var normalized = value.Trim().ToLowerInvariant();
		return normalized switch
		{
			"" or "normal" or "match-parent" => null,
			"left" => "Left",
			"right" => "Right",
			"center" => "Center",
			"justify" or "justify-all" => "Justify",
			"start" => "Start",
			"end" => "End",
			_ => null
		};
	}

	private bool IsCompositePhrasingContainer() =>
		XamlChildPlacement == ElementXamlChildPlacementKind.Inlines
		&& Children.Count != 0;

	private void WriteDirectTextChild(
		System.Text.StringBuilder output,
		int depth)
	{
		var ownText = DataSource("content.ownText");
		var value = DataSourceValue("content.ownText");
		if (ownText is null || string.IsNullOrWhiteSpace(value))
			return;
		var textStyles = new Dictionary<string, GeneratedXamlAttribute>(
			StringComparer.Ordinal);
		AddTextStyles(textStyles, supportsTextAlignment: true);
		output.Append(' ', depth * 2)
			.Append("<TextBlock Text=\"")
			.Append(EscapeXamlAttribute(value))
			.Append("\" Tag=\"")
			.Append(EscapeXamlAttribute(
				$"generated-own-text:{DocumentScope}::{XPath}"))
			.Append('"');
		foreach (var attribute in textStyles.Values.OrderBy(
			static attribute => attribute.Name,
			StringComparer.Ordinal))
		{
			output.Append(' ')
				.Append(attribute.Name)
				.Append("=\"")
				.Append(EscapeXamlAttribute(attribute.Value))
				.Append('"');
		}
		output.Append(" />").AppendLine();
	}

	private static bool SupportsTextAlignment(string elementName) =>
		elementName is
			"TextBlock"
			or "TextBox"
			or "RichEditBox"
			or "RichTextBlock"
			or "HtmlBidiIsolationTextBlock"
			or "HtmlBidiOverrideTextBlock"
			or "HtmlSubscriptTextBlock"
			or "HtmlSuperscriptTextBlock"
			or "HtmlRubyAnnotationTextBlock";

	private void AddTextStyle(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourceName,
		string targetName,
		Func<string, string?>? convert = null)
	{
		var property = RuntimeProperty(sourceName);
		var value = property is null
			? null
			: property.SourceInitialization.IsSet
				? property.SourceInitialization.Value
				: property.SourceRuntime.IsSet
					? property.SourceRuntime.Value
					: null;
		if (value is not null && convert is not null)
			value = convert(value);
		SetXamlAttribute(attributes, targetName, value, property);
	}
}

public abstract class HtmlContainerDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementCategory category = ElementCategory.LayoutContainer) :
	HtmlLayoutDomElementDefinition(
		mapping,
		tagName,
		category,
		ElementContentModel.Flow,
		XamlConversionSupport.Composite,
		ElementDefaultDisplay.Block,
		ElementXamlChildPlacementKind.DirectChildren);

public abstract class HtmlSectioningDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementCategory category = ElementCategory.Sectioning) :
	HtmlLayoutDomElementDefinition(
		mapping,
		tagName,
		category,
		ElementContentModel.Flow,
		XamlConversionSupport.Composite,
		ElementDefaultDisplay.Block,
		ElementXamlChildPlacementKind.DirectChildren);

public abstract class HtmlPhrasingDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlTextVisualDomElementDefinition(
		mapping,
		tagName,
		ElementCategory.Text,
		ElementVisualKind.TextContent,
		ElementContentModel.Phrasing,
		ElementClosure.OpenContainer,
		ElementSyntax.Normal,
		XamlConversionSupport.Direct,
		XamlControlFamily.Text,
		ElementDefaultDisplay.Inline,
		ElementInteractionKind.None,
		ElementXamlChildPlacementKind.Inlines);

public abstract class HtmlInteractiveDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementInteractionKind interactionKind,
	XamlControlFamily controlFamily) :
	HtmlTextVisualDomElementDefinition(
		mapping,
		tagName,
		ElementCategory.Interactive,
		ElementVisualKind.NativeControl,
		ElementContentModel.Phrasing,
		ElementClosure.ReplacedControl,
		ElementSyntax.Normal,
		XamlConversionSupport.Composite,
		controlFamily,
		ElementDefaultDisplay.Inline,
		interactionKind,
		ElementXamlChildPlacementKind.Content)
{
	public override DomElementQuerySpecialization QuerySpecialization =>
		DomElementQuerySpecialization.Interactive;
}

public abstract class HtmlFormControlDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementContentModel contentModel,
	ElementClosure closure,
	ElementSyntax syntax,
	XamlControlFamily controlFamily) :
	HtmlTextVisualDomElementDefinition(
		mapping,
		tagName,
		ElementCategory.FormControl,
		ElementVisualKind.NativeControl,
		contentModel,
		closure,
		syntax,
		XamlConversionSupport.Composite,
		controlFamily,
		ElementDefaultDisplay.InlineBlock,
		ElementInteractionKind.Input,
		ElementXamlChildPlacementKind.Content)
{
	public override DomElementQuerySpecialization QuerySpecialization =>
		DomElementQuerySpecialization.FormControl;

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		AddStrongTextVisualControlBoxAttributes(attributes);
		return attributes.Values
			.OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
			.ToArray();
	}

	private void AddControlBoxColor(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourceName,
		string targetName)
	{
		var property = RuntimeProperty(sourceName);
		SetXamlAttribute(
			attributes,
			targetName,
			property is null ? null : NormalizeXamlColor(ActiveControlValue(property)),
			property);
	}

	private void AddControlBoxThickness(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourcePrefix,
		string targetName,
		string sourceSuffix = "")
	{
		var top = RuntimeProperty(sourcePrefix + "Top" + sourceSuffix);
		var right = RuntimeProperty(sourcePrefix + "Right" + sourceSuffix);
		var bottom = RuntimeProperty(sourcePrefix + "Bottom" + sourceSuffix);
		var left = RuntimeProperty(sourcePrefix + "Left" + sourceSuffix);
		if (top is null || right is null || bottom is null || left is null)
			return;
		var values = new[]
		{
			NormalizeXamlLength(ActiveControlValue(left)),
			NormalizeXamlLength(ActiveControlValue(top)),
			NormalizeXamlLength(ActiveControlValue(right)),
			NormalizeXamlLength(ActiveControlValue(bottom))
		};
		if (values.Any(static value => value is null))
			return;
		SetCompositeXamlAttribute(
			attributes,
			targetName,
			string.Join(",", values),
			[left, top, right, bottom],
			XamlCompositeValueKind.Thickness);
	}

	private void AddControlBoxCornerRadius(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		var topLeft = RuntimeProperty("style.borderTopLeftRadius");
		var topRight = RuntimeProperty("style.borderTopRightRadius");
		var bottomRight = RuntimeProperty("style.borderBottomRightRadius");
		var bottomLeft = RuntimeProperty("style.borderBottomLeftRadius");
		if (topLeft is null || topRight is null || bottomRight is null || bottomLeft is null)
			return;
		var values = new[]
		{
			NormalizeXamlLength(ActiveControlValue(topLeft)),
			NormalizeXamlLength(ActiveControlValue(topRight)),
			NormalizeXamlLength(ActiveControlValue(bottomRight)),
			NormalizeXamlLength(ActiveControlValue(bottomLeft))
		};
		if (values.Any(static value => value is null))
			return;
		SetCompositeXamlAttribute(
			attributes,
			"CornerRadius",
			string.Join(",", values),
			[topLeft, topRight, bottomRight, bottomLeft],
			XamlCompositeValueKind.CornerRadius);
	}

	private void AddControlBoxUniformBorderColor(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		var top = RuntimeProperty("style.borderTopColor");
		var right = RuntimeProperty("style.borderRightColor");
		var bottom = RuntimeProperty("style.borderBottomColor");
		var left = RuntimeProperty("style.borderLeftColor");
		if (top is null || right is null || bottom is null || left is null)
			return;
		var colors = new[]
		{
			NormalizeXamlColor(ActiveControlValue(top)),
			NormalizeXamlColor(ActiveControlValue(right)),
			NormalizeXamlColor(ActiveControlValue(bottom)),
			NormalizeXamlColor(ActiveControlValue(left))
		};
		if (colors.Any(static value => value is null)
			|| colors.Skip(1).Any(value =>
				!string.Equals(value, colors[0], StringComparison.Ordinal)))
			return;
		SetCompositeXamlAttribute(
			attributes,
			"BorderBrush",
			colors[0],
			[top, right, bottom, left],
			XamlCompositeValueKind.UniformValue);
	}

	private void ExpandControlContentBoxDimensions(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		if (!string.Equals(
			ActiveControlValue(RuntimeProperty("style.boxSizing")),
			"content-box",
			StringComparison.OrdinalIgnoreCase))
			return;
		ExpandControlContentBoxDimension(
			attributes,
			"Width",
			"style.width",
			["style.paddingLeft", "style.paddingRight", "style.borderLeftWidth", "style.borderRightWidth"]);
		ExpandControlContentBoxDimension(
			attributes,
			"Height",
			"style.height",
			["style.paddingTop", "style.paddingBottom", "style.borderTopWidth", "style.borderBottomWidth"]);
	}

	private void ExpandControlContentBoxDimension(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string targetName,
		string sizeName,
		IReadOnlyList<string> edgeNames)
	{
		var owners = new List<DomElementRuntimeProperty>();
		var size = RuntimeProperty(sizeName);
		if (!TryReadControlXamlNumber(size, out var total))
			return;
		owners.Add(size!);
		foreach (var edgeName in edgeNames)
		{
			var edge = RuntimeProperty(edgeName);
			if (!TryReadControlXamlNumber(edge, out var value))
				return;
			owners.Add(edge!);
			total += value;
		}
		SetCompositeXamlAttribute(
			attributes,
			targetName,
			total.ToString("R", CultureInfo.InvariantCulture),
			owners,
			XamlCompositeValueKind.SumLengths);
	}

	private static bool TryReadControlXamlNumber(
		DomElementRuntimeProperty? property,
		out double value) =>
		double.TryParse(
			NormalizeXamlLength(ActiveControlValue(property)),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out value);

	private static string? ActiveControlValue(DomElementRuntimeProperty? property) =>
		property?.SourceRuntime.IsSet == true
			? property.SourceRuntime.Value
			: property?.SourceInitialization.IsSet == true
				? property.SourceInitialization.Value
				: null;

	protected bool ResolveDisabledState(DomElementStringProperty disabled)
	{
		var runtimeDisabled = RuntimeProperty("state.disabled");
		var disabledValue = runtimeDisabled?.SourceRuntime.Value;
		return disabledValue is null
			? disabled.SourceInitialization.IsSet
			: disabledValue.Equals("true", StringComparison.OrdinalIgnoreCase)
				|| disabledValue == "1";
	}

	protected void ApplyDisabledAttribute(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		DomElementStringProperty disabled)
	{
		var runtimeDisabled = RuntimeProperty("state.disabled");
		SetXamlAttribute(
			attributes,
			"IsEnabled",
			ResolveDisabledState(disabled) ? "False" : "True",
			runtimeDisabled ?? (IXamlPropertySlotOwner)disabled);
	}

	protected void ApplyValidationAttributes(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		var valid = RuntimeProperty("state.valid");
		var willValidate = RuntimeProperty("state.willValidate");
		var message = RuntimeProperty("state.validationMessage");
		SetXamlAttribute(
			attributes,
			"HtmlValidation.IsValid",
			RuntimeValue("state.valid"),
			valid);
		SetXamlAttribute(
			attributes,
			"HtmlValidation.WillValidate",
			RuntimeValue("state.willValidate"),
			willValidate);
		SetXamlAttribute(
			attributes,
			"HtmlValidation.ValidationMessage",
			RuntimeValue("state.validationMessage"),
			message);
	}
}

public abstract class HtmlOwnedFormControlDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementContentModel contentModel,
	ElementClosure closure,
	ElementSyntax syntax,
	XamlControlFamily controlFamily) :
	HtmlFormControlDomElementDefinition(
		mapping,
		tagName,
		contentModel,
		closure,
		syntax,
		controlFamily),
	IHtmlFormControlOwnerProperties
{
	private readonly HtmlFormControlOwnerPropertySet _formOwner =
		new(tagName);

	[HtmlElementProperty]
	public DomElementStringProperty Disabled => _formOwner.Disabled;

	[HtmlElementProperty]
	public DomElementStringProperty Form => _formOwner.Form;

	[HtmlElementProperty]
	public DomElementStringProperty Name => _formOwner.Name;
}

public abstract class HtmlSubmitterDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementContentModel contentModel,
	ElementClosure closure,
	ElementSyntax syntax,
	XamlControlFamily controlFamily) :
	HtmlOwnedFormControlDomElementDefinition(
		mapping,
		tagName,
		contentModel,
		closure,
		syntax,
		controlFamily),
	IHtmlSubmitterCommonProperties
{
	private readonly HtmlSubmitterCommonPropertySet _submitter =
		new(tagName);

	[HtmlElementProperty]
	public DomElementStringProperty FormAction => _submitter.FormAction;

	[HtmlElementProperty]
	public DomElementStringProperty FormEncodingType =>
		_submitter.FormEncodingType;

	[HtmlElementProperty]
	public DomElementStringProperty FormMethod => _submitter.FormMethod;

	[HtmlElementProperty]
	public DomElementStringProperty FormNoValidate =>
		_submitter.FormNoValidate;

	[HtmlElementProperty]
	public DomElementStringProperty FormTarget => _submitter.FormTarget;
}

public abstract class HtmlMediaDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementContentModel contentModel = ElementContentModel.Flow) :
	HtmlDomElementDefinition(
		mapping,
		tagName,
		ElementCategory.Media,
		ElementVisualKind.ReplacedContent,
		contentModel,
		ElementClosure.ReplacedControl,
		ElementSyntax.Normal,
		XamlConversionSupport.RuntimeReplacement,
		XamlControlFamily.Media,
		ElementDefaultDisplay.InlineBlock,
		ElementInteractionKind.Media,
		ElementXamlChildPlacementKind.Content)
{
	public override DomElementQuerySpecialization QuerySpecialization =>
		DomElementQuerySpecialization.Media;

}

public abstract class HtmlPlayableMediaDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlMediaDomElementDefinition(mapping, tagName),
	IHtmlMediaCommonProperties
{
	[HtmlElementProperty] public DomElementStringProperty Source { get; } = Attribute("src", tagName);
	[HtmlElementProperty] public DomElementStringProperty CrossOrigin { get; } = Attribute("crossorigin", tagName);
	[HtmlElementProperty] public DomElementStringProperty Preload { get; } = Attribute("preload", tagName);
	[HtmlElementProperty] public DomElementStringProperty AutoPlay { get; } = Attribute("autoplay", tagName);
	[HtmlElementProperty] public DomElementStringProperty Loop { get; } = Attribute("loop", tagName);
	[HtmlElementProperty] public DomElementStringProperty Muted { get; } = Attribute("muted", tagName);
	[HtmlElementProperty] public DomElementStringProperty Controls { get; } = Attribute("controls", tagName);
	[HtmlElementProperty] public DomElementStringProperty Loading { get; } = Attribute("loading", tagName);

	protected string? BuildTrackSourceDescriptors()
	{
		var descriptors = Children
			.OfType<HtmlTrackDomElement>()
			.Select(static track => new[]
			{
				track.Source.SourceInitialization.Value,
				track.Kind.SourceInitialization.Value,
				track.Label.SourceInitialization.Value,
				track.SourceLanguage.SourceInitialization.Value,
				track.Default.SourceInitialization.IsSet ? "true" : "false"
			})
			.Where(static fields => !string.IsNullOrWhiteSpace(fields[0]))
			.Select(static fields => string.Join(
				'\u001F',
				fields.Select(static value => value ?? string.Empty)))
			.ToArray();
		return descriptors.Length == 0
			? null
			: string.Join('\u001E', descriptors);
	}
}

public abstract class HtmlModificationDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlPhrasingDomElementDefinition(mapping, tagName),
	IHtmlModificationCommonProperties
{
	[HtmlElementProperty] public DomElementStringProperty Cite { get; } = Attribute("cite", tagName);
	[HtmlElementProperty] public DomElementStringProperty DateTime { get; } = Attribute("datetime", tagName);
}

public abstract class HtmlPhrasingQuoteDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlPhrasingDomElementDefinition(mapping, tagName),
	IHtmlQuoteCommonProperties
{
	private readonly HtmlQuoteCommonPropertySet _quote = new(tagName);

	[HtmlElementProperty]
	public DomElementStringProperty Cite => _quote.Cite;
}

public abstract class HtmlSectioningQuoteDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlSectioningDomElementDefinition(mapping, tagName),
	IHtmlQuoteCommonProperties
{
	private readonly HtmlQuoteCommonPropertySet _quote = new(tagName);

	[HtmlElementProperty]
	public DomElementStringProperty Cite => _quote.Cite;
}

public abstract class HtmlSectioningFormOwnerDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlSectioningDomElementDefinition(mapping, tagName),
	IHtmlFormControlOwnerProperties
{
	private readonly HtmlFormControlOwnerPropertySet _formOwner =
		new(tagName);

	[HtmlElementProperty]
	public DomElementStringProperty Disabled => _formOwner.Disabled;

	[HtmlElementProperty]
	public DomElementStringProperty Form => _formOwner.Form;

	[HtmlElementProperty]
	public DomElementStringProperty Name => _formOwner.Name;
}

public abstract class HtmlMetadataDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementSyntax syntax = ElementSyntax.Normal) :
	HtmlDomElementDefinition(
		mapping,
		tagName,
		ElementCategory.Metadata,
		ElementVisualKind.NonVisual,
		ElementContentModel.None,
		ElementClosure.OpenContainer,
		syntax,
		XamlConversionSupport.NonVisual,
		XamlControlFamily.None,
		ElementDefaultDisplay.None,
		ElementInteractionKind.None,
		ElementXamlChildPlacementKind.None)
{
	public override DomElementQuerySpecialization QuerySpecialization =>
		DomElementQuerySpecialization.Metadata;
}

public abstract class HtmlBlockTextDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlTextVisualDomElementDefinition(
		mapping, tagName, ElementCategory.Text,
		ElementVisualKind.TextContent,
		ElementContentModel.Flow,
		ElementClosure.OpenContainer, ElementSyntax.Normal,
		XamlConversionSupport.Direct, XamlControlFamily.Text,
		ElementDefaultDisplay.Block, ElementInteractionKind.None,
		ElementXamlChildPlacementKind.Inlines);

public abstract class HtmlVoidTextDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlTextVisualDomElementDefinition(
		mapping, tagName, ElementCategory.Text,
		ElementVisualKind.TextContent,
		ElementContentModel.None,
		ElementClosure.ClosedLeaf, ElementSyntax.Void,
		XamlConversionSupport.Direct, XamlControlFamily.Text,
		ElementDefaultDisplay.Inline, ElementInteractionKind.None,
		ElementXamlChildPlacementKind.None);

public abstract class HtmlListDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementDefaultDisplay display = ElementDefaultDisplay.Block) :
	HtmlLayoutDomElementDefinition(
		mapping, tagName, ElementCategory.List,
		ElementContentModel.Flow,
		XamlConversionSupport.Composite,
		display,
		ElementXamlChildPlacementKind.Items);

public abstract class HtmlTableDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementDefaultDisplay display,
	ElementXamlChildPlacementKind childPlacement =
		ElementXamlChildPlacementKind.DirectChildren) :
	HtmlLayoutDomElementDefinition(
		mapping, tagName, ElementCategory.Table,
		ElementContentModel.TableStructure,
		XamlConversionSupport.Composite,
		display,
		childPlacement);

public abstract class HtmlEmbeddedDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementSyntax syntax = ElementSyntax.Normal) :
	HtmlDomElementDefinition(
		mapping, tagName, ElementCategory.EmbeddedContent,
		ElementVisualKind.ReplacedContent, ElementContentModel.Flow,
		syntax == ElementSyntax.Void
			? ElementClosure.ClosedLeaf
			: ElementClosure.ReplacedControl,
		syntax, XamlConversionSupport.RuntimeReplacement,
		XamlControlFamily.EmbeddedHost, ElementDefaultDisplay.Replaced,
		ElementInteractionKind.EmbeddedDocument,
		ElementXamlChildPlacementKind.Content)
{
	protected void ApplyEmbeddedDimensions(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		DomElementStringProperty width,
		DomElementStringProperty height)
	{
		SetXamlAttribute(attributes, "Width", NormalizeXamlLength(SourceInitialization(width)), width);
		SetXamlAttribute(attributes, "Height", NormalizeXamlLength(SourceInitialization(height)), height);
	}
}

public abstract class HtmlPhrasingNamedFormOwnerDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlPhrasingDomElementDefinition(mapping, tagName),
	IHtmlNamedFormOwnerProperties
{
	private readonly HtmlNamedFormOwnerPropertySet _formOwner =
		new(tagName);

	[HtmlElementProperty]
	public DomElementStringProperty Form => _formOwner.Form;

	[HtmlElementProperty]
	public DomElementStringProperty Name => _formOwner.Name;
}

public abstract class HtmlEmbeddedNamedFormOwnerDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlEmbeddedDomElementDefinition(mapping, tagName),
	IHtmlNamedFormOwnerProperties
{
	private readonly HtmlNamedFormOwnerPropertySet _formOwner =
		new(tagName);

	[HtmlElementProperty]
	public DomElementStringProperty Form => _formOwner.Form;

	[HtmlElementProperty]
	public DomElementStringProperty Name => _formOwner.Name;
}

public abstract class HtmlHyperlinkDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	HtmlInteractiveDomElementDefinition(
		mapping,
		tagName,
		ElementInteractionKind.Navigation,
		XamlControlFamily.Button),
	IHtmlHyperlinkCommonProperties
{
	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		if (CreateXaml().ObjectType is
			XamlElementObjectType.HtmlInteractiveFlexPanel
			or XamlElementObjectType.HyperlinkButton)
		{
			AddStrongTextVisualControlBoxAttributes(attributes);
		}
		return attributes.Values
			.OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
			.ToArray();
	}

	[HtmlElementProperty] public DomElementStringProperty Href { get; } = Attribute("href", tagName);
	[HtmlElementProperty] public DomElementStringProperty Target { get; } = Attribute("target", tagName);
	[HtmlElementProperty] public DomElementStringProperty Download { get; } = Attribute("download", tagName);
	[HtmlElementProperty] public DomElementStringProperty Ping { get; } = Attribute("ping", tagName);
	[HtmlElementProperty] public DomElementStringProperty Rel { get; } = Attribute("rel", tagName);
	[HtmlElementProperty] public DomElementStringProperty ReferrerPolicy { get; } = Attribute("referrerpolicy", tagName);
}

public abstract class HtmlNonVisualHyperlinkDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementSyntax syntax) :
	HtmlDomElementDefinition(
		mapping,
		tagName,
		ElementCategory.Interactive,
		ElementVisualKind.NonVisual,
		ElementContentModel.None,
		ElementClosure.ClosedLeaf,
		syntax,
		XamlConversionSupport.NonVisual,
		XamlControlFamily.None,
		ElementDefaultDisplay.None,
		ElementInteractionKind.Navigation,
		ElementXamlChildPlacementKind.None),
	IHtmlHyperlinkCommonProperties
{
	[HtmlElementProperty] public DomElementStringProperty Href { get; } = Attribute("href", tagName);
	[HtmlElementProperty] public DomElementStringProperty Target { get; } = Attribute("target", tagName);
	[HtmlElementProperty] public DomElementStringProperty Download { get; } = Attribute("download", tagName);
	[HtmlElementProperty] public DomElementStringProperty Ping { get; } = Attribute("ping", tagName);
	[HtmlElementProperty] public DomElementStringProperty Rel { get; } = Attribute("rel", tagName);
	[HtmlElementProperty] public DomElementStringProperty ReferrerPolicy { get; } = Attribute("referrerpolicy", tagName);
}

public abstract class HtmlTableCellDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementDefaultDisplay display) :
	HtmlTableDomElementDefinition(
		mapping,
		tagName,
		display,
		ElementXamlChildPlacementKind.Content),
	IHtmlTableCellCommonProperties
{
	[HtmlElementProperty] public DomElementStringProperty ColumnSpan { get; } = Attribute("colspan", tagName);
	[HtmlElementProperty] public DomElementStringProperty RowSpan { get; } = Attribute("rowspan", tagName);
	[HtmlElementProperty] public DomElementStringProperty Headers { get; } = Attribute("headers", tagName);

	protected void ApplyCellSpans(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		DomElementStringProperty columnSpan,
		DomElementStringProperty rowSpan)
	{
		SetXamlAttribute(attributes, "HtmlTable.ColumnSpan", SourceInitialization(columnSpan), columnSpan);
		SetXamlAttribute(attributes, "HtmlTable.RowSpan", SourceInitialization(rowSpan), rowSpan);
	}

	protected override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans()
	{
		var children = base.BuildXamlObjectChildPlans();
		if (children.Count == 0)
			return [];
		return
		[
			CreateSyntheticXamlObjectPlan(
				XamlElementObjectType.Grid,
				[],
				ElementXamlChildPlacementKind.DirectChildren,
				children,
				$"Table-cell content for {DocumentScope}::{XPath}.")
		];
	}
}
