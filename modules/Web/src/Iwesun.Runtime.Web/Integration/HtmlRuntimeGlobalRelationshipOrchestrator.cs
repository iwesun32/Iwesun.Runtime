using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Iwesun.Runtime.Web;

public enum HtmlRuntimeDataContentKind
{
	Text,
	Graphic,
	Media,
	Navigation,
	FormState,
	Collection,
	Other
}

public sealed record HtmlRuntimeGlobalDataBinding(
	DomElement Element,
	DomElementDataSource Source,
	DomDataSourceDomain Domain,
	HtmlRuntimeDataContentKind ContentKind,
	HtmlRuntimeDynamicDataValue DynamicValue)
{
	public string Value => DynamicValue.Value;
}

public sealed class HtmlRuntimeDynamicDataValue : INotifyPropertyChanged
{
	private readonly object _gate = new();
	private string _value = string.Empty;
	private long _revision;

	internal HtmlRuntimeDynamicDataValue(
		string identity,
		DomDataSourceDomain domain,
		HtmlRuntimeDataContentKind contentKind)
	{
		Identity = identity;
		Domain = domain;
		ContentKind = contentKind;
	}

	public string Identity { get; }

	public DomDataSourceDomain Domain { get; }

	public HtmlRuntimeDataContentKind ContentKind { get; }

	public string Value
	{
		get
		{
			lock (_gate)
				return _value;
		}
	}

