namespace Iwesun.Runtime.Data;

public readonly record struct StoreRecordId(long Value)
{
    public override string ToString() =>
        Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Provides optional cooperative serialization for callers that must access one Source store
/// from more than one asynchronous execution flow. RecordStore does not acquire this gate automatically.
/// </summary>
public sealed class RecordStoreAccessGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private int _isEntered;

    public bool IsEntered => Volatile.Read(ref _isEntered) != 0;

    public RecordStoreAccessLease Enter(CancellationToken cancellationToken = default)
    {
        _semaphore.Wait(cancellationToken);
        Volatile.Write(ref _isEntered, 1);
        return new RecordStoreAccessLease(this);
    }

    public async ValueTask<RecordStoreAccessLease> EnterAsync(
        CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _isEntered, 1);
        return new RecordStoreAccessLease(this);
    }

    internal void Release()
    {
        Volatile.Write(ref _isEntered, 0);
        _semaphore.Release();
    }
}

public sealed class RecordStoreAccessLease : IDisposable, IAsyncDisposable
{
    private RecordStoreAccessGate? _gate;

    internal RecordStoreAccessLease(RecordStoreAccessGate gate) => _gate = gate;

    public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

public interface IDeepCloneStrategy<TValue>
{
    TValue Clone(in TValue value);
}

public interface ITableValueCodec<TValue>
{
    byte[] Encode(in TValue value);
    TValue Decode(ReadOnlySpan<byte> data);
}

public sealed class ValueCopyCloneStrategy<TValue> : IDeepCloneStrategy<TValue>
{
    public static ValueCopyCloneStrategy<TValue> Instance { get; } = new();

    private ValueCopyCloneStrategy()
    {
    }

    public TValue Clone(in TValue value)
    {
        if (!typeof(TValue).IsValueType)
            throw new InvalidOperationException(
                $"Reference record type '{typeof(TValue).FullName}' requires an explicit deep clone strategy.");
        return value;
    }
}
