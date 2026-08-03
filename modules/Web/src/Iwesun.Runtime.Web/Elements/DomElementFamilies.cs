namespace Iwesun.Runtime.Web;

public abstract class HtmlDomElementDefinition : DomElement
{
	protected HtmlDomElementDefinition(
		DomElementMapping mapping,
		string tagName,
		ElementCategory category,
		ElementVisualKind visualKind,
		ElementContentModel contentModel,
		ElementClosure closure,
		ElementSyntax syntax,
		XamlConversionSupport xamlSupport,
		XamlControlFamily xamlControlFamily,
		ElementDefaultDisplay defaultDisplay,
		ElementInteractionKind interactionKind,
		ElementXamlChildPlacementKind childPlacement) :
		base(mapping)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
		TagName = tagName;
		Category = category;
		VisualKind = visualKind;
		ContentModel = contentModel;
		Closure = closure;
		Syntax = syntax;
		XamlSupport = xamlSupport;
		XamlControlFamily = xamlControlFamily;
		DefaultDisplay = defaultDisplay;
		InteractionKind = interactionKind;
		XamlChildPlacement = childPlacement;
		SupportedEventHandlers = HtmlEventHandlerCatalog.GetSupported(tagName);
	}

	public sealed override string TagName { get; }
	public sealed override ElementNamespace ElementNamespace => ElementNamespace.Html;
	public sealed override ElementCategory Category { get; }
	public sealed override ElementVisualKind VisualKind { get; }
	public sealed override ElementContentModel ContentModel { get; }
	public sealed override ElementClosure Closure { get; }
	public sealed override ElementSyntax Syntax { get; }
	public sealed override XamlConversionSupport XamlSupport { get; }
	public sealed override XamlControlFamily XamlControlFamily { get; }
	public sealed override string XamlElementName =>
		CreateXaml().ElementName;
	public sealed override ElementDefaultDisplay DefaultDisplay { get; }
	public sealed override ElementInteractionKind InteractionKind { get; }
	public sealed override ElementXamlChildPlacementKind XamlChildPlacement { get; }

	protected abstract override XamlElementMappingDecision
		CreateXaml();

	protected abstract override IReadOnlyList<DomElementDataSource>
		CreateDataSources();

	protected override void ValidateXamlBuildReadiness()
	{
		if (!HasCompletedDomFill)
		{
			throw new InvalidOperationException(
				$"{TagName} {DocumentScope}::{XPath} must complete DOM Fill "
					+ "before its strongly typed XAML object and properties "
					+ "can be selected.");
		}
	}

	protected virtual IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling>
		ElementSpecificXamlHandling =>
		new Dictionary<string, HtmlElementAttributeXamlHandling>(StringComparer.Ordinal);

	internal IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling>
		GetElementSpecificXamlHandling() =>
		ElementSpecificXamlHandling;

	protected static IReadOnlyDictionary<string, HtmlElementAttributeXamlHandling>
		HandleXamlAttributes(
			params (string PropertyName, HtmlElementAttributeXamlHandling Handling)[] items) =>
		items.ToDictionary(
			static item => item.PropertyName,
			static item => item.Handling,
			StringComparer.Ordinal);

	protected override IReadOnlyList<DomElementRuntimeProperty> XamlRuntimeProperties =>
		[
			.. RuntimeProperties,
			.. (HtmlRoot?.ResolveGlobalStyleProperties(this) ?? [])
		];

	protected override DomElementSlottedPropertyAuditResult AuditSlottedProperty(
		IDomElementSlotAuditOwner owner,
		ElementPropertyTraits traits)
	{
		if (XamlSupport == XamlConversionSupport.NonVisual
			&& owner.Name.Equals(
				"content.ownText",
				StringComparison.Ordinal))
		{
			var notRequired = ElementSlotFeatureAuditResult.NotRequired(
				"Non-visual HTML source text is retained as DOM evidence "
				+ "and intentionally has no visible XAML projection.");
			return new(
				owner.Name,
				owner.Category,
				notRequired,
				notRequired,
				notRequired,
				notRequired,
				"Declared non-visual text projection.");
		}
		return base.AuditSlottedProperty(owner, traits);
	}

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		SetXamlAttribute(
			attributes,
			"AutomationProperties.AutomationId",
			SourceInitialization(Id),
			Id);
		SetXamlAttribute(
			attributes,
			"ToolTipService.ToolTip",
			SourceInitialization(Title),
			Title);
		SetXamlAttribute(
			attributes,
			"Language",
			SourceInitialization(Language),
			Language);
		var direction = SourceInitialization(Direction);
		if (direction is not null)
		{
			SetXamlAttribute(
				attributes,
				"FlowDirection",
				direction.Equals("rtl", StringComparison.OrdinalIgnoreCase)
					? "RightToLeft"
					: "LeftToRight",
				Direction);
		}
		if (Hidden.SourceInitialization.IsSet)
			SetXamlAttribute(attributes, "Visibility", "Collapsed", Hidden);
		SetXamlAttribute(
			attributes,
			"TabIndex",
			SourceInitialization(TabIndex),
			TabIndex);
		AddCssLayoutParticipationAttribute(attributes, "style.flexGrow", "HtmlCssBoxGrid.FlexGrow");
		AddCssLayoutParticipationAttribute(attributes, "style.flexShrink", "HtmlCssBoxGrid.FlexShrink");
		AddCssLayoutParticipationAttribute(attributes, "style.flexBasis", "HtmlCssBoxGrid.FlexBasis");
		AddCssLayoutParticipationAttribute(attributes, "style.alignSelf", "HtmlCssBoxGrid.AlignSelf");
		AddCssLayoutParticipationAttribute(attributes, "style.order", "HtmlCssBoxGrid.Order");
		AddCssLayoutParticipationAttribute(attributes, "style.gridRow", "HtmlCssBoxGrid.GridRowExpression");
		AddCssLayoutParticipationAttribute(attributes, "style.gridColumn", "HtmlCssBoxGrid.GridColumnExpression");
		AddCssLayoutParticipationAttribute(attributes, "style.gridArea", "HtmlCssBoxGrid.GridAreaExpression");
		AddRuntimeContentAttribute(attributes);
		return attributes.Values
			.OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
			.ToArray();
	}

	private void AddCssLayoutParticipationAttribute(
		IDictionary<string, GeneratedXamlAttribute> attributes,
		string sourceName,
		string targetName)
	{
		var property = RuntimeProperty(sourceName);
		var value = property?.SourceRuntime.IsSet == true
			? property.SourceRuntime.Value
			: property?.SourceInitialization.IsSet == true
				? property.SourceInitialization.Value
				: null;
		SetXamlAttribute(attributes, targetName, value, property);
	}

	protected DomElementRuntimeProperty? RuntimeProperty(string name) =>
		RuntimeProperties.FirstOrDefault(property =>
			property.Name.Equals(name, StringComparison.Ordinal))
		?? HtmlRoot?.ResolveGlobalStyleProperty(this, name);

	protected DomElementDataSource? DataSource(string name) =>
		DataSources.FirstOrDefault(source =>
			source.Name.Equals(name, StringComparison.Ordinal));

	protected string? DataSourceValue(string name)
	{
		var source = DataSource(name);
		if (source?.SourceRuntime.IsSet == true)
			return source.SourceRuntime.Value;
		return source?.SourceInitialization.IsSet == true
			? source.SourceInitialization.Value
			: null;
	}

	protected string? RuntimeInitialization(string name)
	{
		if (DataSourceValue(name) is { } dataSourceValue)
			return dataSourceValue;
		if (name.StartsWith("style.", StringComparison.Ordinal)
			|| name.StartsWith("effect.", StringComparison.Ordinal))
		{
			var global = HtmlRoot?.ResolveGlobalStyleValue(
				this,
				name,
				DomPropertyDataSlot.Initialization);
			if (!string.IsNullOrWhiteSpace(global))
				return global;
		}
		var property = RuntimeProperty(name);
		return property?.SourceInitialization.IsSet == true
			? property.SourceInitialization.Value
			: null;
	}

	protected string? RuntimeValue(string name)
	{
		if (DataSourceValue(name) is { } dataSourceValue)
			return dataSourceValue;
		if (name.StartsWith("state.", StringComparison.Ordinal))
		{
			var globalState = HtmlRoot?.ResolveGlobalRuntimeStateValue(
				this,
				name);
			if (globalState is not null)
				return globalState;
		}
		if (name.StartsWith("style.", StringComparison.Ordinal)
			|| name.StartsWith("effect.", StringComparison.Ordinal))
		{
			var global = HtmlRoot?.ResolveGlobalStyleValue(
					this,
					name,
					DomPropertyDataSlot.Runtime)
				?? HtmlRoot?.ResolveGlobalStyleValue(
					this,
					name,
					DomPropertyDataSlot.Initialization);
			if (!string.IsNullOrWhiteSpace(global))
				return global;
		}
		var property = RuntimeProperty(name);
		if (property?.SourceRuntime.IsSet == true)
			return property.SourceRuntime.Value;
		return property?.SourceInitialization.IsSet == true
			? property.SourceInitialization.Value
			: null;
	}

	protected HtmlCssFormattingContext FormattingContext() =>
		HtmlXamlSemanticState.ParseFormattingContext(
			RuntimeValue("style.display"),
			RuntimeValue("style.flexDirection"));

	private void AddRuntimeContentAttribute(
		IDictionary<string, GeneratedXamlAttribute> attributes)
	{
		if (Children.Count != 0)
			return;
		var ownText = DataSource("content.ownText");
		var value = DataSourceValue("content.ownText");
		if (string.IsNullOrWhiteSpace(value))
			return;
		var target = CreateXaml().ElementName switch
		{
			"TextBlock"
				or "TextBox"
				or "HtmlSpanBoxControl"
				or "HtmlBidiIsolationTextBlock"
				or "HtmlBidiOverrideTextBlock"
				or "HtmlSubscriptTextBlock"
				or "HtmlSuperscriptTextBlock"
				or "HtmlRubyAnnotationTextBlock" => "Text",
			"Button"
				or "HtmlFormButton"
				or "HtmlImageSubmitButton"
				or "HtmlFileInputControl"
				or "HyperlinkButton"
				or "ComboBoxItem"
				or "ListViewItem" => "Content",
			_ => null
		};
		if (target is not null)
			SetXamlAttribute(attributes, target, value, ownText);
	}

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "Standards-defined event handler capabilities for this HTML element type.")]
	public IReadOnlyList<HtmlEventHandlerDefinition> SupportedEventHandlers { get; }

	[ElementProperty(PropertyValueKind.Identity, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Semantic, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		IsXamlOutputProperty = true,
		TargetProperty = "AutomationProperties.AutomationId",
		Description = "HTML global id attribute.")]
	public DomElementStringProperty Id { get; } = Attribute("id");

	[ElementProperty(PropertyValueKind.Text, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "HTML class token list.")]
	public DomElementStringProperty Class { get; } = Attribute("class");

	[ElementProperty(PropertyValueKind.Text, PropertyValueStage.CssInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.ManualReview,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Inline CSS declaration block.")]
	public DomElementStringProperty Style { get; } = Attribute("style");

	[ElementProperty(PropertyValueKind.Text, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Semantic, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Advisory title text.")]
	public DomElementStringProperty Title { get; } = Attribute("title");

	[ElementProperty(PropertyValueKind.Text, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Semantic, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Language tag.")]
	public DomElementStringProperty Language { get; } = Attribute("lang");

	[ElementProperty(PropertyValueKind.Enumeration, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Semantic, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Text direction.")]
	public DomElementStringProperty Direction { get; } = Attribute("dir");

	[ElementProperty(PropertyValueKind.Boolean, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Semantic, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Hidden state.")]
	public DomElementStringProperty Hidden { get; } = Attribute("hidden");

	[ElementProperty(PropertyValueKind.Number, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Semantic, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Sequential focus navigation index.")]
	public DomElementStringProperty TabIndex { get; } = Attribute("tabindex");

	[ElementProperty(PropertyValueKind.Enumeration, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Semantic, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Explicit accessibility role.")]
	public DomElementStringProperty Role { get; } = Attribute("role");

	[ElementProperty(PropertyValueKind.Text, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Semantic, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Keyboard shortcut hint.")]
	public DomElementStringProperty AccessKey { get; } = Attribute("accesskey");

	[ElementProperty(PropertyValueKind.Enumeration, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Whether the element is editable.")]
	public DomElementStringProperty ContentEditable { get; } = Attribute("contenteditable");

	[ElementProperty(PropertyValueKind.Boolean, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Native drag capability.")]
	public DomElementStringProperty Draggable { get; } = Attribute("draggable");

	[ElementProperty(PropertyValueKind.Boolean, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Spell checking preference.")]
	public DomElementStringProperty SpellCheck { get; } = Attribute("spellcheck");

	[ElementProperty(PropertyValueKind.Boolean, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Translation eligibility.")]
	public DomElementStringProperty Translate { get; } = Attribute("translate");

	[ElementProperty(PropertyValueKind.Identity, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Shadow DOM slot name.")]
	public DomElementStringProperty Slot { get; } = Attribute("slot");

	[ElementProperty(PropertyValueKind.Text, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Shadow part token list.")]
	public DomElementStringProperty Part { get; } = Attribute("part");

	[ElementProperty(PropertyValueKind.Text, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Shadow-part export mapping defined by CSS Shadow Parts.")]
	public DomElementStringProperty ExportParts { get; } = Attribute("exportparts");

	[ElementProperty(PropertyValueKind.Enumeration, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Popover behavior.")]
	public DomElementStringProperty Popover { get; } = Attribute("popover");

	[ElementProperty(PropertyValueKind.Boolean, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Inert subtree state.")]
	public DomElementStringProperty Inert { get; } = Attribute("inert");

	[ElementProperty(PropertyValueKind.Enumeration, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Virtual keyboard input mode.")]
	public DomElementStringProperty InputMode { get; } = Attribute("inputmode");

	[ElementProperty(PropertyValueKind.Enumeration, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Virtual keyboard enter key hint.")]
	public DomElementStringProperty EnterKeyHint { get; } = Attribute("enterkeyhint");

	[ElementProperty(PropertyValueKind.Enumeration, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Automatic capitalization preference.")]
	public DomElementStringProperty AutoCapitalize { get; } = Attribute("autocapitalize");

	[ElementProperty(PropertyValueKind.Enumeration, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Automatic correction preference.")]
	public DomElementStringProperty AutoCorrect { get; } = Attribute("autocorrect");

	[ElementProperty(PropertyValueKind.Boolean, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Semantic, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Automatic focus request.")]
	public DomElementStringProperty AutoFocus { get; } = Attribute("autofocus");

	[ElementProperty(PropertyValueKind.Number, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Heading level offset.")]
	public DomElementStringProperty HeadingOffset { get; } = Attribute("headingoffset");

	[ElementProperty(PropertyValueKind.Boolean, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Heading level reset marker.")]
	public DomElementStringProperty HeadingReset { get; } = Attribute("headingreset");

	[ElementProperty(PropertyValueKind.Identity, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Customized built-in element name.")]
	public DomElementStringProperty Is { get; } = Attribute("is");

	[ElementProperty(PropertyValueKind.Enumeration, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Writing suggestions preference.")]
	public DomElementStringProperty WritingSuggestions { get; } =
		Attribute("writingsuggestions");

	[ElementProperty(PropertyValueKind.Identity, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.NotApplicable, Comparison = PropertyComparisonKind.ManualReview,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Content security policy nonce.")]
	public DomElementStringProperty Nonce { get; } = Attribute("nonce");

	[ElementProperty(PropertyValueKind.Identity, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Microdata item identifier.")]
	public DomElementStringProperty ItemId { get; } = Attribute("itemid");

	[ElementProperty(PropertyValueKind.Text, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Microdata property token list.")]
	public DomElementStringProperty ItemProp { get; } = Attribute("itemprop");

	[ElementProperty(PropertyValueKind.Text, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Microdata referenced item identifiers.")]
	public DomElementStringProperty ItemRef { get; } = Attribute("itemref");

	[ElementProperty(PropertyValueKind.Boolean, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Microdata item scope marker.")]
	public DomElementStringProperty ItemScope { get; } = Attribute("itemscope");

	[ElementProperty(PropertyValueKind.Text, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsHtmlDefinedProperty = true, IsSlottedProperty = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Microdata vocabulary type URLs.")]
	public DomElementStringProperty ItemType { get; } = Attribute("itemtype");

	[ElementProperty(PropertyValueKind.State, PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.ManualReview,
		IsHtmlDefinedProperty = true, IsSlottedPropertyCollection = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "DOM attributes not represented by a named property on the concrete element type.")]
	public List<DomElementStringProperty> ExtensionAttributes { get; } = [];

	[ElementProperty(PropertyValueKind.State, PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.Composite, Comparison = PropertyComparisonKind.Semantic,
		IsSlottedPropertyCollection = true, IsFillRequired = true,
		IsXamlFillRequired = true, IsAuditRequired = true,
		Description = "Live geometry, computed style, state, and effect values.")]
	public List<DomElementRuntimeProperty> RuntimeProperties { get; } = [];

	public void AddExtensionAttribute(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (NamedAttributeNames.Contains(name)
			|| ElementPropertyTraitsReflector
				.GetAttributes(GetType())
				.Where(static traits =>
					traits.IsHtmlDefinedProperty && traits.IsSlottedProperty)
				.Select(traits => GetType().GetProperty(traits.PropertyName)?.GetValue(this))
				.OfType<DomElementStringProperty>()
				.Any(item => string.Equals(
					item.Name,
					name,
					StringComparison.OrdinalIgnoreCase))
			|| ExtensionAttributes.Any(
				item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}
		ExtensionAttributes.Add(Attribute(name));
	}

	public void AddRuntimeProperty(
		string name,
		ElementSlotCategory category,
		ElementEvidenceKind evidenceKind)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (RuntimeProperties.Any(item =>
			string.Equals(item.Name, name, StringComparison.Ordinal)))
		{
			return;
		}
		RuntimeProperties.Add(new(name, category, evidenceKind));
	}

	public void AddRuntimeProperty(string name, ElementSlotCategory category) =>
		AddRuntimeProperty(
			name,
			category,
			DomElementRuntimePropertyCatalog.ResolveEvidenceKind(name));

	private static readonly IReadOnlySet<string> NamedAttributeNames =
		new HashSet<string>(
			[
				"id", "class", "style", "title", "lang", "dir", "hidden",
				"tabindex", "role", "accesskey", "contenteditable", "draggable",
				"spellcheck", "translate", "slot", "part", "exportparts", "popover", "inert",
				"inputmode", "enterkeyhint", "autocapitalize", "nonce",
				"autocorrect", "autofocus", "headingoffset", "headingreset",
				"is", "writingsuggestions", "itemid", "itemprop", "itemref",
				"itemscope", "itemtype"
			],
			StringComparer.OrdinalIgnoreCase);

	protected static DomElementStringProperty Attribute(
		string name,
		string? tagName = null) =>
		new HtmlDomAttributeProperty(name, tagName);
}

public abstract class SvgDomElementDefinition : DomElement
{
	protected SvgDomElementDefinition(
		DomElementMapping mapping,
		string tagName,
		ElementCategory category,
		ElementVisualKind visualKind,
		ElementContentModel contentModel,
		ElementClosure closure,
		XamlConversionSupport xamlSupport,
		XamlControlFamily xamlControlFamily,
		ElementXamlChildPlacementKind childPlacement) :
		base(mapping)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
		TagName = tagName;
		Category = category;
		VisualKind = visualKind;
		ContentModel = contentModel;
		Closure = closure;
		XamlSupport = xamlSupport;
		XamlControlFamily = xamlControlFamily;
		XamlChildPlacement = childPlacement;
		SupportedEventHandlers = HtmlEventHandlerCatalog.GetSupported(tagName);
	}

	public sealed override string TagName { get; }
	public sealed override ElementNamespace ElementNamespace => ElementNamespace.Svg;
	public sealed override ElementCategory Category { get; }
	public sealed override ElementVisualKind VisualKind { get; }
	public sealed override ElementContentModel ContentModel { get; }
	public sealed override ElementClosure Closure { get; }
	public sealed override ElementSyntax Syntax => ElementSyntax.ForeignNamespace;
	public sealed override XamlConversionSupport XamlSupport { get; }
	public sealed override XamlControlFamily XamlControlFamily { get; }
	public sealed override string XamlElementName =>
		CreateXaml().ElementName;
	public sealed override ElementDefaultDisplay DefaultDisplay =>
		ElementDefaultDisplay.Inline;
	public sealed override ElementInteractionKind InteractionKind =>
		ElementInteractionKind.None;
	public sealed override ElementXamlChildPlacementKind XamlChildPlacement { get; }

	public override DomElementQuerySpecialization QuerySpecialization =>
		DomElementQuerySpecialization.Svg;

	protected virtual IReadOnlyDictionary<string, SvgElementAttributeXamlHandling>
		ElementSpecificXamlHandling =>
		new Dictionary<string, SvgElementAttributeXamlHandling>(StringComparer.Ordinal);

	internal IReadOnlyDictionary<string, SvgElementAttributeXamlHandling>
		GetElementSpecificXamlHandling() =>
		ElementSpecificXamlHandling;

	protected static IReadOnlyDictionary<string, SvgElementAttributeXamlHandling>
		HandleSvgXamlAttributes(
			params (string PropertyName, SvgElementAttributeXamlHandling Handling)[] items) =>
		items.ToDictionary(
			static item => item.PropertyName,
			static item => item.Handling,
			StringComparer.Ordinal);

	protected override IReadOnlyList<DomElementRuntimeProperty> XamlRuntimeProperties =>
		[
			.. RuntimeProperties,
			.. (HtmlRoot?.ResolveGlobalStyleProperties(this) ?? [])
		];

	protected override IReadOnlyList<GeneratedXamlAttribute> BuildXamlAttributes()
	{
		var attributes = base.BuildXamlAttributes()
			.ToDictionary(static attribute => attribute.Name, StringComparer.Ordinal);
		SetXamlAttribute(
			attributes,
			"AutomationProperties.AutomationId",
			SourceInitialization(Id),
			Id);
		SetXamlAttribute(
			attributes,
			"TabIndex",
			SourceInitialization(TabIndex),
			TabIndex);
		return attributes.Values
			.OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
			.ToArray();
	}

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.TypeIntrinsic,
		Translation = PropertyTranslationKind.NotApplicable,
		IsElementDesignProperty = true,
		Description = "Standards-defined event handler capabilities for SVG elements.")]
	public IReadOnlyList<HtmlEventHandlerDefinition> SupportedEventHandlers { get; }

	[SvgElementProperty] public SvgDomAttributeProperty Id { get; } = Attribute("id");
	[SvgElementProperty] public SvgDomAttributeProperty TabIndex { get; } = Attribute("tabindex");
	[SvgElementProperty] public SvgDomAttributeProperty Language { get; } = Attribute("lang");
	[SvgElementProperty] public SvgDomAttributeProperty XmlSpace { get; } = Attribute("xml:space");
	[SvgElementProperty] public SvgDomAttributeProperty Class { get; } = Attribute("class");
	[SvgElementProperty] public SvgDomAttributeProperty Style { get; } = Attribute("style");
	[SvgElementProperty] public SvgDomAttributeProperty RequiredExtensions { get; } = Attribute("requiredExtensions");
	[SvgElementProperty] public SvgDomAttributeProperty SystemLanguage { get; } = Attribute("systemLanguage");
	[SvgElementProperty] public SvgDomAttributeProperty AlignmentBaseline { get; } = Attribute("alignment-baseline");
	[SvgElementProperty] public SvgDomAttributeProperty BaselineShift { get; } = Attribute("baseline-shift");
	[SvgElementProperty] public SvgDomAttributeProperty Clip { get; } = Attribute("clip");
	[SvgElementProperty] public SvgDomAttributeProperty ClipPath { get; } = Attribute("clip-path");
	[SvgElementProperty] public SvgDomAttributeProperty ClipRule { get; } = Attribute("clip-rule");
	[SvgElementProperty] public SvgDomAttributeProperty Color { get; } = Attribute("color");
	[SvgElementProperty] public SvgDomAttributeProperty ColorInterpolation { get; } = Attribute("color-interpolation");
	[SvgElementProperty] public SvgDomAttributeProperty ColorInterpolationFilters { get; } = Attribute("color-interpolation-filters");
	[SvgElementProperty] public SvgDomAttributeProperty ColorRendering { get; } = Attribute("color-rendering");
	[SvgElementProperty] public SvgDomAttributeProperty Cursor { get; } = Attribute("cursor");
	[SvgElementProperty] public SvgDomAttributeProperty Direction { get; } = Attribute("direction");
	[SvgElementProperty] public SvgDomAttributeProperty Display { get; } = Attribute("display");
	[SvgElementProperty] public SvgDomAttributeProperty DominantBaseline { get; } = Attribute("dominant-baseline");
	[SvgElementProperty] public SvgDomAttributeProperty Fill { get; } = Attribute("fill");
	[SvgElementProperty] public SvgDomAttributeProperty FillOpacity { get; } = Attribute("fill-opacity");
	[SvgElementProperty] public SvgDomAttributeProperty FillRule { get; } = Attribute("fill-rule");
	[SvgElementProperty] public SvgDomAttributeProperty Filter { get; } = Attribute("filter");
	[SvgElementProperty] public SvgDomAttributeProperty FloodColor { get; } = Attribute("flood-color");
	[SvgElementProperty] public SvgDomAttributeProperty FloodOpacity { get; } = Attribute("flood-opacity");
	[SvgElementProperty] public SvgDomAttributeProperty FontFamily { get; } = Attribute("font-family");
	[SvgElementProperty] public SvgDomAttributeProperty FontSize { get; } = Attribute("font-size");
	[SvgElementProperty] public SvgDomAttributeProperty FontSizeAdjust { get; } = Attribute("font-size-adjust");
	[SvgElementProperty] public SvgDomAttributeProperty FontStretch { get; } = Attribute("font-stretch");
	[SvgElementProperty] public SvgDomAttributeProperty FontStyle { get; } = Attribute("font-style");
	[SvgElementProperty] public SvgDomAttributeProperty FontVariant { get; } = Attribute("font-variant");
	[SvgElementProperty] public SvgDomAttributeProperty FontWeight { get; } = Attribute("font-weight");
	[SvgElementProperty] public SvgDomAttributeProperty GlyphOrientationHorizontal { get; } = Attribute("glyph-orientation-horizontal");
	[SvgElementProperty] public SvgDomAttributeProperty GlyphOrientationVertical { get; } = Attribute("glyph-orientation-vertical");
	[SvgElementProperty] public SvgDomAttributeProperty ImageRendering { get; } = Attribute("image-rendering");
	[SvgElementProperty] public SvgDomAttributeProperty LetterSpacing { get; } = Attribute("letter-spacing");
	[SvgElementProperty] public SvgDomAttributeProperty LightingColor { get; } = Attribute("lighting-color");
	[SvgElementProperty] public SvgDomAttributeProperty MarkerEnd { get; } = Attribute("marker-end");
	[SvgElementProperty] public SvgDomAttributeProperty MarkerMid { get; } = Attribute("marker-mid");
	[SvgElementProperty] public SvgDomAttributeProperty MarkerStart { get; } = Attribute("marker-start");
	[SvgElementProperty] public SvgDomAttributeProperty Mask { get; } = Attribute("mask");
	[SvgElementProperty] public SvgDomAttributeProperty Opacity { get; } = Attribute("opacity");
	[SvgElementProperty] public SvgDomAttributeProperty Overflow { get; } = Attribute("overflow");
	[SvgElementProperty] public SvgDomAttributeProperty PaintOrder { get; } = Attribute("paint-order");
	[SvgElementProperty] public SvgDomAttributeProperty PointerEvents { get; } = Attribute("pointer-events");
	[SvgElementProperty] public SvgDomAttributeProperty ShapeRendering { get; } = Attribute("shape-rendering");
	[SvgElementProperty] public SvgDomAttributeProperty StopColor { get; } = Attribute("stop-color");
	[SvgElementProperty] public SvgDomAttributeProperty StopOpacity { get; } = Attribute("stop-opacity");
	[SvgElementProperty] public SvgDomAttributeProperty Stroke { get; } = Attribute("stroke");
	[SvgElementProperty] public SvgDomAttributeProperty StrokeDashArray { get; } = Attribute("stroke-dasharray");
	[SvgElementProperty] public SvgDomAttributeProperty StrokeDashOffset { get; } = Attribute("stroke-dashoffset");
	[SvgElementProperty] public SvgDomAttributeProperty StrokeLineCap { get; } = Attribute("stroke-linecap");
	[SvgElementProperty] public SvgDomAttributeProperty StrokeLineJoin { get; } = Attribute("stroke-linejoin");
	[SvgElementProperty] public SvgDomAttributeProperty StrokeMiterLimit { get; } = Attribute("stroke-miterlimit");
	[SvgElementProperty] public SvgDomAttributeProperty StrokeOpacity { get; } = Attribute("stroke-opacity");
	[SvgElementProperty] public SvgDomAttributeProperty StrokeWidth { get; } = Attribute("stroke-width");
	[SvgElementProperty] public SvgDomAttributeProperty TextAnchor { get; } = Attribute("text-anchor");
	[SvgElementProperty] public SvgDomAttributeProperty TextDecoration { get; } = Attribute("text-decoration");
	[SvgElementProperty] public SvgDomAttributeProperty TextRendering { get; } = Attribute("text-rendering");
	[SvgElementProperty] public SvgDomAttributeProperty Transform { get; } = Attribute("transform");
	[SvgElementProperty] public SvgDomAttributeProperty UnicodeBidi { get; } = Attribute("unicode-bidi");
	[SvgElementProperty] public SvgDomAttributeProperty VectorEffect { get; } = Attribute("vector-effect");
	[SvgElementProperty] public SvgDomAttributeProperty Visibility { get; } = Attribute("visibility");
	[SvgElementProperty] public SvgDomAttributeProperty WordSpacing { get; } = Attribute("word-spacing");
	[SvgElementProperty] public SvgDomAttributeProperty WritingMode { get; } = Attribute("writing-mode");

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.HtmlInitialization,
		Translation = PropertyTranslationKind.Composite,
		Comparison = PropertyComparisonKind.ManualReview,
		IsHtmlDefinedProperty = true,
		IsSlottedPropertyCollection = true,
		IsFillRequired = true,
		IsXamlFillRequired = true,
		IsAuditRequired = true,
		Description = "Open data-*, aria-*, namespaced, or compatibility SVG attributes.")]
	public List<DomElementStringProperty> ExtensionAttributes { get; } = [];

	[ElementProperty(
		PropertyValueKind.State,
		PropertyValueStage.DomRuntime,
		Translation = PropertyTranslationKind.Composite,
		Comparison = PropertyComparisonKind.Semantic,
		IsSlottedPropertyCollection = true,
		IsFillRequired = true,
		IsXamlFillRequired = true,
		IsAuditRequired = true,
		Description = "Live SVG geometry, computed style, state, and effect values.")]
	public List<DomElementRuntimeProperty> RuntimeProperties { get; } = [];

	public void AddExtensionAttribute(string name)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		var declared = ElementPropertyTraitsReflector
			.GetAttributes(GetType())
			.Where(static traits =>
				traits.IsHtmlDefinedProperty && traits.IsSlottedProperty)
			.Select(traits => GetType().GetProperty(traits.PropertyName)?.GetValue(this))
			.OfType<SvgDomAttributeProperty>()
			.Any(item => string.Equals(item.Name, name, StringComparison.Ordinal));
		if (!declared && !ExtensionAttributes.Any(
			item => string.Equals(item.Name, name, StringComparison.Ordinal)))
		{
			ExtensionAttributes.Add(Attribute(name));
		}
	}

	public void AddRuntimeProperty(
		string name,
		ElementSlotCategory category,
		ElementEvidenceKind evidenceKind)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (!RuntimeProperties.Any(item =>
			string.Equals(item.Name, name, StringComparison.Ordinal)))
		{
			RuntimeProperties.Add(new(name, category, evidenceKind));
		}
	}

	public void AddRuntimeProperty(string name, ElementSlotCategory category) =>
		AddRuntimeProperty(
			name,
			category,
			DomElementRuntimePropertyCatalog.ResolveEvidenceKind(name));

	protected static SvgDomAttributeProperty Attribute(string name) => new(name);
}
