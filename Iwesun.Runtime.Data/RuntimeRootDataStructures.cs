using System.Collections;

namespace Iwesun.Runtime.Data;

public readonly struct RuntimeRootEntryEnvelope :
	IEquatable<RuntimeRootEntryEnvelope>,
	IComparable<RuntimeRootEntryEnvelope>
{
	public RuntimeRootEntryEnvelope(
		string id,
		string primaryKey,
		IReadOnlyDictionary<string, string>? secondaryKeys = null,
		object? payload = null,
		DateTimeOffset? registeredAt = null,
		DateTimeOffset? updatedAt = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(id);
		ArgumentException.ThrowIfNullOrWhiteSpace(primaryKey);

		Id = id;
		PrimaryKey = primaryKey;
		SecondaryKeys = secondaryKeys ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		RegisteredAt = registeredAt ?? DateTimeOffset.UtcNow;
		UpdatedAt = updatedAt ?? RegisteredAt;
		Payload = payload;
	}

	public string Id { get; }
	public string PrimaryKey { get; }
	public IReadOnlyDictionary<string, string> SecondaryKeys { get; }
	public DateTimeOffset RegisteredAt { get; }
	public DateTimeOffset UpdatedAt { get; }
	public object? Payload { get; }

	public bool Equals(RuntimeRootEntryEnvelope other) =>
		Id.Equals(other.Id, StringComparison.OrdinalIgnoreCase)
		&& PrimaryKey.Equals(other.PrimaryKey, StringComparison.OrdinalIgnoreCase);

	public override bool Equals(object? obj) =>
		obj is RuntimeRootEntryEnvelope other && Equals(other);

	public override int GetHashCode() =>
		HashCode.Combine(
			StringComparer.OrdinalIgnoreCase.GetHashCode(Id),
			StringComparer.OrdinalIgnoreCase.GetHashCode(PrimaryKey));

	public int CompareTo(RuntimeRootEntryEnvelope other)
	{
		var primary = string.Compare(PrimaryKey, other.PrimaryKey, StringComparison.OrdinalIgnoreCase);
		if (primary != 0)
		{
			return primary;
		}

		return string.Compare(Id, other.Id, StringComparison.OrdinalIgnoreCase);
	}

	public static bool operator ==(RuntimeRootEntryEnvelope left, RuntimeRootEntryEnvelope right) => left.Equals(right);
	public static bool operator !=(RuntimeRootEntryEnvelope left, RuntimeRootEntryEnvelope right) => !left.Equals(right);
}

public sealed record RuntimeRootTableSnapshot(
	string TableName,
	int Count,
	IReadOnlyList<RuntimeRootEntryEnvelope> Entries);

public sealed record RuntimeRootSnapshot(
	DateTimeOffset GeneratedAt,
	IReadOnlyList<RuntimeRootTableSnapshot> Tables,
	IReadOnlyList<RuntimeFilePathDescriptor> FilePathDescriptors);

public readonly struct RuntimeFilePathDescriptor :
	IEquatable<RuntimeFilePathDescriptor>,
	IComparable<RuntimeFilePathDescriptor>
{
	public RuntimeFilePathDescriptor(
		string filePathName,
		string? description = null,
		string? purpose = null,
		string? source = null,
		bool isPrimaryRecord = false,
		IReadOnlyDictionary<string, string>? annotations = null,
		DateTimeOffset? registeredAt = null,
		DateTimeOffset? updatedAt = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePathName);

		FilePathName = filePathName.Trim();
		Description = description?.Trim() ?? string.Empty;
		Purpose = purpose?.Trim() ?? string.Empty;
		Source = source?.Trim() ?? string.Empty;
		IsPrimaryRecord = isPrimaryRecord;
		Annotations = annotations ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		RegisteredAt = registeredAt ?? DateTimeOffset.UtcNow;
		UpdatedAt = updatedAt ?? RegisteredAt;
	}

	public string FilePathName { get; }
	public string Description { get; }
	public string Purpose { get; }
	public string Source { get; }
	public bool IsPrimaryRecord { get; }
	public IReadOnlyDictionary<string, string> Annotations { get; }
	public DateTimeOffset RegisteredAt { get; }
	public DateTimeOffset UpdatedAt { get; }

	public RuntimeFilePathDescriptor NormalizeAs(bool isPrimaryRecord) =>
		new(
			FilePathName,
			Description,
			Purpose,
			Source,
			isPrimaryRecord,
			Annotations,
			RegisteredAt,
			DateTimeOffset.UtcNow);

	public bool Equals(RuntimeFilePathDescriptor other) =>
		FilePathName.Equals(other.FilePathName, StringComparison.OrdinalIgnoreCase);

	public override bool Equals(object? obj) =>
		obj is RuntimeFilePathDescriptor other && Equals(other);

	public override int GetHashCode() =>
		StringComparer.OrdinalIgnoreCase.GetHashCode(FilePathName);

	public int CompareTo(RuntimeFilePathDescriptor other)
	{
		if (IsPrimaryRecord && !other.IsPrimaryRecord)
		{
			return -1;
		}

		if (!IsPrimaryRecord && other.IsPrimaryRecord)
		{
			return 1;
		}

		return string.Compare(FilePathName, other.FilePathName, StringComparison.OrdinalIgnoreCase);
	}

	public static bool operator ==(RuntimeFilePathDescriptor left, RuntimeFilePathDescriptor right) => left.Equals(right);
	public static bool operator !=(RuntimeFilePathDescriptor left, RuntimeFilePathDescriptor right) => !left.Equals(right);
}

