using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Iwesun.Runtime.Web;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Iwesun.Runtime.Web.WinUI;

public sealed partial class WinUiXamlElementObjectFactory
{
	public void BindGlobalRelationships(
		HtmlRuntimeXamlGlobalRelationshipGraph relationships)
	{
		ArgumentNullException.ThrowIfNull(relationships);
		BindGlobalStyles(relationships.GlobalStyleBindings);
		foreach (var binding in relationships.DataBindings)
			BindData(binding);
		if (_eventEvidenceRegistry is null)
			return;
		foreach (var binding in relationships.EventBindings)
			BindEvent(binding);
	}

	private void BindGlobalStyles(
		IReadOnlyList<HtmlRuntimeXamlGlobalStyleBinding> bindings)
	{
		if (_styles is null)
			return;
		var alreadyMaterialized = _styles.XamlTargets
			.Select(static target => (
				target.Source.DocumentScope,
				target.Source.XPath,
				target.Source.PropertyName))
			.ToHashSet();
		var materialized = bindings.Where(static binding =>
			binding.IsMaterialized
				&& binding.Target is not null
				&& binding.Resolution is not null
				&& !string.IsNullOrWhiteSpace(
					binding.Execution.MarkupAttributeName)
				&& !IsOutOfFlowPositionBinding(binding))
			.Where(binding => !alreadyMaterialized.Contains((
				binding.Source.Source.DocumentScope,
				binding.Source.Source.XPath,
				binding.Source.Source.PropertyName)))
			.ToArray();
		foreach (var binding in materialized)
		{
			if (binding.Execution.MarkupAttributeName is
				("Margin" or "Padding" or "BorderThickness" or "CornerRadius"))
			{
				throw new InvalidDataException(
					$"{binding.Source.Element.GetType().Name} did not materialize "
						+ $"the composite {binding.Execution.MarkupAttributeName} "
						+ "inside its Runtime Web element-class conversion.");
			}
			_styles.RegisterXamlTarget(
				binding.Resolution!,
				ResolveGlobalStyleTarget(
					binding.Target!,
					binding.Execution.MarkupAttributeName),
				binding.Execution.MarkupAttributeName);
		}
	}

	private static bool IsOutOfFlowPositionBinding(
		HtmlRuntimeXamlGlobalStyleBinding binding) =>
		binding.Source.Element.XamlObjectNode?.Plan.LayoutPlacement?.IsOutOfFlow
			== true
		&& binding.Execution.MarkupAttributeName is
			"HtmlPosition.Left"
				or "HtmlPosition.Top"
				or "HtmlPosition.Right"
				or "HtmlPosition.Bottom";

	private static object ResolveGlobalStyleTarget(
		object target,
		string targetProperty) =>
		target is HtmlFilteredElementHost filtered
			&& targetProperty.StartsWith("HtmlText", StringComparison.Ordinal)
				? filtered.InnerElement
				: target;

	private static HtmlRuntimeXamlGlobalStyleBinding[]? TryOrderComposite(
		IReadOnlyList<HtmlRuntimeXamlGlobalStyleBinding> bindings)
	{
		if (bindings.Count != 4)
			return null;
		var suffixes = new[] { "Top", "Right", "Bottom", "Left" };
		var ordered = new HtmlRuntimeXamlGlobalStyleBinding[4];
		for (var index = 0; index < suffixes.Length; index++)
		{
			var suffix = suffixes[index];
			var match = bindings.SingleOrDefault(binding =>
				binding.Source.Source.PropertyName.EndsWith(
					suffix,
					StringComparison.Ordinal));
			if (match is null)
				return null;
			ordered[index] = match;
		}
		return ordered;
	}

	internal void RebindRuntimeMutableData(
		HtmlRuntimeXamlGlobalRelationshipGraph relationships)
	{
		ArgumentNullException.ThrowIfNull(relationships);
		foreach (var binding in relationships.DataBindings.Where(static item =>
			item.Source.Source.XamlTargetKind is
				XamlControlDataTargetKind.IsChecked
				or XamlControlDataTargetKind.IsIndeterminate
				or XamlControlDataTargetKind.IsEnabled
				or XamlControlDataTargetKind.IsReadOnly
				or XamlControlDataTargetKind.TabIndex
				or XamlControlDataTargetKind.IsValid
				or XamlControlDataTargetKind.WillValidate
				or XamlControlDataTargetKind.ValidationMessage
				or XamlControlDataTargetKind.SelectedItem
				or XamlControlDataTargetKind.SelectedValue
				or XamlControlDataTargetKind.Value
			|| item.Source.Source.XamlTargetKind is
					XamlControlDataTargetKind.Text
					or XamlControlDataTargetKind.Content
				&& item.Target is ContentControl))
		{
			BindData(binding);
		}
	}

