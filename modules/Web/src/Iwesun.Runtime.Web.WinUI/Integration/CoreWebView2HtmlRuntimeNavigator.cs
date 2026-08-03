using Iwesun.Runtime.Web;
using Iwesun.Runtime.WebView2;
using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.Web.WinUI;

public sealed class CoreWebView2HtmlRuntimeNavigator(
	CoreWebView2 browser) : IHtmlRuntimeNavigator
{
	private static readonly TimeSpan NavigationEvidenceTimeout =
		TimeSpan.FromSeconds(5);
	private static readonly TimeSpan StableDocumentTimeout =
		TimeSpan.FromSeconds(5);
	private static readonly TimeSpan StableDocumentQuietWindow =
		TimeSpan.FromMilliseconds(300);
	private WebRuntimeCdpDomAccess? _domAccess;
	private readonly List<string> _navigationTrace = [];

	public string NavigationTrace =>
		string.Join(Environment.NewLine, _navigationTrace);

	public void Attach(WebRuntimeCdpDomAccess domAccess)
	{
		ArgumentNullException.ThrowIfNull(domAccess);
		if (Interlocked.CompareExchange(
			ref _domAccess,
			domAccess,
			null) is not null)
		{
			throw new InvalidOperationException(
				"The Runtime CDP DOM access is already attached.");
		}
	}

	public async ValueTask NavigateAsync(
		Uri url,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(url);
		var completion = new TaskCompletionSource(
			TaskCreationOptions.RunContinuationsAsynchronously);
		void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args)
		{
			if (args.IsSuccess)
				completion.TrySetResult();
			else
				completion.TrySetException(new InvalidOperationException(
					$"WebView2 navigation failed: {args.WebErrorStatus}."));
		}
		browser.NavigationCompleted += Completed;
		try
		{
			browser.Navigate(url.AbsoluteUri);
			await completion.Task.WaitAsync(cancellationToken);
		}
		finally
		{
			browser.NavigationCompleted -= Completed;
		}
	}

	public async ValueTask WaitForStableDocumentAsync(
		CancellationToken cancellationToken = default)
	{
		long activityVersion = 0;
		void MarkActivity() => Interlocked.Increment(ref activityVersion);
		void SourceChanged(
			object? sender,
			CoreWebView2SourceChangedEventArgs args) => MarkActivity();
		void NavigationStarting(
			object? sender,
			CoreWebView2NavigationStartingEventArgs args) => MarkActivity();
		void NavigationCompleted(
			object? sender,
			CoreWebView2NavigationCompletedEventArgs args) => MarkActivity();
		void DomContentLoaded(
			object? sender,
			CoreWebView2DOMContentLoadedEventArgs args) => MarkActivity();

		browser.SourceChanged += SourceChanged;
		browser.NavigationStarting += NavigationStarting;
		browser.NavigationCompleted += NavigationCompleted;
		browser.DOMContentLoaded += DomContentLoaded;
		try
		{
			var started = TimeProvider.System.GetTimestamp();
			while (TimeProvider.System.GetElapsedTime(started)
				< StableDocumentTimeout)
			{
				var observedVersion = Volatile.Read(ref activityVersion);
				await Task.Delay(
					StableDocumentQuietWindow,
					cancellationToken);
				if (observedVersion == Volatile.Read(ref activityVersion))
					return;
			}
			throw new NavigationEvidenceException(
				$"WebView2 did not reach a stable document state within "
				+ $"{StableDocumentTimeout.TotalSeconds:0.#} seconds. "
				+ $"Current URL: {browser.Source}.");
		}
		finally
		{
			browser.SourceChanged -= SourceChanged;
			browser.NavigationStarting -= NavigationStarting;
			browser.NavigationCompleted -= NavigationCompleted;
			browser.DOMContentLoaded -= DomContentLoaded;
		}
	}

	public async ValueTask ClickAsync(
		HtmlRuntimeNavigationOperation operation,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(operation);
		var access = _domAccess
			?? throw new InvalidOperationException(
				"Runtime CDP DOM access must be attached before navigation.");
		Exception? lastError = null;
		for (var attempt = 0; attempt < 120; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				var before = browser.Source;
				var nodeAttributes = await access.GetAttributesAsync(
					operation.XPath,
					operation.DocumentScope,
					cancellationToken);
				var parentSeparator = operation.XPath.LastIndexOf('/');
				var parentXPath = parentSeparator > 0
					? operation.XPath[..parentSeparator]
					: operation.XPath;
				var parentAttributes = parentXPath.Equals(
					operation.XPath,
					StringComparison.Ordinal)
						? new Dictionary<string, string>()
						: await access.GetAttributesAsync(
							parentXPath,
							operation.DocumentScope,
							cancellationToken);
				var expectedSource = ResolveExpectedSource(
					before,
					nodeAttributes,
					parentAttributes);
				var result = await access.ClickAsync(
					operation.XPath,
					operation.DocumentScope,
					cancellationToken);
				if (expectedSource is not null)
				{
					await WaitForExpectedSourceAsync(
						expectedSource,
						cancellationToken);
					await WaitForStableDocumentAsync(cancellationToken);
				}
				else
				{
					await WaitForStableDocumentAsync(cancellationToken);
				}
				_navigationTrace.Add(
					$"{operation.Name}: revision={result.Revision}, "
					+ $"nodeId={result.NodeId}, xpath={result.XPath}, "
					+ $"node=[{FormatAttributes(nodeAttributes)}], "
					+ $"parent={parentXPath}, "
					+ $"parentAttributes=[{FormatAttributes(parentAttributes)}], "
					+ $"input={result.InputX},{result.InputY}, "
					+ $"hitNodeId={result.HitNodeId}, "
					+ $"hitBackendNodeId={result.HitBackendNodeId}, "
					+ $"before={before}, after={browser.Source}");
				return;
			}
			catch (Exception exception)
				when (exception is (
						ArgumentException
						or
						KeyNotFoundException
						or InvalidOperationException)
					&& exception is not NavigationEvidenceException)
			{
				lastError = exception;
			}
			if (!operation.Required)
				return;
			await Task.Delay(250, cancellationToken);
		}
		throw new InvalidOperationException(
			$"Required navigation XPath is unavailable: {operation.XPath}. "
				+ (lastError is null
					? "Runtime CDP returned no node."
					: lastError.Message)
				+ $" Current URL: {browser.Source}. Completed clicks: "
				+ (NavigationTrace.Length == 0
					? "<none>"
					: NavigationTrace),
			lastError);
	}

	private async ValueTask WaitForExpectedSourceAsync(
		Uri expectedSource,
		CancellationToken cancellationToken)
	{
		if (SourceMatches(expectedSource, browser.Source))
			return;

		var completion = new TaskCompletionSource(
			TaskCreationOptions.RunContinuationsAsynchronously);
		void SourceChanged(
			object? sender,
			CoreWebView2SourceChangedEventArgs args)
		{
			if (SourceMatches(expectedSource, browser.Source))
				completion.TrySetResult();
		}

		browser.SourceChanged += SourceChanged;
		try
		{
			if (SourceMatches(expectedSource, browser.Source))
				return;

			try
			{
				await completion.Task.WaitAsync(
					NavigationEvidenceTimeout,
					cancellationToken);
			}
			catch (TimeoutException exception)
			{
				throw new NavigationEvidenceException(
					$"The XPath click did not reach the expected URL "
					+ $"within {NavigationEvidenceTimeout.TotalSeconds:0.#} "
					+ $"seconds. Expected: {expectedSource.AbsoluteUri}; "
					+ $"actual: {browser.Source}.",
					exception);
			}
		}
		finally
		{
			browser.SourceChanged -= SourceChanged;
		}
	}

	private static Uri? ResolveExpectedSource(
		string currentSource,
		IReadOnlyDictionary<string, string> nodeAttributes,
		IReadOnlyDictionary<string, string> parentAttributes)
	{
		if (!Uri.TryCreate(currentSource, UriKind.Absolute, out var baseUri))
			return null;

		return ResolveHttpSource(baseUri, nodeAttributes)
			?? ResolveHttpSource(baseUri, parentAttributes);
	}

	private static Uri? ResolveHttpSource(
		Uri baseUri,
		IReadOnlyDictionary<string, string> attributes)
	{
		if (!attributes.TryGetValue("href", out var href)
			|| !Uri.TryCreate(baseUri, href, out var source)
			|| (source.Scheme != Uri.UriSchemeHttp
				&& source.Scheme != Uri.UriSchemeHttps))
		{
			return null;
		}
		return source;
	}

	private static bool SourceMatches(Uri expectedSource, string actualSource) =>
		Uri.TryCreate(actualSource, UriKind.Absolute, out var actual)
		&& expectedSource.Equals(actual);

	private static string FormatAttributes(
		IReadOnlyDictionary<string, string> attributes) =>
		string.Join(
			",",
			attributes
				.Where(static pair =>
					pair.Key.Equals("href", StringComparison.OrdinalIgnoreCase)
					|| pair.Key.Equals("target", StringComparison.OrdinalIgnoreCase)
					|| pair.Key.Equals("role", StringComparison.OrdinalIgnoreCase)
					|| pair.Key.Equals("class", StringComparison.OrdinalIgnoreCase)
					|| pair.Key.Equals(
						"aria-label",
						StringComparison.OrdinalIgnoreCase))
				.Select(static pair => $"{pair.Key}={pair.Value}"));

	private sealed class NavigationEvidenceException : InvalidOperationException
	{
		internal NavigationEvidenceException(string message)
			: base(message)
		{
		}

		internal NavigationEvidenceException(
			string message,
			Exception innerException)
			: base(message, innerException)
		{
		}
	}
}
