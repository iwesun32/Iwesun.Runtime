using Microsoft.Web.WebView2.Core;

namespace Iwesun.Runtime.WebView2;

/// <summary>
/// Adapts one CoreWebView2 instance to the fixed Runtime CDP method and event
/// boundary. The adapter never accepts or executes page JavaScript.
/// </summary>
public sealed class CoreWebView2DevToolsSession(CoreWebView2 browser)
	: IWebRuntimeDevToolsSession
{
	private readonly CoreWebView2 _browser =
		browser ?? throw new ArgumentNullException(nameof(browser));
	private readonly SynchronizationContext? _ownerContext =
		SynchronizationContext.Current;
	private string _currentUrl = browser.Source;

	public string CurrentUrl => Volatile.Read(ref _currentUrl);

	public async Task<string> CallDevToolsProtocolMethodAsync(
		string method,
		string parametersJson,
		CancellationToken ct)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(method);
		ArgumentNullException.ThrowIfNull(parametersJson);
		ct.ThrowIfCancellationRequested();
		if (_ownerContext is null
			|| ReferenceEquals(SynchronizationContext.Current, _ownerContext))
		{
			Volatile.Write(ref _currentUrl, _browser.Source);
			return await _browser.CallDevToolsProtocolMethodAsync(
				method,
				parametersJson).WaitAsync(ct);
		}
		var completion = new TaskCompletionSource<string>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		_ownerContext.Post(
			async _ =>
			{
				try
				{
					Volatile.Write(ref _currentUrl, _browser.Source);
					completion.TrySetResult(
						await _browser.CallDevToolsProtocolMethodAsync(
							method,
							parametersJson));
				}
				catch (Exception exception)
				{
					completion.TrySetException(exception);
				}
			},
			null);
		return await completion.Task.WaitAsync(ct);
	}

	public IDisposable SubscribeDevToolsProtocolEvent(
		string eventName,
		EventHandler<WebRuntimeDevToolsProtocolEventArgs> handler)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
		ArgumentNullException.ThrowIfNull(handler);
		if (_ownerContext is not null
			&& !ReferenceEquals(
				SynchronizationContext.Current,
				_ownerContext))
		{
			throw new InvalidOperationException(
				"CDP event subscriptions must be created on the WebView2 "
				+ "owner context.");
		}
		var receiver = _browser.GetDevToolsProtocolEventReceiver(eventName);
		EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> bridge =
			(_, args) => handler(
				this,
				new(args.ParameterObjectAsJson));
		receiver.DevToolsProtocolEventReceived += bridge;
		return new EventSubscription(receiver, bridge, _ownerContext);
	}

	private sealed class EventSubscription(
		CoreWebView2DevToolsProtocolEventReceiver receiver,
		EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs> handler,
		SynchronizationContext? ownerContext)
		: IDisposable
	{
		private CoreWebView2DevToolsProtocolEventReceiver? _receiver = receiver;
		private EventHandler<CoreWebView2DevToolsProtocolEventReceivedEventArgs>?
			_handler = handler;

		public void Dispose()
		{
			var currentReceiver = Interlocked.Exchange(ref _receiver, null);
			var currentHandler = Interlocked.Exchange(ref _handler, null);
			if (currentReceiver is not null && currentHandler is not null)
			{
				void Detach() =>
					currentReceiver.DevToolsProtocolEventReceived -=
						currentHandler;
				if (ownerContext is null
					|| ReferenceEquals(
						SynchronizationContext.Current,
						ownerContext))
				{
					Detach();
				}
				else
				{
					ownerContext.Send(static state =>
					{
						((Action)state!).Invoke();
					}, (Action)Detach);
				}
			}
		}
	}
}
