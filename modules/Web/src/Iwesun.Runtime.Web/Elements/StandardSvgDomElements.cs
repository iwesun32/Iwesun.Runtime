namespace Iwesun.Runtime.Web;

public abstract class SvgContainerDomElementDefinition(
	DomElementMapping mapping,
	string tagName,
	ElementCategory category = ElementCategory.VectorContainer) :
	SvgDomElementDefinition(
		mapping,
		tagName,
		category,
		ElementVisualKind.VectorContainer,
		ElementContentModel.SvgChildren,
		ElementClosure.OpenContainer,
		XamlConversionSupport.Composite,
		XamlControlFamily.Panel,
		ElementXamlChildPlacementKind.DirectChildren)
{
}

public abstract class SvgGeometryDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	SvgDomElementDefinition(
		mapping,
		tagName,
		ElementCategory.VectorGeometry,
		ElementVisualKind.VectorGeometry,
		ElementContentModel.None,
		ElementClosure.ClosedLeaf,
		XamlConversionSupport.Direct,
		XamlControlFamily.Shape,
		ElementXamlChildPlacementKind.None)
{
	protected override bool ShouldEmitXamlRuntimeProperty(
		DomElementRuntimeProperty property) =>
		property.XamlExecution.MarkupAttributeName is
			"Width" or "Height" or "MinWidth" or "MinHeight" or "MaxWidth" or "MaxHeight"
			? false
			: base.ShouldEmitXamlRuntimeProperty(property);

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		// SVG geometry has an intrinsic bounding box determined by its strongly
		// typed geometry attributes. CSS width/height keywords do not turn a
		// path, polygon, line, or ellipse into an HTML layout box.
		foreach (var name in new[]
		{
			"Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight"
		})
		{
			attributes.Remove(name);
		}
		AddPaint(attributes, Fill, "Fill", NormalizeXamlColor);
		AddPaint(attributes, Stroke, "Stroke", NormalizeXamlColor);
		AddPaint(attributes, StrokeWidth, "StrokeThickness", NormalizeXamlLength);
		AddPaint(attributes, StrokeDashArray, "StrokeDashArray");
		AddPaint(attributes, Opacity, "Opacity");
		AddComputedPaint(attributes, "style.fill", "Fill", NormalizeXamlColor);
		AddComputedPaint(attributes, "style.stroke", "Stroke", NormalizeXamlColor);
		AddComputedPaint(attributes, "style.strokeWidth", "StrokeThickness", NormalizeXamlLength);
		AddComputedPaint(attributes, "style.strokeLinecap", "StrokeStartLineCap", NormalizeLineCap);
		AddComputedPaint(attributes, "style.strokeLinecap", "StrokeEndLineCap", NormalizeLineCap);
		AddComputedPaint(attributes, "style.strokeLinecap", "StrokeDashCap", NormalizeLineCap);
		AddComputedPaint(attributes, "style.strokeLinejoin", "StrokeLineJoin", NormalizeLineJoin);
		return attributes.Values.ToArray();
	}

	private void AddComputedPaint(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourceName,
		string target,
		Func<string, string?>? convert = null)
	{
		var source = HtmlRoot?.ResolveGlobalStyleProperty(this, sourceName);
		if (source is null)
			return;
		var value = SourceInitialization(source);
		if (value is not null && convert is not null)
			value = convert(value);
		SetXamlAttribute(attributes, target, value, source);
	}

	private static string? NormalizeLineCap(string value) =>
		value.Trim().ToLowerInvariant() switch
		{
			"round" => "Round",
			"square" => "Square",
			"butt" => "Flat",
			_ => null
		};

	private static string? NormalizeLineJoin(string value) =>
		value.Trim().ToLowerInvariant() switch
		{
			"round" => "Round",
			"bevel" => "Bevel",
			"miter" or "miter-clip" => "Miter",
			_ => null
		};

	protected static void AddSvgAttribute(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		SvgDomAttributeProperty source,
		string target,
		Func<string, string?>? convert = null)
	{
		var value = SourceInitialization(source);
		if (value is not null && convert is not null)
			value = convert(value);
		SetXamlAttribute(attributes, target, value, source);
	}

	private static void AddPaint(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		SvgDomAttributeProperty source,
		string target,
		Func<string, string?>? convert = null) =>
		AddSvgAttribute(attributes, source, target, convert);
}

