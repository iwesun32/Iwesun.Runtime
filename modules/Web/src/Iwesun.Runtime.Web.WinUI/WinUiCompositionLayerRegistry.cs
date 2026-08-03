using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Owns the single XAML child-visual slot and partitions it into named,
/// independently auditable target-side Composition layers.
/// </summary>
internal static class WinUiCompositionLayerRegistry
{
	private sealed class State
	{
		internal required ContainerVisual Root { get; init; }

		internal Dictionary<string, ContainerVisual> Layers { get; } =
			new(StringComparer.Ordinal);
	}

	private static readonly ConditionalWeakTable<FrameworkElement, State> States =
		new();

	internal static ContainerVisual Acquire(
		FrameworkElement owner,
		string key)
	{
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		var state = States.GetValue(owner, CreateState);
		if (!ReferenceEquals(
			ElementCompositionPreview.GetElementChildVisual(owner),
			state.Root))
		{
			throw new InvalidOperationException(
				$"{owner.GetType().Name} Composition root was replaced externally.");
		}
		if (state.Layers.ContainsKey(key))
			throw new InvalidOperationException($"Composition layer '{key}' already exists.");
		var layer = state.Root.Compositor.CreateContainerVisual();
		state.Root.Children.InsertAtTop(layer);
		state.Layers.Add(key, layer);
		return layer;
	}

	internal static void Release(FrameworkElement owner, string key)
	{
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentException.ThrowIfNullOrWhiteSpace(key);
		if (!States.TryGetValue(owner, out var state)
			|| !state.Layers.Remove(key, out var layer))
		{
			return;
		}
		state.Root.Children.Remove(layer);
		if (state.Layers.Count != 0)
			return;
		if (ReferenceEquals(
			ElementCompositionPreview.GetElementChildVisual(owner),
			state.Root))
		{
			ElementCompositionPreview.SetElementChildVisual(owner, null);
		}
		States.Remove(owner);
	}

	internal static bool Owns(
		FrameworkElement owner,
		string key,
		ContainerVisual layer) =>
		States.TryGetValue(owner, out var state)
		&& ReferenceEquals(
			ElementCompositionPreview.GetElementChildVisual(owner),
			state.Root)
		&& state.Layers.TryGetValue(key, out var registered)
		&& ReferenceEquals(registered, layer)
		&& ReferenceEquals(layer.Parent, state.Root);

	internal static bool OwnsRoot(FrameworkElement owner) =>
		States.TryGetValue(owner, out var state)
		&& ReferenceEquals(
			ElementCompositionPreview.GetElementChildVisual(owner),
			state.Root);

	private static State CreateState(FrameworkElement owner)
	{
		if (ElementCompositionPreview.GetElementChildVisual(owner) is not null)
			throw new InvalidOperationException(
				$"{owner.GetType().Name} already owns an external Composition child visual.");
		var compositor = ElementCompositionPreview.GetElementVisual(owner).Compositor;
		var root = compositor.CreateContainerVisual();
		ElementCompositionPreview.SetElementChildVisual(owner, root);
		return new State { Root = root };
	}
}
