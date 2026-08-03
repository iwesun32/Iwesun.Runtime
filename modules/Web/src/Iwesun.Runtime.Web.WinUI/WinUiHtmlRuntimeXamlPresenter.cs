using Iwesun.Runtime.Web;
using Iwesun.Runtime.WebView2;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Iwesun.Runtime.Web.WinUI;

internal sealed class WinUiHtmlRuntimeXamlPresenter(
	Window window,
	bool activateWindow = true,
	Windows.Foundation.Size? viewportSize = null) :
	IHtmlRuntimeXamlPresenter
{
	private readonly Window _window =
		window ?? throw new ArgumentNullException(nameof(window));
	private readonly bool _activateWindow = activateWindow;
	private readonly Windows.Foundation.Size? _viewportSize = viewportSize;

	public async ValueTask<HtmlRuntimeXamlDisplayResult> DisplayAsync(
		HtmlRuntimeXamlObjectTree tree,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(tree);
		cancellationToken.ThrowIfCancellationRequested();
		var dispatcher = DispatcherQueue.GetForCurrentThread();
		if (dispatcher is null || !dispatcher.HasThreadAccess)
		{
			throw new InvalidOperationException(
				"WinUI XAML trees must be displayed on the owning UI thread.");
		}
		if (tree.Documents.Count == 0
			|| tree.Documents[0].RootObjects.Count != 1
			|| tree.Documents[0].RootObjects[0] is not FrameworkElement primary)
		{
			throw new InvalidDataException(
				"The HTML runtime did not produce one primary FrameworkElement.");
		}
		if (_viewportSize is
			{
				Width: > 0,
				Height: > 0
			} viewport)
		{
			var viewportHost = new Canvas
			{
				Width = viewport.Width,
				Height = viewport.Height,
				Background = new SolidColorBrush(Microsoft.UI.Colors.White),
				HorizontalAlignment = HorizontalAlignment.Left,
				VerticalAlignment = VerticalAlignment.Top
			};
			// The HTML root owns the captured CSS runtime viewport. The WinUI
			// client can be half a DIP shorter because a physical pixel cannot
			// be represented evenly at 200% scaling; that host boundary must
			// clip the clone, not proportionally shrink its entire layout tree.
			primary.Width = viewport.Width;
			primary.Height = viewport.Height;
			primary.HorizontalAlignment = HorizontalAlignment.Left;
			primary.VerticalAlignment = VerticalAlignment.Top;
			Canvas.SetLeft(primary, 0);
			Canvas.SetTop(primary, 0);
			viewportHost.Children.Add(primary);
			// The viewport is expressed in WinUI DIPs. Windows performs the one
			// system rasterization step that maps it to physical screen pixels.
			// Do not add a Viewbox or an application DPI transform here: either
			// would scale text and geometry a second time.
			_window.Content = viewportHost;
		}
		else
		{
			_window.Content = primary;
		}
		if (_activateWindow)
			_window.Activate();
		await WaitForFirstLayoutAsync(primary, cancellationToken);
		await WaitForStableLayoutAsync(primary, cancellationToken);
		foreach (var frameDocument in tree.Documents.Skip(1))
		{
			if (frameDocument.RootObjects.Count != 1
				|| frameDocument.RootObjects[0] is not FrameworkElement frameRoot)
			{
				throw new InvalidDataException(
					"A nested document root is not attached to its iframe owner.");
			}
			if (frameRoot.Parent is null
				&& frameDocument.Roots[0].Plan.SourceElement?
					.EmbeddingOwner?.XamlElement
					is not FrameworkElement
					{
						Visibility: Visibility.Collapsed
					})
			{
				throw new InvalidDataException(
					"A visible nested document root is not attached "
					+ "to its iframe owner.");
			}
		}
		return new HtmlRuntimeXamlDisplayResult(
			_window,
			DateTimeOffset.UtcNow);
	}

	private static async Task WaitForFirstLayoutAsync(
		FrameworkElement primary,
		CancellationToken cancellationToken)
	{
		if (!primary.IsLoaded)
		{
			var completion = new TaskCompletionSource(
				TaskCreationOptions.RunContinuationsAsynchronously);
			void Loaded(object sender, RoutedEventArgs args) =>
				completion.TrySetResult();
			primary.Loaded += Loaded;
			try
			{
				if (primary.IsLoaded)
					completion.TrySetResult();
				await completion.Task.WaitAsync(cancellationToken);
			}
			finally
			{
				primary.Loaded -= Loaded;
			}
		}
		cancellationToken.ThrowIfCancellationRequested();
		primary.UpdateLayout();
		await Task.Yield();
		cancellationToken.ThrowIfCancellationRequested();
		primary.UpdateLayout();
	}

	private static async Task WaitForStableLayoutAsync(
		FrameworkElement primary,
		CancellationToken cancellationToken)
	{
		var dispatcher = DispatcherQueue.GetForCurrentThread()
			?? throw new InvalidOperationException(
				"The WinUI dispatcher is unavailable during layout stabilization.");
		LayoutFingerprint? previous = null;
		var consecutiveStablePasses = 0;
		var lastDifference = "No second layout sample was captured.";
		for (var pass = 0; pass < 64; pass++)
		{
			await YieldToDispatcherAsync(dispatcher, cancellationToken);
			primary.UpdateLayout();
			var current = CaptureLayoutFingerprint(primary);
			if (previous is not null
				&& LayoutFingerprintsEquivalent(previous, current))
			{
				consecutiveStablePasses++;
				if (consecutiveStablePasses >= 3)
					return;
			}
			else
			{
				consecutiveStablePasses = 0;
				if (previous is not null)
					lastDifference = DescribeLayoutDifference(previous, current);
			}
			previous = current;
		}
		throw new InvalidDataException(
			"The generated WinUI tree did not reach a stable measured layout "
				+ $"before XamlFill. Last difference: {lastDifference}");
	}

	private static async Task YieldToDispatcherAsync(
		DispatcherQueue dispatcher,
		CancellationToken cancellationToken)
	{
		var completion = new TaskCompletionSource(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using var registration = cancellationToken.Register(
			() => completion.TrySetCanceled(cancellationToken));
		if (!dispatcher.TryEnqueue(
			DispatcherQueuePriority.Low,
			() => completion.TrySetResult()))
		{
			throw new InvalidOperationException(
				"Unable to enqueue a WinUI layout-stabilization pass.");
		}
		await completion.Task;
	}

	private static LayoutFingerprint CaptureLayoutFingerprint(
		DependencyObject root)
	{
		var visualCount = 0;
		var loadedCount = 0;
		var nodes = new List<LayoutNodeFingerprint>();
		var pending = new Stack<DependencyObject>();
		pending.Push(root);
		while (pending.Count != 0)
		{
			var current = pending.Pop();
			visualCount++;
			if (current is FrameworkElement element)
			{
				if (element.IsLoaded)
					loadedCount++;
				nodes.Add(new(
					element.GetType().FullName ?? element.GetType().Name,
					element.Tag?.ToString() ?? string.Empty,
					element.ActualWidth,
					element.ActualHeight,
					element.ActualOffset.X,
					element.ActualOffset.Y,
					element.Visibility));
			}
			var childCount = VisualTreeHelper.GetChildrenCount(current);
			for (var index = 0; index < childCount; index++)
				pending.Push(VisualTreeHelper.GetChild(current, index));
		}
		return new(visualCount, loadedCount, nodes);
	}

	private static bool LayoutFingerprintsEquivalent(
		LayoutFingerprint first,
		LayoutFingerprint second)
	{
		if (first.VisualCount != second.VisualCount
			|| first.LoadedCount != second.LoadedCount
			|| first.Nodes.Count != second.Nodes.Count)
		{
			return false;
		}
		for (var index = 0; index < first.Nodes.Count; index++)
		{
			var a = first.Nodes[index];
			var b = second.Nodes[index];
			if (a.Visibility != b.Visibility
				|| !NearlyEqual(a.Width, b.Width)
				|| !NearlyEqual(a.Height, b.Height)
				|| !NearlyEqual(a.OffsetX, b.OffsetX)
				|| !NearlyEqual(a.OffsetY, b.OffsetY))
			{
				return false;
			}
		}
		return true;
	}

	private static string DescribeLayoutDifference(
		LayoutFingerprint first,
		LayoutFingerprint second)
	{
		if (first.VisualCount != second.VisualCount)
			return $"visualCount {first.VisualCount} -> {second.VisualCount}";
		if (first.LoadedCount != second.LoadedCount)
			return $"loadedCount {first.LoadedCount} -> {second.LoadedCount}";
		if (first.Nodes.Count != second.Nodes.Count)
			return $"frameworkElementCount {first.Nodes.Count} -> {second.Nodes.Count}";
		for (var index = 0; index < first.Nodes.Count; index++)
		{
			var a = first.Nodes[index];
			var b = second.Nodes[index];
			if (a.Visibility == b.Visibility
				&& NearlyEqual(a.Width, b.Width)
				&& NearlyEqual(a.Height, b.Height)
				&& NearlyEqual(a.OffsetX, b.OffsetX)
				&& NearlyEqual(a.OffsetY, b.OffsetY))
			{
				continue;
			}
			return $"node[{index}] {a.TypeName} Tag='{a.Tag}' "
				+ $"size ({a.Width:R},{a.Height:R}) -> ({b.Width:R},{b.Height:R}), "
				+ $"offset ({a.OffsetX:R},{a.OffsetY:R}) -> ({b.OffsetX:R},{b.OffsetY:R}), "
				+ $"visibility {a.Visibility} -> {b.Visibility}";
		}
		return "fingerprints differed without a classified node delta";
	}

	private static bool NearlyEqual(double first, double second) =>
		first.Equals(second)
		|| double.IsFinite(first)
			&& double.IsFinite(second)
			&& Math.Abs(first - second) <= .01;

	private sealed record LayoutFingerprint(
		int VisualCount,
		int LoadedCount,
		IReadOnlyList<LayoutNodeFingerprint> Nodes);

	private readonly record struct LayoutNodeFingerprint(
		string TypeName,
		string Tag,
		double Width,
		double Height,
		double OffsetX,
		double OffsetY,
		Visibility Visibility);
}
