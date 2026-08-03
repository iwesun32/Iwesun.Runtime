namespace Iwesun.Runtime.Web;

public sealed class HtmlDomEventHandler(HtmlEventHandlerDefinition definition) :
	DomElementEvent(
		$"handler.{definition.AttributeName}",
		definition.Translation,
		ElementEventSlot<DomEventRegistration>.Unset,
		ElementEventLink.None,
		ElementEventSlot<DomEventRuntimeEvidence>.Unset,
		ElementEventSlot<XamlEventRegistration>.Unset,
		ElementEventLink.None,
		ElementEventSlot<XamlEventRuntimeEvidence>.Unset)
{
	public HtmlEventHandlerDefinition Definition { get; } = definition;

	public override DomQueryApplicationResult ApplyDomQueryResult(
		DomPropertyDataSlot slot,
		DomPropertyQueryResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (result.Status == DomPropertyQueryStatus.ConfirmedAbsent)
		{
			switch (slot)
			{
				case DomPropertyDataSlot.Initialization:
					DomInitialization = ElementEventSlot<DomEventRegistration>.Unset;
					DomLink = ElementEventLink.None;
					break;
				case DomPropertyDataSlot.Link:
					if (!DomInitialization.IsSet)
						DomLink = ElementEventLink.None;
					break;
				case DomPropertyDataSlot.Runtime:
					DomRuntime = ElementEventSlot<DomEventRuntimeEvidence>.Unset;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(slot));
			}
			return DomQueryApplicationResult.NotApplied(result.Description);
		}
		if (!result.HasValue)
			return DomQueryApplicationResult.NotApplied(result.Description);
		switch (slot)
		{
			case DomPropertyDataSlot.Initialization:
				var registration = ReadRegistration(result.Value);
				DomInitialization = ElementEventSlot<DomEventRegistration>.FromValue(
					new(
						Definition.DomKind,
						Definition.EventName,
						registration.Kind,
						registration.UseCapture
							? DomEventPhase.Capture
							: DomEventPhase.Target,
						Bubbles(Definition.EventName),
						Cancelable(Definition.EventName),
						Composed(Definition.EventName),
						registration.Passive,
						registration.Once));
				if (!string.IsNullOrWhiteSpace(registration.InlineSource))
					DomLink = new(
						$"inline-handler:{registration.InlineSource}");
				break;
			case DomPropertyDataSlot.Link:
				if (result.Link.IsSet)
					DomLink = new(result.Link.Description);
				break;
			case DomPropertyDataSlot.Runtime:
				DomRuntime = ElementEventSlot<DomEventRuntimeEvidence>.FromValue(
					ReadRuntimeEvidence(result.Value));
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(slot));
		}
		return slot switch
		{
			DomPropertyDataSlot.Initialization => new(
				DomInitialization.IsSet,
				DomInitialization.IsSet,
				DomInitialization.IsSet ? Definition.EventName : string.Empty,
				"Inline DOM handler parsed into a typed registration."),
			DomPropertyDataSlot.Link => new(
				DomLink.IsSet,
				DomLink.IsSet && DomLink.Description == result.Link.Description,
				DomLink.Description,
				"Inline DOM handler link application."),
			DomPropertyDataSlot.Runtime => new(
				DomRuntime.IsSet,
				DomRuntime.IsSet,
				DomRuntime.IsSet
					? DomRuntime.Value!.InvocationCount.ToString(
						System.Globalization.CultureInfo.InvariantCulture)
					: string.Empty,
				"DOM event runtime evidence was applied."),
			_ => throw new ArgumentOutOfRangeException(nameof(slot))
		};
	}

	public override void ApplyXamlQueryResult(
		XamlPropertyDataSlot slot,
		XamlPropertyQueryResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (result.Status == XamlPropertyQueryStatus.ConfirmedAbsent)
		{
			switch (slot)
			{
				case XamlPropertyDataSlot.Initialization:
					XamlInitialization = ElementEventSlot<XamlEventRegistration>.Unset;
					break;
				case XamlPropertyDataSlot.Link:
					XamlLink = ElementEventLink.None;
					break;
				case XamlPropertyDataSlot.Runtime:
					XamlRuntime = ElementEventSlot<XamlEventRuntimeEvidence>.Unset;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(slot));
			}
			return;
		}
		if (!result.HasValue)
			return;
		if (slot == XamlPropertyDataSlot.Initialization)
		{
			XamlInitialization = ElementEventSlot<XamlEventRegistration>.FromValue(
				new(
					Definition.XamlKind,
					Definition.EventName,
					Definition.Translation == EventTranslationKind.Direct
						? XamlEventImplementationKind.RoutedEventHandler
						: XamlEventImplementationKind.RuntimeAdapter,
					XamlEventRoutingStrategy.Bubble,
					false));
		}
		else if (slot == XamlPropertyDataSlot.Link && result.Link.IsSet)
		{
			XamlLink = new(result.Link.Description);
		}
	}

	public override DomElementSlottedPropertyAuditResult AuditSlots()
	{
		var initialization = !DomInitialization.IsSet && !XamlInitialization.IsSet
			? ElementSlotFeatureAuditResult.NotRequired("DOM and XAML handlers are absent.")
			: !DomInitialization.IsSet
				? new(
					ElementSlotFeatureAuditStatus.UnexpectedXamlValue,
					string.Empty,
					Definition.AttributeName,
					"XAML handler exists without a DOM handler.")
			: Definition.Translation == EventTranslationKind.Unsupported
				? new(
					ElementSlotFeatureAuditStatus.ManualReviewRequired,
					Definition.AttributeName,
					string.Empty,
					"Inline DOM handler has no XAML translation.")
				: XamlInitialization.IsSet
					? Passed("DOM and XAML event registrations are present.")
					: new(
						ElementSlotFeatureAuditStatus.MissingXamlValue,
						Definition.AttributeName,
						string.Empty,
						"XAML event registration is missing.");
		return new(
			Name,
			Category,
			initialization,
			AuditRuntime(),
			!DomLink.IsSet || DomLink == XamlLink
				? Passed("Event link is absent or matches.")
				: new(
					ElementSlotFeatureAuditStatus.LinkMismatch,
					DomLink.Description,
					XamlLink.Description,
					"Inline handler source was not translated."),
			ElementSlotFeatureAuditResult.NotRequired("No layout contract."),
			$"{Definition.Specification}: {Definition.AttributeName}.");
	}

	private ElementSlotFeatureAuditResult AuditRuntime()
	{
		if (!DomRuntime.IsSet && !XamlRuntime.IsSet)
			return ElementSlotFeatureAuditResult.NotRequired(
				"No invocation evidence was captured.");
		if (!DomRuntime.IsSet)
		{
			return new(
				ElementSlotFeatureAuditStatus.UnexpectedXamlValue,
				string.Empty,
				XamlRuntime.Value?.InvocationCount.ToString(
					System.Globalization.CultureInfo.InvariantCulture)
					?? string.Empty,
				"XAML has invocation evidence absent from the DOM.");
		}
		if (!XamlRuntime.IsSet)
		{
			return new(
				ElementSlotFeatureAuditStatus.MissingXamlValue,
				DomRuntime.Value!.InvocationCount.ToString(
					System.Globalization.CultureInfo.InvariantCulture),
				string.Empty,
				"XAML invocation evidence is missing.");
		}
		var passed = DomRuntime.Value!.InvocationCount
			== XamlRuntime.Value!.InvocationCount;
		return new(
			passed
				? ElementSlotFeatureAuditStatus.Passed
				: ElementSlotFeatureAuditStatus.ValueMismatch,
			DomRuntime.Value.InvocationCount.ToString(
				System.Globalization.CultureInfo.InvariantCulture),
			XamlRuntime.Value.InvocationCount.ToString(
				System.Globalization.CultureInfo.InvariantCulture),
			passed
				? "DOM and XAML invocation counts match."
				: "DOM and XAML invocation counts differ.");
	}

	private static (
		DomEventRegistrationKind Kind,
		bool UseCapture,
		bool Passive,
		bool Once,
		string InlineSource) ReadRegistration(string json)
	{
		if (!json.TrimStart().StartsWith('{'))
		{
			return (
				DomEventRegistrationKind.InlineAttribute,
				false,
				false,
				false,
				json);
		}
		using var document = System.Text.Json.JsonDocument.Parse(json);
		var root = document.RootElement;
		var kind = root.TryGetProperty("registrationKind", out var kindValue)
			&& Enum.TryParse<DomEventRegistrationKind>(
				kindValue.GetString(),
				true,
				out var parsed)
					? parsed
					: DomEventRegistrationKind.InlineAttribute;
		return (
			kind,
			ReadBoolean(root, "useCapture"),
			ReadBoolean(root, "passive"),
			ReadBoolean(root, "once"),
			ReadString(root, "source"));
	}

	private static DomEventRuntimeEvidence ReadRuntimeEvidence(string json)
	{
		using var document = System.Text.Json.JsonDocument.Parse(json);
		var root = document.RootElement;
		return new(
			ReadInt64(root, "invocationCount"),
			ReadDateTimeOffset(root, "lastInvokedAt"),
			ReadString(root, "targetXPath"),
			ReadString(root, "currentTargetXPath"),
			ReadEnum(root, "phase", DomEventPhase.None),
			ReadBoolean(root, "defaultPrevented"),
			ReadBoolean(root, "propagationStopped"),
			ReadBoolean(root, "immediatePropagationStopped"));
	}

	private static bool ReadBoolean(
		System.Text.Json.JsonElement root,
		string propertyName) =>
		root.TryGetProperty(propertyName, out var value)
			&& value.ValueKind == System.Text.Json.JsonValueKind.True;

	private static long ReadInt64(
		System.Text.Json.JsonElement root,
		string propertyName) =>
		root.TryGetProperty(propertyName, out var value)
			&& value.TryGetInt64(out var result)
				? result
				: 0;

	private static string ReadString(
		System.Text.Json.JsonElement root,
		string propertyName) =>
		root.TryGetProperty(propertyName, out var value)
			? value.GetString() ?? string.Empty
			: string.Empty;

	private static DateTimeOffset? ReadDateTimeOffset(
		System.Text.Json.JsonElement root,
		string propertyName) =>
		root.TryGetProperty(propertyName, out var value)
			&& value.ValueKind == System.Text.Json.JsonValueKind.String
			&& value.TryGetDateTimeOffset(out var result)
				? result
				: null;

	private static TEnum ReadEnum<TEnum>(
		System.Text.Json.JsonElement root,
		string propertyName,
		TEnum fallback)
		where TEnum : struct, Enum =>
		root.TryGetProperty(propertyName, out var value)
			&& Enum.TryParse<TEnum>(value.GetString(), true, out var result)
				? result
				: fallback;

	private static bool Bubbles(string eventName) =>
		eventName is not ("blur" or "focus" or "mouseenter" or "mouseleave"
			or "pointerenter" or "pointerleave" or "load" or "unload");

	private static bool Cancelable(string eventName) =>
		eventName is not ("load" or "unload" or "scroll" or "resize");

	private static bool Composed(string eventName) =>
		eventName is not ("mouseenter" or "mouseleave");

	private static ElementSlotFeatureAuditResult Passed(string description) =>
		new(ElementSlotFeatureAuditStatus.Passed, string.Empty, string.Empty, description);
}
