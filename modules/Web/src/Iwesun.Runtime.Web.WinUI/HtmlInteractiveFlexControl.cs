using Iwesun.Runtime.Web;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Native Control projection for an interactive HTML element which also owns
/// a multi-child flex/grid formatting context.
/// </summary>
internal sealed class HtmlInteractiveFlexPanel : ContentControl, IHtmlCursorTarget
{
	private readonly Grid _layoutRoot = new();
	private string _cursorRule = "auto";

	internal HtmlInteractiveFlexPanel()
	{
		DefaultStyleKey = typeof(ContentControl);
		HorizontalContentAlignment = HorizontalAlignment.Stretch;
		VerticalContentAlignment = VerticalAlignment.Stretch;
		Content = _layoutRoot;
	}

	internal Grid LayoutRoot => _layoutRoot;

	internal UIElementCollection Children => _layoutRoot.Children;

	internal void AddChild(
		UIElement child,
		XamlElementLayoutPlacement? placement) =>
		HtmlContainingBlockChildHost.Attach(
			_layoutRoot,
			this,
			child,
			placement);

	public static readonly DependencyProperty NavigateUriProperty =
		DependencyProperty.Register(
			nameof(NavigateUri),
			typeof(Uri),
			typeof(HtmlInteractiveFlexPanel),
			new PropertyMetadata(null));

	public static readonly DependencyProperty CommandKindProperty =
		DependencyProperty.Register(
			nameof(CommandKind),
			typeof(string),
			typeof(HtmlInteractiveFlexPanel),
			new PropertyMetadata("submit"));

	public static readonly DependencyProperty FormOwnerIdProperty =
		DependencyProperty.Register(
			nameof(FormOwnerId),
			typeof(string),
			typeof(HtmlInteractiveFlexPanel),
			new PropertyMetadata(null));

	public static readonly DependencyProperty FormActionProperty =
		DependencyProperty.Register(
			nameof(FormAction),
			typeof(string),
			typeof(HtmlInteractiveFlexPanel),
			new PropertyMetadata(null));

	public Uri? NavigateUri
	{
		get => (Uri?)GetValue(NavigateUriProperty);
		set => SetValue(NavigateUriProperty, value);
	}

	public string? CommandKind
	{
		get => (string?)GetValue(CommandKindProperty);
		set => SetValue(CommandKindProperty, value);
	}

	public string? FormOwnerId
	{
		get => (string?)GetValue(FormOwnerIdProperty);
		set => SetValue(FormOwnerIdProperty, value);
	}

	public string? FormAction
	{
		get => (string?)GetValue(FormActionProperty);
		set => SetValue(FormActionProperty, value);
	}

	public void ApplyCursor(string rule)
	{
		var cursor = HtmlCursorContract.Create(rule);
		_cursorRule = cursor.Rule;
		ProtectedCursor = cursor.Cursor;
	}

	public bool TryReadCursor(out string value)
	{
		value = _cursorRule;
		return HtmlCursorContract.Matches(_cursorRule, ProtectedCursor);
	}

}

internal static class HtmlContainingBlockChildHost
{
	private sealed class PaddingBoxHost : Grid
	{
		protected override Windows.Foundation.Size MeasureOverride(
			Windows.Foundation.Size availableSize)
		{
			foreach (var child in Children)
				child.Measure(availableSize);
			return new Windows.Foundation.Size(0, 0);
		}

		protected override Windows.Foundation.Size ArrangeOverride(
			Windows.Foundation.Size finalSize)
		{
			foreach (var child in Children)
			{
				if (child is FrameworkElement element
					&& HtmlOutOfFlowPlacementState.TryGet(
						element,
						out var placement))
				{
					child.Arrange(CreateOutOfFlowSlot(
						element,
						placement,
						finalSize));
					continue;
				}
				child.Arrange(new Windows.Foundation.Rect(
					0,
					0,
					Math.Max(0, finalSize.Width),
					Math.Max(0, finalSize.Height)));
			}
			return finalSize;
		}

		private static Windows.Foundation.Rect CreateOutOfFlowSlot(
			FrameworkElement element,
			HtmlOutOfFlowPlacementValue placement,
			Windows.Foundation.Size finalSize)
		{
			var margin = element.Margin;
			var desiredWidth = double.IsFinite(element.Width)
				? element.Width + margin.Left + margin.Right
				: element.DesiredSize.Width;
			var desiredHeight = double.IsFinite(element.Height)
				? element.Height + margin.Top + margin.Bottom
				: element.DesiredSize.Height;
			var stretchWidth = placement.HasLeft && placement.HasRight
				&& !placement.HasDefiniteWidth;
			var stretchHeight = placement.HasTop && placement.HasBottom
				&& !placement.HasDefiniteHeight;
			var width = stretchWidth
				? finalSize.Width - placement.Left - placement.Right
				: desiredWidth;
			var height = stretchHeight
				? finalSize.Height - placement.Top - placement.Bottom
				: desiredHeight;
			var x = placement.HasLeft
				? placement.Left
				: placement.HasRight
					? finalSize.Width - placement.Right - width
					: 0;
			var y = placement.HasTop
				? placement.Top
				: placement.HasBottom
					? finalSize.Height - placement.Bottom - height
					: 0;
			return new(
				x,
				y,
				Math.Max(0, width),
				Math.Max(0, height));
		}
	}

