using Iwesun.Runtime.WebView2;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.Web.WinUI;

/// <summary>
/// Thin STA-owned adapter over the original WebView2 CDP API. It accepts no
/// JavaScript source and exposes only protocol method calls.
/// </summary>
public sealed class CoreWebView2DevToolsSession(
	CoreWebView2 browser) :
	IWebRuntimeDevToolsSession
{
	private readonly DispatcherQueue _dispatcher =
		DispatcherQueue.GetForCurrentThread()
		?? throw new InvalidOperationException(
			"WebView2 CDP adapter must be created on a DispatcherQueue thread.");
	private string _currentUrl = browser.Source;

	public string CurrentUrl => Volatile.Read(ref _currentUrl);

	public async Task<string> CallDevToolsProtocolMethodAsync(
		string method,
		string parametersJson,
		CancellationToken ct)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);
		ct.ThrowIfCancellationRequested();
		if (_dispatcher.HasThreadAccess)
		{
			var result = await CallWithTransientRetryAsync(
				browser,
				method,
				parametersJson,
				ct);
			Volatile.Write(ref _currentUrl, browser.Source);
			return result;
		}
		var completion = new TaskCompletionSource<string>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		if (!_dispatcher.TryEnqueue(async () =>
			{
				try
				{
					var result = await CallWithTransientRetryAsync(
						browser,
						method,
						parametersJson,
						ct);
					Volatile.Write(ref _currentUrl, browser.Source);
					completion.TrySetResult(result);
				}
				catch (Exception exception)
				{
					completion.TrySetException(exception);
				}
			}))
		{
			throw new InvalidOperationException(
				"WebView2 DispatcherQueue rejected a CDP operation.");
		}
		return await completion.Task.WaitAsync(ct);
	}

	private static async Task<string> CallWithTransientRetryAsync(
		CoreWebView2 browser,
		string method,
		string parametersJson,
		CancellationToken cancellationToken)
	{
		const int maximumAttempts = 20;
		var retryDelay = TimeSpan.FromMilliseconds(250);
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return await browser.CallDevToolsProtocolMethodAsync(
						method,
						parametersJson)
					.AsTask()
					.WaitAsync(cancellationToken);
			}
			catch (ArgumentException) when (attempt < maximumAttempts)
			{
				await Task.Delay(retryDelay, cancellationToken);
			}
		}
	}

	public IDisposable SubscribeDevToolsProtocolEvent(
		string eventName,
		EventHandler<WebRuntimeDevToolsProtocolEventArgs> handler)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
		ArgumentNullException.ThrowIfNull(handler);
		if (!_dispatcher.HasThreadAccess)
		{
			throw new InvalidOperationException(
				"CDP event subscriptions must be created on the WebView2 "
				+ "DispatcherQueue thread.");
		}
		var receiver = browser.GetDevToolsProtocolEventReceiver(eventName);
		Windows.Foundation.TypedEventHandler<
			CoreWebView2,
			CoreWebView2DevToolsProtocolEventReceivedEventArgs> bridge =
			(_, args) => handler(
				this,
				new(args.ParameterObjectAsJson));
		receiver.DevToolsProtocolEventReceived += bridge;
		return new EventSubscription(receiver, bridge, _dispatcher);
	}

	private sealed class EventSubscription(
		CoreWebView2DevToolsProtocolEventReceiver receiver,
		Windows.Foundation.TypedEventHandler<
			CoreWebView2,
			CoreWebView2DevToolsProtocolEventReceivedEventArgs> handler,
		DispatcherQueue dispatcher) : IDisposable
	{
		private CoreWebView2DevToolsProtocolEventReceiver? _receiver = receiver;
		private Windows.Foundation.TypedEventHandler<
			CoreWebView2,
			CoreWebView2DevToolsProtocolEventReceivedEventArgs>? _handler =
				handler;

		public void Dispose()
		{
			var currentReceiver = Interlocked.Exchange(ref _receiver, null);
			var currentHandler = Interlocked.Exchange(ref _handler, null);
			if (currentReceiver is null || currentHandler is null)
				return;
			void Detach() =>
				currentReceiver.DevToolsProtocolEventReceived -= currentHandler;
			if (dispatcher.HasThreadAccess)
			{
				Detach();
				return;
			}
			if (!dispatcher.TryEnqueue(Detach))
			{
				throw new InvalidOperationException(
					"WebView2 DispatcherQueue rejected CDP event detachment.");
			}
		}
	}

}