public interface IRuntimeRootAuxIndex
{
	string Name { get; }
	void Rebuild(IReadOnlyList<RuntimeRootEntryEnvelope> entries);
	IReadOnlyList<RuntimeRootEntryEnvelope> QueryPrefix(string keyPrefix, int take = 100);
}

public sealed class RuntimeRootSortedPrimaryKeyIndex : IRuntimeRootAuxIndex
{
	private readonly object _gate = new();
	private RuntimeRootEntryEnvelope[] _sorted = Array.Empty<RuntimeRootEntryEnvelope>();

	public string Name => "sorted-primary";

	public void Rebuild(IReadOnlyList<RuntimeRootEntryEnvelope> entries)
	{
		ArgumentNullException.ThrowIfNull(entries);
		lock (_gate)
		{
			_sorted = entries
				.OrderBy(x => x.PrimaryKey, StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}
	}

	public IReadOnlyList<RuntimeRootEntryEnvelope> QueryPrefix(string keyPrefix, int take = 100)
	{
		if (string.IsNullOrWhiteSpace(keyPrefix))
		{
			return Array.Empty<RuntimeRootEntryEnvelope>();
		}

		take = Math.Clamp(take, 1, 4096);
		lock (_gate)
		{
			if (_sorted.Length == 0)
			{
				return Array.Empty<RuntimeRootEntryEnvelope>();
			}

			var start = LowerBound(_sorted, keyPrefix);
			if (start >= _sorted.Length)
			{
				return Array.Empty<RuntimeRootEntryEnvelope>();
			}

			var result = new List<RuntimeRootEntryEnvelope>(Math.Min(take, 64));
			for (var i = start; i < _sorted.Length && result.Count < take; i++)
			{
				var current = _sorted[i];
				if (!current.PrimaryKey.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
				{
					break;
				}

				result.Add(current);
			}

			return result;
		}
	}

	private static int LowerBound(RuntimeRootEntryEnvelope[] entries, string key)
	{
		var left = 0;
		var right = entries.Length;
		while (left < right)
		{
			var mid = left + ((right - left) / 2);
			if (string.Compare(entries[mid].PrimaryKey, key, StringComparison.OrdinalIgnoreCase) < 0)
			{
				left = mid + 1;
			}
			else
			{
				right = mid;
			}
		}

		return left;
	}
}

public sealed class RuntimeDListNode<T>(T value)
{
	public T Value { get; set; } = value;
	public RuntimeDListNode<T>? Previous { get; internal set; }
	public RuntimeDListNode<T>? Next { get; internal set; }
}

public enum RuntimeDListAddOutcome
{
	Added,
	Merged,
	RejectedByFilter,
	DuplicateRejected
}

public sealed record RuntimeDListAddResult<T>(
	RuntimeDListAddOutcome Outcome,
	RuntimeDListNode<T>? Node,
	T Value);

public sealed record RuntimeDListBatchAddResult(
	int Total,
	int Added,
	int Merged,
	int RejectedByFilter,
	int DuplicateRejected);

public class RuntimeDList<T> : IEnumerable<T>, IReadOnlyCollection<T>
{
	private RuntimeDListNode<T>? _head;
	private RuntimeDListNode<T>? _tail;
	public int Count { get; private set; }
	public bool AllowDuplicates { get; set; } = true;
	public bool MergeOnDuplicate { get; set; }
	public Func<T, T, bool>? DuplicatePredicate { get; set; }
	public Func<T, bool>? FilterPredicate { get; set; }
	public Func<T, T, T>? MergeDelegate { get; set; }

	public virtual RuntimeDListNode<T> AddLast(T value)
	{
		var result = TryAdd(value);
		if (result.Node != null)
		{
			return result.Node;
		}

		throw new InvalidOperationException("Entry was rejected by DList filter predicate.");
	}

	public virtual RuntimeDListAddResult<T> TryAdd(T value)
	{
		if (!ShouldAccept(value))
		{
			return new RuntimeDListAddResult<T>(RuntimeDListAddOutcome.RejectedByFilter, null, value);
		}

		var duplicateNode = FindFirstDuplicateNode(value);
		if (duplicateNode != null && !AllowDuplicates)
		{
			if (MergeOnDuplicate && TryMergeDuplicate(duplicateNode.Value, value, out var merged))
			{
				duplicateNode.Value = merged;
				return new RuntimeDListAddResult<T>(RuntimeDListAddOutcome.Merged, duplicateNode, duplicateNode.Value);
			}

			return new RuntimeDListAddResult<T>(RuntimeDListAddOutcome.DuplicateRejected, duplicateNode, duplicateNode.Value);
		}

		var node = AddNodeRaw(value);
		return new RuntimeDListAddResult<T>(RuntimeDListAddOutcome.Added, node, value);
	}

	public virtual RuntimeDListBatchAddResult AddRange(IEnumerable<T> values)
	{
		ArgumentNullException.ThrowIfNull(values);

		var total = 0;
		var added = 0;
		var merged = 0;
		var filtered = 0;
		var duplicateRejected = 0;

		foreach (var value in values)
		{
			total++;
			var result = TryAdd(value);
			switch (result.Outcome)
			{
				case RuntimeDListAddOutcome.Added:
					added++;
					break;
				case RuntimeDListAddOutcome.Merged:
					merged++;
					break;
				case RuntimeDListAddOutcome.RejectedByFilter:
					filtered++;
					break;
				case RuntimeDListAddOutcome.DuplicateRejected:
					duplicateRejected++;
					break;
			}
		}

		return new RuntimeDListBatchAddResult(total, added, merged, filtered, duplicateRejected);
	}

	public bool Remove(RuntimeDListNode<T> node)
	{
		ArgumentNullException.ThrowIfNull(node);
		if (Count == 0)
		{
			return false;
		}

		var previous = node.Previous;
		var next = node.Next;

		if (previous != null)
		{
			previous.Next = next;
		}
		else
		{
			_head = next;
		}

		if (next != null)
		{
			next.Previous = previous;
		}
		else
		{
			_tail = previous;
		}

		node.Previous = null;
		node.Next = null;
		Count--;
		return true;
	}

	public void Clear()
	{
		_head = null;
		_tail = null;
		Count = 0;
	}

	public bool Contains(T value, IEqualityComparer<T>? comparer = null)
	{
		comparer ??= EqualityComparer<T>.Default;
		var current = _head;
		while (current != null)
		{
			if (comparer.Equals(current.Value, value))
			{
				return true;
			}

			current = current.Next;
		}

		return false;
	}

	public bool RemoveFirst(T value, IEqualityComparer<T>? comparer = null)
	{
		comparer ??= EqualityComparer<T>.Default;
		var current = _head;
		while (current != null)
		{
			if (comparer.Equals(current.Value, value))
			{
				return Remove(current);
			}

			current = current.Next;
		}

		return false;
	}

	public virtual void Sort(Comparison<T> comparison)
	{
		ArgumentNullException.ThrowIfNull(comparison);
		var ordered = SortCore(ToArraySnapshot(), comparison);
		RebuildFromOrdered(ordered);
	}

	public virtual void Sort(IComparer<T> comparer)
	{
		ArgumentNullException.ThrowIfNull(comparer);
		Sort(comparer.Compare);
	}

	public IReadOnlyList<T> ToArraySnapshot()
	{
		var list = new List<T>(Count);
		foreach (var item in this)
		{
			list.Add(item);
		}

		return list;
	}

	public IEnumerator<T> GetEnumerator()
	{
		var current = _head;
		while (current != null)
		{
			yield return current.Value;
			current = current.Next;
		}
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	private RuntimeDListNode<T> AddNodeRaw(T value)
	{
		var node = new RuntimeDListNode<T>(value);
		if (_tail == null)
		{
			_head = node;
			_tail = node;
		}
		else
		{
			node.Previous = _tail;
			_tail.Next = node;
			_tail = node;
		}

		Count++;
		return node;
	}

	protected virtual RuntimeDListNode<T>? FindFirstDuplicateNode(T value)
	{
		var current = _head;
		while (current != null)
		{
			if (IsDuplicate(current.Value, value))
			{
				return current;
			}

			current = current.Next;
		}

		return null;
	}

	protected virtual bool IsDuplicate(T left, T right)
	{
		if (DuplicatePredicate != null)
		{
			return DuplicatePredicate(left, right);
		}

		return EqualityComparer<T>.Default.Equals(left, right);
	}

	protected virtual bool ShouldAccept(T value)
	{
		if (FilterPredicate != null)
		{
			return FilterPredicate(value);
		}

		return true;
	}

	protected virtual bool TryMergeDuplicate(T existing, T incoming, out T merged)
	{
		if (MergeDelegate != null)
		{
			merged = MergeDelegate(existing, incoming);
			return true;
		}

		merged = existing;
		return false;
	}

	protected virtual IReadOnlyList<T> SortCore(IReadOnlyList<T> values, Comparison<T> comparison)
	{
		var list = values.ToList();
		list.Sort(comparison);
		return list;
	}

	protected virtual void RebuildFromOrdered(IReadOnlyList<T> values)
	{
		Clear();
		foreach (var value in values)
		{
			AddNodeRaw(value);
		}
	}
}
