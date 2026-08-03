namespace Iwesun.Runtime.Networks;

internal sealed class NetworkHttpConnectionPool : IDisposable
{
	private readonly object _gate = new();
	private readonly Dictionary<NetworkHttpConnectionPoolKey, NetworkHttpPoolEntry> _entries = [];
	private readonly TimeSpan _idleRetention;
	private readonly int _maxCount;
	private readonly int _sweepInterval;
	private readonly Timer _idleTimer;
	private int _rentCount;
	private bool _disposed;

	public NetworkHttpConnectionPool(TimeSpan idleRetention, int maxCount, int sweepInterval)
	{
		if (idleRetention <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleRetention));
		if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
		if (sweepInterval <= 0) throw new ArgumentOutOfRangeException(nameof(sweepInterval));
		_idleRetention = idleRetention;
		_maxCount = maxCount;
		_sweepInterval = sweepInterval;
		_idleTimer = new Timer(
			static state => ((NetworkHttpConnectionPool)state!).SweepExpired(),
			this,
			idleRetention,
			idleRetention);
	}

	public int Count
	{
		get
		{
			lock (_gate)
			{
				return _entries.Count;
			}
		}
	}

	public NetworkHttpPoolLease Rent(
		NetworkHttpConnectionPoolKey key,
		ResolvedAccessPlan resolved,
		Func<ResolvedAccessPlan, bool> isCurrent,
		Func<NetworkHttpPoolEntry> factory)
	{
		ArgumentNullException.ThrowIfNull(isCurrent);
		ArgumentNullException.ThrowIfNull(factory);
		List<NetworkHttpPoolEntry>? retired = null;
		NetworkHttpPoolLease lease;
		lock (_gate)
		{
			if (_disposed) throw new ObjectDisposedException(nameof(NetworkHttpConnectionPool));
			var now = Environment.TickCount64;
			if (++_rentCount >= _sweepInterval)
			{
				_rentCount = 0;
				retired = RetireIdleEntries(now, requireExpiration: true);
			}
			if (_entries.TryGetValue(key, out var existing))
			{
				if (existing.Resolved == resolved && isCurrent(resolved) && existing.TryAcquire(now))
				{
					lease = new NetworkHttpPoolLease(existing);
					goto Complete;
				}
				_entries.Remove(key);
				(retired ??= []).Add(existing);
			}
			if (_entries.Count >= _maxCount)
			{
				var capacityRetired = RetireIdleEntries(now, requireExpiration: false, maximumCount: 1);
				if (capacityRetired is not null) (retired ??= []).AddRange(capacityRetired);
			}
			var created = factory();
			if (_entries.Count >= _maxCount)
			{
				created.TryAcquire(now);
				created.Retire();
				lease = new NetworkHttpPoolLease(created);
				goto Complete;
			}
			if (!created.TryAcquire(now))
			{
				created.Dispose();
				throw new InvalidOperationException("The HTTP pool entry could not be acquired.");
			}
			_entries.Add(key, created);
			lease = new NetworkHttpPoolLease(created);
		}

	Complete:
		if (retired is not null)
			foreach (var entry in retired) entry.Retire();
		return lease;
	}

	public NetworkHttpPoolLease RentTransient(Func<NetworkHttpPoolEntry> factory)
	{
		ArgumentNullException.ThrowIfNull(factory);
		var entry = factory();
		if (!entry.TryAcquire(Environment.TickCount64))
		{
			entry.Dispose();
			throw new InvalidOperationException("The transient HTTP entry could not be acquired.");
		}
		entry.Retire();
		return new NetworkHttpPoolLease(entry);
	}

	public void Dispose()
	{
		NetworkHttpPoolEntry[] entries;
		lock (_gate)
		{
			if (_disposed) return;
			_disposed = true;
			_idleTimer.Dispose();
			entries = [.. _entries.Values];
			_entries.Clear();
		}
		foreach (var entry in entries) entry.Retire();
	}

	private void SweepExpired()
	{
		List<NetworkHttpPoolEntry>? retired;
		lock (_gate)
		{
			if (_disposed) return;
			retired = RetireIdleEntries(Environment.TickCount64, requireExpiration: true);
		}
		if (retired is not null)
			foreach (var entry in retired) entry.Retire();
	}

	private List<NetworkHttpPoolEntry>? RetireIdleEntries(
		long now,
		bool requireExpiration,
		int maximumCount = int.MaxValue)
	{
		List<NetworkHttpPoolEntry>? retired = null;
		foreach (var item in _entries
			.OrderBy(pair => pair.Value.LastUsedTick)
			.ToArray())
		{
			if (retired?.Count >= maximumCount) break;
			if (!item.Value.CanRetire(now, _idleRetention, requireExpiration)) continue;
			if (!_entries.Remove(item.Key)) continue;
			(retired ??= []).Add(item.Value);
		}
		return retired;
	}
}

internal sealed class NetworkHttpPoolLease : IDisposable
{
	private NetworkHttpPoolEntry? _entry;

	public NetworkHttpPoolLease(NetworkHttpPoolEntry entry) => _entry = entry;

	public NetworkHttpPoolEntry Entry =>
		_entry ?? throw new ObjectDisposedException(nameof(NetworkHttpPoolLease));

	public void Dispose() => Interlocked.Exchange(ref _entry, null)?.Release();
}
