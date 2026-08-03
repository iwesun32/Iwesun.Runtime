using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

internal static class HtmlFormState
{
	private static readonly object OwnerGate = new();
	private static readonly List<WeakReference<FrameworkElement>> Owners = [];

	public static readonly DependencyProperty IsFormOwnerProperty =
		DependencyProperty.RegisterAttached(
			"IsFormOwner",
			typeof(bool),
			typeof(HtmlFormState),
			new PropertyMetadata(false));

	public static readonly DependencyProperty FormOwnerIdProperty =
		DependencyProperty.RegisterAttached(
			"FormOwnerId",
			typeof(string),
			typeof(HtmlFormState),
			new PropertyMetadata(string.Empty));

	public static readonly DependencyProperty InitialValueProperty =
		DependencyProperty.RegisterAttached(
			"InitialValue",
			typeof(string),
			typeof(HtmlFormState),
			new PropertyMetadata(string.Empty));

	public static readonly DependencyProperty InitialCheckedProperty =
		DependencyProperty.RegisterAttached(
			"InitialChecked",
			typeof(bool),
			typeof(HtmlFormState),
			new PropertyMetadata(false));

	public static readonly DependencyProperty InitialSelectedProperty =
		DependencyProperty.RegisterAttached(
			"InitialSelected",
			typeof(bool),
			typeof(HtmlFormState),
			new PropertyMetadata(false));

	public static bool GetIsFormOwner(DependencyObject element) =>
		(bool)element.GetValue(IsFormOwnerProperty);

	public static void SetIsFormOwner(
		DependencyObject element,
		bool value)
	{
		element.SetValue(IsFormOwnerProperty, value);
		if (value && element is FrameworkElement owner)
			RegisterOwner(owner);
	}

	public static string GetFormOwnerId(DependencyObject element) =>
		(string)element.GetValue(FormOwnerIdProperty);

	public static void SetFormOwnerId(
		DependencyObject element,
		string value) =>
		element.SetValue(FormOwnerIdProperty, value);

	public static string GetInitialValue(DependencyObject element) =>
		(string)element.GetValue(InitialValueProperty);

	public static void SetInitialValue(
		DependencyObject element,
		string value) =>
		element.SetValue(InitialValueProperty, value);

	public static bool GetInitialChecked(DependencyObject element) =>
		(bool)element.GetValue(InitialCheckedProperty);

	public static void SetInitialChecked(
		DependencyObject element,
		bool value) =>
		element.SetValue(InitialCheckedProperty, value);

	public static bool GetInitialSelected(DependencyObject element) =>
		(bool)element.GetValue(InitialSelectedProperty);

	public static void SetInitialSelected(
		DependencyObject element,
		bool value) =>
		element.SetValue(InitialSelectedProperty, value);

	public static FrameworkElement? FindContainingOwner(
		FrameworkElement descendant)
	{
		lock (OwnerGate)
		{
			for (var index = Owners.Count - 1; index >= 0; index--)
			{
				if (!Owners[index].TryGetTarget(out var owner))
				{
					Owners.RemoveAt(index);
					continue;
				}
				if (EnumerateLogical(owner).Any(item =>
					ReferenceEquals(item, descendant)))
				{
					return owner;
				}
			}
		}
		return null;
	}

	public static IEnumerable<FrameworkElement> EnumerateLogical(
		DependencyObject root)
	{
		if (root is FrameworkElement element)
			yield return element;
		foreach (var child in LogicalChildren(root))
		{
			foreach (var descendant in EnumerateLogical(child))
				yield return descendant;
		}
	}

	private static void RegisterOwner(FrameworkElement owner)
	{
		lock (OwnerGate)
		{
			if (Owners.Any(reference =>
				reference.TryGetTarget(out var existing)
				&& ReferenceEquals(existing, owner)))
			{
				return;
			}
			Owners.Add(new(owner));
		}
	}

	private static IEnumerable<DependencyObject> LogicalChildren(
		DependencyObject owner)
	{
		if (owner is Panel panel)
		{
			foreach (var child in panel.Children)
				yield return child;
			yield break;
		}
		if (owner is Border { Child: DependencyObject borderChild })
		{
			yield return borderChild;
			yield break;
		}
		if (owner is ContentControl { Content: DependencyObject content })
			yield return content;
		if (owner is ItemsControl items)
		{
			foreach (var item in items.Items.OfType<DependencyObject>())
				yield return item;
		}
	}
}