public abstract class SvgDefinitionDomElementDefinition(
	DomElementMapping mapping,
	string tagName) :
	SvgContainerDomElementDefinition(
		mapping,
		tagName,
		ElementCategory.VectorDefinition)
{
}

public sealed partial class SvgRootDomElement(DomElementMapping mapping) :
	SvgContainerDomElementDefinition(mapping, "svg")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlSvgViewport, XamlElementMappingKind.PositionedLayout, true, "The svg element owns a CSS box plus an SVG viewport and coordinate system.");
	[SvgElementProperty] public SvgDomAttributeProperty X { get; } = Attribute("x");
	[SvgElementProperty] public SvgDomAttributeProperty Y { get; } = Attribute("y");
	[SvgElementProperty] public SvgDomAttributeProperty Width { get; } = Attribute("width");
	[SvgElementProperty] public SvgDomAttributeProperty Height { get; } = Attribute("height");
	[SvgElementProperty] public SvgDomAttributeProperty ViewBox { get; } = Attribute("viewBox");
	[SvgElementProperty] public SvgDomAttributeProperty PreserveAspectRatio { get; } = Attribute("preserveAspectRatio");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(X), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(Y), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(Width), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(Height), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(ViewBox), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(PreserveAspectRatio), SvgElementAttributeXamlHandling.InlineXaml));
	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		attributes.Remove("Width");
		attributes.Remove("Height");
		SetXamlAttribute(attributes, "Canvas.Left", NormalizeXamlLength(SourceInitialization(X)), X);
		SetXamlAttribute(attributes, "Canvas.Top", NormalizeXamlLength(SourceInitialization(Y)), Y);
		AddViewportThickness(attributes, "style.padding", "Padding", "");
		AddViewportThickness(attributes, "style.border", "BorderThickness", "Width");
		AddViewportDimension(attributes, "Width", "style.width", Width,
			["style.paddingLeft", "style.paddingRight", "style.borderLeftWidth", "style.borderRightWidth"]);
		AddViewportDimension(attributes, "Height", "style.height", Height,
			["style.paddingTop", "style.paddingBottom", "style.borderTopWidth", "style.borderBottomWidth"]);
		SetXamlAttribute(
			attributes,
			"ViewBox",
			SourceInitialization(ViewBox),
			ViewBox);
		SetXamlAttribute(
			attributes,
			"PreserveAspectRatio",
			SourceInitialization(PreserveAspectRatio),
			PreserveAspectRatio);
		return attributes.Values.ToArray();
	}

	private void AddViewportThickness(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourcePrefix,
		string targetName,
		string sourceSuffix)
	{
		var top = Runtime(sourcePrefix + "Top" + sourceSuffix);
		var right = Runtime(sourcePrefix + "Right" + sourceSuffix);
		var bottom = Runtime(sourcePrefix + "Bottom" + sourceSuffix);
		var left = Runtime(sourcePrefix + "Left" + sourceSuffix);
		if (top is null || right is null || bottom is null || left is null)
			return;
		var values = new[] { ActiveLength(left), ActiveLength(top), ActiveLength(right), ActiveLength(bottom) };
		if (values.Any(static value => value is null))
			return;
		SetCompositeXamlAttribute(
			attributes,
			targetName,
			string.Join(",", values),
			[left, top, right, bottom],
			XamlCompositeValueKind.Thickness);
	}

	private void AddViewportDimension(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string targetName,
		string styleName,
		SvgDomAttributeProperty presentationAttribute,
		IReadOnlyList<string> edgeNames)
	{
		var style = Runtime(styleName);
		var normalized = style is null ? null : ActiveLength(style);
		IXamlPropertySlotOwner primary = style is null
			? presentationAttribute
			: style;
		if (normalized is null)
			normalized = NormalizeXamlLength(SourceInitialization(presentationAttribute));
		if (normalized is null)
			return;
		var owners = new List<IXamlPropertySlotOwner> { primary };
		if (string.Equals(Active(Runtime("style.boxSizing")), "content-box", StringComparison.OrdinalIgnoreCase)
			&& double.TryParse(normalized, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out var total))
		{
			foreach (var edgeName in edgeNames)
			{
				var edge = Runtime(edgeName);
				if (edge is null || !double.TryParse(
					ActiveLength(edge),
					System.Globalization.NumberStyles.Float,
					System.Globalization.CultureInfo.InvariantCulture,
					out var edgeValue))
					return;
				owners.Add(edge);
				total += edgeValue;
			}
			normalized = total.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
		}
		if (owners.Count == 1)
		{
			SetXamlAttribute(attributes, targetName, normalized, primary);
			return;
		}
		SetCompositeXamlAttribute(
			attributes,
			targetName,
			normalized,
			owners,
			XamlCompositeValueKind.SumLengths);
	}

	private DomElementRuntimeProperty? Runtime(string name) =>
		RuntimeProperties.FirstOrDefault(property =>
			string.Equals(property.Name, name, StringComparison.Ordinal))
		?? HtmlRoot?.ResolveGlobalStyleProperty(this, name);

	private static string? Active(DomElementRuntimeProperty? property) =>
		property is null
			? null
			: property.SourceRuntime.IsSet
				? property.SourceRuntime.Value
				: property.SourceInitialization.IsSet
					? property.SourceInitialization.Value
					: null;

	private static string? ActiveLength(DomElementRuntimeProperty property) =>
		NormalizeXamlLength(Active(property));
}