	public long Revision
	{
		get
		{
			lock (_gate)
				return _revision;
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	internal void Capture(string value)
	{
		bool changed;
		lock (_gate)
		{
			changed = !_value.Equals(value, StringComparison.Ordinal);
			_value = value;
			_revision++;
		}
		if (changed)
			OnPropertyChanged(nameof(Value));
		OnPropertyChanged(nameof(Revision));
	}

	private void OnPropertyChanged([CallerMemberName] string? name = null) =>
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed record HtmlRuntimeGlobalStyleBinding(
	DomElement Element,
	HtmlRuntimeStyleBinding Source);

public sealed record HtmlRuntimeGlobalLayoutBinding(
	DomElement Element,
	HtmlRuntimeLayoutBinding Source);

public sealed record HtmlRuntimeGlobalEventBinding(
	DomElement Element,
	DomElementEvent Source);

public sealed record HtmlRuntimeGlobalRelationshipGraph(
	IReadOnlyList<HtmlRuntimeGlobalDataBinding> DataBindings,
	IReadOnlyList<HtmlRuntimeGlobalStyleBinding> StyleBindings,
	IReadOnlyList<HtmlRuntimeGlobalLayoutBinding> LayoutBindings,
	IReadOnlyList<HtmlRuntimeGlobalEventBinding> EventBindings,
	HtmlRuntimeViewportState Viewport)
{
	public IReadOnlyList<HtmlRuntimeGlobalDataBinding> UiResources =>
		DataBindings.Where(static binding =>
			binding.Domain == DomDataSourceDomain.UiResource).ToArray();

	public IReadOnlyList<HtmlRuntimeGlobalDataBinding> BusinessInputs =>
		DataBindings.Where(static binding =>
			binding.Domain == DomDataSourceDomain.BusinessInput).ToArray();
}

public sealed record HtmlRuntimeXamlGlobalDataBinding(
	HtmlRuntimeGlobalDataBinding Source,
	object? Target,
	string TargetProperty,
	bool IsMaterialized);

public sealed record HtmlRuntimeXamlGlobalEventBinding(
	HtmlRuntimeGlobalEventBinding Source,
	object? Target,
	bool IsMaterialized);

public sealed record HtmlRuntimeXamlGlobalStyleBinding(
	HtmlRuntimeGlobalStyleBinding Source,
	object? Target,
	XamlPropertyExecutionDescriptor Execution,
	HtmlRuntimeStyleResolution? Resolution,
	bool IsMaterialized);

public sealed record HtmlRuntimeXamlGlobalRelationshipGraph(
	IReadOnlyList<HtmlRuntimeXamlGlobalDataBinding> DataBindings,
	IReadOnlyList<HtmlRuntimeXamlGlobalEventBinding> EventBindings,
	IReadOnlyList<HtmlRuntimeXamlGlobalStyleBinding> GlobalStyleBindings,
	IReadOnlyCollection<HtmlRuntimeXamlStyleTargetBinding> StyleBindings,
	IReadOnlyCollection<HtmlRuntimeXamlLayoutTargetBinding> LayoutBindings,
	IReadOnlyCollection<HtmlRuntimeXamlAnimationTargetBinding> AnimationBindings);

/// <summary>
/// Performs root-owned reverse relationship processing after element Fill and
/// again after strong XAML object creation. The root-level global evidence
/// processor owns CSS/layout/effect acquisition; element Fill owns only the
/// statically declared slots of the concrete element type. This class resolves
/// the resulting CSS/data/event/layout services back to their owning elements
/// and materialized XAML targets.
/// </summary>
public sealed class HtmlRuntimeGlobalRelationshipOrchestrator
{
	private readonly Dictionary<string, HtmlRuntimeDynamicDataValue>
		_dynamicValues = new(StringComparer.Ordinal);
	private readonly object _dynamicGate = new();

	public long UpdateBusinessInput(
		string documentScope,
		string xpath,
		string sourceName,
		string value)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(documentScope);
		ArgumentException.ThrowIfNullOrWhiteSpace(xpath);
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
		ArgumentNullException.ThrowIfNull(value);
		var identity = $"{ElementIdentity(documentScope, xpath)}::{sourceName}";
		HtmlRuntimeDynamicDataValue source;
		lock (_dynamicGate)
		{
			if (!_dynamicValues.TryGetValue(identity, out source!))
			{
				throw new KeyNotFoundException(
					$"Dynamic business-data source '{identity}' has not been captured.");
			}
			if (source.Domain != DomDataSourceDomain.BusinessInput)
			{
				throw new InvalidOperationException(
					$"Dynamic source '{identity}' is a UI resource and cannot be written as business input.");
			}
		}
		source.Capture(value);
		return source.Revision;
	}

	public HtmlRuntimeGlobalRelationshipGraph ResolveDom(
		IReadOnlyList<DomElement> documentRoots,
		HtmlRuntimeDesignRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(documentRoots);
		ArgumentNullException.ThrowIfNull(runtime);
		var elements = BuildElementIndex(documentRoots);
		var data = elements.Values
			.SelectMany(element => element.DataSources
				.Where(static source =>
					source.SourceInitialization.IsSet
					|| source.SourceLink.IsSet
					|| source.SourceRuntime.IsSet)
				.Select(source => CreateDataBinding(element, source)))
			.ToArray();
		var styles = runtime.Styles.Bindings
			.Select(binding => new HtmlRuntimeGlobalStyleBinding(
				ResolveElement(elements, binding.DocumentScope, binding.XPath),
				binding))
			.ToArray();
		var layouts = runtime.Layout.Bindings
			.Select(binding => new HtmlRuntimeGlobalLayoutBinding(
				ResolveElement(
					elements,
					binding.Identity.DocumentScope,
					binding.Identity.XPath),
				binding))
			.ToArray();
		var events = elements.Values
			.SelectMany(static element => element.Events.Select(source =>
				new HtmlRuntimeGlobalEventBinding(element, source)))
			.ToArray();
		return new(data, styles, layouts, events, runtime.Layout.Viewport);
	}

	public HtmlRuntimeXamlGlobalRelationshipGraph ComposeXaml(
		HtmlRuntimeGlobalRelationshipGraph source,
		HtmlRuntimeDesignRuntime runtime)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(runtime);
		var data = source.DataBindings.Select(static binding => new
			HtmlRuntimeXamlGlobalDataBinding(
				binding,
				binding.Element.XamlElement,
				binding.Source.XamlExecution.TargetProperty,
				binding.Element.XamlElement is not null)).ToArray();
		var events = source.EventBindings.Select(static binding => new
			HtmlRuntimeXamlGlobalEventBinding(
				binding,
				binding.Element.XamlElement,
				binding.Element.XamlElement is not null)).ToArray();
		var styles = source.StyleBindings.Select(binding =>
		{
			var execution = DomXamlPropertyExecutionCatalog.Resolve(
				binding.Source.PropertyName);
			return new HtmlRuntimeXamlGlobalStyleBinding(
				binding,
				binding.Element.XamlElement,
				execution,
				runtime.Styles.ResolveForXaml(
					binding.Source.DocumentScope,
					binding.Source.XPath,
					binding.Source.PropertyName),
				binding.Element.XamlElement is not null
					&& execution.IsSupported);
		}).ToArray();
		return new(
			data,
			events,
			styles,
			runtime.Styles.XamlTargets,
			runtime.Layout.XamlTargets,
			runtime.Animations.XamlTargets);
	}

	private HtmlRuntimeGlobalDataBinding CreateDataBinding(
		DomElement element,
		DomElementDataSource source)
	{
		var value = source.SourceRuntime.IsSet
			? source.SourceRuntime.Value ?? string.Empty
			: source.SourceInitialization.IsSet
				? source.SourceInitialization.Value ?? string.Empty
				: string.Empty;
		var contentKind = ClassifyContent(element, source);
		var identity = $"{ElementIdentity(element)}::{source.Name}";
		HtmlRuntimeDynamicDataValue dynamicValue;
		lock (_dynamicGate)
		{
			if (!_dynamicValues.TryGetValue(identity, out dynamicValue!))
			{
				dynamicValue = new(identity, source.Domain, contentKind);
				_dynamicValues.Add(identity, dynamicValue);
			}
			else if (dynamicValue.Domain != source.Domain
				|| dynamicValue.ContentKind != contentKind)
			{
				throw new InvalidDataException(
					$"Dynamic data identity '{identity}' changed its strong type.");
			}
		}
		dynamicValue.Capture(value);
		return new(
			element,
			source,
			source.Domain,
			contentKind,
			dynamicValue);
	}

	private static HtmlRuntimeDataContentKind ClassifyContent(
		DomElement element,
		DomElementDataSource source) => source.XamlTargetKind switch
	{
		XamlControlDataTargetKind.Text
			or XamlControlDataTargetKind.Content
			or XamlControlDataTargetKind.PlaceholderText
			or XamlControlDataTargetKind.Header => HtmlRuntimeDataContentKind.Text,
		XamlControlDataTargetKind.NavigateUri =>
			HtmlRuntimeDataContentKind.Navigation,
		XamlControlDataTargetKind.ItemsSource =>
			HtmlRuntimeDataContentKind.Collection,
		XamlControlDataTargetKind.IsChecked
			or XamlControlDataTargetKind.SelectedItem
			or XamlControlDataTargetKind.SelectedValue
			or XamlControlDataTargetKind.Value =>
			HtmlRuntimeDataContentKind.FormState,
		XamlControlDataTargetKind.Source when element.TagName is
			"audio" or "video" or "track" => HtmlRuntimeDataContentKind.Media,
		XamlControlDataTargetKind.Source => HtmlRuntimeDataContentKind.Graphic,
		_ => HtmlRuntimeDataContentKind.Other
	};

	private static IReadOnlyDictionary<string, DomElement> BuildElementIndex(
		IReadOnlyList<DomElement> roots) => roots
		.SelectMany(EnumeratePreOrder)
		.ToDictionary(ElementIdentity, StringComparer.Ordinal);

	private static DomElement ResolveElement(
		IReadOnlyDictionary<string, DomElement> elements,
		string documentScope,
		string xpath)
	{
		var identity = documentScope.Equals("document", StringComparison.Ordinal)
			? xpath
			: $"{documentScope}::{xpath}";
		return elements.TryGetValue(identity, out var element)
			? element
			: throw new InvalidDataException(
				$"Global relationship source '{identity}' is outside the DOM tree.");
	}

	private static string ElementIdentity(DomElement element) =>
		ElementIdentity(element.DocumentScope, element.XPath);

	private static string ElementIdentity(string documentScope, string xpath) =>
		documentScope.Equals("document", StringComparison.Ordinal)
			? xpath
			: $"{documentScope}::{xpath}";

	private static IEnumerable<DomElement> EnumeratePreOrder(DomElement root)
	{
		yield return root;
		foreach (var child in root.Children)
		{
			foreach (var descendant in EnumeratePreOrder(child))
				yield return descendant;
		}
	}
}