	private static void BindData(HtmlRuntimeXamlGlobalDataBinding binding)
	{
		if (!binding.IsMaterialized
			|| binding.Target is not FrameworkElement frameworkElement)
		{
			return;
		}
		var target = frameworkElement is HtmlFilteredElementHost filtered
			? filtered.InnerElement
			: frameworkElement;
		target = ResolveDataTarget(binding, target);
		if (binding.Source.Source.XamlTargetKind
				== XamlControlDataTargetKind.IsIndeterminate
			&& target is ToggleButton indeterminateToggle)
		{
			var isIndeterminate = ParseBoolean(binding.Source.DynamicValue.Value);
			indeterminateToggle.IsThreeState = isIndeterminate;
			if (isIndeterminate)
				indeterminateToggle.IsChecked = null;
			return;
		}
		if (binding.Source.Source.XamlTargetKind
				== XamlControlDataTargetKind.IsChecked
			&& binding.Source.Element.TryGetDomStringSlotValue(
				"state.indeterminate",
				DomPropertyDataSlot.Runtime,
				out var indeterminate)
			&& ParseBoolean(indeterminate))
		{
			// HTML indeterminate is a separate runtime state. Binding checked=false
			// would overwrite the already materialized WinUI null state.
			return;
		}
		if (!TryResolveDataTarget(binding, target, out var owner, out var property,
			out var converter))
		{
			return;
		}
		owner.SetBinding(
			property,
			new Binding
			{
				Source = binding.Source.DynamicValue,
				Path = new PropertyPath(
					nameof(HtmlRuntimeDynamicDataValue.Value)),
				Converter = converter,
				ConverterParameter =
					(binding.Source.Element.HtmlRoot as HtmlRuntimeDocumentRoot)
						?.NavigationUrl,
				Mode = BindingMode.OneWay
			});
	}

	private static FrameworkElement ResolveDataTarget(
		HtmlRuntimeXamlGlobalDataBinding binding,
		FrameworkElement target)
	{
		var kind = binding.Source.Source.XamlTargetKind;
		if (kind == XamlControlDataTargetKind.Text
			&& target is not (TextBlock or TextBox or ContentControl))
		{
			return binding.Source.Element.XamlOwnedObjects
				.OfType<FrameworkElement>()
				.FirstOrDefault(static item => item is TextBlock or TextBox)
				?? target;
		}
		if (kind == XamlControlDataTargetKind.Source
			&& target is not (Image or HtmlImageView
				or HtmlMediaElementControl or HtmlEmbeddedContentHost))
		{
			return binding.Source.Element.XamlOwnedObjects
				.OfType<FrameworkElement>()
				.FirstOrDefault(static item => item is Image or HtmlImageView
					or HtmlMediaElementControl or HtmlEmbeddedContentHost)
				?? target;
		}
		return target;
	}

