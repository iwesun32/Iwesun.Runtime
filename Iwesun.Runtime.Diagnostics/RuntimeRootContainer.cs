using System.Diagnostics.CodeAnalysis;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeRootTable
{
	private readonly object _gate = new();
	private readonly RuntimeDList<RuntimeRootEntryEnvelope> _entries = new();
	private readonly Dictionary<string, RuntimeDListNode<RuntimeRootEntryEnvelope>> _byId = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, RuntimeDListNode<RuntimeRootEntryEnvelope>> _byPrimary = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, HashSet<string>> _bySecondary = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, IRuntimeRootAuxIndex> _auxIndexes = new(StringComparer.OrdinalIgnoreCase);

	public RuntimeRootTable(string tableName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
		TableName = tableName;
		_entries.AllowDuplicates = false;
		_entries.MergeOnDuplicate = true;
		_entries.DuplicatePredicate = static (left, right) =>
			left.Id.Equals(right.Id, StringComparison.OrdinalIgnoreCase);
		_entries.MergeDelegate = static (existing, incoming) => MergeEnvelope(existing, incoming);
		_entries.FilterPredicate = static entry =>
			!string.IsNullOrWhiteSpace(entry.Id)
			&& !string.IsNullOrWhiteSpace(entry.PrimaryKey);
		_auxIndexes["sorted-primary"] = new RuntimeRootSortedPrimaryKeyIndex();
	}

	public string TableName { get; }

	public IReadOnlyList<string> AuxIndexNames
	{
		get
		{
			lock (_gate)
			{
				return _auxIndexes.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
			}
		}
	}

	public void RegisterAuxIndex(IRuntimeRootAuxIndex index)
	{
		ArgumentNullException.ThrowIfNull(index);
		lock (_gate)
		{
			_auxIndexes[index.Name] = index;
			RebuildAuxIndexesLocked();
		}
	}

	public void SetEntries(IEnumerable<RuntimeRootEntryEnvelope> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		lock (_gate)
		{
			_entries.Clear();
			_byId.Clear();
			_byPrimary.Clear();
			_bySecondary.Clear();

			foreach (var entry in entries)
			{
				AddInternal(entry);
			}

			RebuildAuxIndexesLocked();
		}
	}

	public void Upsert(RuntimeRootEntryEnvelope entry)
	{
		lock (_gate)
		{
			AddInternal(entry);
			RebuildAuxIndexesLocked();
		}
	}

	public bool TryGetById(string id, [NotNullWhen(true)] out RuntimeRootEntryEnvelope? entry)
	{
		lock (_gate)
		{
			if (_byId.TryGetValue(id, out var node))
			{
				entry = node.Value;
				return true;
			}
		}

		entry = null;
		return false;
	}

	public bool TryGetByPrimary(string primaryKey, [NotNullWhen(true)] out RuntimeRootEntryEnvelope? entry)
	{
		lock (_gate)
		{
			if (_byPrimary.TryGetValue(primaryKey, out var node))
			{
				entry = node.Value;
				return true;
			}
		}

		entry = null;
		return false;
	}

	public IReadOnlyList<RuntimeRootEntryEnvelope> QueryBySecondary(string key, string value)
	{
		var composite = ComposeSecondaryIndexKey(key, value);
		lock (_gate)
		{
			if (!_bySecondary.TryGetValue(composite, out var ids))
			{
				return Array.Empty<RuntimeRootEntryEnvelope>();
			}

			var result = new List<RuntimeRootEntryEnvelope>(ids.Count);
			foreach (var id in ids)
			{
				if (_byId.TryGetValue(id, out var node))
				{
					result.Add(node.Value);
				}
			}

			return result;
		}
	}

	public IReadOnlyList<RuntimeRootEntryEnvelope> QueryByPrimaryPrefix(string prefix, int take = 100)
	{
		lock (_gate)
		{
			if (!_auxIndexes.TryGetValue("sorted-primary", out var index))
			{
				return Array.Empty<RuntimeRootEntryEnvelope>();
			}

			return index.QueryPrefix(prefix, take);
		}
	}

	public RuntimeRootTableSnapshot Snapshot()
	{
		lock (_gate)
		{
			var entries = _entries.ToArraySnapshot();
			return new RuntimeRootTableSnapshot(TableName, entries.Count, entries);
		}
	}

	private void AddInternal(RuntimeRootEntryEnvelope entry)
	{
		_byId.TryGetValue(entry.Id, out var existingNode);
		var previous = existingNode?.Value;
		var result = _entries.TryAdd(entry);
		if (result.Node == null)
		{
			throw new InvalidOperationException($"Entry '{entry.Id}' was rejected by RuntimeRootTable DLIST policy.");
		}

		var node = result.Node;
		if (previous.HasValue)
		{
			RemoveSecondaryIndexes(previous.Value);
			_byPrimary.Remove(previous.Value.PrimaryKey);
		}

		var stored = node.Value;
		if (_byPrimary.TryGetValue(stored.PrimaryKey, out var primaryConflictNode)
			&& !ReferenceEquals(primaryConflictNode, node))
		{
			var displaced = primaryConflictNode.Value;
			_entries.Remove(primaryConflictNode);
			_byId.Remove(displaced.Id);
			RemoveSecondaryIndexes(displaced);
		}

		_byId[stored.Id] = node;
		_byPrimary[stored.PrimaryKey] = node;
		AddSecondaryIndexes(stored);
	}

	private void AddSecondaryIndexes(RuntimeRootEntryEnvelope entry)
	{
		foreach (var pair in entry.SecondaryKeys)
		{
			var composite = ComposeSecondaryIndexKey(pair.Key, pair.Value);
			if (!_bySecondary.TryGetValue(composite, out var ids))
			{
				ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				_bySecondary[composite] = ids;
			}

			ids.Add(entry.Id);
		}
	}

	private void RemoveSecondaryIndexes(RuntimeRootEntryEnvelope entry)
	{
		foreach (var pair in entry.SecondaryKeys)
		{
			var composite = ComposeSecondaryIndexKey(pair.Key, pair.Value);
			if (!_bySecondary.TryGetValue(composite, out var ids))
			{
				continue;
			}

			ids.Remove(entry.Id);
			if (ids.Count == 0)
			{
				_bySecondary.Remove(composite);
			}
		}
	}

	private static string ComposeSecondaryIndexKey(string key, string value) =>
		$"{key}\u001f{value}";

	private void RebuildAuxIndexesLocked()
	{
		var entries = _entries.ToArraySnapshot();
		foreach (var index in _auxIndexes.Values)
		{
			index.Rebuild(entries);
		}
	}

	private static RuntimeRootEntryEnvelope MergeEnvelope(RuntimeRootEntryEnvelope existing, RuntimeRootEntryEnvelope incoming)
	{
		var secondary = new Dictionary<string, string>(existing.SecondaryKeys, StringComparer.OrdinalIgnoreCase);
		foreach (var pair in incoming.SecondaryKeys)
		{
			secondary[pair.Key] = pair.Value;
		}

		return new RuntimeRootEntryEnvelope(
			existing.Id,
			incoming.PrimaryKey,
			secondary,
			incoming.Payload,
			existing.RegisteredAt,
			DateTimeOffset.UtcNow);
	}
}

