namespace Iwesun.Runtime.SampleHost;

public sealed class SampleHostRandomState
{
	private readonly object _gate = new();
	private int _currentValue;
	private int _previousValue;
	private int _minimumValue = int.MaxValue;
	private int _maximumValue = int.MinValue;
	private long _sampleCount;
	private DateTimeOffset _updatedAt = DateTimeOffset.UtcNow;

	public event EventHandler<SampleHostRandomUpdatedEventArgs>? Updated;

	public int CurrentValue => Snapshot().CurrentValue;
	public int PreviousValue => Snapshot().PreviousValue;
	public int MinimumValue => Snapshot().MinimumValue;
	public int MaximumValue => Snapshot().MaximumValue;
	public long SampleCount => Snapshot().SampleCount;
	public DateTimeOffset UpdatedAt => Snapshot().UpdatedAt;

	public void Record(int value)
	{
		SampleHostRandomSnapshot snapshot;
		lock (_gate)
		{
			_previousValue = _sampleCount == 0 ? value : _currentValue;
			_currentValue = value;
			_minimumValue = Math.Min(_minimumValue, value);
			_maximumValue = Math.Max(_maximumValue, value);
			_sampleCount++;
			_updatedAt = DateTimeOffset.UtcNow;
			snapshot = CreateSnapshot();
		}

		Updated?.Invoke(this, new SampleHostRandomUpdatedEventArgs(snapshot));
	}

	public SampleHostRandomSnapshot Snapshot()
	{
		lock (_gate)
		{
			return CreateSnapshot();
		}
	}

	public SampleHostRandomSnapshot RecordValidationSample(int value)
	{
		Record(value);
		return Snapshot();
	}

	private SampleHostRandomSnapshot CreateSnapshot() => new(
		_currentValue,
		_previousValue,
		_sampleCount == 0 ? 0 : _minimumValue,
		_sampleCount == 0 ? 0 : _maximumValue,
		_sampleCount,
		_updatedAt);
}

public sealed record SampleHostRandomSnapshot(
	int CurrentValue,
	int PreviousValue,
	int MinimumValue,
	int MaximumValue,
	long SampleCount,
	DateTimeOffset UpdatedAt);

public sealed record SampleHostRandomUpdatedEventArgs(SampleHostRandomSnapshot Snapshot);