public sealed partial class SvgGroupDomElement(DomElementMapping mapping) :
	SvgContainerDomElementDefinition(mapping, "g")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Canvas, XamlElementMappingKind.PositionedLayout, true, "The g element groups vector children in one transformed coordinate space.");
}

public sealed partial class SvgPathDomElement(DomElementMapping mapping) :
	SvgGeometryDomElementDefinition(mapping, "path")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Path, XamlElementMappingKind.TypeDefault, false, "The path element owns path geometry.");
	[SvgElementProperty] public SvgDomAttributeProperty Data { get; } = Attribute("d");
	[SvgElementProperty] public SvgDomAttributeProperty PathLength { get; } = Attribute("pathLength");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(Data), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(PathLength), SvgElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		AddSvgAttribute(attributes, Data, "Data");
		return attributes.Values.ToArray();
	}
}

public sealed partial class SvgUseDomElement(DomElementMapping mapping) :
	SvgGeometryDomElementDefinition(mapping, "use")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Path, XamlElementMappingKind.TypeDefault, false, "The use element instantiates referenced vector geometry.");
	[SvgElementProperty] public SvgDomAttributeProperty Href { get; } = Attribute("href");
	[SvgElementProperty] public SvgDomAttributeProperty X { get; } = Attribute("x");
	[SvgElementProperty] public SvgDomAttributeProperty Y { get; } = Attribute("y");
	[SvgElementProperty] public SvgDomAttributeProperty Width { get; } = Attribute("width");
	[SvgElementProperty] public SvgDomAttributeProperty Height { get; } = Attribute("height");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(Href), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(X), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Y), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Width), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Height), SvgElementAttributeXamlHandling.RuntimeDataSource));
}

public sealed partial class SvgCircleDomElement(DomElementMapping mapping) :
	SvgGeometryDomElementDefinition(mapping, "circle")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Ellipse, XamlElementMappingKind.TypeDefault, false, "The circle element owns circular ellipse geometry.");
	[SvgElementProperty] public SvgDomAttributeProperty CenterX { get; } = Attribute("cx");
	[SvgElementProperty] public SvgDomAttributeProperty CenterY { get; } = Attribute("cy");
	[SvgElementProperty] public SvgDomAttributeProperty Radius { get; } = Attribute("r");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(CenterX), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(CenterY), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Radius), SvgElementAttributeXamlHandling.InlineXaml));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		if (double.TryParse(
			SourceInitialization(Radius),
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out var radius))
		{
			var diameter = (radius * 2).ToString(
				"R",
				System.Globalization.CultureInfo.InvariantCulture);
			SetXamlAttribute(attributes, "Width", diameter, Radius);
			SetXamlAttribute(attributes, "Height", diameter, Radius);
		}
		return attributes.Values.ToArray();
	}
}

