using System.Collections.ObjectModel;

namespace Iwesun.Runtime.Web;

public readonly record struct ElementViewport
{
	public ElementViewport(double width, double height)
	{
		if (!double.IsFinite(width) || width < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (!double.IsFinite(height) || height < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}

		Width = width;
		Height = height;
	}

	public double Width { get; }

	public double Height { get; }
}

public readonly record struct ElementSize
{
	public ElementSize(double width, double height)
	{
		if (!double.IsFinite(width) || width < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(width));
		}

		if (!double.IsFinite(height) || height < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(height));
		}

		Width = width;
		Height = height;
	}

	public double Width { get; }

	public double Height { get; }
}

public sealed record ElementInheritedStyle(
	string Name,
	string Value,
	PropertyValueKind ValueKind,
	PropertyInheritanceKind Inheritance);

public sealed record ElementInheritanceLink
{
	public static ElementInheritanceLink None { get; } = new(string.Empty);

	public ElementInheritanceLink(string contextId)
	{
		ContextId = contextId ?? throw new ArgumentNullException(nameof(contextId));
	}

	public string ContextId { get; }

	public bool IsLinked => !string.IsNullOrWhiteSpace(ContextId);
}

public sealed class ElementInheritanceSnapshot
{
	public ElementInheritanceSnapshot(
		string id,
		string? parentId,
		ElementViewport viewport,
		ElementSize containerSize,
		IEnumerable<ElementInheritedStyle>? styles = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		Id = id;
		ParentId = string.IsNullOrWhiteSpace(parentId) ? null : parentId;
		Viewport = viewport;
		ContainerSize = containerSize;
		Styles = new ReadOnlyDictionary<string, ElementInheritedStyle>(
			(styles ?? [])
				.ToDictionary(static style => style.Name, StringComparer.Ordinal));
	}

	public string Id { get; }

	public string? ParentId { get; }

	public ElementViewport Viewport { get; }

	public ElementSize ContainerSize { get; }

	public IReadOnlyDictionary<string, ElementInheritedStyle> Styles { get; }
}

public sealed record ResolvedElementInheritance(
	ElementViewport Viewport,
	ElementSize ContainerSize,
	IReadOnlyDictionary<string, ElementInheritedStyle> Styles);

public sealed class ElementInheritanceService
{
	private readonly Dictionary<string, ElementInheritanceSnapshot> _snapshots =
		new(StringComparer.Ordinal);

	public ElementInheritanceLink Publish(ElementInheritanceSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		if (snapshot.ParentId is not null && !_snapshots.ContainsKey(snapshot.ParentId))
		{
			throw new InvalidOperationException(
				$"Inheritance parent '{snapshot.ParentId}' must be published first.");
		}

		_snapshots[snapshot.Id] = snapshot;
		return new ElementInheritanceLink(snapshot.Id);
	}

	public ResolvedElementInheritance Resolve(ElementInheritanceLink link)
	{
		ArgumentNullException.ThrowIfNull(link);
		if (!link.IsLinked || !_snapshots.TryGetValue(link.ContextId, out var snapshot))
		{
			throw new KeyNotFoundException(
				$"Inheritance context '{link.ContextId}' is not published.");
		}

		var chain = new Stack<ElementInheritanceSnapshot>();
		for (var current = snapshot; current is not null;)
		{
			chain.Push(current);
			current = current.ParentId is not null
				? _snapshots[current.ParentId]
				: null;
		}

		var styles = new Dictionary<string, ElementInheritedStyle>(StringComparer.Ordinal);
		while (chain.TryPop(out var current))
		{
			foreach (var pair in current.Styles)
			{
				if (pair.Value.Inheritance != PropertyInheritanceKind.NotInherited
					|| ReferenceEquals(current, snapshot))
				{
					styles[pair.Key] = pair.Value;
				}
			}
		}

		return new(
			snapshot.Viewport,
			snapshot.ContainerSize,
			new ReadOnlyDictionary<string, ElementInheritedStyle>(styles));
	}
}