public sealed class RuntimeRootContainer
{
	private readonly object _gate = new();
	private readonly Dictionary<string, RuntimeRootTable> _tables = new(StringComparer.OrdinalIgnoreCase);

	public void SetTableEntries(string tableName, IEnumerable<RuntimeRootEntryEnvelope> entries)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
		ArgumentNullException.ThrowIfNull(entries);

		lock (_gate)
		{
			var table = GetOrCreateTable(tableName);
			table.SetEntries(entries);
		}
	}

	public void Upsert(string tableName, RuntimeRootEntryEnvelope entry)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
		lock (_gate)
		{
			var table = GetOrCreateTable(tableName);
			table.Upsert(entry);
		}
	}

	public bool TryGet(string tableName, string id, [NotNullWhen(true)] out RuntimeRootEntryEnvelope? entry)
	{
		lock (_gate)
		{
			if (_tables.TryGetValue(tableName, out var table))
			{
				return table.TryGetById(id, out entry);
			}
		}

		entry = null;
		return false;
	}

	public bool TryGetByPrimary(string tableName, string primaryKey, [NotNullWhen(true)] out RuntimeRootEntryEnvelope? entry)
	{
		lock (_gate)
		{
			if (_tables.TryGetValue(tableName, out var table))
			{
				return table.TryGetByPrimary(primaryKey, out entry);
			}
		}

		entry = null;
		return false;
	}

	public IReadOnlyList<RuntimeRootEntryEnvelope> QueryBySecondary(string tableName, string key, string value)
	{
		lock (_gate)
		{
			if (_tables.TryGetValue(tableName, out var table))
			{
				return table.QueryBySecondary(key, value);
			}
		}

		return Array.Empty<RuntimeRootEntryEnvelope>();
	}

	public IReadOnlyList<RuntimeRootEntryEnvelope> QueryByPrimaryPrefix(string tableName, string prefix, int take = 100)
	{
		lock (_gate)
		{
			if (_tables.TryGetValue(tableName, out var table))
			{
				return table.QueryByPrimaryPrefix(prefix, take);
			}
		}

		return Array.Empty<RuntimeRootEntryEnvelope>();
	}

	public IReadOnlyList<string> ListAuxIndexes(string tableName)
	{
		lock (_gate)
		{
			if (_tables.TryGetValue(tableName, out var table))
			{
				return table.AuxIndexNames;
			}
		}

		return Array.Empty<string>();
	}

	public RuntimeRootSnapshot Snapshot()
	{
		lock (_gate)
		{
			var tables = _tables.Values
				.OrderBy(x => x.TableName, StringComparer.OrdinalIgnoreCase)
				.Select(x => x.Snapshot())
				.ToArray();

			return new RuntimeRootSnapshot(DateTimeOffset.UtcNow, tables);
		}
	}

	private RuntimeRootTable GetOrCreateTable(string tableName)
	{
		if (_tables.TryGetValue(tableName, out var table))
		{
			return table;
		}

		table = new RuntimeRootTable(tableName);
		_tables[tableName] = table;
		return table;
	}
}