	internal static void Attach(
		Grid layoutRoot,
		FrameworkElement owner,
		UIElement child,
		XamlElementLayoutPlacement? placement)
	{
		ArgumentNullException.ThrowIfNull(layoutRoot);
		ArgumentNullException.ThrowIfNull(owner);
		ArgumentNullException.ThrowIfNull(child);
		if (placement?.ContainingBlock
			!= XamlElementContainingBlockKind.PaddingBox)
		{
			layoutRoot.Children.Add(child);
			return;
		}
		ApplyOutOfFlowAlignment(child, placement);

		var paddingBoxHost = new PaddingBoxHost
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch
		};
		Grid.SetRowSpan(
			paddingBoxHost,
			Math.Max(1, layoutRoot.RowDefinitions.Count));
		Grid.SetColumnSpan(
			paddingBoxHost,
			Math.Max(1, layoutRoot.ColumnDefinitions.Count));
		paddingBoxHost.Children.Add(child);
		layoutRoot.Children.Add(paddingBoxHost);
		ApplyPaddingBoxMargin();
		var paddingProperty = owner switch
		{
			Control => Control.PaddingProperty,
			Grid => Grid.PaddingProperty,
			_ => null
		};
		if (paddingProperty is not null)
			owner.RegisterPropertyChangedCallback(
				paddingProperty,
				(_, _) => ApplyPaddingBoxMargin());
#if DEBUG
		if (child is FrameworkElement)
		{
			owner.Loaded += (_, _) => RecordSnapshot();
			paddingBoxHost.SizeChanged += (_, _) => RecordSnapshot();
		}
#endif

		void ApplyPaddingBoxMargin()
		{
			var padding = owner switch
			{
				Control control => control.Padding,
				Grid grid => grid.Padding,
				_ => default
			};
			var margin = new Thickness(
				-padding.Left,
				-padding.Top,
				-padding.Right,
				-padding.Bottom);
			if (!ThicknessEquals(paddingBoxHost.Margin, margin))
				paddingBoxHost.Margin = margin;
		}

#if DEBUG
		void RecordSnapshot()
		{
			if (child is not FrameworkElement framework)
				return;
			try
			{
				var hostOffset = paddingBoxHost
					.TransformToVisual(owner)
					.TransformPoint(new Windows.Foundation.Point());
				var childOffset = framework
					.TransformToVisual(owner)
					.TransformPoint(new Windows.Foundation.Point());
				WinUiLayoutDiagnostics.Record(new(
					owner.GetType().FullName ?? owner.GetType().Name,
					framework.GetType().FullName ?? framework.GetType().Name,
					owner.ActualWidth,
					owner.ActualHeight,
					layoutRoot.ActualWidth,
					layoutRoot.ActualHeight,
					paddingBoxHost.ActualWidth,
					paddingBoxHost.ActualHeight,
					hostOffset.X,
					hostOffset.Y,
					childOffset.X,
					childOffset.Y,
					Grid.GetRowSpan(paddingBoxHost),
					Grid.GetColumnSpan(paddingBoxHost),
					layoutRoot.RowDefinitions.Count,
					layoutRoot.ColumnDefinitions.Count,
					Grid.GetRow(framework),
					Grid.GetColumn(framework),
					Grid.GetRowSpan(framework),
					Grid.GetColumnSpan(framework),
					placement.IsOutOfFlow,
					placement.Left is not null,
					placement.Top is not null,
					placement.Right is not null,
					placement.Bottom is not null,
					framework.Tag?.ToString() ?? string.Empty,
					paddingBoxHost.Margin.ToString(),
					framework.Margin.ToString(),
					framework.HorizontalAlignment.ToString(),
					framework.VerticalAlignment.ToString(),
					framework.Translation.ToString()));
			}
			catch (InvalidOperationException)
			{
				// The visual is not connected yet; a later Loaded/SizeChanged event retries.
			}
		}
#endif

		static bool ThicknessEquals(Thickness left, Thickness right) =>
			Math.Abs(left.Left - right.Left) <= 0.001
			&& Math.Abs(left.Top - right.Top) <= 0.001
			&& Math.Abs(left.Right - right.Right) <= 0.001
			&& Math.Abs(left.Bottom - right.Bottom) <= 0.001;
	}

	internal static bool ContainsHostedChild(
		Panel candidate,
		DependencyObject child) =>
		candidate is PaddingBoxHost paddingBox
		&& paddingBox.Children.Any(hosted => ReferenceEquals(hosted, child));

	private static void ApplyOutOfFlowAlignment(
		UIElement child,
		XamlElementLayoutPlacement placement)
	{
		if (!placement.IsOutOfFlow || child is not FrameworkElement element)
			return;
		var hasLeft = placement.Left is not null;
		var hasRight = placement.Right is not null;
		var hasTop = placement.Top is not null;
		var hasBottom = placement.Bottom is not null;
		element.HorizontalAlignment = hasLeft && hasRight
			&& !placement.HasDefiniteWidth
				? HorizontalAlignment.Stretch
				: hasRight && !hasLeft
					? HorizontalAlignment.Right
					: HorizontalAlignment.Left;
		element.VerticalAlignment = hasTop && hasBottom
			&& !placement.HasDefiniteHeight
				? VerticalAlignment.Stretch
				: hasBottom && !hasTop
					? VerticalAlignment.Bottom
					: VerticalAlignment.Top;
	}

	internal static void SynchronizeTrackSpans(Grid layoutRoot)
	{
		ArgumentNullException.ThrowIfNull(layoutRoot);
		var rowSpan = Math.Max(1, layoutRoot.RowDefinitions.Count);
		var columnSpan = Math.Max(1, layoutRoot.ColumnDefinitions.Count);
		foreach (var host in layoutRoot.Children.OfType<PaddingBoxHost>())
		{
			Grid.SetRowSpan(host, rowSpan);
			Grid.SetColumnSpan(host, columnSpan);
		}
	}
}