	private static bool TryResolveDataTarget(
		HtmlRuntimeXamlGlobalDataBinding binding,
		FrameworkElement target,
		out FrameworkElement owner,
		out DependencyProperty property,
		out IValueConverter? converter)
	{
		owner = target;
		property = FrameworkElement.TagProperty;
		converter = null;
		var kind = binding.Source.Source.XamlTargetKind;
		switch (kind)
		{
			case XamlControlDataTargetKind.DataContext:
				owner = target;
				property = FrameworkElement.DataContextProperty;
				return true;
			case XamlControlDataTargetKind.PlaceholderText
				when target is TextBox textBox:
				owner = textBox;
				property = TextBox.PlaceholderTextProperty;
				return true;
			case XamlControlDataTargetKind.IsChecked
				when target is ToggleButton toggle:
				owner = toggle;
				property = ToggleButton.IsCheckedProperty;
				converter = BooleanDynamicValueConverter.Instance;
				return true;
			case XamlControlDataTargetKind.IsEnabled
				when target is Control control:
				owner = control;
				property = Control.IsEnabledProperty;
				converter = InverseBooleanDynamicValueConverter.Instance;
				return true;
			case XamlControlDataTargetKind.IsReadOnly
				when target is TextBox readOnlyTextBox:
				owner = readOnlyTextBox;
				property = TextBox.IsReadOnlyProperty;
				converter = BooleanDynamicValueConverter.Instance;
				return true;
			case XamlControlDataTargetKind.TabIndex
				when target is Control tabStop:
				owner = tabStop;
				property = Control.TabIndexProperty;
				converter = Int32DynamicValueConverter.Instance;
				return true;
			case XamlControlDataTargetKind.IsValid:
				owner = target;
				property = HtmlValidation.IsValidProperty;
				converter = BooleanDynamicValueConverter.Instance;
				return true;
			case XamlControlDataTargetKind.WillValidate:
				owner = target;
				property = HtmlValidation.WillValidateProperty;
				converter = BooleanDynamicValueConverter.Instance;
				return true;
			case XamlControlDataTargetKind.ValidationMessage:
				owner = target;
				property = HtmlValidation.ValidationMessageProperty;
				return true;
			case XamlControlDataTargetKind.SelectedValue
				when target is Selector selector:
				owner = selector;
				property = Selector.SelectedValueProperty;
				return true;
			case XamlControlDataTargetKind.SelectedItem
				when target is Selector selector:
				owner = selector;
				property = Selector.SelectedItemProperty;
				return true;
			case XamlControlDataTargetKind.CommandParameter
				when target is ButtonBase button:
				owner = button;
				property = ButtonBase.CommandParameterProperty;
				return true;
			case XamlControlDataTargetKind.Value
				when target is RangeBase range:
				owner = range;
				property = RangeBase.ValueProperty;
				converter = DoubleDynamicValueConverter.Instance;
				return true;
			case XamlControlDataTargetKind.Value
				when target is NumberBox number:
				owner = number;
				property = NumberBox.ValueProperty;
				converter = DoubleDynamicValueConverter.Instance;
				return true;
			case XamlControlDataTargetKind.Value
				when target is TextBox editor:
				owner = editor;
				property = TextBox.TextProperty;
				return true;
			case XamlControlDataTargetKind.Value
				when target is PasswordBox password:
				owner = password;
				property = PasswordBox.PasswordProperty;
				return true;
			case XamlControlDataTargetKind.Value
				when target is HtmlCursorPasswordBoxHost passwordHost:
				owner = passwordHost.Editor;
				property = PasswordBox.PasswordProperty;
				return true;
			case XamlControlDataTargetKind.Text when target is TextBox editor:
				owner = editor;
				property = TextBox.TextProperty;
				return true;
			case XamlControlDataTargetKind.Text when target is TextBlock text:
				owner = text;
				property = TextBlock.TextProperty;
				return true;
			case XamlControlDataTargetKind.Text
				when target is ContentControl content:
				owner = content;
				property = ContentControl.ContentProperty;
				return true;
			case XamlControlDataTargetKind.Content
				when target is ContentControl content:
				owner = content;
				property = ContentControl.ContentProperty;
				return true;
			case XamlControlDataTargetKind.NavigateUri
				when target is HyperlinkButton hyperlink:
				owner = hyperlink;
				property = HyperlinkButton.NavigateUriProperty;
				converter = UriDynamicValueConverter.Instance;
				return true;
			case XamlControlDataTargetKind.Source
				when target is HtmlMediaElementControl media:
				owner = media;
				property = HtmlMediaElementControl.SourceProperty;
				return true;
			case XamlControlDataTargetKind.Source
				when target is HtmlEmbeddedContentHost embedded:
				owner = embedded;
				property = HtmlEmbeddedContentHost.SourceProperty;
				return true;
			case XamlControlDataTargetKind.Source when target is Image image:
				owner = image;
				property = Image.SourceProperty;
				converter = BitmapDynamicValueConverter.Instance;
				return true;
			case XamlControlDataTargetKind.Source
				when target is HtmlImageView imageView:
				owner = imageView.Image;
				property = Image.SourceProperty;
				converter = BitmapDynamicValueConverter.Instance;
				return true;
			default:
				return false;
		}
	}

	private void BindEvent(HtmlRuntimeXamlGlobalEventBinding binding)
	{
		if (!binding.IsMaterialized
			|| binding.Target is not FrameworkElement target)
		{
			return;
		}
		if (string.IsNullOrWhiteSpace(target.Name))
			target.Name = CreateElementName(binding.Source.Element);
		var eventName = binding.Source.Source.Name.StartsWith(
			"event.", StringComparison.Ordinal)
				? binding.Source.Source.Name["event.".Length..]
				: binding.Source.Source.Name;
		var path = binding.Source.Element.DocumentScope.Equals(
			"document", StringComparison.Ordinal)
				? binding.Source.Element.XPath
				: $"{binding.Source.Element.DocumentScope}::"
					+ binding.Source.Element.XPath;
		var registration = binding.Source.Source.DomInitialization.Value;
		var routing = registration?.Phase switch
		{
			DomEventPhase.Capture => XamlEventRoutingStrategy.Tunnel,
			DomEventPhase.Target => XamlEventRoutingStrategy.Direct,
			DomEventPhase.Bubble => XamlEventRoutingStrategy.Bubble,
			_ when registration?.Bubbles == true =>
				XamlEventRoutingStrategy.Bubble,
			_ => XamlEventRoutingStrategy.Direct
		};
		_eventEvidenceRegistry!.Register(target, path, eventName, routing);
	}

