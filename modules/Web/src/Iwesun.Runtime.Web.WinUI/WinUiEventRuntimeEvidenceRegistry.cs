using System.Text.Json;
using Iwesun.Runtime.Web;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Iwesun.Runtime.Web.WinUI;

public sealed class WinUiEventRuntimeEvidenceRegistry
{
	private static readonly JsonSerializerOptions EventJsonOptions =
		new(JsonSerializerDefaults.Web);

	private readonly Dictionary<(string ElementName, string EventName), EventEvidence>
		_byRoute = new();

	public void Register(
		FrameworkElement element,
		string elementPath,
		string eventName,
		XamlEventRoutingStrategy routingStrategy)
	{
		ArgumentNullException.ThrowIfNull(element);
		ArgumentException.ThrowIfNullOrWhiteSpace(element.Name);
		ArgumentException.ThrowIfNullOrWhiteSpace(elementPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
		var key = (element.Name, eventName.ToLowerInvariant());
		if (_byRoute.ContainsKey(key))
			return;
		var evidence = new EventEvidence(
			elementPath,
			eventName,
			routingStrategy);
		if (!Attach(element, eventName))
		{
			throw new InvalidDataException(
				$"DOM event '{eventName}' has no real WinUI event adapter for {element.GetType().Name}.");
		}
		_byRoute[key] = evidence;
	}

	private void Record(
		FrameworkElement currentTarget,
		RoutedEventArgs args,
		string eventName)
	{
		if (!_byRoute.TryGetValue(
			(currentTarget.Name, eventName.ToLowerInvariant()),
			out var evidence))
			return;
		evidence.InvocationCount++;
		evidence.LastInvokedAt = DateTimeOffset.UtcNow;
		evidence.OriginalSourceIdentity =
			(args.OriginalSource as FrameworkElement)?.Name ?? string.Empty;
		evidence.CurrentTargetIdentity = currentTarget.Name;
		evidence.Handled = args switch
		{
			TappedRoutedEventArgs tapped => tapped.Handled,
			DoubleTappedRoutedEventArgs doubleTapped => doubleTapped.Handled,
			RightTappedRoutedEventArgs rightTapped => rightTapped.Handled,
			PointerRoutedEventArgs pointer => pointer.Handled,
			KeyRoutedEventArgs key => key.Handled,
			_ => false
		};
	}

	public XamlPropertyQueryResult Query(
		string elementPath,
		string propertyName,
		XamlPropertyDataSlot slot)
	{
		var eventName = EventName(propertyName);
		var evidence = _byRoute.Values.FirstOrDefault(item =>
			item.ElementPath.Equals(elementPath, StringComparison.Ordinal)
				&& item.EventName.Equals(
					eventName,
					StringComparison.OrdinalIgnoreCase));
		if (evidence is null)
		{
			return XamlPropertyQueryResult.ConfirmedAbsent(
				$"No registered WinUI route for {elementPath}.{eventName}.");
		}
		return slot switch
		{
			XamlPropertyDataSlot.Initialization =>
				XamlPropertyQueryResult.DirectConstant(
					JsonSerializer.Serialize(
						new
						{
							kind = EventKind(evidence.EventName).ToString(),
							eventName = evidence.EventName,
							implementationKind =
								XamlEventImplementationKind.RoutedEventHandler.ToString(),
							routingStrategy = evidence.RoutingStrategy.ToString(),
							handledEventsToo = false
						},
						EventJsonOptions),
					"Read from the registered WinUI routed-event adapter."),
			XamlPropertyDataSlot.Link =>
				XamlPropertyQueryResult.Captured(
					$"event-map:{elementPath}:{evidence.EventName}",
					ElementPropertyValueSource.LinkedCalculation,
					new(
						ElementPropertyLinkKind.XamlBinding,
						$"event-map:{elementPath}:{evidence.EventName}"),
					"Verified against the active event-map registration."),
			XamlPropertyDataSlot.Runtime when evidence.InvocationCount > 0 =>
				XamlPropertyQueryResult.DirectConstant(
					JsonSerializer.Serialize(
						new
						{
							evidence.InvocationCount,
							evidence.LastInvokedAt,
							evidence.OriginalSourceIdentity,
							evidence.CurrentTargetIdentity,
							routingStrategy =
								evidence.RoutingStrategy.ToString(),
							evidence.Handled
						},
						EventJsonOptions),
					"Read from the live WinUI routed-event invocation counter."),
			XamlPropertyDataSlot.Runtime =>
				XamlPropertyQueryResult.ConfirmedAbsent(
					"The registered WinUI event has not been invoked."),
			_ => XamlPropertyQueryResult.TargetUnsupported(
				$"Unsupported event evidence slot: {slot}.")
		};
	}

	private static string EventName(string propertyName)
	{
		var parts = propertyName.Split(
			'.',
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return parts.Length >= 2 ? parts[1] : propertyName;
	}

	private static XamlEventKind EventKind(string eventName) =>
		eventName.Equals("dblclick", StringComparison.OrdinalIgnoreCase)
			? XamlEventKind.DoubleTapped
			:
		Enum.TryParse<XamlEventKind>(
			eventName,
			ignoreCase: true,
			out var kind)
				? kind
				: XamlEventKind.Custom;

	private bool Attach(FrameworkElement element, string eventName)
	{
		void RecordEvent(object sender, RoutedEventArgs args)
		{
			if (sender is FrameworkElement target)
				Record(target, args, eventName);
		}
		switch (eventName.ToLowerInvariant())
		{
			case "click" when element is ButtonBase button:
				button.Click += RecordEvent;
				return true;
			case "click":
				element.Tapped += RecordEvent;
				return true;
			case "tapped":
				element.Tapped += RecordEvent;
				return true;
			case "doubletapped":
			case "dblclick":
				element.DoubleTapped += RecordEvent;
				return true;
			case "righttapped":
			case "contextmenu":
			case "auxclick":
				element.RightTapped += RecordEvent;
				return true;
			case "pointerpressed":
			case "pointerdown":
			case "mousedown":
				element.PointerPressed += RecordEvent;
				return true;
			case "pointerreleased":
			case "pointerup":
			case "mouseup":
				element.PointerReleased += RecordEvent;
				return true;
			case "pointermoved":
			case "pointermove":
			case "mousemove":
				element.PointerMoved += RecordEvent;
				return true;
			case "pointerentered":
			case "pointerenter":
			case "pointerover":
			case "mouseenter":
			case "mouseover":
				element.PointerEntered += RecordEvent;
				return true;
			case "pointerexited":
			case "pointerleave":
			case "pointerout":
			case "mouseleave":
			case "mouseout":
				element.PointerExited += RecordEvent;
				return true;
			case "pointercanceled":
			case "pointercancel":
				element.PointerCanceled += RecordEvent;
				return true;
			case "pointercapturelost":
			case "lostpointercapture":
				element.PointerCaptureLost += RecordEvent;
				return true;
			case "wheel":
				element.PointerWheelChanged += RecordEvent;
				return true;
			case "keydown":
				element.KeyDown += RecordEvent;
				return true;
			case "keyup":
				element.KeyUp += RecordEvent;
				return true;
			case "gotfocus":
			case "focus":
			case "focusin":
				element.GotFocus += RecordEvent;
				return true;
			case "lostfocus":
			case "blur":
			case "focusout":
				element.LostFocus += RecordEvent;
				return true;
			case "loaded":
			case "load":
				element.Loaded += RecordEvent;
				return true;
			case "unloaded":
			case "unload":
				element.Unloaded += RecordEvent;
				return true;
			case "textchanged" or "input" when element is TextBox textBox:
				textBox.TextChanged += RecordEvent;
				return true;
			case "input" when element is PasswordBox passwordBox:
				passwordBox.PasswordChanged += RecordEvent;
				return true;
			case "input" when element is HtmlCursorPasswordBoxHost passwordHost:
				passwordHost.Editor.PasswordChanged += RecordEvent;
				return true;
			case "selectionchanged" or "select" when element is TextBox selectionTextBox:
				selectionTextBox.SelectionChanged += RecordEvent;
				return true;
			case "selectionchanged" or "change" when element is Selector selector:
				selector.SelectionChanged += RecordEvent;
				return true;
			case "valuechanged" when element is RangeBase range:
				range.ValueChanged += RecordEvent;
				return true;
			case "toggled" or "change" when element is ToggleSwitch toggle:
				toggle.Toggled += RecordEvent;
				return true;
			case "change" when element is ToggleButton toggleButton:
				toggleButton.Checked += RecordEvent;
				toggleButton.Unchecked += RecordEvent;
				return true;
			case "dragenter":
				element.DragEnter += RecordEvent;
				return true;
			case "dragleave":
				element.DragLeave += RecordEvent;
				return true;
			case "dragover":
				element.DragOver += RecordEvent;
				return true;
			case "drop":
				element.Drop += RecordEvent;
				return true;
		}
		return false;
	}

	private sealed class EventEvidence(
		string elementPath,
		string eventName,
		XamlEventRoutingStrategy routingStrategy)
	{
		public string ElementPath { get; } = elementPath;
		public string EventName { get; } = eventName;
		public XamlEventRoutingStrategy RoutingStrategy { get; } =
			routingStrategy;
		public long InvocationCount { get; set; }
		public DateTimeOffset? LastInvokedAt { get; set; }
		public string OriginalSourceIdentity { get; set; } = string.Empty;
		public string CurrentTargetIdentity { get; set; } = string.Empty;
		public bool Handled { get; set; }
	}
}
