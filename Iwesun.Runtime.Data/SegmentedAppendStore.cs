using System.Collections;

namespace Iwesun.Runtime.Data;

internal sealed class SegmentedAppendStore<T> : IEnumerable<T>
{
    private readonly int _segmentSize;
    private readonly List<T[]> _segments = [];
    public SegmentedAppendStore(int segmentSize = 1024)
    {
        if (segmentSize < 16) throw new ArgumentOutOfRangeException(nameof(segmentSize));
        _segmentSize = segmentSize;
    }

    public int Count { get; private set; }
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            return _segments[index / _segmentSize][index % _segmentSize];
        }
        set
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            _segments[index / _segmentSize][index % _segmentSize] = value;
        }
    }

    public int Add(T item)
    {
        var index = Count;
        var segmentIndex = Count / _segmentSize;
        var offset = Count % _segmentSize;
        if (segmentIndex == _segments.Count) _segments.Add(new T[_segmentSize]);
        _segments[segmentIndex][offset] = item;
        Count++;
        return index;
    }

    public void EnsureAdditionalCapacity(int additionalCount)
    {
        if (additionalCount < 0) throw new ArgumentOutOfRangeException(nameof(additionalCount));
        if (additionalCount == 0) return;
        var requiredCount = checked(Count + additionalCount);
        var requiredSegments = (requiredCount + _segmentSize - 1) / _segmentSize;
        _segments.EnsureCapacity(requiredSegments);
        while (_segments.Count < requiredSegments)
            _segments.Add(new T[_segmentSize]);
    }

    public ref T GetReference(int index)
    {
        if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
        return ref _segments[index / _segmentSize][index % _segmentSize];
    }

    public int RemoveAll(Predicate<T> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var kept = new List<T>(Count);
        foreach (var item in this) if (!predicate(item)) kept.Add(item);
        var removed = Count - kept.Count;
        if (removed == 0) return 0;
        Clear();
        foreach (var item in kept) Add(item);
        return removed;
    }

    public void Clear()
    {
        _segments.Clear();
        Count = 0;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
            yield return _segments[index / _segmentSize][index % _segmentSize];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