	private static string CreateElementName(DomElement element)
	{
		if (element.NodeId > 0)
			return $"DomNode_{element.NodeId}";
		var identity = Encoding.UTF8.GetBytes(
			$"{element.DocumentScope}::{element.XPath}");
		return "Dom_" + Convert.ToHexString(SHA256.HashData(identity))[..12];
	}

	private static bool ParseBoolean(string value) =>
		value.Equals("true", StringComparison.OrdinalIgnoreCase)
		|| value.Equals("checked", StringComparison.OrdinalIgnoreCase)
		|| value == "1";

	internal static double ParseBindingDouble(string value) =>
		double.TryParse(
			value.TrimEnd('p', 'x'),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var parsed)
				? parsed
				: double.NaN;

	internal static BitmapImage? CreateBitmap(string value, Uri? documentUri) =>
		ResolveDynamicUri(value, documentUri) is { } uri
			? new BitmapImage(uri)
			: null;

	internal static Uri? ResolveDynamicUri(string value, Uri? documentUri)
	{
		if (documentUri is not null
			&& value.StartsWith("//", StringComparison.Ordinal)
			&& Uri.TryCreate(
				$"{documentUri.Scheme}:{value}",
				UriKind.Absolute,
				out var resolved))
		{
			return resolved;
		}
		if (Uri.TryCreate(value, UriKind.Absolute, out resolved))
			return resolved;
		return documentUri is not null
			&& Uri.TryCreate(documentUri, value, out resolved)
				? resolved
				: null;
	}

}

internal sealed class BooleanDynamicValueConverter : IValueConverter
{
	public static BooleanDynamicValueConverter Instance { get; } = new();

	public object Convert(object value, Type targetType, object parameter,
		string language) => value is string text && (
		text.Equals("true", StringComparison.OrdinalIgnoreCase)
		|| text.Equals("checked", StringComparison.OrdinalIgnoreCase)
		|| text == "1");

	public object ConvertBack(object value, Type targetType, object parameter,
		string language) => throw new NotSupportedException();
}

internal sealed class InverseBooleanDynamicValueConverter : IValueConverter
{
	public static InverseBooleanDynamicValueConverter Instance { get; } = new();

	public object Convert(object value, Type targetType, object parameter,
		string language) => !Parse(value);

	public object ConvertBack(object value, Type targetType, object parameter,
		string language) => throw new NotSupportedException();

	private static bool Parse(object value) => value is string text && (
		text.Equals("true", StringComparison.OrdinalIgnoreCase)
		|| text.Equals("checked", StringComparison.OrdinalIgnoreCase)
		|| text == "1");
}

internal sealed class Int32DynamicValueConverter : IValueConverter
{
	public static Int32DynamicValueConverter Instance { get; } = new();

	public object Convert(object value, Type targetType, object parameter,
		string language) => value is string text
		&& int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture,
			out var result)
			? result
			: int.MaxValue;

	public object ConvertBack(object value, Type targetType, object parameter,
		string language) => throw new NotSupportedException();
}

internal sealed class DoubleDynamicValueConverter : IValueConverter
{
	public static DoubleDynamicValueConverter Instance { get; } = new();

	public object Convert(object value, Type targetType, object parameter,
		string language) => value is string text
		? WinUiXamlElementObjectFactory.ParseBindingDouble(text)
		: double.NaN;

	public object ConvertBack(object value, Type targetType, object parameter,
		string language) => throw new NotSupportedException();
}

internal sealed class UriDynamicValueConverter : IValueConverter
{
	public static UriDynamicValueConverter Instance { get; } = new();

	public object? Convert(object value, Type targetType, object parameter,
		string language) => value is string text
		? WinUiXamlElementObjectFactory.ResolveDynamicUri(
			text,
			parameter as Uri)
		: null;

	public object ConvertBack(object value, Type targetType, object parameter,
		string language) => throw new NotSupportedException();
}

internal sealed class BitmapDynamicValueConverter : IValueConverter
{
	public static BitmapDynamicValueConverter Instance { get; } = new();

	public object? Convert(object value, Type targetType, object parameter,
		string language) => value is string text
		? WinUiXamlElementObjectFactory.CreateBitmap(text, parameter as Uri)
		: null;

	public object ConvertBack(object value, Type targetType, object parameter,
		string language) => throw new NotSupportedException();
}
