namespace Iwesun.Runtime.Web;

public sealed partial class HtmlFormDomElement(DomElementMapping m) : HtmlSectioningDomElementDefinition(m, "form")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() switch
		{
			HtmlCssFormattingContext.FlexRow or HtmlCssFormattingContext.FlexColumn =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.FlexLayout, true, "The form element owns a flex formatting context."),
			HtmlCssFormattingContext.Grid =>
				new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.GridLayout, true, "The form element owns a grid formatting context."),
			_ => new(XamlElementObjectType.HtmlCssBoxGrid, XamlElementMappingKind.BlockFlow, true, "The form element owns normal form-associated block flow.")
		};
	[HtmlElementProperty] public DomElementStringProperty AcceptCharset { get; } = Attribute("accept-charset", "form");
	[HtmlElementProperty] public DomElementStringProperty Action { get; } = Attribute("action", "form");
	[HtmlElementProperty] public DomElementStringProperty AutoComplete { get; } = Attribute("autocomplete", "form");
	[HtmlElementProperty] public DomElementStringProperty EncodingType { get; } = Attribute("enctype", "form");
	[HtmlElementProperty] public DomElementStringProperty Method { get; } = Attribute("method", "form");
	[HtmlElementProperty] public DomElementStringProperty Name { get; } = Attribute("name", "form");
	[HtmlElementProperty] public DomElementStringProperty NoValidate { get; } = Attribute("novalidate", "form");
	[HtmlElementProperty] public DomElementStringProperty Rel { get; } = Attribute("rel", "form");
	[HtmlElementProperty] public DomElementStringProperty Target { get; } = Attribute("target", "form");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(AcceptCharset), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(AutoComplete), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(EncodingType), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Method), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(NoValidate), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Rel), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Target), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute>
		BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		SetXamlAttribute(
			attributes,
			"HtmlFormState.IsFormOwner",
			"True",
			null);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlLabelDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "label")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(
			XamlElementObjectType.HtmlFormLabelPanel,
			XamlElementMappingKind.InlineFlow,
			true,
			"The label element owns label phrasing and a labelable-control "
				+ "activation connection.");
	[HtmlElementProperty] public DomElementStringProperty For { get; } = Attribute("for", "label");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes((nameof(For), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.DirectChildren,
			HasGeneratedXamlContent()
				? XamlElementContentProjectionKind.Composite
				: XamlElementContentProjectionKind.DirectChildren);

	protected override bool HasGeneratedXamlContent() =>
		!string.IsNullOrWhiteSpace(
			RuntimeInitialization("content.ownText"));

	protected override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans()
	{
		var plans = new List<XamlElementObjectPlan>();
		var ownText = DataSource("content.ownText");
		var value = DataSourceValue("content.ownText");
		if (ownText is not null && !string.IsNullOrWhiteSpace(value))
		{
			plans.Add(CreateSyntheticXamlObjectPlan(
				XamlElementObjectType.TextBlock,
				[new("Text", value, ownText)],
				ElementXamlChildPlacementKind.None,
				[],
				$"Label text for {DocumentScope}::{XPath}."));
		}
		plans.AddRange(BuildDirectDomChildObjectPlans());
		return plans;
	}

	protected override IReadOnlyList<GeneratedXamlAttribute>
		BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		SetXamlAttribute(
			attributes,
			"TargetId",
			SourceInitialization(For),
			For);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlButtonDomElement(DomElementMapping m) :
	HtmlSubmitterDomElementDefinition(
		m, "button", ElementContentModel.Phrasing, ElementClosure.OpenContainer,
		ElementSyntax.Normal, XamlControlFamily.Button)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		FormattingContext() is
			HtmlCssFormattingContext.FlexRow
			or HtmlCssFormattingContext.FlexColumn
				? new(
					XamlElementObjectType.HtmlInteractiveFlexPanel,
					XamlElementMappingKind.FlexLayout,
					true,
					"The button element is an interactive multi-child "
						+ "flex formatting context.")
				: new(
					XamlElementObjectType.HtmlFormButton,
					XamlElementMappingKind.TypeDefault,
					false,
					"The button element is a strongly typed submit, reset, "
						+ "or command control.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		CreateXaml().Kind == XamlElementMappingKind.FlexLayout
			? new(
				CreateXaml(),
				ElementXamlChildPlacementKind.DirectChildren,
				XamlElementContentProjectionKind.Composite)
			: base.ResolveXamlObjectProjection();

	protected override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans() =>
		CreateXaml().Kind == XamlElementMappingKind.FlexLayout
			? BuildDirectDomChildObjectPlans()
			: base.BuildXamlObjectChildPlans();

	protected override bool TryBuildXamlFlexLayout(
		out bool vertical,
		out IReadOnlyList<XamlGridTrackDefinition> definitions)
	{
		if (CreateXaml().Kind != XamlElementMappingKind.FlexLayout)
		{
			vertical = false;
			definitions = [];
			return false;
		}
		return TryBuildStrongTextVisualFlexLayout(out vertical, out definitions);
		/* The retained legacy body below is unreachable and remains only until
		 * the project-wide no-deletion gate permits archival cleanup. */
#pragma warning disable CS0162
		vertical = ActiveRuntimeValue(this, "style.flexDirection")
			?.StartsWith("column", StringComparison.OrdinalIgnoreCase) == true;
		var verticalAxis = vertical;
		var children = Children
			.Where(static child =>
				ActiveRuntimeValue(child, "style.position")
					is not ("absolute" or "fixed"))
			.Select(static (child, sourceIndex) => new
			{
				Child = child,
				SourceIndex = sourceIndex,
				Order = ParseButtonFlexNumber(
					ActiveRuntimeValue(child, "style.order"),
					0)
			})
			.OrderBy(static item => item.Order)
			.ThenBy(static item => item.SourceIndex)
			.Select(static item => item.Child)
			.ToArray();
		if (children.Length == 0)
		{
			definitions = [];
			return false;
		}
		var extent = ResolveButtonFlexContentExtent(verticalAxis);
		var gap = ParseButtonFlexLength(ActiveRuntimeValue(
			this,
			verticalAxis ? "style.rowGap" : "style.columnGap"));
		var bases = children
			.Select(child => ResolveButtonFlexBase(child, verticalAxis))
			.ToArray();
		var margins = children
			.Select(child => ResolveButtonFlexMargins(child, verticalAxis))
			.ToArray();
		var grow = children
			.Select(child => Math.Max(
				0,
				ParseButtonFlexNumber(
					ActiveRuntimeValue(child, "style.flexGrow"),
					0)))
			.ToArray();
		var shrink = children
			.Select(child => Math.Max(
				0,
				ParseButtonFlexNumber(
					ActiveRuntimeValue(child, "style.flexShrink"),
					1)))
			.ToArray();
		var lengths = bases.ToArray();
		if (extent > 0)
		{
			var available = Math.Max(
				0,
				extent
					- gap * Math.Max(0, children.Length - 1)
					- margins.Sum());
			var free = available - bases.Sum();
			if (free > 0 && grow.Sum() > 0)
			{
				var totalGrow = grow.Sum();
				for (var index = 0; index < lengths.Length; index++)
					lengths[index] += free * grow[index] / totalGrow;
			}
			else if (free < 0)
			{
				var weights = shrink
					.Select((factor, index) => factor * bases[index])
					.ToArray();
				var totalWeight = weights.Sum();
				if (totalWeight > 0)
				{
					for (var index = 0; index < lengths.Length; index++)
					{
						lengths[index] = Math.Max(
							0,
							lengths[index] + free * weights[index] / totalWeight);
					}
				}
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
				lengths[index].ToString(
					"R",
					System.Globalization.CultureInfo.InvariantCulture),
				NormalizeXamlLength(ActiveRuntimeValue(child, $"style.min{axis}")),
				NormalizeXamlLength(ActiveRuntimeValue(child, $"style.max{axis}")));
		}).ToArray();
		return definitions.Count > 0;
#pragma warning restore CS0162
	}

	protected override bool TryResolveXamlLayoutTrackIndex(
		DomElement child,
		out bool vertical,
		out int index)
	{
		return TryResolveStrongTextVisualFlexTrackIndex(
			child,
			out vertical,
			out index);
		/* Retained under the no-deletion gate; the inherited strong planner is
		 * the only active path. */
#pragma warning disable CS0162
		if (!TryBuildXamlFlexLayout(out vertical, out var definitions))
		{
			index = -1;
			return false;
		}
		var ordered = Children
			.Where(static candidate =>
				ActiveRuntimeValue(candidate, "style.position")
					is not ("absolute" or "fixed"))
			.Select(static (candidate, sourceIndex) => new
			{
				Candidate = candidate,
				SourceIndex = sourceIndex,
				Order = ParseButtonFlexNumber(
					ActiveRuntimeValue(candidate, "style.order"),
					0)
			})
			.OrderBy(static item => item.Order)
			.ThenBy(static item => item.SourceIndex)
			.Select(static item => item.Candidate)
			.ToArray();
		index = Array.IndexOf(ordered, child);
		if (index < 0 || index >= definitions.Count)
			return false;
		if (ActiveRuntimeValue(this, "style.flexDirection")
			?.EndsWith("-reverse", StringComparison.OrdinalIgnoreCase) == true)
		{
			index = definitions.Count - index - 1;
		}
		return true;
#pragma warning restore CS0162
	}

	private double ResolveButtonFlexContentExtent(bool vertical)
	{
		var axis = vertical ? "style.height" : "style.width";
		var extent = ParseButtonFlexLength(ActiveRuntimeValue(this, axis));
		if (extent <= 0
			|| !string.Equals(
				ActiveRuntimeValue(this, "style.boxSizing"),
				"border-box",
				StringComparison.OrdinalIgnoreCase))
		{
			return extent;
		}
		return Math.Max(
			0,
			extent
				- ResolveButtonFlexEdge(this, vertical ? "style.paddingTop" : "style.paddingLeft")
				- ResolveButtonFlexEdge(this, vertical ? "style.paddingBottom" : "style.paddingRight")
				- ResolveButtonFlexEdge(this, vertical ? "style.borderTopWidth" : "style.borderLeftWidth")
				- ResolveButtonFlexEdge(this, vertical ? "style.borderBottomWidth" : "style.borderRightWidth"));
	}

	private static double ResolveButtonFlexBase(DomElement child, bool vertical)
	{
		var basis = ActiveRuntimeValue(child, "style.flexBasis");
		var value = !string.IsNullOrWhiteSpace(basis)
			&& basis is not ("auto" or "content")
				? ParseButtonFlexLength(basis)
				: ParseButtonFlexLength(ActiveRuntimeValue(
					child,
					vertical ? "style.height" : "style.width"));
		if (string.Equals(
			ActiveRuntimeValue(child, "style.boxSizing"),
			"border-box",
			StringComparison.OrdinalIgnoreCase))
		{
			return value;
		}
		return value
			+ ResolveButtonFlexEdge(child, vertical ? "style.paddingTop" : "style.paddingLeft")
			+ ResolveButtonFlexEdge(child, vertical ? "style.paddingBottom" : "style.paddingRight")
			+ ResolveButtonFlexEdge(child, vertical ? "style.borderTopWidth" : "style.borderLeftWidth")
			+ ResolveButtonFlexEdge(child, vertical ? "style.borderBottomWidth" : "style.borderRightWidth");
	}

	private static double ResolveButtonFlexMargins(DomElement child, bool vertical) =>
		ResolveButtonFlexEdge(child, vertical ? "style.marginTop" : "style.marginLeft")
		+ ResolveButtonFlexEdge(child, vertical ? "style.marginBottom" : "style.marginRight");

	private static double ResolveButtonFlexEdge(DomElement element, string name) =>
		ParseButtonFlexLength(ActiveRuntimeValue(element, name));

	private static double ParseButtonFlexLength(string? value)
	{
		var token = value?.Trim() ?? string.Empty;
		if (token.EndsWith("px", StringComparison.OrdinalIgnoreCase))
			token = token[..^2];
		return double.TryParse(
			token,
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out var result)
			? result
			: 0;
	}

	private static double ParseButtonFlexNumber(string? value, double fallback) =>
		double.TryParse(
			value,
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out var result)
			? result
			: fallback;

	private static XamlGridTrackDefinition BuildButtonFlexTrack(
		DomElement child,
		bool vertical)
	{
		if (child is not HtmlDomElementDefinition html)
			return new("Auto", null, null);
		var properties = new[]
		{
			"style.flexGrow",
			"style.width",
			"style.height",
			"style.minWidth",
			"style.maxWidth",
			"style.minHeight",
			"style.maxHeight"
		}
			.Select(name => html.RuntimeProperties.FirstOrDefault(property =>
				property.Name.Equals(name, StringComparison.Ordinal))
				?? html.HtmlRoot?.ResolveGlobalStyleProperty(html, name))
			.Where(static property => property is not null)
			.Cast<DomElementRuntimeProperty>()
			.ToDictionary(
				static property => property.Name,
				StringComparer.Ordinal);
		var length = "Auto";
		if (properties.TryGetValue("style.flexGrow", out var grow)
			&& double.TryParse(
				ActiveRuntimeValue(grow),
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out var growth)
			&& growth > 0)
		{
			length = growth == 1
				? "*"
				: $"{growth.ToString(
					"R",
					System.Globalization.CultureInfo.InvariantCulture)}*";
		}
		else if (properties.TryGetValue(
			vertical ? "style.height" : "style.width",
			out var size))
		{
			length = NormalizeXamlLength(SourceInitialization(size))
				?? NormalizeXamlLength(ActiveRuntimeValue(size))
				?? "Auto";
		}
		var axis = vertical ? "Height" : "Width";
		var minimum = properties.TryGetValue($"style.min{axis}", out var min)
			? NormalizeXamlLength(SourceInitialization(min))
				?? NormalizeXamlLength(ActiveRuntimeValue(min))
			: null;
		var maximum = properties.TryGetValue($"style.max{axis}", out var max)
			? NormalizeXamlLength(SourceInitialization(max))
				?? NormalizeXamlLength(ActiveRuntimeValue(max))
			: null;
		return new(length, minimum, maximum);
	}

	private static string? ActiveRuntimeValue(
		DomElement element,
		string propertyName)
	{
		var global = element.HtmlRoot?.ResolveGlobalStyleValue(
				element,
				propertyName,
				DomPropertyDataSlot.Runtime)
			?? element.HtmlRoot?.ResolveGlobalStyleValue(
				element,
				propertyName,
				DomPropertyDataSlot.Initialization);
		if (!string.IsNullOrWhiteSpace(global))
			return global.Trim();
		var property = element switch
		{
			HtmlDomElementDefinition html => html.RuntimeProperties.FirstOrDefault(
				candidate => candidate.Name.Equals(
					propertyName,
					StringComparison.Ordinal)),
			SvgDomElementDefinition svg => svg.RuntimeProperties.FirstOrDefault(
				candidate => candidate.Name.Equals(
					propertyName,
					StringComparison.Ordinal)),
			_ => null
		};
		return ActiveRuntimeValue(property);
	}

	private static string? ActiveRuntimeValue(
		DomElementRuntimeProperty? property) =>
		property?.SourceRuntime.IsSet == true
			? property.SourceRuntime.Value?.Trim()
			: property?.SourceInitialization.IsSet == true
				? property.SourceInitialization.Value?.Trim()
				: null;

	[HtmlElementProperty] public DomElementStringProperty Command { get; } = Attribute("command", "button");
	[HtmlElementProperty] public DomElementStringProperty CommandFor { get; } = Attribute("commandfor", "button");
	[HtmlElementProperty] public DomElementStringProperty PopoverTarget { get; } = Attribute("popovertarget", "button");
	[HtmlElementProperty] public DomElementStringProperty PopoverTargetAction { get; } = Attribute("popovertargetaction", "button");
	[HtmlElementProperty] public DomElementStringProperty Type { get; } = Attribute("type", "button");
	[HtmlElementProperty] public DomElementStringProperty Value { get; } = Attribute("value", "button");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Command), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(CommandFor), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Disabled), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Form), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(FormEncodingType), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(FormMethod), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(FormNoValidate), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(FormTarget), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(PopoverTarget), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(PopoverTargetAction), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Type), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Value), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		ApplyDisabledAttribute(attributes, Disabled);
		SetXamlAttribute(
			attributes,
			"CommandKind",
			SourceInitialization(Type) ?? "submit",
			Type);
		SetXamlAttribute(
			attributes,
			"FormOwnerId",
			SourceInitialization(Form),
			Form);
		SetXamlAttribute(
			attributes,
			"FormAction",
			SourceInitialization(FormAction),
			FormAction);
		return attributes.Values.ToArray();
	}

	public bool IsDisabledState => ResolveDisabledState(Disabled);
}
public sealed partial class HtmlInputDomElement(DomElementMapping m) :
	HtmlSubmitterDomElementDefinition(
		m, "input", ElementContentModel.None, ElementClosure.ClosedLeaf,
		ElementSyntax.Void, XamlControlFamily.Input)
{
	protected override void ValidateXamlBuildReadiness()
	{
		if (!HasCompletedDomFill)
		{
			throw new InvalidOperationException(
				$"Input {DocumentScope}::{XPath} must complete DOM Fill before "
					+ "its type-specific XAML object can be selected.");
		}
		base.ValidateXamlBuildReadiness();
	}

	public HtmlInputTypeState InputType =>
		HtmlXamlSemanticState.ParseInputType(SourceInitialization(Type));

	public bool IsDisabledState => ResolveDisabledState(Disabled);

	public bool IsCheckedState => IsTrueState(SourceInitialization(Checked));

	public bool IsReadOnlyState => IsTrueState(SourceInitialization(ReadOnly));

	protected override XamlElementMappingDecision CreateXaml() =>
		InputType switch
		{
			HtmlInputTypeState.Hidden => new(
				XamlElementObjectType.ContentControl,
				XamlElementMappingKind.TypeDefault,
				false,
				"The hidden input retains form state in a collapsed object."),
			HtmlInputTypeState.Checkbox => new(
				XamlElementObjectType.CheckBox,
				XamlElementMappingKind.TypeDefault,
				false,
				"The checkbox input is a two-state selectable control."),
			HtmlInputTypeState.Radio => new(
				XamlElementObjectType.RadioButton,
				XamlElementMappingKind.TypeDefault,
				false,
				"The radio input is a mutually-exclusive selectable control."),
			HtmlInputTypeState.Range => new(
				XamlElementObjectType.Slider,
				XamlElementMappingKind.TypeDefault,
				false,
				"The range input is a bounded scalar slider."),
			HtmlInputTypeState.Number => new(
				XamlElementObjectType.NumberBox,
				XamlElementMappingKind.TypeDefault,
				false,
				"The number input is a numeric editor."),
			HtmlInputTypeState.Date => new(
					XamlElementObjectType.HtmlDateInputControl,
					XamlElementMappingKind.TypeDefault,
					false,
					"The date input uses a strongly typed date editor."),
			HtmlInputTypeState.Month => new(
					XamlElementObjectType.HtmlMonthInputControl,
					XamlElementMappingKind.TypeDefault,
					false,
					"The month input uses a strongly typed month editor."),
			HtmlInputTypeState.Week => new(
					XamlElementObjectType.HtmlWeekInputControl,
					XamlElementMappingKind.TypeDefault,
					false,
					"The week input uses a strongly typed week editor."),
			HtmlInputTypeState.Time => new(
				XamlElementObjectType.HtmlTimeInputControl,
				XamlElementMappingKind.TypeDefault,
				false,
				"The time input uses a strongly typed time editor."),
			HtmlInputTypeState.DateTimeLocal => new(
				XamlElementObjectType.HtmlDateTimeLocalInputControl,
				XamlElementMappingKind.TypeDefault,
				false,
				"The local date-time input owns a strongly typed composite editor."),
			HtmlInputTypeState.Color => new(
				XamlElementObjectType.ColorPicker,
				XamlElementMappingKind.TypeDefault,
				false,
				"The color input uses a color picker."),
			HtmlInputTypeState.Password => new(
				XamlElementObjectType.PasswordBox,
				XamlElementMappingKind.TypeDefault,
				false,
				"The password input prevents plain-text display."),
			HtmlInputTypeState.File => new(
					XamlElementObjectType.HtmlFileInputControl,
					XamlElementMappingKind.TypeDefault,
					false,
					"The file input owns a file-selection command surface."),
			HtmlInputTypeState.Image => new(
					XamlElementObjectType.HtmlImageSubmitButton,
					XamlElementMappingKind.TypeDefault,
					false,
					"The image input owns an image-backed submit surface."),
			HtmlInputTypeState.Submit
				or HtmlInputTypeState.Reset
				or HtmlInputTypeState.Button => new(
					XamlElementObjectType.HtmlFormButton,
					XamlElementMappingKind.TypeDefault,
					false,
					"The command-like input is represented by a button control."),
			HtmlInputTypeState.Text
				or HtmlInputTypeState.Search
				or HtmlInputTypeState.Telephone
				or HtmlInputTypeState.Url
				or HtmlInputTypeState.Email
				when ResolveDatalistValues().Count != 0 => new(
					XamlElementObjectType.HtmlDatalistInputControl,
					XamlElementMappingKind.TypeDefault,
					false,
					"The text-family input is connected to its datalist "
						+ "suggestion source."),
			_ => new(
				XamlElementObjectType.TextBox,
				XamlElementMappingKind.TypeDefault,
				false,
				"The text-family input is represented by a text editor.")
		};

	protected override bool HasXamlOutput() => true;

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.None,
			XamlElementContentProjectionKind.GeneratedContent);

	protected override void ToXaml(
		System.Text.StringBuilder output,
		int depth,
		bool isDocumentRoot)
	{
		base.ToXaml(output, depth, isDocumentRoot);
	}

	protected internal override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectPlans() =>
		base.BuildXamlObjectPlans();

	[HtmlElementProperty] public DomElementStringProperty Accept { get; } = Attribute("accept", "input");
	[HtmlElementProperty] public DomElementStringProperty Alpha { get; } = Attribute("alpha", "input");
	[HtmlElementProperty] public DomElementStringProperty Alt { get; } = Attribute("alt", "input");
	[HtmlElementProperty] public DomElementStringProperty AutoComplete { get; } = Attribute("autocomplete", "input");
	[HtmlElementProperty] public DomElementStringProperty Checked { get; } = Attribute("checked", "input");
	[HtmlElementProperty] public DomElementStringProperty ColorSpace { get; } = Attribute("colorspace", "input");
	[HtmlElementProperty] public DomElementStringProperty DirectionName { get; } = Attribute("dirname", "input");
	[HtmlElementProperty] public DomElementStringProperty Height { get; } = Attribute("height", "input");
	[HtmlElementProperty] public DomElementStringProperty List { get; } = Attribute("list", "input");
	[HtmlElementProperty] public DomElementStringProperty Maximum { get; } = Attribute("max", "input");
	[HtmlElementProperty] public DomElementStringProperty MaximumLength { get; } = Attribute("maxlength", "input");
	[HtmlElementProperty] public DomElementStringProperty Minimum { get; } = Attribute("min", "input");
	[HtmlElementProperty] public DomElementStringProperty MinimumLength { get; } = Attribute("minlength", "input");
	[HtmlElementProperty] public DomElementStringProperty Multiple { get; } = Attribute("multiple", "input");
	[HtmlElementProperty] public DomElementStringProperty Pattern { get; } = Attribute("pattern", "input");
	[HtmlElementProperty] public DomElementStringProperty Placeholder { get; } = Attribute("placeholder", "input");
	[HtmlElementProperty] public DomElementStringProperty PopoverTarget { get; } = Attribute("popovertarget", "input");
	[HtmlElementProperty] public DomElementStringProperty PopoverTargetAction { get; } = Attribute("popovertargetaction", "input");
	[HtmlElementProperty] public DomElementStringProperty ReadOnly { get; } = Attribute("readonly", "input");
	[HtmlElementProperty] public DomElementStringProperty Required { get; } = Attribute("required", "input");
	[HtmlElementProperty] public DomElementStringProperty Size { get; } = Attribute("size", "input");
	[HtmlElementProperty] public DomElementStringProperty Source { get; } = Attribute("src", "input");
	[HtmlElementProperty] public DomElementStringProperty Step { get; } = Attribute("step", "input");
	[HtmlElementProperty] public DomElementStringProperty Type { get; } = Attribute("type", "input");
	[HtmlElementProperty] public DomElementStringProperty Value { get; } = Attribute("value", "input");
	[HtmlElementProperty] public DomElementStringProperty Width { get; } = Attribute("width", "input");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Accept), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Alpha), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Alt), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(AutoComplete), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Checked), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(ColorSpace), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(DirectionName), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Disabled), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Form), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(FormEncodingType), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(FormMethod), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(FormNoValidate), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(FormTarget), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Height), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(List), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Maximum), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(MaximumLength), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Minimum), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(MinimumLength), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Multiple), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Pattern), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Placeholder), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(PopoverTarget), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(PopoverTargetAction), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(ReadOnly), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Required), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Size), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Step), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Type), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Value), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Width), HtmlElementAttributeXamlHandling.InlineXaml));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		var runtimePlaceholder = RuntimeProperty("content.placeholder");
		var placeholder = RuntimeValue("content.placeholder")
			?? SourceInitialization(Placeholder);
		var runtimeValue = RuntimeProperty("content.value");
		var value = RuntimeValue("content.value")
			?? SourceInitialization(Value);
		IXamlPropertySlotOwner valueSource =
			runtimeValue is not null ? runtimeValue : Value;
		switch (InputType)
		{
			case HtmlInputTypeState.Checkbox:
			case HtmlInputTypeState.Radio:
				var checkedState = RuntimeValue("state.checked")
					?? SourceInitialization(Checked);
				var checkedOwner = (IXamlPropertySlotOwner?)
					RuntimeProperty("state.checked") ?? Checked;
				var indeterminateState =
					RuntimeValue("state.indeterminate");
				if (InputType == HtmlInputTypeState.Checkbox
					&& IsTrueState(indeterminateState))
				{
					SetXamlAttribute(
						attributes,
						"IsThreeState",
						"True",
						RuntimeProperty("state.indeterminate"));
					SetXamlAttribute(
						attributes,
						"IsChecked",
						"null",
						RuntimeProperty("state.indeterminate"));
				}
				else
				{
					SetXamlAttribute(
						attributes,
						"IsChecked",
						IsTrueState(checkedState) ? "True" : "False",
						checkedOwner);
				}
				SetXamlAttribute(
					attributes,
					"Content",
					SourceInitialization(Alt) ?? value,
					valueSource);
				break;
			case HtmlInputTypeState.Range:
				SetXamlAttribute(attributes, "Value", value, valueSource);
				SetXamlAttribute(attributes, "Minimum", SourceInitialization(Minimum), Minimum);
				SetXamlAttribute(attributes, "Maximum", SourceInitialization(Maximum), Maximum);
				SetXamlAttribute(attributes, "StepFrequency", SourceInitialization(Step), Step);
				break;
			case HtmlInputTypeState.Number:
				SetXamlAttribute(attributes, "Value", value, valueSource);
				SetXamlAttribute(attributes, "Minimum", SourceInitialization(Minimum), Minimum);
				SetXamlAttribute(attributes, "Maximum", SourceInitialization(Maximum), Maximum);
				SetXamlAttribute(attributes, "SmallChange", SourceInitialization(Step), Step);
				SetXamlAttribute(
					attributes,
					"PlaceholderText",
					placeholder,
					runtimePlaceholder is not null ? runtimePlaceholder : Placeholder);
				break;
			case HtmlInputTypeState.Password:
				SetXamlAttribute(attributes, "Password", value, valueSource);
				SetXamlAttribute(
					attributes,
					"PlaceholderText",
					placeholder,
					runtimePlaceholder is not null ? runtimePlaceholder : Placeholder);
				SetXamlAttribute(
					attributes,
					"MaxLength",
					SourceInitialization(MaximumLength),
					MaximumLength);
				if (ReadOnly.SourceInitialization.IsSet)
				{
					SetXamlAttribute(
						attributes,
						"IsHitTestVisible",
						"False",
						ReadOnly);
					SetXamlAttribute(
						attributes,
						"IsTabStop",
						"False",
						ReadOnly);
				}
				break;
			case HtmlInputTypeState.File:
				SetXamlAttribute(
					attributes,
					"Content",
					SourceInitialization(Alt) ?? value ?? "Choose file",
					valueSource);
				SetXamlAttribute(
					attributes,
					"Accept",
					SourceInitialization(Accept),
					Accept);
				SetXamlAttribute(
					attributes,
					"AllowsMultiple",
					Multiple.SourceInitialization.IsSet ? "True" : "False",
					Multiple);
				break;
			case HtmlInputTypeState.Submit:
				SetXamlAttribute(attributes, "Content", value ?? "Submit", valueSource);
				ApplyFormCommandAttributes(attributes, "submit");
				break;
			case HtmlInputTypeState.Reset:
				SetXamlAttribute(attributes, "Content", value ?? "Reset", valueSource);
				ApplyFormCommandAttributes(attributes, "reset");
				break;
			case HtmlInputTypeState.Button:
				SetXamlAttribute(
					attributes,
					"Content",
					value ?? SourceInitialization(Alt),
					valueSource);
				ApplyFormCommandAttributes(attributes, "button");
				break;
			case HtmlInputTypeState.Image:
				SetXamlAttribute(
					attributes,
					"Source",
					SourceInitialization(Source),
					Source);
				SetXamlAttribute(
					attributes,
					"AutomationProperties.Name",
					SourceInitialization(Alt),
					Alt);
				SetXamlAttribute(
					attributes,
					"Width",
					NormalizeXamlLength(SourceInitialization(Width)),
					Width);
				SetXamlAttribute(
					attributes,
					"Height",
					NormalizeXamlLength(SourceInitialization(Height)),
					Height);
				ApplyFormCommandAttributes(attributes, "submit");
				break;
			case HtmlInputTypeState.Date:
			case HtmlInputTypeState.Month:
			case HtmlInputTypeState.Week:
			case HtmlInputTypeState.Time:
			case HtmlInputTypeState.DateTimeLocal:
				SetXamlAttribute(attributes, "Value", value, valueSource);
				SetXamlAttribute(
					attributes,
					"Minimum",
					SourceInitialization(Minimum),
					Minimum);
				SetXamlAttribute(
					attributes,
					"Maximum",
					SourceInitialization(Maximum),
					Maximum);
				SetXamlAttribute(
					attributes,
					"Step",
					SourceInitialization(Step),
					Step);
				if (ReadOnly.SourceInitialization.IsSet)
					SetXamlAttribute(attributes, "IsReadOnly", "True", ReadOnly);
				break;
			case HtmlInputTypeState.Color:
				SetXamlAttribute(attributes, "Color", value, valueSource);
				break;
			case HtmlInputTypeState.Hidden:
				SetXamlAttribute(
					attributes,
					"Visibility",
					"Collapsed",
					Type);
				break;
			default:
				SetXamlAttribute(
					attributes,
					"PlaceholderText",
					placeholder,
					runtimePlaceholder is not null ? runtimePlaceholder : Placeholder);
				SetXamlAttribute(attributes, "Text", value, valueSource);
				SetXamlAttribute(
					attributes,
					"MaxLength",
					SourceInitialization(MaximumLength),
					MaximumLength);
				if (ResolveDatalistValues() is { Count: > 0 } suggestions)
				{
					SetXamlAttribute(
						attributes,
						"SuggestionValues",
						string.Join('\u001F', suggestions),
						List);
				}
				break;
		}
		ApplyDisabledAttribute(attributes, Disabled);
		ApplyValidationAttributes(attributes);
		SetXamlAttribute(
			attributes,
			"HtmlFormState.InitialValue",
			SourceInitialization(Value) ?? string.Empty,
			Value);
		SetXamlAttribute(
			attributes,
			"HtmlFormState.InitialChecked",
			Checked.SourceInitialization.IsSet ? "True" : "False",
			Checked);
		SetXamlAttribute(
			attributes,
			"HtmlFormState.FormOwnerId",
			SourceInitialization(Form) ?? string.Empty,
			Form);
		if (ReadOnly.SourceInitialization.IsSet
			&& InputType is
				HtmlInputTypeState.Text
				or HtmlInputTypeState.Search
				or HtmlInputTypeState.Telephone
				or HtmlInputTypeState.Url
				or HtmlInputTypeState.Email
				or HtmlInputTypeState.Number)
		{
			SetXamlAttribute(attributes, "IsReadOnly", "True", ReadOnly);
		}
		return attributes.Values.ToArray();
	}

	private IReadOnlyList<string> ResolveDatalistValues()
	{
		var listId = SourceInitialization(List);
		if (string.IsNullOrWhiteSpace(listId) || HtmlRoot is null)
			return [];
		var datalist = HtmlRoot.DocumentRoots
			.SelectMany(EnumeratePreOrder)
			.OfType<HtmlDataListDomElement>()
			.FirstOrDefault(candidate =>
				candidate.Id.SourceInitialization.Value is { } id
				&& id.Equals(
					listId,
					StringComparison.Ordinal));
		if (datalist is null)
			return [];
		return datalist.Children
			.OfType<HtmlOptionDomElement>()
			.Select(static option =>
				option.Value.SourceInitialization.Value
				?? option.Label.SourceInitialization.Value
				?? option.DataSources
					.FirstOrDefault(static property =>
						property.Name.Equals(
							"content.ownText",
							StringComparison.Ordinal))
					?.SourceInitialization.Value)
			.Where(static value => !string.IsNullOrWhiteSpace(value))
			.Select(static value => value!)
			.Distinct(StringComparer.Ordinal)
			.ToArray();
	}

	private static IEnumerable<DomElement> EnumeratePreOrder(
		DomElement root)
	{
		yield return root;
		foreach (var child in root.Children)
			foreach (var descendant in EnumeratePreOrder(child))
				yield return descendant;
	}

	private void ApplyFormCommandAttributes(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string commandKind)
	{
		SetXamlAttribute(attributes, "CommandKind", commandKind, Type);
		SetXamlAttribute(
			attributes,
			"FormOwnerId",
			SourceInitialization(Form),
			Form);
		SetXamlAttribute(
			attributes,
			"FormAction",
			SourceInitialization(FormAction),
			FormAction);
	}

	private static bool IsTrueState(string? value) =>
		value is not null
			&& (value.Equals("true", StringComparison.OrdinalIgnoreCase)
				|| value == "1"
				|| value.Length == 0);
}
public sealed partial class HtmlTextAreaDomElement(DomElementMapping m) :
	HtmlOwnedFormControlDomElementDefinition(
		m, "textarea", ElementContentModel.TextOnly, ElementClosure.OpenContainer,
		ElementSyntax.Normal, XamlControlFamily.Input)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.TextBox, XamlElementMappingKind.TypeDefault, false, "The textarea element is a multiline text input control.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.None,
			XamlElementContentProjectionKind.GeneratedContent);

	public bool IsReadOnlyState =>
		SourceInitialization(ReadOnly) is { } value
		&& !value.Equals("false", StringComparison.OrdinalIgnoreCase);

	[HtmlElementProperty] public DomElementStringProperty AutoComplete { get; } = Attribute("autocomplete", "textarea");
	[HtmlElementProperty] public DomElementStringProperty Columns { get; } = Attribute("cols", "textarea");
	[HtmlElementProperty] public DomElementStringProperty DirectionName { get; } = Attribute("dirname", "textarea");
	[HtmlElementProperty] public DomElementStringProperty MaximumLength { get; } = Attribute("maxlength", "textarea");
	[HtmlElementProperty] public DomElementStringProperty MinimumLength { get; } = Attribute("minlength", "textarea");
	[HtmlElementProperty] public DomElementStringProperty Placeholder { get; } = Attribute("placeholder", "textarea");
	[HtmlElementProperty] public DomElementStringProperty ReadOnly { get; } = Attribute("readonly", "textarea");
	[HtmlElementProperty] public DomElementStringProperty Required { get; } = Attribute("required", "textarea");
	[HtmlElementProperty] public DomElementStringProperty Rows { get; } = Attribute("rows", "textarea");
	[HtmlElementProperty] public DomElementStringProperty Wrap { get; } = Attribute("wrap", "textarea");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(AutoComplete), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Columns), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(DirectionName), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Disabled), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Form), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(MaximumLength), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(MinimumLength), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Placeholder), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(ReadOnly), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Required), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Rows), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Wrap), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		var runtimePlaceholder = RuntimeProperty("content.placeholder");
		SetXamlAttribute(
			attributes,
			"PlaceholderText",
			RuntimeValue("content.placeholder")
				?? SourceInitialization(Placeholder),
			runtimePlaceholder ?? (IXamlPropertySlotOwner)Placeholder);
		SetXamlAttribute(
			attributes,
			"MaxLength",
			SourceInitialization(MaximumLength),
			MaximumLength);
		IXamlPropertySlotOwner? textValueOwner =
			DataSource("content.value") ?? (IXamlPropertySlotOwner?)
				RuntimeProperty("content.value");
		SetXamlAttribute(
			attributes,
			"Text",
			DataSourceValue("content.value")
				?? RuntimeValue("content.value"),
			textValueOwner);
		SetXamlAttribute(attributes, "AcceptsReturn", "True", Rows);
		SetXamlAttribute(attributes, "TextWrapping", "Wrap", Wrap);
		if (int.TryParse(
			SourceInitialization(Rows),
			System.Globalization.NumberStyles.Integer,
			System.Globalization.CultureInfo.InvariantCulture,
			out var rows)
			&& rows > 0)
		{
			SetXamlAttribute(
				attributes,
				"MinHeight",
				(rows * 20d).ToString(
					"R",
					System.Globalization.CultureInfo.InvariantCulture),
				Rows);
		}
		if (int.TryParse(
			SourceInitialization(Columns),
			System.Globalization.NumberStyles.Integer,
			System.Globalization.CultureInfo.InvariantCulture,
			out var columns)
			&& columns > 0)
		{
			SetXamlAttribute(
				attributes,
				"MinWidth",
				(columns * 8d).ToString(
					"R",
					System.Globalization.CultureInfo.InvariantCulture),
				Columns);
		}
		ApplyDisabledAttribute(attributes, Disabled);
		ApplyValidationAttributes(attributes);
		SetXamlAttribute(
			attributes,
			"HtmlFormState.InitialValue",
			DataSource("content.value")?.SourceInitialization.Value
				?? string.Empty,
			DataSource("content.value"));
		SetXamlAttribute(
			attributes,
			"HtmlFormState.FormOwnerId",
			SourceInitialization(Form) ?? string.Empty,
			Form);
		if (ReadOnly.SourceInitialization.IsSet)
			SetXamlAttribute(attributes, "IsReadOnly", "True", ReadOnly);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlSelectDomElement(DomElementMapping m) :
	HtmlOwnedFormControlDomElementDefinition(
		m, "select", ElementContentModel.FormOptions, ElementClosure.OpenContainer,
		ElementSyntax.Normal, XamlControlFamily.Selector)
{
	public HtmlSelectPresentationState Presentation =>
		HtmlXamlSemanticState.ResolveSelectPresentation(
			Multiple.SourceInitialization.IsSet,
			int.TryParse(
				SourceInitialization(Size),
				System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture,
				out var size)
					? size
					: null);

	protected override XamlElementMappingDecision CreateXaml() =>
		Presentation switch
		{
			HtmlSelectPresentationState.DropDownSingleSelection => new(
				XamlElementObjectType.ComboBox,
				XamlElementMappingKind.TypeDefault,
				false,
				"The compact single-select element is a drop-down selector."),
			HtmlSelectPresentationState.VisibleSingleSelectionList => new(
				XamlElementObjectType.ListView,
				XamlElementMappingKind.TypeDefault,
				false,
				"The sized single-select element is a visible selection list."),
			HtmlSelectPresentationState.VisibleMultipleSelectionList => new(
				XamlElementObjectType.ListView,
				XamlElementMappingKind.TypeDefault,
				false,
				"The multiple select element is a multiple-selection list."),
			_ => throw new InvalidOperationException(
				$"Unsupported select presentation {Presentation}.")
		};
	protected internal override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectPlans()
	{
		var mapping = GetXamlElementMapping();
		return
		[
			new(
				this,
				mapping,
				BuildResolvedXamlAttributes(),
				ElementXamlChildPlacementKind.Items,
				[],
				[],
				BuildSelectItemPlans(),
				$"Select options for {DocumentScope}::{XPath}.")
		];
	}

	private IReadOnlyList<XamlElementObjectPlan> BuildSelectItemPlans()
	{
		var items = new List<XamlElementObjectPlan>();
		foreach (var child in Children)
		{
			if (child is HtmlOptionGroupDomElement group)
			{
				items.AddRange(group.BuildSelectItemPlans());
				continue;
			}
			items.AddRange(child.BuildXamlObjectPlans());
		}
		return items;
	}

	[HtmlElementProperty] public DomElementStringProperty AutoComplete { get; } = Attribute("autocomplete", "select");
	[HtmlElementProperty] public DomElementStringProperty Multiple { get; } = Attribute("multiple", "select");
	[HtmlElementProperty] public DomElementStringProperty Required { get; } = Attribute("required", "select");
	[HtmlElementProperty] public DomElementStringProperty Size { get; } = Attribute("size", "select");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(AutoComplete), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Disabled), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Form), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Multiple), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Required), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Size), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		ApplyDisabledAttribute(attributes, Disabled);
		ApplyValidationAttributes(attributes);
		SetXamlAttribute(
			attributes,
			"HtmlFormState.FormOwnerId",
			SourceInitialization(Form) ?? string.Empty,
			Form);
		if (Presentation == HtmlSelectPresentationState.VisibleMultipleSelectionList)
		{
			SetXamlAttribute(
				attributes,
				"SelectionMode",
				"Multiple",
				Multiple);
		}
		else if (Presentation == HtmlSelectPresentationState.VisibleSingleSelectionList)
		{
			SetXamlAttribute(
				attributes,
				"SelectionMode",
				"Single",
				Size);
		}
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlOptionDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "option")
{
	private HtmlSelectDomElement? OwnerSelect =>
		Parent as HtmlSelectDomElement
			?? Parent?.Parent as HtmlSelectDomElement;

	protected override XamlElementMappingDecision CreateXaml() =>
		OwnerSelect?.GetXamlElementMapping().ElementName == "ListView"
			? new(
				XamlElementObjectType.ListViewItem,
				XamlElementMappingKind.TypeDefault,
				false,
				"The option element is a visible-list selectable item.")
			: new(
				XamlElementObjectType.ComboBoxItem,
				XamlElementMappingKind.TypeDefault,
				false,
				"The option element is a drop-down selectable item.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.None,
			XamlElementContentProjectionKind.GeneratedContent);
	[HtmlElementProperty] public DomElementStringProperty Disabled { get; } = Attribute("disabled", "option");
	[HtmlElementProperty] public DomElementStringProperty Label { get; } = Attribute("label", "option");
	[HtmlElementProperty] public DomElementStringProperty Selected { get; } = Attribute("selected", "option");
	[HtmlElementProperty] public DomElementStringProperty Value { get; } = Attribute("value", "option");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Disabled), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Label), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Selected), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Value), HtmlElementAttributeXamlHandling.RuntimeDataSource));
	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		var disabled = RuntimeValue("state.disabled");
		SetXamlAttribute(
			attributes,
			"IsEnabled",
			disabled is not null
				? IsTrueState(disabled) ? "False" : "True"
				: Disabled.SourceInitialization.IsSet ? "False" : "True",
			(IXamlPropertySlotOwner?)
				RuntimeProperty("state.disabled") ?? Disabled);
		var selected = RuntimeValue("state.selected");
		SetXamlAttribute(
			attributes,
			"IsSelected",
			selected is not null
				? IsTrueState(selected) ? "True" : "False"
				: Selected.SourceInitialization.IsSet ? "True" : "False",
			(IXamlPropertySlotOwner?)
				RuntimeProperty("state.selected") ?? Selected);
		SetXamlAttribute(
			attributes,
			"HtmlFormState.InitialSelected",
			Selected.SourceInitialization.IsSet ? "True" : "False",
			Selected);
		SetXamlAttribute(attributes, "Content", SourceInitialization(Label), Label);
		return attributes.Values.ToArray();
	}

	private static bool IsTrueState(string value) =>
		value.Equals("true", StringComparison.OrdinalIgnoreCase)
			|| value == "1"
			|| value.Length == 0;
}
public sealed partial class HtmlFieldSetDomElement(DomElementMapping m) :
	HtmlSectioningFormOwnerDomElementDefinition(m, "fieldset")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlFieldSetPanel, XamlElementMappingKind.BlockFlow, true, "The fieldset element owns a legend header and form-control block flow.");
	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.DirectChildren,
			XamlElementContentProjectionKind.Composite);
	protected override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans() =>
		Children
			.Where(static child => child is not HtmlLegendDomElement)
			.Where(static child => child.XamlSupport != XamlConversionSupport.NonVisual)
			.SelectMany(static child => child.BuildXamlObjectPlans())
			.ToArray();
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Disabled), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Form), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource));
	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		if (Disabled.SourceInitialization.IsSet)
			SetXamlAttribute(attributes, "IsEnabled", "False", Disabled);
		var legend = Children.OfType<HtmlLegendDomElement>().FirstOrDefault();
		var legendText = legend?.DataSources.FirstOrDefault(
			static property => property.Name == "content.ownText");
		if (legendText?.SourceInitialization.IsSet == true)
		{
			SetXamlAttribute(
				attributes,
				"Header",
				legendText.SourceInitialization.Value,
				legendText);
		}
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlLegendDomElement(DomElementMapping m) : HtmlPhrasingDomElementDefinition(m, "legend")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The legend element is leaf fieldset caption text.")
			: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The legend element owns composite fieldset-caption phrasing.");
}
public sealed partial class HtmlDataListDomElement(DomElementMapping m) :
	HtmlDomElementDefinition(
		m, "datalist", ElementCategory.FormControl, ElementVisualKind.NonVisual,
		ElementContentModel.FormOptions, ElementClosure.OpenContainer,
		ElementSyntax.Normal, XamlConversionSupport.NonVisual,
		XamlControlFamily.None, ElementDefaultDisplay.None,
		ElementInteractionKind.None, ElementXamlChildPlacementKind.None)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The datalist element is a nonvisual suggestion data source.");

	protected override bool HasXamlOutput() => false;

	protected override void ToXaml(
		System.Text.StringBuilder output,
		int depth,
		bool isDocumentRoot)
	{
	}

	protected internal override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectPlans() => [];
}
public sealed partial class HtmlOptionGroupDomElement(DomElementMapping m) :
	HtmlDomElementDefinition(
		m, "optgroup", ElementCategory.FormControl, ElementVisualKind.NonVisual,
		ElementContentModel.FormOptions, ElementClosure.OpenContainer,
		ElementSyntax.Normal, XamlConversionSupport.NonVisual,
		XamlControlFamily.None, ElementDefaultDisplay.None,
		ElementInteractionKind.None, ElementXamlChildPlacementKind.None)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The optgroup element contributes a labelled option group to its owning selector.");
	[HtmlElementProperty] public DomElementStringProperty Disabled { get; } = Attribute("disabled", "optgroup");
	[HtmlElementProperty] public DomElementStringProperty Label { get; } = Attribute("label", "optgroup");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Disabled), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Label), HtmlElementAttributeXamlHandling.RuntimeDataSource));
	protected override bool HasXamlOutput() => false;

	protected override void ToXaml(
		System.Text.StringBuilder output,
		int depth,
		bool isDocumentRoot)
	{
	}

	protected internal override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectPlans() => [];

	internal IReadOnlyList<XamlElementObjectPlan> BuildSelectItemPlans()
	{
		var items = new List<XamlElementObjectPlan>();
		if (Label.SourceInitialization.IsSet)
		{
			items.Add(
				CreateSyntheticXamlObjectPlan(
					XamlElementObjectType.TextBlock,
					[
						new(
							"Text",
							Label.SourceInitialization.Value ?? string.Empty,
							Label),
						new("FontWeight", "SemiBold", Label)
					],
					ElementXamlChildPlacementKind.None,
					[],
					$"Option group label for {DocumentScope}::{XPath}."));
		}
		foreach (var option in Children.OfType<HtmlOptionDomElement>())
		{
			var optionPlans = option.BuildXamlObjectPlans();
			if (!Disabled.SourceInitialization.IsSet)
			{
				items.AddRange(optionPlans);
				continue;
			}
			foreach (var plan in optionPlans)
			{
				items.Add(
					plan with
					{
						InitializationAttributes =
						plan.InitializationAttributes
							.Where(static attribute =>
								attribute.Name != "IsEnabled")
							.Append(
								new GeneratedXamlAttribute(
									"IsEnabled",
									"False",
									Disabled))
							.ToArray()
					});
			}
		}
		return items;
	}
}
public sealed partial class HtmlOutputDomElement(DomElementMapping m) :
	HtmlPhrasingNamedFormOwnerDomElementDefinition(m, "output")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.InlineFlow, false, "The output element is leaf calculated output text.")
			: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The output element owns composite calculated output phrasing.");
	[HtmlElementProperty] public DomElementStringProperty For { get; } = Attribute("for", "output");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(For), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Form), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource));
}
public sealed partial class HtmlMeterDomElement(DomElementMapping m) :
	HtmlFormControlDomElementDefinition(
		m, "meter", ElementContentModel.Phrasing, ElementClosure.OpenContainer,
		ElementSyntax.Normal, XamlControlFamily.Custom)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlMeterControl, XamlElementMappingKind.TypeDefault, false, "The meter element preserves scalar range and threshold semantics.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.None,
			XamlElementContentProjectionKind.GeneratedContent);
	[HtmlElementProperty] public DomElementStringProperty Value { get; } = Attribute("value", "meter");
	[HtmlElementProperty] public DomElementStringProperty Minimum { get; } = Attribute("min", "meter");
	[HtmlElementProperty] public DomElementStringProperty Maximum { get; } = Attribute("max", "meter");
	[HtmlElementProperty] public DomElementStringProperty Low { get; } = Attribute("low", "meter");
	[HtmlElementProperty] public DomElementStringProperty High { get; } = Attribute("high", "meter");
	[HtmlElementProperty] public DomElementStringProperty Optimum { get; } = Attribute("optimum", "meter");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Value), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Minimum), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Maximum), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Low), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(High), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Optimum), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		SetXamlAttribute(attributes, "Minimum", SourceInitialization(Minimum), Minimum);
		SetXamlAttribute(attributes, "Maximum", SourceInitialization(Maximum), Maximum);
		SetXamlAttribute(attributes, "Value", SourceInitialization(Value), Value);
		SetXamlAttribute(attributes, "Low", SourceInitialization(Low), Low);
		SetXamlAttribute(attributes, "High", SourceInitialization(High), High);
		SetXamlAttribute(attributes, "Optimum", SourceInitialization(Optimum), Optimum);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlProgressDomElement(DomElementMapping m) :
	HtmlFormControlDomElementDefinition(
		m, "progress", ElementContentModel.Phrasing, ElementClosure.OpenContainer,
		ElementSyntax.Normal, XamlControlFamily.Custom)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.ProgressBar, XamlElementMappingKind.TypeDefault, false, "The progress element presents task completion progress.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.None,
			XamlElementContentProjectionKind.GeneratedContent);
	[HtmlElementProperty] public DomElementStringProperty Value { get; } = Attribute("value", "progress");
	[HtmlElementProperty] public DomElementStringProperty Maximum { get; } = Attribute("max", "progress");

	public bool IsIndeterminateState => !Value.SourceInitialization.IsSet;

	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Value), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Maximum), HtmlElementAttributeXamlHandling.InlineXaml));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		SetXamlAttribute(attributes, "Minimum", "0", Value);
		SetXamlAttribute(
			attributes,
			"Maximum",
			SourceInitialization(Maximum) ?? "1",
			Maximum);
		if (Value.SourceInitialization.IsSet)
			SetXamlAttribute(attributes, "Value", SourceInitialization(Value), Value);
		else
			SetXamlAttribute(attributes, "IsIndeterminate", "True", Value);
		return attributes.Values.ToArray();
	}
}

