using Iwesun.Runtime.Web;
using Iwesun.Runtime.WebView2;
using Microsoft.UI.Xaml;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Holds CSS semantics that belong to the generated XAML object but do not
/// always have a one-to-one WinUI dependency property. Native WinUI values
/// remain authoritative whenever a native representation exists.
/// </summary>
internal sealed class WinUiCssSemanticState
{
	private readonly IReadOnlyDictionary<
		string,
		HtmlRuntimeStyleBinding> _bindings;

	internal WinUiCssSemanticState(
		IEnumerable<HtmlRuntimeStyleBinding> bindings)
	{
		ArgumentNullException.ThrowIfNull(bindings);
		_bindings = bindings.ToDictionary(
			static binding => binding.PropertyName,
			StringComparer.Ordinal);
	}

	[Obsolete(
		"Source DOM/CSS evidence is not target WinUI runtime evidence. "
		+ "Audit/query code must read the live WinUI object or report "
		+ "TargetUnsupported.",
		true)]
	internal bool TryRead(
		string propertyName,
		XamlPropertyDataSlot slot,
		out string value)
	{
		value = string.Empty;
		if (!_bindings.TryGetValue(propertyName, out var binding))
			return false;
		var evidence = slot switch
		{
			XamlPropertyDataSlot.Initialization => binding.Initialization,
			XamlPropertyDataSlot.Runtime => binding.Runtime,
			_ => null
		};
		if (evidence?.Status != WebRuntimeDomPropertyStatus.Captured)
			return false;
		value = evidence.Value;
		return true;
	}

	internal bool HasActiveLink(string propertyName) =>
		_bindings.TryGetValue(propertyName, out var binding)
		&& binding.Link?.Status == WebRuntimeDomPropertyStatus.Captured;
}

internal static class WinUiCssSemantic
{
	private static readonly DependencyProperty StateProperty =
		DependencyProperty.RegisterAttached(
			"State",
			typeof(WinUiCssSemanticState),
			typeof(WinUiCssSemantic),
			new PropertyMetadata(null));
	private static readonly DependencyProperty PositioningStateProperty =
		DependencyProperty.RegisterAttached(
			"PositioningState",
			typeof(WinUiPositioningState),
			typeof(WinUiCssSemantic),
			new PropertyMetadata(null));
	private static readonly DependencyProperty VisibilityStateProperty =
		DependencyProperty.RegisterAttached(
			"VisibilityState",
			typeof(WinUiVisibilityState),
			typeof(WinUiCssSemantic),
			new PropertyMetadata(null));

	internal static void SetState(
		DependencyObject target,
		WinUiCssSemanticState state)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(state);
		target.SetValue(StateProperty, state);
	}

	[Obsolete(
		"Source DOM/CSS semantic state must never be used as a WinUI "
		+ "audit/query fallback.",
		true)]
	internal static WinUiCssSemanticState? GetState(
		DependencyObject target)
	{
		ArgumentNullException.ThrowIfNull(target);
		return target.GetValue(StateProperty) as WinUiCssSemanticState;
	}

	internal static void SetPositioningState(
		DependencyObject target,
		WinUiPositioningState state)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(state);
		target.SetValue(PositioningStateProperty, state);
	}

	internal static WinUiPositioningState? GetPositioningState(
		DependencyObject target)
	{
		ArgumentNullException.ThrowIfNull(target);
		return target.GetValue(PositioningStateProperty)
			as WinUiPositioningState;
	}

	internal static void SetVisibilityState(
		DependencyObject target,
		WinUiVisibilityState state)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(state);
		target.SetValue(VisibilityStateProperty, state);
	}

	internal static WinUiVisibilityState? GetVisibilityState(
		DependencyObject target)
	{
		ArgumentNullException.ThrowIfNull(target);
		return target.GetValue(VisibilityStateProperty)
			as WinUiVisibilityState;
	}
}

internal sealed record WinUiPositioningState(
	string Position,
	double? Left,
	double? Right,
	double? Top,
	double? Bottom,
	double X,
	double Y);

internal sealed record WinUiVisibilityState(
	string Display,
	string Visibility);