public sealed partial class SvgRectangleDomElement(DomElementMapping mapping) :
	SvgGeometryDomElementDefinition(mapping, "rect")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Rectangle, XamlElementMappingKind.TypeDefault, false, "The rect element owns rectangular geometry and corner radii.");
	[SvgElementProperty] public SvgDomAttributeProperty X { get; } = Attribute("x");
	[SvgElementProperty] public SvgDomAttributeProperty Y { get; } = Attribute("y");
	[SvgElementProperty] public SvgDomAttributeProperty Width { get; } = Attribute("width");
	[SvgElementProperty] public SvgDomAttributeProperty Height { get; } = Attribute("height");
	[SvgElementProperty] public SvgDomAttributeProperty RadiusX { get; } = Attribute("rx");
	[SvgElementProperty] public SvgDomAttributeProperty RadiusY { get; } = Attribute("ry");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(X), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(Y), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(Width), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(Height), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(RadiusX), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(RadiusY), SvgElementAttributeXamlHandling.InlineXaml));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		AddSvgAttribute(attributes, X, "Canvas.Left", NormalizeXamlLength);
		AddSvgAttribute(attributes, Y, "Canvas.Top", NormalizeXamlLength);
		AddSvgAttribute(attributes, Width, "Width", NormalizeXamlLength);
		AddSvgAttribute(attributes, Height, "Height", NormalizeXamlLength);
		AddSvgAttribute(attributes, RadiusX, "RadiusX", NormalizeXamlLength);
		AddSvgAttribute(attributes, RadiusY, "RadiusY", NormalizeXamlLength);
		return attributes.Values.ToArray();
	}
}

public sealed partial class SvgLineDomElement(DomElementMapping mapping) :
	SvgGeometryDomElementDefinition(mapping, "line")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Line, XamlElementMappingKind.TypeDefault, false, "The line element owns two-endpoint line geometry.");
	[SvgElementProperty] public SvgDomAttributeProperty X1 { get; } = Attribute("x1");
	[SvgElementProperty] public SvgDomAttributeProperty Y1 { get; } = Attribute("y1");
	[SvgElementProperty] public SvgDomAttributeProperty X2 { get; } = Attribute("x2");
	[SvgElementProperty] public SvgDomAttributeProperty Y2 { get; } = Attribute("y2");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(X1), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(Y1), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(X2), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(Y2), SvgElementAttributeXamlHandling.InlineXaml));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		AddSvgAttribute(attributes, X1, "X1", NormalizeXamlLength);
		AddSvgAttribute(attributes, Y1, "Y1", NormalizeXamlLength);
		AddSvgAttribute(attributes, X2, "X2", NormalizeXamlLength);
		AddSvgAttribute(attributes, Y2, "Y2", NormalizeXamlLength);
		return attributes.Values.ToArray();
	}
}

public sealed partial class SvgPolygonDomElement(DomElementMapping mapping) :
	SvgGeometryDomElementDefinition(mapping, "polygon")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Polygon, XamlElementMappingKind.TypeDefault, false, "The polygon element owns closed point geometry.");
	[SvgElementProperty] public SvgDomAttributeProperty Points { get; } = Attribute("points");
	[SvgElementProperty] public SvgDomAttributeProperty PathLength { get; } = Attribute("pathLength");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(Points), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(PathLength), SvgElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		AddSvgAttribute(attributes, Points, "Points");
		return attributes.Values.ToArray();
	}
}

public sealed partial class SvgPolylineDomElement(DomElementMapping mapping) :
	SvgGeometryDomElementDefinition(mapping, "polyline")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Polyline, XamlElementMappingKind.TypeDefault, false, "The polyline element owns open point geometry.");
	[SvgElementProperty] public SvgDomAttributeProperty Points { get; } = Attribute("points");
	[SvgElementProperty] public SvgDomAttributeProperty PathLength { get; } = Attribute("pathLength");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(Points), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(PathLength), SvgElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		AddSvgAttribute(attributes, Points, "Points");
		return attributes.Values.ToArray();
	}
}