public sealed partial class HtmlTableDomElement(DomElementMapping m) :
	HtmlTableDomElementDefinition(m, "table", ElementDefaultDisplay.Table)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlTablePanel, XamlElementMappingKind.TableLayout, true, "The table element owns the complete table track model.");

	protected override bool TryBuildXamlTableLayout(
		out IReadOnlyList<XamlGridTrackDefinition> rowDefinitions,
		out IReadOnlyList<XamlGridTrackDefinition> columnDefinitions)
	{
		rowDefinitions = [];
		var columns = new List<XamlGridTrackDefinition>();
		foreach (var child in Children)
		{
			switch (child)
			{
				case HtmlTableColumnDomElement column:
					AddColumnTracks(columns, column, null);
					break;
				case HtmlTableColumnGroupDomElement group:
					{
						var groupedColumns = group.Children
							.OfType<HtmlTableColumnDomElement>()
							.ToArray();
						if (groupedColumns.Length == 0)
						{
							AddRepeatedTrack(
								columns,
								ResolveTrack(group, null),
								ReadSpan(group.Span));
							break;
						}
						foreach (var groupedColumn in groupedColumns)
							AddColumnTracks(columns, groupedColumn, group);
						break;
					}
			}
		}
		columnDefinitions = columns;
		return columns.Count != 0;
	}

	private static void AddColumnTracks(
		ICollection<XamlGridTrackDefinition> tracks,
		HtmlTableColumnDomElement column,
		HtmlTableColumnGroupDomElement? group) =>
		AddRepeatedTrack(
			tracks,
			ResolveTrack(column, group),
			ReadSpan(column.Span));

	private static void AddRepeatedTrack(
		ICollection<XamlGridTrackDefinition> tracks,
		XamlGridTrackDefinition definition,
		int span)
	{
		for (var index = 0; index < span; index++)
			tracks.Add(definition);
	}

	private static int ReadSpan(DomElementStringProperty property) =>
		int.TryParse(
			SourceInitialization(property),
			System.Globalization.NumberStyles.Integer,
			System.Globalization.CultureInfo.InvariantCulture,
			out var span)
				? Math.Clamp(span, 1, 1000)
				: 1;

	private static XamlGridTrackDefinition ResolveTrack(
		HtmlDomElementDefinition element,
		HtmlTableColumnGroupDomElement? fallback)
	{
		var width = ReadStyleLength(element, "style.width")
			?? (fallback is null
				? null
				: ReadStyleLength(fallback, "style.width"));
		var minimum = ReadStyleLength(element, "style.minWidth")
			?? (fallback is null
				? null
				: ReadStyleLength(fallback, "style.minWidth"));
		var maximum = ReadStyleLength(element, "style.maxWidth")
			?? (fallback is null
				? null
				: ReadStyleLength(fallback, "style.maxWidth"));
		return new(
			NormalizeTrackLength(width) ?? "Auto",
			NormalizeXamlLength(minimum),
			NormalizeXamlLength(maximum));
	}

	private static string? ReadStyleLength(
		HtmlDomElementDefinition element,
		string name) =>
		element.HtmlRoot?.ResolveGlobalStyleValue(element, name);

	private static string? NormalizeTrackLength(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
			return null;
		value = value.Trim();
		if (value.EndsWith('%')
			&& double.TryParse(
				value[..^1],
				System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture,
				out var percent)
			&& double.IsFinite(percent)
			&& percent > 0)
		{
			return percent.ToString(
				"R",
				System.Globalization.CultureInfo.InvariantCulture) + "*";
		}
		return NormalizeXamlLength(value);
	}
}
public sealed partial class HtmlTableHeadDomElement(DomElementMapping m) :
	HtmlTableDomElementDefinition(m, "thead", ElementDefaultDisplay.TableSection)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlTableSectionPanel, XamlElementMappingKind.TableLayout, true, "The thead element owns header rows.");
}
public sealed partial class HtmlTableBodyDomElement(DomElementMapping m) :
	HtmlTableDomElementDefinition(m, "tbody", ElementDefaultDisplay.TableSection)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlTableSectionPanel, XamlElementMappingKind.TableLayout, true, "The tbody element owns body rows.");
}
public sealed partial class HtmlTableFootDomElement(DomElementMapping m) :
	HtmlTableDomElementDefinition(m, "tfoot", ElementDefaultDisplay.TableSection)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlTableSectionPanel, XamlElementMappingKind.TableLayout, true, "The tfoot element owns footer rows.");
}
public sealed partial class HtmlTableRowDomElement(DomElementMapping m) :
	HtmlTableDomElementDefinition(m, "tr", ElementDefaultDisplay.TableRow)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlTableRowPanel, XamlElementMappingKind.TableLayout, true, "The tr element owns an ordered row of table cells.");
}
public sealed partial class HtmlTableHeaderCellDomElement(DomElementMapping m) :
	HtmlTableCellDomElementDefinition(m, "th", ElementDefaultDisplay.TableCell)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Border, XamlElementMappingKind.TableLayout, true, "The th element owns a bordered header cell and its spans.");
	[HtmlElementProperty] public DomElementStringProperty Scope { get; } = Attribute("scope", "th");
	[HtmlElementProperty] public DomElementStringProperty Abbreviation { get; } = Attribute("abbr", "th");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(ColumnSpan), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(RowSpan), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Headers), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Scope), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Abbreviation), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		ApplyCellSpans(attributes, ColumnSpan, RowSpan);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlTableCellDomElement(DomElementMapping m) :
	HtmlTableCellDomElementDefinition(m, "td", ElementDefaultDisplay.TableCell)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Border, XamlElementMappingKind.TableLayout, true, "The td element owns a bordered data cell and its spans.");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(ColumnSpan), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(RowSpan), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Headers), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		ApplyCellSpans(attributes, ColumnSpan, RowSpan);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlTableCaptionDomElement(DomElementMapping m) :
	HtmlBlockTextDomElementDefinition(m, "caption")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		Children.Count == 0
			? new(XamlElementObjectType.TextBlock, XamlElementMappingKind.BlockFlow, false, "The caption element is leaf table-caption text.")
			: new(XamlElementObjectType.HtmlInlineFlowPanel, XamlElementMappingKind.InlineFlow, true, "The caption element owns composite table-caption phrasing.");
}
public sealed partial class HtmlTableColumnGroupDomElement :
	HtmlDomElementDefinition
{
	public HtmlTableColumnGroupDomElement(DomElementMapping mapping) :
		base(
			mapping,
			"colgroup",
			ElementCategory.Table,
			ElementVisualKind.NonVisual,
			ElementContentModel.TableStructure,
			ElementClosure.OpenContainer,
			ElementSyntax.Normal,
			XamlConversionSupport.NonVisual,
			XamlControlFamily.None,
			ElementDefaultDisplay.Table,
			ElementInteractionKind.None,
			ElementXamlChildPlacementKind.None)
	{
	}

	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TableLayout, true, "The colgroup element contributes nonvisual shared column-track metadata.");
	protected override bool HasXamlOutput() => false;
	protected override void ToXaml(
		System.Text.StringBuilder output,
		int depth,
		bool isDocumentRoot)
	{
	}
	protected internal override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectPlans() => [];
	[HtmlElementProperty] public DomElementStringProperty Span { get; } = Attribute("span", "colgroup");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes((nameof(Span), HtmlElementAttributeXamlHandling.RuntimeDataSource));
}
public sealed partial class HtmlTableColumnDomElement :
	HtmlDomElementDefinition
{
	public HtmlTableColumnDomElement(DomElementMapping mapping) :
		base(
			mapping,
			"col",
			ElementCategory.Table,
			ElementVisualKind.NonVisual,
			ElementContentModel.None,
			ElementClosure.ClosedLeaf,
			ElementSyntax.Void,
			XamlConversionSupport.NonVisual,
			XamlControlFamily.None,
			ElementDefaultDisplay.Table,
			ElementInteractionKind.None,
			ElementXamlChildPlacementKind.None)
	{
	}

	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TableLayout, true, "The col element contributes nonvisual column-track metadata.");
	protected override bool HasXamlOutput() => false;
	protected override void ToXaml(
		System.Text.StringBuilder output,
		int depth,
		bool isDocumentRoot)
	{
	}
	protected internal override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectPlans() => [];
	[HtmlElementProperty] public DomElementStringProperty Span { get; } = Attribute("span", "col");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes((nameof(Span), HtmlElementAttributeXamlHandling.RuntimeDataSource));
}

