namespace Iwesun.Runtime.Data;

/// <summary>
/// Defines deterministic execution semantics for RecordStore multicast delegates.
/// </summary>
public static class RecordStoreDelegateChains
{
    public static TValue Format<TValue>(
        this ValueFormatter<TValue> chain,
        TValue value)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var current = value;
        foreach (ValueFormatter<TValue> formatter in chain.GetInvocationList())
            current = formatter(current);
        return current;
    }

    public static bool Accept<TValue>(
        this RecordFilter<TValue> chain,
        TValue value)
    {
        ArgumentNullException.ThrowIfNull(chain);
        foreach (RecordFilter<TValue> filter in chain.GetInvocationList())
            if (!filter(value)) return false;
        return true;
    }

    public static TValue Resolve<TValue>(
        this MergeResolver<TValue> chain,
        TValue existing,
        TValue incoming)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var current = existing;
        foreach (MergeResolver<TValue> resolver in chain.GetInvocationList())
            current = resolver(current, incoming);
        return current;
    }

    public static bool TryMerge<TValue>(
        this MergeAdd<TValue> chain,
        TValue incoming,
        out StoreRecordId recordId)
    {
        ArgumentNullException.ThrowIfNull(chain);
        foreach (MergeAdd<TValue> merge in chain.GetInvocationList())
            if (merge(incoming, out recordId)) return true;
        recordId = default;
        return false;
    }

    public static bool Accept<TValue>(
        this LimitPredicate<TValue> chain,
        TValue value,
        IReadOnlyList<TValue> current)
    {
        ArgumentNullException.ThrowIfNull(chain);
        foreach (LimitPredicate<TValue> limit in chain.GetInvocationList())
            if (!limit(value, current)) return false;
        return true;
    }

    public static bool Accept<TValue>(
        this PublishSourceFilter<TValue> chain,
        TValue value)
    {
        ArgumentNullException.ThrowIfNull(chain);
        foreach (PublishSourceFilter<TValue> filter in chain.GetInvocationList())
            if (!filter(value)) return false;
        return true;
    }

    public static bool Accept<TValue>(
        this PublishResultFilter<TValue> chain,
        TValue value)
    {
        ArgumentNullException.ThrowIfNull(chain);
        foreach (PublishResultFilter<TValue> filter in chain.GetInvocationList())
            if (!filter(value)) return false;
        return true;
    }

    public static int Compare<TValue>(
        this PublishResultComparison<TValue> chain,
        TValue left,
        TValue right)
    {
        ArgumentNullException.ThrowIfNull(chain);
        foreach (PublishResultComparison<TValue> comparison in chain.GetInvocationList())
        {
            var result = comparison(left, right);
            if (result != 0) return result;
        }
        return 0;
    }

    public static bool Accept<TValue>(
        this ViewSourceFilter<TValue> chain,
        TValue value)
    {
        ArgumentNullException.ThrowIfNull(chain);
        foreach (ViewSourceFilter<TValue> filter in chain.GetInvocationList())
            if (!filter(value)) return false;
        return true;
    }

    public static int Compare<TValue>(
        this ViewGroupComparison<TValue> chain,
        TValue left,
        TValue right)
    {
        ArgumentNullException.ThrowIfNull(chain);
        foreach (ViewGroupComparison<TValue> comparison in chain.GetInvocationList())
        {
            var result = comparison(left, right);
            if (result != 0) return result;
        }
        return 0;
    }

    public static bool Accept<TValue>(
        this ViewResultFilter<TValue> chain,
        TValue value)
    {
        ArgumentNullException.ThrowIfNull(chain);
        foreach (ViewResultFilter<TValue> filter in chain.GetInvocationList())
            if (!filter(value)) return false;
        return true;
    }

    public static int Compare<TValue>(
        this ViewResultComparison<TValue> chain,
        TValue left,
        TValue right)
    {
        ArgumentNullException.ThrowIfNull(chain);
        foreach (ViewResultComparison<TValue> comparison in chain.GetInvocationList())
        {
            var result = comparison(left, right);
            if (result != 0) return result;
        }
        return 0;
    }

    public static TValue Aggregate<TValue>(
        this Aggregate<TValue> chain,
        IReadOnlyList<TValue> values)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
            throw new ArgumentException("An Aggregate delegate chain requires at least one value.", nameof(values));

        TValue result = default!;
        var current = values;
        foreach (Aggregate<TValue> aggregate in chain.GetInvocationList())
        {
            result = aggregate(current);
            current = [result];
        }
        return result;
    }

    internal static void EnsureSingleSelector(Delegate selector, string parameterName)
    {
        if (selector.GetInvocationList().Length != 1)
            throw new ArgumentException(
                "Key, PrimaryKey, and unique-constraint selectors must contain exactly one delegate.",
                parameterName);
    }
}
