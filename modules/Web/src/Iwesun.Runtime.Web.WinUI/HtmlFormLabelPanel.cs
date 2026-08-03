using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class HtmlFormLabelPanel : HtmlInlineFlowPanel
{
	public static readonly DependencyProperty TargetIdProperty =
		DependencyProperty.Register(
			nameof(TargetId),
			typeof(string),
			typeof(HtmlFormLabelPanel),
			new PropertyMetadata(string.Empty));

	internal HtmlFormLabelPanel()
	{
		PointerReleased += OnPointerReleased;
	}

	public string TargetId
	{
		get => (string)GetValue(TargetIdProperty);
		set => SetValue(TargetIdProperty, value);
	}

	private void OnPointerReleased(
		object sender,
		PointerRoutedEventArgs args)
	{
		var target = string.IsNullOrWhiteSpace(TargetId)
			? FindFirstLabelableDescendant(this)
			: FindByAutomationId(
				XamlRoot?.Content as DependencyObject,
				TargetId);
		if (target is null || ReferenceEquals(target, this))
			return;
		switch (target)
		{
			case ToggleButton toggle:
				toggle.IsChecked = toggle.IsChecked != true;
				break;
			case ButtonBase button:
				button.Focus(FocusState.Programmatic);
				break;
			case Control control:
				control.Focus(FocusState.Programmatic);
				break;
		}
		args.Handled = true;
	}

	private static Control? FindFirstLabelableDescendant(
		DependencyObject root)
	{
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var index = 0; index < count; index++)
		{
			var child = VisualTreeHelper.GetChild(root, index);
			if (child is Control control
				&& child is not HtmlFormLabelPanel)
			{
				return control;
			}
			var nested = FindFirstLabelableDescendant(child);
			if (nested is not null)
				return nested;
		}
		return null;
	}

	private static FrameworkElement? FindByAutomationId(
		DependencyObject? root,
		string targetId)
	{
		if (root is null)
			return null;
		if (root is FrameworkElement element
			&& AutomationProperties.GetAutomationId(element).Equals(
				targetId,
				StringComparison.Ordinal))
		{
			return element;
		}
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (var index = 0; index < count; index++)
		{
			var match = FindByAutomationId(
				VisualTreeHelper.GetChild(root, index),
				targetId);
			if (match is not null)
				return match;
		}
		return null;
	}
}
