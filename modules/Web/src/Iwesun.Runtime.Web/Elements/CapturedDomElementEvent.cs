namespace Iwesun.Runtime.Web;

/// <summary>
/// A concrete event slot created from a listener registered on a live DOM element.
/// </summary>
public sealed class CapturedDomElementEvent(
	string name,
	DomEventKind kind,
	string eventName) :
	DomElementEvent(
		name,
		EventTranslationKind.RuntimeAdapter,
		ElementEventSlot<DomEventRegistration>.Unset,
		ElementEventLink.None,
		ElementEventSlot<DomEventRuntimeEvidence>.Unset,
		ElementEventSlot<XamlEventRegistration>.Unset,
		ElementEventLink.None,
		ElementEventSlot<XamlEventRuntimeEvidence>.Unset)
{
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
					break;
				case DomPropertyDataSlot.Link:
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
				DomInitialization = ElementEventSlot<DomEventRegistration>.FromValue(
					new(
						kind,
						eventName,
						DomEventRegistrationKind.EventListener,
						ReadCapture(result.Value)
							? DomEventPhase.Capture
							: DomEventPhase.Bubble,
						Bubbles(eventName),
						Cancelable(eventName),
						Composed(eventName),
						ReadBoolean(result.Value, "passive"),
						ReadBoolean(result.Value, "once")));
				break;
			case DomPropertyDataSlot.Link:
				if (result.Link.IsSet)
					DomLink = new(result.Link.Description);
				break;
			case DomPropertyDataSlot.Runtime:
				DomRuntime = ElementEventSlot<DomEventRuntimeEvidence>.FromValue(
					ReadDomRuntime(result.Value));
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(slot));
		}
		return slot switch
		{
			DomPropertyDataSlot.Initialization => new(
				DomInitialization.IsSet,
				DomInitialization.IsSet,
				DomInitialization.IsSet ? eventName : string.Empty,
				"DOM listener JSON parsed into a typed registration."),
			DomPropertyDataSlot.Link => new(
				DomLink.IsSet,
				DomLink.IsSet && DomLink.Description == result.Link.Description,
				DomLink.Description,
				"DOM listener link application."),
			DomPropertyDataSlot.Runtime => new(
				DomRuntime.IsSet,
				DomRuntime.IsSet,
				DomRuntime.IsSet
					? DomRuntime.Value!.InvocationCount.ToString(
						System.Globalization.CultureInfo.InvariantCulture)
					: string.Empty,
				"DOM invocation evidence parsed into a typed runtime slot."),
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
		switch (slot)
		{
			case XamlPropertyDataSlot.Initialization:
				XamlInitialization =
					ElementEventSlot<XamlEventRegistration>.FromValue(
						ReadXamlRegistration(result.Value));
				break;
			case XamlPropertyDataSlot.Link:
				if (result.Link.IsSet)
					XamlLink = new(result.Link.Description);
				break;
			case XamlPropertyDataSlot.Runtime:
				XamlRuntime =
					ElementEventSlot<XamlEventRuntimeEvidence>.FromValue(
						ReadXamlRuntime(result.Value));
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(slot));
		}
	}

	public override DomElementSlottedPropertyAuditResult AuditSlots()
	{
		var initialization = !DomInitialization.IsSet && !XamlInitialization.IsSet
			? ElementSlotFeatureAuditResult.NotRequired("DOM and XAML listeners are absent.")
			: !DomInitialization.IsSet
				? new(
					ElementSlotFeatureAuditStatus.UnexpectedXamlValue,
					string.Empty,
					eventName,
					"XAML event registration exists without a DOM listener.")
			: XamlInitialization.IsSet
				? Passed("DOM and XAML event registrations are present.")
				: new(
					ElementSlotFeatureAuditStatus.MissingXamlValue,
					eventName,
					string.Empty,
					"XAML event registration is missing.");
		return new(
			Name,
			Category,
			initialization,
			AuditRuntime(),
			DomLink == XamlLink
				? Passed("Event links match.")
				: new(
					ElementSlotFeatureAuditStatus.LinkMismatch,
					DomLink.Description,
					XamlLink.Description,
					"Event links differ."),
			ElementSlotFeatureAuditResult.NotRequired("No layout contract."),
			"Live DOM event registration audit.");
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

	private static DomEventRuntimeEvidence ReadDomRuntime(string json)
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

	private static XamlEventRegistration ReadXamlRegistration(string json)
	{
		using var document = System.Text.Json.JsonDocument.Parse(json);
		var root = document.RootElement;
		return new(
			ReadEnum(root, "kind", XamlEventKind.Custom),
			ReadString(root, "eventName"),
			ReadEnum(
				root,
				"implementationKind",
				XamlEventImplementationKind.RuntimeAdapter),
			ReadEnum(root, "routingStrategy", XamlEventRoutingStrategy.Direct),
			ReadBoolean(root, "handledEventsToo"));
	}

	private static XamlEventRuntimeEvidence ReadXamlRuntime(string json)
	{
		using var document = System.Text.Json.JsonDocument.Parse(json);
		var root = document.RootElement;
		return new(
			ReadInt64(root, "invocationCount"),
			ReadDateTimeOffset(root, "lastInvokedAt"),
			ReadString(root, "originalSourceIdentity"),
			ReadString(root, "currentTargetIdentity"),
			ReadEnum(root, "routingStrategy", XamlEventRoutingStrategy.Direct),
			ReadBoolean(root, "handled"));
	}

	private static bool ReadCapture(string json) =>
		ReadBoolean(json, "useCapture");

	private static bool ReadBoolean(string json, string propertyName)
	{
		using var document = System.Text.Json.JsonDocument.Parse(json);
		return ReadBoolean(document.RootElement, propertyName);
	}

	private static bool ReadBoolean(
		System.Text.Json.JsonElement root,
		string propertyName) =>
		root.TryGetProperty(propertyName, out var value)
			&& value.ValueKind is System.Text.Json.JsonValueKind.True;

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
			? value.ValueKind == System.Text.Json.JsonValueKind.Number
				&& value.TryGetInt32(out var numeric)
				&& Enum.IsDefined(typeof(TEnum), numeric)
					? (TEnum)Enum.ToObject(typeof(TEnum), numeric)
					: value.ValueKind == System.Text.Json.JsonValueKind.String
						&& Enum.TryParse<TEnum>(
							value.GetString(),
							ignoreCase: true,
							out var result)
								? result
								: fallback
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
