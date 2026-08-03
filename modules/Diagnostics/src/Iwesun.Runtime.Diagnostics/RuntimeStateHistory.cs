using Iwesun.Runtime.Data;

namespace Iwesun.Runtime.Diagnostics;

internal sealed class RuntimeStateHistory
{
	private static readonly RecordStoreDefinition<StoredRuntimeState, long> Definition = CreateDefinition();

	private readonly object _gate = new();
	private RecordStore<StoredRuntimeState, long> _store = CreateStore();
	private readonly int _capacity;
	private long _nextSequence;

	public RuntimeStateHistory(int capacity = 128)
	{
		if (capacity <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(capacity));
		}

		_capacity = capacity;
	}

	public void Append(RuntimeState state)
	{
		lock (_gate)
		{
			_store.Add(new StoredRuntimeState
			{
				Sequence = checked(++_nextSequence),
				State = CloneState(state)
			});
			while (_store.Count > _capacity)
			{
				var oldestId = _store.First().StoreRecordId;
				if (!_store.TryDeprecate(oldestId))
				{
					break;
				}
			}
		}
	}

	public void Clear()
	{
		lock (_gate)
		{
			_store = CreateStore();
			_nextSequence = 0;
		}
	}

	public IReadOnlyList<RuntimeState> Snapshot()
	{
		lock (_gate)
		{
			return _store.Select(static row => CloneState(row.State)).ToArray();
		}
	}

	private static RecordStoreDefinition<StoredRuntimeState, long> CreateDefinition()
	{
		var sequence = new RecordKeyDefinition<StoredRuntimeState, long>(
			"sequence", static row => row.Sequence);
		var code = new RecordKeyDefinition<StoredRuntimeState, int>(
			"code", static row => row.State.Code);
		return new RecordStoreDefinition<StoredRuntimeState, long>(
			[sequence, code],
			new PrimaryKeyDefinition<StoredRuntimeState, long>(["sequence"], static row => row.Sequence));
	}

	private static RecordStore<StoredRuntimeState, long> CreateStore() =>
		new(Definition) { CloneStrategy = StoredRuntimeStateCloneStrategy.Instance };

	private static RuntimeState CloneState(in RuntimeState value) =>
		new(
			value.Code,
			value.Name,
			value.Group,
			value.DisplayName,
			value.Level,
			value.Key,
			value.ParentCode);

	private struct StoredRuntimeState : IRecordStoreValue
	{
		public StoreRecordId StoreRecordId { get; set; }
		public long Sequence { get; init; }
		public RuntimeState State { get; init; }
	}

	private sealed class StoredRuntimeStateCloneStrategy : IDeepCloneStrategy<StoredRuntimeState>
	{
		public static StoredRuntimeStateCloneStrategy Instance { get; } = new();

		public StoredRuntimeState Clone(in StoredRuntimeState value) => new()
		{
			StoreRecordId = value.StoreRecordId,
			Sequence = value.Sequence,
			State = CloneState(value.State)
		};
	}
}
