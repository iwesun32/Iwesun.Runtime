namespace Iwesun.Runtime.Data;

public partial class RecordStore<TValue, TPrimaryKey>
    where TValue : IRecordStoreValue
    where TPrimaryKey : notnull
{
    public RecordStore<TValue, TPrimaryKey> CreateView(
        RecordStoreViewDefinition<TValue> definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        return BuildView(CaptureViewValues(cancellationToken), definition, cancellationToken);
    }

    public Task<RecordStore<TValue, TPrimaryKey>> CreateViewAsync(
        RecordStoreViewDefinition<TValue> definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        var captured = CaptureViewValues(cancellationToken);
        return Task.Run(() => BuildView(captured, definition, cancellationToken), cancellationToken);
    }

    public TResult[] CreateView<TResult>(
        RecordStoreResultViewDefinition<TValue, TResult> definition,
        CancellationToken cancellationToken = default)
        where TResult : struct
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        return BuildResultView(CaptureViewValues(cancellationToken), definition, cancellationToken);
    }

    public Task<TResult[]> CreateViewAsync<TResult>(
        RecordStoreResultViewDefinition<TValue, TResult> definition,
        CancellationToken cancellationToken = default)
        where TResult : struct
    {
        ArgumentNullException.ThrowIfNull(definition);
        definition.Validate();
        var captured = CaptureViewValues(cancellationToken);
        return Task.Run(() => BuildResultView(captured, definition, cancellationToken), cancellationToken);
    }

    public RecordStore<TValue, TPrimaryKey> DeepClone()
    {
        var clone = CreateEmptyOutput(RecordStoreOrigin.Clone);
        CopyMutableConfigurationTo(clone);
        ConfigureDerivedOutputStore(clone, RecordStoreOrigin.Clone);
        foreach (var slot in _slots)
            if (slot.IsActive) clone.AppendClonedValue(slot.Value);
        clone.DataVersion = DataVersion;
        return clone;
    }

    private TValue[] CaptureViewValues(CancellationToken cancellationToken)
    {
        var values = new TValue[Count];
        var outputIndex = 0;
        foreach (var slot in _slots)
        {
            if (!slot.IsActive) continue;
            cancellationToken.ThrowIfCancellationRequested();
            values[outputIndex++] = Clone(slot.Value);
        }
        return values;
    }

    private RecordStore<TValue, TPrimaryKey> BuildView(
        TValue[] source,
        RecordStoreViewDefinition<TValue> definition,
        CancellationToken cancellationToken)
    {
        var values = source.Select((value, index) => new ViewValue(value, index))
            .Where(value => definition.SourceFilter is null || definition.SourceFilter.Accept(value.Value))
            .ToList();

        if (definition.GroupComparison is not null && definition.GroupAggregate is not null)
        {
            var groupComparisonCalls = 0;
            SortWithCancellation(values, (left, right) =>
            {
                if ((++groupComparisonCalls & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                var order = definition.GroupComparison.Compare(Clone(left.Value), Clone(right.Value));
                return order != 0 ? order : left.SourceIndex.CompareTo(right.SourceIndex);
            });
            var grouped = new List<ViewValue>();
            for (var start = 0; start < values.Count;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var end = start + 1;
                while (end < values.Count
                       && definition.GroupComparison.Compare(
                           Clone(values[start].Value),
                           Clone(values[end].Value)) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    end++;
                }
                var group = values.GetRange(start, end - start).Select(value => Clone(value.Value)).ToArray();
                grouped.Add(new ViewValue(
                    definition.GroupAggregate.Aggregate(group),
                    values[start].SourceIndex));
                start = end;
            }
            values = grouped;
        }

        if (definition.ResultFilter is not null)
            values.RemoveAll(value => !definition.ResultFilter.Accept(Clone(value.Value)));
        if (definition.ResultComparison is not null)
        {
            var resultComparisonCalls = 0;
            SortWithCancellation(values, (left, right) =>
            {
                if ((++resultComparisonCalls & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                var order = definition.ResultComparison.Compare(Clone(left.Value), Clone(right.Value));
                return order != 0 ? order : left.SourceIndex.CompareTo(right.SourceIndex);
            });
        }

        return CreateOutput(
            values.Select(static value => value.Value),
            RecordStoreOrigin.View,
            cancellationToken);
    }

    private TResult[] BuildResultView<TResult>(
        TValue[] source,
        RecordStoreResultViewDefinition<TValue, TResult> definition,
        CancellationToken cancellationToken)
        where TResult : struct
    {
        var sourceValues = new List<ViewValue>(source.Length);
        for (var index = 0; index < source.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = source[index];
            if (definition.SourceFilter is null || definition.SourceFilter.Accept(value))
                sourceValues.Add(new ViewValue(value, index));
        }

        var cloneStrategy = definition.ResultCloneStrategy ?? ValueCopyCloneStrategy<TResult>.Instance;
        var results = new List<ResultViewValue<TResult>>(sourceValues.Count);
        if (definition.Projector is not null)
        {
            foreach (var sourceValue in sourceValues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var projected = definition.Projector(Clone(sourceValue.Value));
                results.Add(new ResultViewValue<TResult>(cloneStrategy.Clone(projected), sourceValue.SourceIndex));
            }
        }
        else
        {
            var comparison = definition.GroupComparison!;
            var aggregate = definition.GroupAggregate!;
            var groupComparisonCalls = 0;
            SortWithCancellation(sourceValues, (left, right) =>
            {
                if ((++groupComparisonCalls & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                var order = comparison.Compare(Clone(left.Value), Clone(right.Value));
                return order != 0 ? order : left.SourceIndex.CompareTo(right.SourceIndex);
            });
            for (var start = 0; start < sourceValues.Count;)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var end = start + 1;
                while (end < sourceValues.Count
                       && comparison.Compare(
                           sourceValues[start].Value,
                           sourceValues[end].Value) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    end++;
                }
                var group = new TValue[end - start];
                for (var index = start; index < end; index++)
                    group[index - start] = Clone(sourceValues[index].Value);
                var aggregated = aggregate(group);
                results.Add(new ResultViewValue<TResult>(
                    cloneStrategy.Clone(aggregated), sourceValues[start].SourceIndex));
                start = end;
            }
        }

        if (definition.ResultFilter is not null)
            results.RemoveAll(value => !definition.ResultFilter.Accept(cloneStrategy.Clone(value.Value)));
        if (definition.ResultComparison is not null)
        {
            var resultComparisonCalls = 0;
            SortWithCancellation(results, (left, right) =>
            {
                if ((++resultComparisonCalls & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                var order = definition.ResultComparison.Compare(
                    cloneStrategy.Clone(left.Value),
                    cloneStrategy.Clone(right.Value));
                return order != 0 ? order : left.SourceIndex.CompareTo(right.SourceIndex);
            });
        }

        var output = new TResult[results.Count];
        for (var index = 0; index < results.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output[index] = cloneStrategy.Clone(results[index].Value);
        }
        return output;
    }

    private readonly record struct ViewValue(TValue Value, int SourceIndex);
    private readonly record struct ResultViewValue<TResult>(TResult Value, int SourceIndex)
        where TResult : struct;

    private static void SortWithCancellation<T>(
        List<T> values,
        RecordStoreSortComparison<T> comparison)
    {
        try
        {
            values.Sort((left, right) => comparison(left, right));
        }
        catch (InvalidOperationException exception)
            when (exception.InnerException is OperationCanceledException cancellation)
        {
            throw cancellation;
        }
    }
}
