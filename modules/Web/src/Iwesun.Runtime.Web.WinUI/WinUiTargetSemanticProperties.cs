using System.Reflection;
using Iwesun.Runtime.Web;
using Microsoft.UI.Xaml;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed record WinUiTargetSemanticValue(
	ElementPropertySlot<string> Initialization,
	ElementPropertyLink Link,
	ElementPropertySlot<string> Runtime);

internal sealed class WinUiTargetSemanticState(
	IReadOnlyDictionary<string, WinUiTargetSemanticValue> values)
{
	private readonly IReadOnlyDictionary<string, WinUiTargetSemanticValue>
		_values = values;

	internal bool TryRead(
		string propertyName,
		XamlPropertyDataSlot slot,
		out string value)
	{
		value = string.Empty;
		if (!_values.TryGetValue(propertyName, out var semantic))
			return false;
		var propertySlot = slot switch
		{
			XamlPropertyDataSlot.Initialization => semantic.Initialization,
			XamlPropertyDataSlot.Runtime => semantic.Runtime,
			_ => ElementPropertySlot<string>.Unset
		};
		if (!propertySlot.IsSet)
			return false;
		value = propertySlot.Value ?? string.Empty;
		return true;
	}

	internal bool TryReadLink(
		string propertyName,
		out ElementPropertyLink link)
	{
		link = ElementPropertyLink.None;
		if (!_values.TryGetValue(propertyName, out var semantic)
			|| !semantic.Link.IsSet)
		{
			return false;
		}
		link = semantic.Link;
		return true;
	}
}

internal static class WinUiTargetSemanticProperties
{
	private static readonly DependencyProperty StateProperty =
		DependencyProperty.RegisterAttached(
			"State",
			typeof(WinUiTargetSemanticState),
			typeof(WinUiTargetSemanticProperties),
			new PropertyMetadata(null));

	internal static void Materialize(
		DependencyObject target,
		DomElement source)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(source);
		var values = new Dictionary<string, WinUiTargetSemanticValue>(
			StringComparer.Ordinal);
		foreach (var traits in ElementPropertyTraitsReflector
			.GetAttributes(source.GetType())
			.Where(static traits => traits.IsXamlFillRequired))
		{
			var property = source.GetType().GetProperty(
				traits.PropertyName,
				BindingFlags.Instance | BindingFlags.Public)
				?? throw new InvalidOperationException(
					$"{source.GetType().FullName}.{traits.PropertyName} "
						+ "is missing during target semantic materialization.");
			foreach (var owner in EnumerateOwners(property.GetValue(source)))
			{
				if (owner.XamlExecution.Kind
					!= XamlPropertyExecutionKind.SemanticMetadata)
				{
					continue;
				}
				values.Add(
					owner.XamlExecution.SourcePropertyName,
					new(
						owner.SourceInitialization,
						owner.SourceLink,
						owner.SourceRuntime));
			}
		}
		if (values.Count != 0)
			target.SetValue(StateProperty, new WinUiTargetSemanticState(values));
	}

	internal static bool TryRead(
		DependencyObject target,
		string propertyName,
		XamlPropertyDataSlot slot,
		out string value)
	{
		value = string.Empty;
		return target.GetValue(StateProperty)
				is WinUiTargetSemanticState state
			&& state.TryRead(propertyName, slot, out value);
	}

	internal static bool TryReadLink(
		DependencyObject target,
		string propertyName,
		out ElementPropertyLink link)
	{
		link = ElementPropertyLink.None;
		return target.GetValue(StateProperty)
				is WinUiTargetSemanticState state
			&& state.TryReadLink(propertyName, out link);
	}

	private static IEnumerable<IXamlPropertyExecutionOwner>
		EnumerateOwners(object? value)
	{
		if (value is IXamlPropertyExecutionOwner owner)
			yield return owner;
		if (value is not IEnumerable<IXamlPropertyExecutionOwner> owners)
			yield break;
		foreach (var item in owners)
			yield return item;
	}
}