public sealed partial class SvgEllipseDomElement(DomElementMapping mapping) :
	SvgGeometryDomElementDefinition(mapping, "ellipse")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Ellipse, XamlElementMappingKind.TypeDefault, false, "The ellipse element owns independent horizontal and vertical radii.");
	[SvgElementProperty] public SvgDomAttributeProperty CenterX { get; } = Attribute("cx");
	[SvgElementProperty] public SvgDomAttributeProperty CenterY { get; } = Attribute("cy");
	[SvgElementProperty] public SvgDomAttributeProperty RadiusX { get; } = Attribute("rx");
	[SvgElementProperty] public SvgDomAttributeProperty RadiusY { get; } = Attribute("ry");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(CenterX), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(CenterY), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(RadiusX), SvgElementAttributeXamlHandling.InlineXaml),
			(nameof(RadiusY), SvgElementAttributeXamlHandling.InlineXaml));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		AddDiameter(attributes, RadiusX, "Width");
		AddDiameter(attributes, RadiusY, "Height");
		return attributes.Values.ToArray();
	}

	private static void AddDiameter(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		SvgDomAttributeProperty source,
		string target)
	{
		if (double.TryParse(
			SourceInitialization(source),
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out var radius))
		{
			SetXamlAttribute(
				attributes,
				target,
				(radius * 2).ToString(
					"R",
					System.Globalization.CultureInfo.InvariantCulture),
				source);
		}
	}
}

public sealed partial class SvgDefinitionsDomElement(DomElementMapping mapping) :
	SvgDefinitionDomElementDefinition(mapping, "defs")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Canvas, XamlElementMappingKind.PositionedLayout, false, "The defs element owns non-rendered reusable vector definitions.");
}

public sealed partial class SvgClipPathDomElement(DomElementMapping mapping) :
	SvgDefinitionDomElementDefinition(mapping, "clipPath")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Canvas, XamlElementMappingKind.PositionedLayout, false, "The clipPath element owns reusable clipping geometry.");
	[SvgElementProperty] public SvgDomAttributeProperty ClipPathUnits { get; } = Attribute("clipPathUnits");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(ClipPathUnits), SvgElementAttributeXamlHandling.RuntimeDataSource));
}

public sealed partial class SvgMaskDomElement(DomElementMapping mapping) :
	SvgDefinitionDomElementDefinition(mapping, "mask")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Canvas, XamlElementMappingKind.PositionedLayout, false, "The mask element owns reusable mask content and bounds.");
	[SvgElementProperty] public SvgDomAttributeProperty MaskUnits { get; } = Attribute("maskUnits");
	[SvgElementProperty] public SvgDomAttributeProperty MaskContentUnits { get; } = Attribute("maskContentUnits");
	[SvgElementProperty] public SvgDomAttributeProperty X { get; } = Attribute("x");
	[SvgElementProperty] public SvgDomAttributeProperty Y { get; } = Attribute("y");
	[SvgElementProperty] public SvgDomAttributeProperty Width { get; } = Attribute("width");
	[SvgElementProperty] public SvgDomAttributeProperty Height { get; } = Attribute("height");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(MaskUnits), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(MaskContentUnits), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(X), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Y), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Width), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Height), SvgElementAttributeXamlHandling.RuntimeDataSource));
}

public sealed partial class SvgTextDomElement(DomElementMapping mapping) :
	SvgDomElementDefinition(
		mapping,
		"text",
		ElementCategory.Text,
		ElementVisualKind.TextContent,
		ElementContentModel.TextOnly,
		ElementClosure.OpenContainer,
		XamlConversionSupport.Direct,
		XamlControlFamily.Text,
		ElementXamlChildPlacementKind.Inlines)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.TextBlock, XamlElementMappingKind.PositionedLayout, true, "The SVG text element owns positioned vector text content.");
	[SvgElementProperty] public SvgDomAttributeProperty X { get; } = Attribute("x");
	[SvgElementProperty] public SvgDomAttributeProperty Y { get; } = Attribute("y");
	[SvgElementProperty] public SvgDomAttributeProperty DeltaX { get; } = Attribute("dx");
	[SvgElementProperty] public SvgDomAttributeProperty DeltaY { get; } = Attribute("dy");
	[SvgElementProperty] public SvgDomAttributeProperty Rotate { get; } = Attribute("rotate");
	[SvgElementProperty] public SvgDomAttributeProperty TextLength { get; } = Attribute("textLength");
	[SvgElementProperty] public SvgDomAttributeProperty LengthAdjust { get; } = Attribute("lengthAdjust");
	protected override IReadOnlyDictionary<string, SvgElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleSvgXamlAttributes(
			(nameof(X), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Y), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(DeltaX), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(DeltaY), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Rotate), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(TextLength), SvgElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(LengthAdjust), SvgElementAttributeXamlHandling.RuntimeDataSource));
}
