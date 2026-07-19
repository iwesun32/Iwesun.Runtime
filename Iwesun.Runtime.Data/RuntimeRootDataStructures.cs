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
