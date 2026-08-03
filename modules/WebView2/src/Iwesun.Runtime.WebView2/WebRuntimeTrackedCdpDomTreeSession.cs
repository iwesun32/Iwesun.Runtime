using System.Text.Json;
namespace Iwesun.Runtime.WebView2;

/// <summary>
/// Owns a revisioned CDP DOM tree. Structural CDP events invalidate and
/// rebuild the tree; attribute events are merged into a new atomic revision.
/// </summary>
public sealed class WebRuntimeTrackedCdpDomTreeSession
	: IWebRuntimeDomTreeSession,
	IAsyncDisposable
{
	private static readonly string[] StructuralEvents =
	[
		"DOM.documentUpdated",
		"DOM.setChildNodes",
		"DOM.childNodeInserted",
		"DOM.childNodeRemoved",
		"DOM.childNodeCountUpdated",
		"DOM.characterDataModified",
		"DOM.shadowRootPushed",
		"DOM.shadowRootPopped",
		"DOM.pseudoElementAdded",
		"DOM.pseudoElementRemoved",
		"DOM.distributedNodesUpdated"
	];
	private static readonly string[] NavigationEvents =
	[
		"Page.frameNavigated",
		"Page.navigatedWithinDocument"
	];

	private readonly IWebRuntimeDevToolsSession _session;
	private readonly WebRuntimeCdpDomTreeReader _reader;
	private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
	private readonly SemaphoreSlim _refreshGate = new(1, 1);
	private readonly object _eventSync = new();
	private readonly List<IDisposable> _subscriptions = [];
	private readonly CancellationTokenSource _disposeSource = new();
	private WebRuntimeCdpDomTreeIndex? _current;
	private Task? _eventRefreshTask;
	private Exception? _lastRefreshError;
	private long _revision;
	private long _invalidationVersion;
	private int _initialized;
	private int _invalidated = 1;
	private int _refreshWorkerScheduled;
	private int _disposed;

	public WebRuntimeTrackedCdpDomTreeSession(
		IWebRuntimeDevToolsSession session)
	{
		_session = session ?? throw new ArgumentNullException(nameof(session));
		_reader = new(session, NextRevision);
	}

	public WebRuntimeCdpDomTreeIndex? Current =>
		Volatile.Read(ref _invalidated) == 0
			? Volatile.Read(ref _current)
			: null;

	/// <summary>
	/// Gets the last complete immutable tree even while a newer renderer
	/// revision is being rebuilt. Capture consumers use this only as the pinned
	/// identity source for one evidence transaction; live navigation and click
	/// operations must continue to use <see cref="Current"/>.
	/// </summary>
	public WebRuntimeDomTreeSnapshot? LastCompleteSnapshot =>
		Volatile.Read(ref _current)?.Snapshot;

	public Exception? LastRefreshError =>
		Volatile.Read(ref _lastRefreshError);

	public async ValueTask InitializeAsync(
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed();
		if (Volatile.Read(ref _initialized) != 0)
			return;
		await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			if (_initialized != 0)
				return;
			try
			{
				foreach (var eventName in StructuralEvents)
					_subscriptions.Add(_session.SubscribeDevToolsProtocolEvent(
						eventName,
						OnStructuralEvent));
				foreach (var eventName in NavigationEvents)
					_subscriptions.Add(_session.SubscribeDevToolsProtocolEvent(
						eventName,
						OnNavigationEvent));
				_subscriptions.Add(_session.SubscribeDevToolsProtocolEvent(
					"DOM.attributeModified",
					OnAttributeModified));
				_subscriptions.Add(_session.SubscribeDevToolsProtocolEvent(
					"DOM.attributeRemoved",
					OnAttributeRemoved));
				await _session.CallDevToolsProtocolMethodAsync(
					"DOM.enable",
					"{}",
					cancellationToken).ConfigureAwait(false);
				await _session.CallDevToolsProtocolMethodAsync(
					"Page.enable",
					"{}",
					cancellationToken).ConfigureAwait(false);
				await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
				Volatile.Write(ref _initialized, 1);
			}
			catch
			{
				DetachSubscriptions();
				throw;
			}
		}
		finally
		{
			_lifecycleGate.Release();
		}
	}

	public async ValueTask<WebRuntimeDomTreeSnapshot> ReadDomTreeAsync(
		CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken).ConfigureAwait(false);
		await EnsureCurrentAsync(cancellationToken).ConfigureAwait(false);
		return Current?.Snapshot
			?? throw new InvalidOperationException(
				"The CDP DOM tree has not been initialized.");
	}

	public async ValueTask<WebRuntimeCdpDomTreeNode> ResolveXPathAsync(
		string xpath,
		string documentScope = "document",
		CancellationToken cancellationToken = default)
	{
		await ReadDomTreeAsync(cancellationToken).ConfigureAwait(false);
		return Current!.ResolveXPath(documentScope, xpath);
	}

	public async ValueTask<int> ResolveNodeIdAsync(
		string xpath,
		string documentScope = "document",
		CancellationToken cancellationToken = default)
	{
		await ReadDomTreeAsync(cancellationToken).ConfigureAwait(false);
		return Current!.ResolveNodeId(documentScope, xpath);
	}

	internal async ValueTask<T> UseNodeAsync<T>(
		string documentScope,
		string xpath,
		Func<WebRuntimeCdpDomTreeNode, CancellationToken, Task<T>> operation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(operation);
		await InitializeAsync(cancellationToken).ConfigureAwait(false);
		await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			for (var attempt = 0; ; attempt++)
			{
				if (Volatile.Read(ref _invalidated) != 0)
				{
					await RefreshWithoutGateAsync(cancellationToken)
						.ConfigureAwait(false);
				}
				var node = Current!.ResolveXPath(documentScope, xpath);
				try
				{
					return await operation(node, cancellationToken)
						.ConfigureAwait(false);
				}
				catch (Exception exception)
					when (attempt == 0
						&& IsInvalidNodeIdentityException(exception))
				{
					MarkInvalidated();
					await RefreshWithoutGateAsync(cancellationToken)
						.ConfigureAwait(false);
				}
			}
		}
		finally
		{
			_refreshGate.Release();
		}
	}

	public void Invalidate()
	{
		ThrowIfDisposed();
		MarkInvalidated();
		if (Volatile.Read(ref _initialized) != 0)
			ScheduleEventRefresh();
	}

	public async ValueTask RefreshAsync(
		CancellationToken cancellationToken = default)
	{
		await InitializeAsync(cancellationToken).ConfigureAwait(false);
		MarkInvalidated();
		await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask EnsureCurrentAsync(
		CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref _invalidated) == 0
			&& Current is not null
			&& LastRefreshError is null)
		{
			return;
		}
		await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
	}

	private async Task RefreshCoreAsync(CancellationToken cancellationToken)
	{
		await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await RefreshWithoutGateAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_refreshGate.Release();
		}
	}

	private async Task RefreshWithoutGateAsync(
		CancellationToken cancellationToken)
	{
		long invalidationVersion;
		lock (_eventSync)
			invalidationVersion = _invalidationVersion;
		try
		{
			var snapshot = await _reader.ReadAsync(
				cancellationToken).ConfigureAwait(false);
			var index = WebRuntimeCdpDomTreeIndex.Create(snapshot);
			lock (_eventSync)
			{
				if (_invalidationVersion != invalidationVersion)
					return;
				Volatile.Write(ref _current, index);
				Volatile.Write(ref _lastRefreshError, null);
				Volatile.Write(ref _invalidated, 0);
			}
		}
		catch (Exception exception)
		{
			MarkInvalidated();
			Volatile.Write(ref _lastRefreshError, exception);
			throw;
		}
	}

	private void OnStructuralEvent(
		object? sender,
		WebRuntimeDevToolsProtocolEventArgs args)
	{
		if (Volatile.Read(ref _disposed) != 0)
			return;
		MarkInvalidated();
		ScheduleEventRefresh();
	}

	private void OnNavigationEvent(
		object? sender,
		WebRuntimeDevToolsProtocolEventArgs args)
	{
		if (Volatile.Read(ref _disposed) != 0)
			return;
		MarkInvalidated();
		ScheduleEventRefresh();
	}

	private void OnAttributeModified(
		object? sender,
		WebRuntimeDevToolsProtocolEventArgs args) =>
		ApplyAttributeEvent(args.ParameterJson, remove: false);

	private void OnAttributeRemoved(
		object? sender,
		WebRuntimeDevToolsProtocolEventArgs args) =>
		ApplyAttributeEvent(args.ParameterJson, remove: true);

	private void ApplyAttributeEvent(string json, bool remove)
	{
		if (Volatile.Read(ref _disposed) != 0)
			return;
		try
		{
			using var document = JsonDocument.Parse(json);
			var root = document.RootElement;
			var nodeId = root.GetProperty("nodeId").GetInt32();
			var name = root.GetProperty("name").GetString();
			if (nodeId <= 0 || string.IsNullOrWhiteSpace(name)
				|| !TryPatchAttribute(
					nodeId,
					name,
					remove
						? null
						: root.GetProperty("value").GetString() ?? string.Empty))
			{
				MarkInvalidated();
				ScheduleEventRefresh();
			}
		}
		catch (JsonException)
		{
			MarkInvalidated();
			ScheduleEventRefresh();
		}
	}

	private void MarkInvalidated()
	{
		lock (_eventSync)
		{
			_invalidationVersion++;
			Volatile.Write(ref _invalidated, 1);
		}
	}

	private bool TryPatchAttribute(
		int nodeId,
		string name,
		string? value)
	{
		if (_refreshGate.CurrentCount == 0)
			return false;
		lock (_eventSync)
		{
			var current = Current;
			if (current is null || !current.TryGetNode(nodeId, out _))
				return false;
			var elements = current.Snapshot.Elements.ToArray();
			var index = Array.FindIndex(
				elements,
				element => element.NodeId == nodeId);
			if (index < 0)
				return false;
			var attributes = new Dictionary<string, string>(
				elements[index].AttributeValues,
				StringComparer.OrdinalIgnoreCase);
			if (value is null)
				attributes.Remove(name);
			else
				attributes[name] = value;
			elements[index] = elements[index] with
			{
				AttributeNames = attributes.Keys.ToArray(),
				AttributeValues = attributes
			};
			var snapshot = current.Snapshot with
			{
				Revision = NextRevision(),
				CapturedAt = DateTimeOffset.UtcNow,
				Elements = elements
			};
			Volatile.Write(
				ref _current,
				WebRuntimeCdpDomTreeIndex.Create(snapshot));
			return true;
		}
	}

	private void ScheduleEventRefresh()
	{
		if (Interlocked.CompareExchange(
				ref _refreshWorkerScheduled,
				1,
				0) != 0)
		{
			return;
		}
		var refreshTask = RefreshFromEventAsync();
		lock (_eventSync)
		{
			_eventRefreshTask = refreshTask;
		}
	}

	private async Task RefreshFromEventAsync()
	{
		var failed = false;
		try
		{
			await Task.Yield();
			while (Volatile.Read(ref _invalidated) != 0
				&& !_disposeSource.IsCancellationRequested)
			{
				await RefreshCoreAsync(_disposeSource.Token).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
			when (_disposeSource.IsCancellationRequested)
		{
		}
		catch (Exception exception)
		{
			failed = true;
			Volatile.Write(ref _lastRefreshError, exception);
		}
		finally
		{
			Interlocked.Exchange(ref _refreshWorkerScheduled, 0);
			if (!failed
				&& Volatile.Read(ref _invalidated) != 0
				&& Volatile.Read(ref _disposed) == 0)
			{
				ScheduleEventRefresh();
			}
		}
	}

	private long NextRevision() =>
		Interlocked.Increment(ref _revision);

	internal static bool IsInvalidNodeIdentityException(Exception exception)
	{
		for (Exception? current = exception;
			current is not null;
			current = current.InnerException)
		{
			var message = current.Message;
			if (message.Contains(
					"Could not find node with given id",
					StringComparison.OrdinalIgnoreCase)
				|| message.Contains(
					"No node with given id",
					StringComparison.OrdinalIgnoreCase)
				|| message.Contains(
					"Node with given id does not belong to the document",
					StringComparison.OrdinalIgnoreCase)
				|| message.Contains(
					"Invalid node id",
					StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	private void DetachSubscriptions()
	{
		for (var index = _subscriptions.Count - 1; index >= 0; index--)
			_subscriptions[index].Dispose();
		_subscriptions.Clear();
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
			return;
		_disposeSource.Cancel();
		DetachSubscriptions();
		Task? refresh;
		lock (_eventSync)
			refresh = _eventRefreshTask;
		if (refresh is not null)
		{
			try
			{
				await refresh.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
		}
		_disposeSource.Dispose();
	}

	private void ThrowIfDisposed() =>
		ObjectDisposedException.ThrowIf(
			Volatile.Read(ref _disposed) != 0,
			this);
}
