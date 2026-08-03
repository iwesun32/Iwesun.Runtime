using Xunit;
using System.Text.Json;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreConstraintMergeTests
{
    [Fact]
    public void AutoMerge_ShouldRequireMergeAddBeforeResolver()
    {
        var resolverCalls = 0;
        var store = CreateStore(
            new RecordStoreOptions(AutoMerge: true),
            mergeResolver: (existing, incoming) =>
            {
                resolverCalls++;
                return existing with { Value = existing.Value + incoming.Value };
            });
        store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 2 });

        Assert.False(store.TryAdd(new Row { Group = "a", Code = "1", Alias = "y", Value = 3 }, out _));
        Assert.Equal(0, resolverCalls);
        Assert.Equal(2, Assert.Single(store).Value);
    }

    [Fact]
    public void AutoMerge_ShouldDelegateUniqueConstraintConflictToDerivedStore()
    {
        var resolverCalls = 0;
        var store = CreateStore(
            new RecordStoreOptions(AutoMerge: true),
            mergeResolver: (existing, incoming) =>
            {
                resolverCalls++;
                return existing with
                {
                Value = existing.Value + incoming.Value
                };
            },
            configureMergeAdd: true);
        var existingId = store.Add(new Row { Group = "a", Code = "1", Alias = "same", Value = 2 });

        var mergedId = store.Add(new Row { Group = "b", Code = "2", Alias = "same", Value = 3 });

        Assert.Equal(existingId, mergedId);
        Assert.Equal(1, resolverCalls);
        Assert.Equal(5, Assert.Single(store).Value);
    }

    [Fact]
    public void AutoMerge_ShouldEvaluatePrimaryCandidateBeforeUniqueCandidate()
    {
        var evaluated = new List<int>();
        var store = CreateStore(
            new RecordStoreOptions(
                AllowPrimaryKeyDuplicate: true,
                AutoMerge: true,
                AllowUniqueConstraintViolation: true),
            mergeResolver: (existing, incoming) =>
            {
                evaluated.Add(existing.Value);
                return existing with { Value = existing.Value + incoming.Value };
            },
            configureMergeAdd: true);
        var primaryId = store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 10 });
        store.Add(new Row { Group = "b", Code = "2", Alias = "y", Value = 20 });

        var mergedId = store.Add(new Row { Group = "a", Code = "1", Alias = "y", Value = 1 });

        Assert.Equal(primaryId, mergedId);
        Assert.Equal([10], evaluated);
        Assert.Equal(2, store.Count);
        Assert.Equal(11, store.GetValues(new PrimaryKey("a", "1"))[0].Value);
    }

    [Fact]
    public void MergeAddFailure_ShouldContinueWithConfiguredConstraintPolicy()
    {
        var resolverCalls = 0;
        var store = CreateStore(
            new RecordStoreOptions(AllowPrimaryKeyDuplicate: true, AutoMerge: true),
            mergeResolver: (existing, incoming) =>
            {
                resolverCalls++;
                return existing with { Value = existing.Value + incoming.Value };
            },
            mergeAdd: static (Row _, out StoreRecordId id) =>
            {
                id = default;
                return false;
            });

        store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 2 });
        store.Add(new Row { Group = "a", Code = "1", Alias = "y", Value = 3 });

        Assert.Equal(0, resolverCalls);
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void MergeAddSuccess_ShouldRequireAnActiveRecordIdFromThisStore()
    {
        var store = CreateStore(
            new RecordStoreOptions(AutoMerge: true),
            mergeAdd: static (Row _, out StoreRecordId id) =>
            {
                id = new StoreRecordId(999);
                return true;
            });
        var existingId = store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 2 });

        Assert.False(store.TryAdd(new Row { Group = "a", Code = "1", Alias = "y", Value = 3 }, out _));
        Assert.True(store.TryGetRecord(existingId, out var existing));
        Assert.Equal(2, existing.Value);
        Assert.Single(store);
    }

    [Fact]
    public void LimitPredicate_ShouldApplyToUpdateMergePublishAndViewResults()
    {
        var store = CreateStore(
            new RecordStoreOptions(AutoMerge: true),
            mergeResolver: static (existing, incoming) => existing with
            {
                Value = existing.Value + incoming.Value
            },
            configureMergeAdd: true,
            limit: static (value, _) => value.Value <= 10);
        var firstId = store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 6 });
        Assert.True(store.TryGetRecord(firstId, out var first));

        Assert.Equal(RecordUpdateResult.Rejected, store.TryUpdate(first with { Value = 11 }));
        Assert.False(store.TryAdd(new Row { Group = "a", Code = "1", Alias = "x", Value = 5 }, out _));
        Assert.Equal(6, Assert.Single(store).Value);

        Assert.Throws<InvalidOperationException>(() => store.Publish(
            new[] { first, first },
            static values => values[0] with { Value = values.Sum(static value => value.Value) }));

        store.Add(new Row { Group = "b", Code = "2", Alias = "y", Value = 5 });
        Assert.Throws<InvalidOperationException>(() => store.CreateView(
            new RecordStoreViewDefinition<Row>(
                GroupComparison: static (_, _) => 0,
                GroupAggregate: static values => values[0] with
                {
                    Value = values.Sum(static value => value.Value)
                })));
    }

    [Fact]
    public void Restore_ShouldRejectLimitViolationsFromPersistedState()
    {
        var schemaId = $"constraint-limit-{Guid.NewGuid():N}.v2";
        var store = CreateStore(
            new RecordStoreOptions(),
            limit: static (value, _) => value.Value <= 10,
            schemaId: schemaId);
        store.Add(new Row { Group = "a", Code = "1", Alias = "x", Value = 6 });
        RecordStoreSchemaRegistry<Row, PrimaryKey>.Register(store);
        var invalidJson = store.ToJson().Replace("\"Value\":6", "\"Value\":11", StringComparison.Ordinal);

        Assert.Throws<System.Text.Json.JsonException>(() =>
            RecordStore<Row, PrimaryKey>.RestoreJson(invalidJson));
    }

    [Fact]
    public void MutablePoliciesAndUniqueConstraints_ShouldTakeEffectImmediatelyAndAtomically()
    {
        var store = CreateStore(new RecordStoreOptions());
        store.RemoveUniqueConstraint("alias-unique");
        store.RecordFilter = static value => value.Value > 0;
        store.Limit = static (_, current) => current.Count < 3;

        Assert.False(store.TryAdd(new Row { Group = "a", Code = "0", Alias = "z", Value = 0 }, out _));
        store.Add(new Row { Group = "a", Code = "1", Alias = "same", Value = 1 });
        store.Add(new Row { Group = "b", Code = "2", Alias = "same", Value = 2 });

        var unique = new UniqueConstraintDefinition<Row, string?>(
            "alias-unique", ["alias"], static value => value.Alias);
        Assert.Throws<InvalidOperationException>(() => store.AddUniqueConstraint(unique));
        Assert.Empty(store.UniqueConstraints);

        store.AllowUniqueConstraintViolation = true;
        store.AddUniqueConstraint(unique);
        Assert.Single(store.UniqueConstraints);
        Assert.Throws<InvalidOperationException>(() => store.AllowUniqueConstraintViolation = false);

        store.RecordFilter = static value => value.Value >= 2;
        Assert.False(store.TryAdd(new Row { Group = "c", Code = "3", Alias = "other", Value = 1 }, out _));
        store.RecordFilter = null;
        Assert.True(store.TryAdd(new Row { Group = "c", Code = "3", Alias = "other", Value = 3 }, out _));
        Assert.False(store.TryAdd(new Row { Group = "d", Code = "4", Alias = "last", Value = 4 }, out _));

        Assert.True(store.RemoveUniqueConstraint("alias-unique"));
        Assert.Empty(store.UniqueConstraints);
    }

    private static RecordStore<Row, PrimaryKey> CreateStore(
        RecordStoreOptions options,
        MergeResolver<Row>? mergeResolver = null,
        MergeAdd<Row>? mergeAdd = null,
        bool configureMergeAdd = false,
        LimitPredicate<Row>? limit = null,
        string schemaId = "constraint-merge.v2")
    {
        var group = new RecordKeyDefinition<Row, string?>("group", static value => value.Group);
        var code = new RecordKeyDefinition<Row, string?>("code", static value => value.Code);
        var alias = new RecordKeyDefinition<Row, string?>("alias", static value => value.Alias);
        var definition = new RecordStoreDefinition<Row, PrimaryKey>(
            [group, code, alias],
            new PrimaryKeyDefinition<Row, PrimaryKey>(
                ["group", "code"], static value => new PrimaryKey(value.Group, value.Code)));
        var store = new MergeTestStore(definition, alias)
        {
            SchemaId = schemaId,
            MergeResolver = mergeResolver,
            Limit = limit
        };
        store.MergeAdd = mergeAdd;
        store.ApplyOptions(options);
        store.SetUniqueConstraints(
            [new UniqueConstraintDefinition<Row, string?>(
                "alias-unique", ["alias"], static value => value.Alias)]);
        if (configureMergeAdd)
            store.MergeAdd = store.TryMergeIncoming;
        return store;
    }

    private sealed class MergeTestStore(
        RecordStoreDefinition<Row, PrimaryKey> definition,
        RecordKeyDefinition<Row, string?> aliasKey)
        : RecordStore<Row, PrimaryKey>(definition)
    {
        public bool TryMergeIncoming(Row incoming, out StoreRecordId recordId)
        {
            recordId = default;
            if (MergeResolver is null) return false;

            var primaryKey = Definition.PrimaryKey.GetPrimaryKey(incoming);
            var candidates = GetValues(primaryKey)
                .Concat(GetValues(aliasKey, incoming.Alias))
                .DistinctBy(static value => value.StoreRecordId);
            foreach (var existing in candidates)
            {
                var merged = MergeResolver.Resolve(existing, incoming);
                if (TryCommitMergedRecord(existing.StoreRecordId, merged, out recordId))
                    return true;
            }
            return false;
        }
    }

    private readonly record struct PrimaryKey(string? Group, string? Code) : IComparable<PrimaryKey>
    {
        public int CompareTo(PrimaryKey other)
        {
            var group = StringComparer.Ordinal.Compare(Group, other.Group);
            return group != 0 ? group : StringComparer.Ordinal.Compare(Code, other.Code);
        }
    }

    private record struct Row : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public string? Group { get; init; }
        public string? Code { get; init; }
        public string? Alias { get; init; }
        public int Value { get; init; }
    }
}