public sealed partial class HtmlPictureDomElement(DomElementMapping m) : HtmlContainerDomElementDefinition(m, "picture")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.ContentControl, XamlElementMappingKind.TypeDefault, false, "The picture element presents its selected image candidate through one content slot.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.Content,
			XamlElementContentProjectionKind.Composite);

	protected override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans()
	{
		var image = Children.OfType<HtmlImageDomElement>().LastOrDefault();
		return image?.BuildXamlObjectPlans() ?? [];
	}
}
public sealed partial class HtmlSourceDomElement(DomElementMapping m) : HtmlMetadataDomElementDefinition(m, "source", ElementSyntax.Void)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The source element is nonvisual media-source metadata.");
	[HtmlElementProperty] public DomElementStringProperty Type { get; } = Attribute("type", "source");
	[HtmlElementProperty] public DomElementStringProperty Media { get; } = Attribute("media", "source");
	[HtmlElementProperty] public DomElementStringProperty Source { get; } = Attribute("src", "source");
	[HtmlElementProperty] public DomElementStringProperty SourceSet { get; } = Attribute("srcset", "source");
	[HtmlElementProperty] public DomElementStringProperty Sizes { get; } = Attribute("sizes", "source");
	[HtmlElementProperty] public DomElementStringProperty Width { get; } = Attribute("width", "source");
	[HtmlElementProperty] public DomElementStringProperty Height { get; } = Attribute("height", "source");
}
public sealed partial class HtmlVideoDomElement(DomElementMapping m) :
	HtmlPlayableMediaDomElementDefinition(m, "video")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlMediaElementControl, XamlElementMappingKind.TypeDefault, false, "The video element owns selected media, transport, poster, and timed-text state.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.None,
			XamlElementContentProjectionKind.GeneratedContent);
	[HtmlElementProperty] public DomElementStringProperty Poster { get; } = Attribute("poster", "video");
	[HtmlElementProperty] public DomElementStringProperty PlaysInline { get; } = Attribute("playsinline", "video");
	[HtmlElementProperty] public DomElementStringProperty Width { get; } = Attribute("width", "video");
	[HtmlElementProperty] public DomElementStringProperty Height { get; } = Attribute("height", "video");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Source), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(CrossOrigin), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Poster), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Preload), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(AutoPlay), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(PlaysInline), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Loop), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Muted), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Controls), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Loading), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Width), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Height), HtmlElementAttributeXamlHandling.InlineXaml));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		var runtimeSource = RuntimeInitialization("resource.mediaSourceUrl");
		SetXamlAttribute(
			attributes,
			"Source",
			runtimeSource ?? SourceInitialization(Source),
			(IXamlPropertySlotOwner?)
				RuntimeProperty("resource.mediaSourceUrl") ?? Source);
		if (AutoPlay.SourceInitialization.IsSet)
			SetXamlAttribute(attributes, "AutoPlay", "True", AutoPlay);
		SetXamlAttribute(
			attributes,
			"AreTransportControlsEnabled",
			Controls.SourceInitialization.IsSet ? "True" : "False",
			Controls);
		SetXamlAttribute(
			attributes,
			"Poster",
			RuntimeInitialization("resource.posterSourceUrl")
				?? SourceInitialization(Poster),
			(IXamlPropertySlotOwner?)
				RuntimeProperty("resource.posterSourceUrl") ?? Poster);
		SetXamlAttribute(
			attributes,
			"TrackSources",
			BuildTrackSourceDescriptors(),
			null);
		SetXamlAttribute(attributes, "Width", NormalizeXamlLength(SourceInitialization(Width)), Width);
		SetXamlAttribute(attributes, "Height", NormalizeXamlLength(SourceInitialization(Height)), Height);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlAudioDomElement(DomElementMapping m) :
	HtmlPlayableMediaDomElementDefinition(m, "audio")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlMediaElementControl, XamlElementMappingKind.TypeDefault, false, "The audio element owns selected media, transport, and timed-text state.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.None,
			XamlElementContentProjectionKind.GeneratedContent);
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Source), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(CrossOrigin), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Preload), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(AutoPlay), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Loop), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Muted), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Controls), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Loading), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		var runtimeSource = RuntimeInitialization("resource.mediaSourceUrl");
		SetXamlAttribute(
			attributes,
			"Source",
			runtimeSource ?? SourceInitialization(Source),
			(IXamlPropertySlotOwner?)
				RuntimeProperty("resource.mediaSourceUrl") ?? Source);
		if (AutoPlay.SourceInitialization.IsSet)
			SetXamlAttribute(attributes, "AutoPlay", "True", AutoPlay);
		SetXamlAttribute(
			attributes,
			"AreTransportControlsEnabled",
			Controls.SourceInitialization.IsSet ? "True" : "False",
			Controls);
		SetXamlAttribute(
			attributes,
			"TrackSources",
			BuildTrackSourceDescriptors(),
			null);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlImageMapDomElement(DomElementMapping m) :
	HtmlDomElementDefinition(
		m, "map", ElementCategory.Media, ElementVisualKind.NonVisual,
		ElementContentModel.Transparent, ElementClosure.OpenContainer,
		ElementSyntax.Normal, XamlConversionSupport.NonVisual,
		XamlControlFamily.None, ElementDefaultDisplay.None,
		ElementInteractionKind.None, ElementXamlChildPlacementKind.None)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The map element contributes nonvisual hotspot definitions to an associated img.");
	[HtmlElementProperty] public DomElementStringProperty Name { get; } = Attribute("name", "map");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes((nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource));
}
public sealed partial class HtmlImageMapAreaDomElement(DomElementMapping m) :
	HtmlNonVisualHyperlinkDomElementDefinition(m, "area", ElementSyntax.Void)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The area element contributes nonvisual hotspot geometry to an associated img.");
	[HtmlElementProperty] public DomElementStringProperty Alt { get; } = Attribute("alt", "area");
	[HtmlElementProperty] public DomElementStringProperty Coordinates { get; } = Attribute("coords", "area");
	[HtmlElementProperty] public DomElementStringProperty Shape { get; } = Attribute("shape", "area");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Alt), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Coordinates), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Shape), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Href), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Target), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Download), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Ping), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Rel), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(ReferrerPolicy), HtmlElementAttributeXamlHandling.RuntimeDataSource));
}
public sealed partial class HtmlTrackDomElement(DomElementMapping m) : HtmlMetadataDomElementDefinition(m, "track", ElementSyntax.Void)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(null, XamlElementMappingKind.TypeDefault, false, "The track element is nonvisual timed-text metadata.");
	[HtmlElementProperty] public DomElementStringProperty Default { get; } = Attribute("default", "track");
	[HtmlElementProperty] public DomElementStringProperty Kind { get; } = Attribute("kind", "track");
	[HtmlElementProperty] public DomElementStringProperty Label { get; } = Attribute("label", "track");
	[HtmlElementProperty] public DomElementStringProperty Source { get; } = Attribute("src", "track");
	[HtmlElementProperty] public DomElementStringProperty SourceLanguage { get; } = Attribute("srclang", "track");
}
public sealed partial class HtmlImageDomElement(DomElementMapping m) :
	HtmlMediaDomElementDefinition(m, "img", ElementContentModel.None)
{
	[ElementProperty(
		PropertyValueKind.Resource,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.Semantic,
		Comparison = PropertyComparisonKind.ResourceIdentity,
		IsSlottedProperty = true,
		IsFillRequired = true,
		IsXamlFillRequired = true,
		IsAuditRequired = true,
		Description = "Resolved image resource selected by the live DOM.")]
	public DomElementRuntimeProperty ResolvedSource { get; } = new(
		"resource.imageSourceUrl",
		ElementSlotCategory.DataOrganization,
		ElementEvidenceKind.Resources);

	private HtmlImageMapDomElement? AssociatedMap =>
		ResolveAssociatedMap();

	public string? ResolveXamlStretch() =>
		RuntimeInitialization("style.objectFit")?.ToLowerInvariant() switch
		{
			"fill" => "Fill",
			"contain" => "Uniform",
			"cover" => "UniformToFill",
			"none" => "None",
			_ => null
		};

	protected override XamlElementMappingDecision CreateXaml() =>
		AssociatedMap is null
			? new(XamlElementObjectType.Image, XamlElementMappingKind.TypeDefault, false, "The img element is replaced image content.")
			: new(XamlElementObjectType.HtmlImageMapComposite, XamlElementMappingKind.PositionedLayout, true, "The mapped img owns its image and hotspot overlay.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		AssociatedMap is null
			? new(
				CreateXaml(),
				ElementXamlChildPlacementKind.None,
				XamlElementContentProjectionKind.GeneratedContent)
			: new(
				CreateXaml(),
				ElementXamlChildPlacementKind.DirectChildren,
				XamlElementContentProjectionKind.Composite);

	protected override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans()
	{
		if (AssociatedMap is not { } map)
			return [];
		var imageAttributes = BuildImageAttributes()
			.Where(static attribute =>
				attribute.Name is
					"Source"
					or "Stretch"
					or "AutomationProperties.Name")
			.ToArray();
		var hotspots = map.Children
			.OfType<HtmlImageMapAreaDomElement>()
			.Select(area =>
				CreateSyntheticXamlObjectPlan(
					XamlElementObjectType.HtmlImageMapHotspot,
					[
						new(
							"Shape",
							area.Shape.SourceInitialization.Value ?? "rect",
							area.Shape),
						new(
							"Coordinates",
							area.Coordinates.SourceInitialization.Value
								?? string.Empty,
							area.Coordinates),
						new(
							"AutomationProperties.Name",
							area.Alt.SourceInitialization.Value ?? string.Empty,
							area.Alt)
					],
					ElementXamlChildPlacementKind.None,
					[],
					$"Image-map hotspot {area.DocumentScope}::{area.XPath}."))
			.ToArray();
		return
		[
			CreateSyntheticXamlObjectPlan(
				XamlElementObjectType.Image,
				imageAttributes,
				ElementXamlChildPlacementKind.None,
				[],
				$"Mapped image for {DocumentScope}::{XPath}."),
			CreateSyntheticXamlObjectPlan(
				XamlElementObjectType.HtmlImageMapOverlay,
				[],
				ElementXamlChildPlacementKind.DirectChildren,
				hotspots,
				$"Image-map overlay for {DocumentScope}::{XPath}.")
		];
	}
	[HtmlElementProperty] public DomElementStringProperty Alt { get; } = Attribute("alt", "img");
	[HtmlElementProperty] public DomElementStringProperty Source { get; } = Attribute("src", "img");
	[HtmlElementProperty] public DomElementStringProperty SourceSet { get; } = Attribute("srcset", "img");
	[HtmlElementProperty] public DomElementStringProperty Sizes { get; } = Attribute("sizes", "img");
	[HtmlElementProperty] public DomElementStringProperty CrossOrigin { get; } = Attribute("crossorigin", "img");
	[HtmlElementProperty] public DomElementStringProperty UseMap { get; } = Attribute("usemap", "img");
	[HtmlElementProperty] public DomElementStringProperty IsMap { get; } = Attribute("ismap", "img");
	[HtmlElementProperty] public DomElementStringProperty ReferrerPolicy { get; } = Attribute("referrerpolicy", "img");
	[HtmlElementProperty] public DomElementStringProperty Decoding { get; } = Attribute("decoding", "img");
	[HtmlElementProperty] public DomElementStringProperty Controls { get; } = Attribute("controls", "img");
	[HtmlElementProperty] public DomElementStringProperty Loading { get; } = Attribute("loading", "img");
	[HtmlElementProperty] public DomElementStringProperty FetchPriority { get; } = Attribute("fetchpriority", "img");
	[HtmlElementProperty] public DomElementStringProperty Width { get; } = Attribute("width", "img");
	[HtmlElementProperty] public DomElementStringProperty Height { get; } = Attribute("height", "img");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Alt), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Source), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(SourceSet), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Sizes), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(CrossOrigin), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(UseMap), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(IsMap), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Controls), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(ReferrerPolicy), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Decoding), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Loading), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(FetchPriority), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Width), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Height), HtmlElementAttributeXamlHandling.InlineXaml));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = BuildImageAttributes();
		if (AssociatedMap is null)
			return attributes;
		return attributes
			.Where(static attribute =>
				attribute.Name is not
					("Source"
						or "Stretch"
						or "AutomationProperties.Name"))
			.ToArray();
	}

	private IReadOnlyList<GeneratedXamlAttribute> BuildImageAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		SetXamlAttribute(attributes, "AutomationProperties.Name", SourceInitialization(Alt), Alt);
		var runtimeSource = SourceInitialization(ResolvedSource);
		IXamlPropertySlotOwner sourceOwner = runtimeSource is null
			? Source
			: ResolvedSource;
		SetXamlAttribute(
			attributes,
			"Source",
			runtimeSource ?? SourceInitialization(Source),
			sourceOwner);
		var objectFit = RuntimeProperty("style.objectFit");
		SetXamlAttribute(
			attributes,
			"Stretch",
			ResolveXamlStretch(),
			objectFit);
		SetXamlAttribute(attributes, "Width", NormalizeXamlLength(SourceInitialization(Width)), Width);
		SetXamlAttribute(attributes, "Height", NormalizeXamlLength(SourceInitialization(Height)), Height);
		return attributes.Values.ToArray();
	}

	private HtmlImageMapDomElement? ResolveAssociatedMap()
	{
		var useMap = SourceInitialization(UseMap);
		if (string.IsNullOrWhiteSpace(useMap))
			return null;
		var expectedName = useMap.TrimStart('#');
		DomElement root = this;
		while (root.Parent is not null)
			root = root.Parent;
		return Enumerate(root)
			.OfType<HtmlImageMapDomElement>()
			.FirstOrDefault(candidate =>
				string.Equals(
					candidate.Name.SourceInitialization.Value,
					expectedName,
					StringComparison.Ordinal));
	}

	private static IEnumerable<DomElement> Enumerate(DomElement element)
	{
		yield return element;
		foreach (var child in element.Children)
		{
			foreach (var descendant in Enumerate(child))
				yield return descendant;
		}
	}
}
public sealed partial class HtmlIframeDomElement(DomElementMapping m) : HtmlEmbeddedDomElementDefinition(m, "iframe")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlEmbeddedDocumentHost, XamlElementMappingKind.TypeDefault, true, "The iframe element owns an embedded document scope and policy state.");

	[HtmlElementProperty] public DomElementStringProperty Source { get; } = Attribute("src", "iframe");
	[HtmlElementProperty] public DomElementStringProperty SourceDocument { get; } = Attribute("srcdoc", "iframe");
	[HtmlElementProperty] public DomElementStringProperty Name { get; } = Attribute("name", "iframe");
	[HtmlElementProperty] public DomElementStringProperty Sandbox { get; } = Attribute("sandbox", "iframe");
	[HtmlElementProperty] public DomElementStringProperty Allow { get; } = Attribute("allow", "iframe");
	[HtmlElementProperty] public DomElementStringProperty AllowFullscreen { get; } = Attribute("allowfullscreen", "iframe");
	[HtmlElementProperty] public DomElementStringProperty Width { get; } = Attribute("width", "iframe");
	[HtmlElementProperty] public DomElementStringProperty Height { get; } = Attribute("height", "iframe");
	[HtmlElementProperty] public DomElementStringProperty ReferrerPolicy { get; } = Attribute("referrerpolicy", "iframe");
	[HtmlElementProperty] public DomElementStringProperty Loading { get; } = Attribute("loading", "iframe");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Source), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(SourceDocument), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Sandbox), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Allow), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(AllowFullscreen), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Width), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Height), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(ReferrerPolicy), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Loading), HtmlElementAttributeXamlHandling.RuntimeDataSource));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		SetXamlAttribute(
			attributes,
			"Source",
			RuntimeInitialization("resource.embeddedSourceUrl")
				?? SourceInitialization(Source),
			(IXamlPropertySlotOwner?)
				RuntimeProperty("resource.embeddedSourceUrl") ?? Source);
		SetXamlAttribute(
			attributes,
			"SourceDocument",
			SourceInitialization(SourceDocument),
			SourceDocument);
		SetXamlAttribute(
			attributes,
			"Sandbox",
			SourceInitialization(Sandbox),
			Sandbox);
		SetXamlAttribute(
			attributes,
			"Allow",
			SourceInitialization(Allow),
			Allow);
		ApplyEmbeddedDimensions(attributes, Width, Height);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlCanvasDomElement(DomElementMapping m) : HtmlEmbeddedDomElementDefinition(m, "canvas")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlCanvasSurface, XamlElementMappingKind.PositionedLayout, true, "The canvas element owns a runtime drawing surface connected by DOM identity.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.None,
			XamlElementContentProjectionKind.GeneratedContent);
	[HtmlElementProperty] public DomElementStringProperty Width { get; } = Attribute("width", "canvas");
	[HtmlElementProperty] public DomElementStringProperty Height { get; } = Attribute("height", "canvas");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Width), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Height), HtmlElementAttributeXamlHandling.InlineXaml));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		ApplyEmbeddedDimensions(attributes, Width, Height);
		SetXamlAttribute(
			attributes,
			"DrawingConnectionKey",
			$"{DocumentScope}::{XPath}",
			null);
		SetXamlAttribute(
			attributes,
			"CommandStream",
			RuntimeValue("resource.canvasCommandStream"),
			RuntimeProperty("resource.canvasCommandStream"));
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlEmbedDomElement(DomElementMapping m) : HtmlEmbeddedDomElementDefinition(m, "embed", ElementSyntax.Void)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlEmbeddedContentHost, XamlElementMappingKind.TypeDefault, true, "The embed element hosts typed replaced external content.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.None,
			XamlElementContentProjectionKind.GeneratedContent);
	[HtmlElementProperty] public DomElementStringProperty Source { get; } = Attribute("src", "embed");
	[HtmlElementProperty] public DomElementStringProperty Type { get; } = Attribute("type", "embed");
	[HtmlElementProperty] public DomElementStringProperty Width { get; } = Attribute("width", "embed");
	[HtmlElementProperty] public DomElementStringProperty Height { get; } = Attribute("height", "embed");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Source), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Type), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Width), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Height), HtmlElementAttributeXamlHandling.InlineXaml));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		SetXamlAttribute(
			attributes,
			"Source",
			RuntimeInitialization("resource.embeddedSourceUrl")
				?? SourceInitialization(Source),
			(IXamlPropertySlotOwner?)
				RuntimeProperty("resource.embeddedSourceUrl") ?? Source);
		SetXamlAttribute(
			attributes,
			"MediaType",
			SourceInitialization(Type),
			Type);
		ApplyEmbeddedDimensions(attributes, Width, Height);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlObjectDomElement(DomElementMapping m) :
	HtmlEmbeddedNamedFormOwnerDomElementDefinition(m, "object")
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.HtmlObjectContentHost, XamlElementMappingKind.TypeDefault, true, "The object element hosts typed data and retains fallback content.");

	protected override XamlElementObjectProjectionDecision
		ResolveXamlObjectProjection() =>
		new(
			CreateXaml(),
			ElementXamlChildPlacementKind.DirectChildren,
			HasGeneratedXamlContent()
				? XamlElementContentProjectionKind.Composite
				: XamlElementContentProjectionKind.DirectChildren);

	protected override bool HasGeneratedXamlContent() =>
		!string.IsNullOrWhiteSpace(
			RuntimeInitialization("content.ownText"));

	protected override IReadOnlyList<XamlElementObjectPlan>
		BuildXamlObjectChildPlans()
	{
		var plans = new List<XamlElementObjectPlan>();
		var ownText = DataSource("content.ownText");
		var value = DataSourceValue("content.ownText");
		if (ownText is not null && !string.IsNullOrWhiteSpace(value))
		{
			plans.Add(CreateSyntheticXamlObjectPlan(
				XamlElementObjectType.TextBlock,
				[new("Text", value, ownText)],
				ElementXamlChildPlacementKind.None,
				[],
				$"Object fallback text for {DocumentScope}::{XPath}."));
		}
		plans.AddRange(BuildDirectDomChildObjectPlans());
		return plans;
	}
	[HtmlElementProperty] public DomElementStringProperty Data { get; } = Attribute("data", "object");
	[HtmlElementProperty] public DomElementStringProperty Type { get; } = Attribute("type", "object");
	[HtmlElementProperty] public DomElementStringProperty Width { get; } = Attribute("width", "object");
	[HtmlElementProperty] public DomElementStringProperty Height { get; } = Attribute("height", "object");
	protected override IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling> ElementSpecificXamlHandling =>
		HandleXamlAttributes(
			(nameof(Data), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Type), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Name), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Form), HtmlElementAttributeXamlHandling.RuntimeDataSource),
			(nameof(Width), HtmlElementAttributeXamlHandling.InlineXaml),
			(nameof(Height), HtmlElementAttributeXamlHandling.InlineXaml));

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		SetXamlAttribute(
			attributes,
			"Source",
			RuntimeInitialization("resource.embeddedSourceUrl")
				?? SourceInitialization(Data),
			(IXamlPropertySlotOwner?)
				RuntimeProperty("resource.embeddedSourceUrl") ?? Data);
		SetXamlAttribute(
			attributes,
			"MediaType",
			SourceInitialization(Type),
			Type);
		ApplyEmbeddedDimensions(attributes, Width, Height);
		return attributes.Values.ToArray();
	}
}
public sealed partial class HtmlHorizontalRuleDomElement(DomElementMapping m) :
	HtmlDomElementDefinition(
		m, "hr", ElementCategory.LayoutContainer, ElementVisualKind.LayoutContainer,
		ElementContentModel.None, ElementClosure.ClosedLeaf, ElementSyntax.Void,
		XamlConversionSupport.Direct, XamlControlFamily.Shape,
		ElementDefaultDisplay.Block, ElementInteractionKind.None,
		ElementXamlChildPlacementKind.None)
{
	protected override XamlElementMappingDecision CreateXaml() =>
		new(XamlElementObjectType.Rectangle, XamlElementMappingKind.TypeDefault, false, "The hr element is a horizontal thematic-break rectangle.");

	public string ResolveFillColor() =>
		NormalizeXamlColor(RuntimeInitialization("style.backgroundColor")) ?? "#FF808080";

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		if (!attributes.ContainsKey("Height"))
			SetXamlAttribute(attributes, "Height", "1", null);
		if (!attributes.ContainsKey("HorizontalAlignment"))
			SetXamlAttribute(attributes, "HorizontalAlignment", "Stretch", null);
		if (!attributes.ContainsKey("Fill"))
			SetXamlAttribute(attributes, "Fill", ResolveFillColor(), null);
		return attributes.Values.ToArray();
	}
}
