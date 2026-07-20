using System.Diagnostics.CodeAnalysis;
using System.Collections.ObjectModel;
using System.Text.Json;
using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

public sealed class RuntimeRootTable
{
	private static readonly RecordStoreDefinition<RuntimeRootEntryRow, string> EntryDefinition =
		CreateEntryDefinition();

	private readonly object _gate = new();
	private RecordStoreV2<RuntimeRootEntryRow, string> _entries = CreateEntryStore();
	private readonly Dictionary<string, StoreRecordId> _byId = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, StoreRecordId> _byPrimary = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, HashSet<string>> _bySecondary = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, IRuntimeRootAuxIndex> _auxIndexes = new(StringComparer.OrdinalIgnoreCase);

	public RuntimeRootTable(string tableName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
		TableName = tableName;
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
			_entries = CreateEntryStore();
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
			if (_byId.TryGetValue(id, out var recordId)
				&& _entries.TryGetRecord(recordId, out var stored))
			{
				entry = stored.Value;
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
			if (_byPrimary.TryGetValue(primaryKey, out var recordId)
				&& _entries.TryGetRecord(recordId, out var stored))
			{
				entry = stored.Value;
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
				if (_byId.TryGetValue(id, out var recordId)
					&& _entries.TryGetRecord(recordId, out var stored))
				{
					result.Add(stored.Value);
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
			var entries = _entries.Select(static row => row.Value).ToArray();
			return new RuntimeRootTableSnapshot(TableName, entries.Length, entries);
		}
	}

	private void AddInternal(RuntimeRootEntryEnvelope entry)
	{
		if (string.IsNullOrWhiteSpace(entry.Id) || string.IsNullOrWhiteSpace(entry.PrimaryKey))
		{
			throw new InvalidOperationException($"Entry '{entry.Id}' was rejected by RuntimeRootTable RecordStore policy.");
		}

		var previous = default(RuntimeRootEntryEnvelope);
		var previousRow = default(RuntimeRootEntryRow);
		var hasExisting = _byId.TryGetValue(entry.Id, out var recordId)
			&& _entries.TryGetRecord(recordId, out previousRow);
		if (hasExisting)
		{
			previous = previousRow.Value;
		}
		var candidate = hasExisting ? MergeEnvelope(previous, entry) : entry;

		if (_byPrimary.TryGetValue(candidate.PrimaryKey, out var primaryConflictId)
			&& (!hasExisting || primaryConflictId != recordId)
			&& _entries.TryGetRecord(primaryConflictId, out var displacedRow))
		{
			var displaced = displacedRow.Value;
			_entries.TryDeprecate(primaryConflictId);
			_byId.Remove(displaced.Id);
			_byPrimary.Remove(displaced.PrimaryKey);
			RemoveSecondaryIndexes(displaced);
		}

		if (hasExisting)
		{
			RemoveSecondaryIndexes(previous);
			_byPrimary.Remove(previous.PrimaryKey);
			var updatedRow = new RuntimeRootEntryRow
			{
				StoreRecordId = recordId,
				Value = candidate
			};
			if (_entries.TryUpdate(updatedRow) != RecordUpdateResult.Updated)
			{
				throw new InvalidOperationException($"Entry '{entry.Id}' could not be updated in RuntimeRootTable RecordStore.");
			}
		}
		else
		{
			recordId = _entries.Add(new RuntimeRootEntryRow { Value = candidate });
		}

		if (!_entries.TryGetRecord(recordId, out var stored))
		{
			throw new InvalidOperationException($"Entry '{entry.Id}' was not readable after RuntimeRootTable RecordStore update.");
		}

		var storedValue = stored.Value;
		_byId[storedValue.Id] = recordId;
		_byPrimary[storedValue.PrimaryKey] = recordId;
		AddSecondaryIndexes(storedValue);
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
		var entries = _entries.Select(static row => row.Value).ToArray();
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

	private static RecordStoreDefinition<RuntimeRootEntryRow, string> CreateEntryDefinition()
	{
		var id = new RecordKeyDefinition<RuntimeRootEntryRow, string>(
			"id",
			static row => row.Value.Id,
			StringComparer.OrdinalIgnoreCase,
			StringComparer.OrdinalIgnoreCase);
		var primary = new RecordKeyDefinition<RuntimeRootEntryRow, string>(
			"primary",
			static row => row.Value.PrimaryKey,
			StringComparer.OrdinalIgnoreCase,
			StringComparer.OrdinalIgnoreCase);
		return new RecordStoreDefinition<RuntimeRootEntryRow, string>(
			[id, primary],
			new PrimaryKeyDefinition<RuntimeRootEntryRow, string>(
				["id"],
				static row => row.Value.Id,
				StringComparer.OrdinalIgnoreCase,
				StringComparer.OrdinalIgnoreCase));
	}

	private static RecordStoreV2<RuntimeRootEntryRow, string> CreateEntryStore() =>
		new(EntryDefinition) { CloneStrategy = RuntimeRootEntryRowCloneStrategy.Instance };

	private struct RuntimeRootEntryRow : IRecordStoreValue
	{
		public StoreRecordId StoreRecordId { get; set; }
		public RuntimeRootEntryEnvelope Value { get; init; }
	}

	private sealed class RuntimeRootEntryRowCloneStrategy : IDeepCloneStrategy<RuntimeRootEntryRow>
	{
		public static RuntimeRootEntryRowCloneStrategy Instance { get; } = new();

		public RuntimeRootEntryRow Clone(in RuntimeRootEntryRow value) => new()
		{
			StoreRecordId = value.StoreRecordId,
			Value = RuntimeRootEntryCloneStrategy.Instance.Clone(value.Value)
		};
	}

	private sealed class RuntimeRootEntryCloneStrategy : IDeepCloneStrategy<RuntimeRootEntryEnvelope>
	{
		public static RuntimeRootEntryCloneStrategy Instance { get; } = new();

		public RuntimeRootEntryEnvelope Clone(in RuntimeRootEntryEnvelope value) =>
			new(
				value.Id,
				value.PrimaryKey,
				new ReadOnlyDictionary<string, string>(
					new Dictionary<string, string>(value.SecondaryKeys, StringComparer.OrdinalIgnoreCase)),
				ClonePayload(value.Payload),
				value.RegisteredAt,
				value.UpdatedAt);

		private static object? ClonePayload(object? payload)
		{
			if (payload is null)
			{
				return null;
			}

			if (payload is JsonElement element)
			{
				return element.Clone();
			}

			try
			{
				return JsonSerializer.SerializeToElement(payload, payload.GetType());
			}
			catch (Exception ex) when (ex is not OutOfMemoryException)
			{
				return JsonSerializer.SerializeToElement(new
				{
					type = payload.GetType().FullName,
					unavailable = true
				});
			}
		}
	}
}

public sealed class RuntimeRootContainer
{
	private static readonly RecordStoreDefinition<RuntimeFilePathRow, string> FilePathDefinition =
		CreateFilePathDefinition();

	private readonly object _gate = new();
	private readonly Dictionary<string, RuntimeRootTable> _tables = new(StringComparer.OrdinalIgnoreCase);
	private RecordStoreV2<RuntimeFilePathRow, string> _filePathDescriptors = CreateFilePathStore();
	private readonly Dictionary<string, StoreRecordId> _filePathByName = new(StringComparer.OrdinalIgnoreCase);

	public RuntimeRootContainer()
	{
	}

	public void SetFilePathDescriptors(IEnumerable<RuntimeFilePathDescriptor> descriptors)
	{
		ArgumentNullException.ThrowIfNull(descriptors);

		lock (_gate)
		{
			_filePathDescriptors = CreateFilePathStore();
			_filePathByName.Clear();

			foreach (var descriptor in descriptors)
			{
				AddOrMergeFilePathDescriptorLocked(descriptor);
			}

			NormalizePrimaryFilePathDescriptorOrderLocked();
		}
	}

	public void UpsertFilePathDescriptor(RuntimeFilePathDescriptor descriptor)
	{
		lock (_gate)
		{
			AddOrMergeFilePathDescriptorLocked(descriptor);
			NormalizePrimaryFilePathDescriptorOrderLocked();
		}
	}

	public IReadOnlyList<RuntimeFilePathDescriptor> GetFilePathDescriptorsSnapshot()
	{
		lock (_gate)
		{
			return GetOrderedFilePathDescriptorsLocked();
		}
	}

	public bool TryGetPrimaryFilePathDescriptor(out RuntimeFilePathDescriptor descriptor)
	{
		lock (_gate)
		{
			foreach (var item in GetOrderedFilePathDescriptorsLocked())
			{
				if (!item.IsPrimaryRecord)
				{
					continue;
				}

				descriptor = item;
				return true;
			}
		}

		descriptor = default;
		return false;
	}

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
			var filePathDescriptors = GetOrderedFilePathDescriptorsLocked();

			return new RuntimeRootSnapshot(DateTimeOffset.UtcNow, tables, filePathDescriptors);
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

	private void AddOrMergeFilePathDescriptorLocked(RuntimeFilePathDescriptor descriptor)
	{
		if (string.IsNullOrWhiteSpace(descriptor.FilePathName))
		{
			throw new InvalidOperationException("File path descriptor was rejected by RuntimeRootContainer RecordStore policy.");
		}

		if (_filePathByName.TryGetValue(descriptor.FilePathName, out var recordId)
			&& _filePathDescriptors.TryGetRecord(recordId, out var existingRow))
		{
			var existing = existingRow.Value;
			var merged = MergeFilePathDescriptor(existing, descriptor);
			var updatedRow = new RuntimeFilePathRow
			{
				StoreRecordId = recordId,
				Value = merged
			};
			if (_filePathDescriptors.TryUpdate(updatedRow) != RecordUpdateResult.Updated)
			{
				throw new InvalidOperationException($"File path descriptor '{descriptor.FilePathName}' could not be updated in RecordStore.");
			}
			return;
		}

		recordId = _filePathDescriptors.Add(new RuntimeFilePathRow { Value = descriptor });
		_filePathByName[descriptor.FilePathName] = recordId;
	}

	private void NormalizePrimaryFilePathDescriptorOrderLocked()
	{
		var ordered = _filePathDescriptors
			.Select(static row => row.Value)
			.OrderBy(static descriptor => descriptor.IsPrimaryRecord ? 0 : 1)
			.ThenBy(static descriptor => descriptor.FilePathName, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (ordered.Count == 0)
		{
			return;
		}

		var firstPrimaryIndex = ordered.FindIndex(static descriptor => descriptor.IsPrimaryRecord);
		if (firstPrimaryIndex >= 0)
		{
			for (var i = 0; i < ordered.Count; i++)
			{
				ordered[i] = ordered[i].NormalizeAs(i == firstPrimaryIndex);
			}
		}

		_filePathDescriptors = CreateFilePathStore();
		_filePathByName.Clear();
		foreach (var descriptor in ordered)
		{
			var recordId = _filePathDescriptors.Add(new RuntimeFilePathRow { Value = descriptor });
			_filePathByName[descriptor.FilePathName] = recordId;
		}
	}

	private IReadOnlyList<RuntimeFilePathDescriptor> GetOrderedFilePathDescriptorsLocked() =>
		_filePathDescriptors
			.Select(static row => row.Value)
			.OrderBy(static descriptor => descriptor.IsPrimaryRecord ? 0 : 1)
			.ThenBy(static descriptor => descriptor.FilePathName, StringComparer.OrdinalIgnoreCase)
			.ToArray();

	private static RuntimeFilePathDescriptor MergeFilePathDescriptor(RuntimeFilePathDescriptor existing, RuntimeFilePathDescriptor incoming)
	{
		var annotations = new Dictionary<string, string>(existing.Annotations, StringComparer.OrdinalIgnoreCase);
		foreach (var pair in incoming.Annotations)
		{
			annotations[pair.Key] = pair.Value;
		}

		var description = string.IsNullOrWhiteSpace(incoming.Description)
			? existing.Description
			: incoming.Description;
		var purpose = string.IsNullOrWhiteSpace(incoming.Purpose)
			? existing.Purpose
			: incoming.Purpose;
		var source = string.IsNullOrWhiteSpace(incoming.Source)
			? existing.Source
			: incoming.Source;

		return new RuntimeFilePathDescriptor(
			existing.FilePathName,
			description,
			purpose,
			source,
			existing.IsPrimaryRecord || incoming.IsPrimaryRecord,
			annotations,
			existing.RegisteredAt,
			DateTimeOffset.UtcNow);
	}

	private static RecordStoreDefinition<RuntimeFilePathRow, string> CreateFilePathDefinition()
	{
		var name = new RecordKeyDefinition<RuntimeFilePathRow, string>(
			"name",
			static row => row.Value.FilePathName,
			StringComparer.OrdinalIgnoreCase,
			StringComparer.OrdinalIgnoreCase);
		return new RecordStoreDefinition<RuntimeFilePathRow, string>(
			[name],
			new PrimaryKeyDefinition<RuntimeFilePathRow, string>(
				["name"],
				static row => row.Value.FilePathName,
				StringComparer.OrdinalIgnoreCase,
				StringComparer.OrdinalIgnoreCase));
	}

	private static RecordStoreV2<RuntimeFilePathRow, string> CreateFilePathStore() =>
		new(FilePathDefinition) { CloneStrategy = RuntimeFilePathRowCloneStrategy.Instance };

	private struct RuntimeFilePathRow : IRecordStoreValue
	{
		public StoreRecordId StoreRecordId { get; set; }
		public RuntimeFilePathDescriptor Value { get; init; }
	}

	private sealed class RuntimeFilePathRowCloneStrategy : IDeepCloneStrategy<RuntimeFilePathRow>
	{
		public static RuntimeFilePathRowCloneStrategy Instance { get; } = new();

		public RuntimeFilePathRow Clone(in RuntimeFilePathRow value) => new()
		{
			StoreRecordId = value.StoreRecordId,
			Value = RuntimeFilePathCloneStrategy.Instance.Clone(value.Value)
		};
	}

	private sealed class RuntimeFilePathCloneStrategy : IDeepCloneStrategy<RuntimeFilePathDescriptor>
	{
		public static RuntimeFilePathCloneStrategy Instance { get; } = new();

		public RuntimeFilePathDescriptor Clone(in RuntimeFilePathDescriptor value) =>
			new(
				value.FilePathName,
				value.Description,
				value.Purpose,
				value.Source,
				value.IsPrimaryRecord,
				new ReadOnlyDictionary<string, string>(
					new Dictionary<string, string>(value.Annotations, StringComparer.OrdinalIgnoreCase)),
				value.RegisteredAt,
				value.UpdatedAt);
	}
}
