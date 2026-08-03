using Xunit;

namespace Iwesun.Runtime.Data.Tests;

public sealed class RecordStoreIndexModeParityTests
{
    [Fact]
    public void SortedAndHashIndexes_ShouldRemainEquivalentAcrossRandomMixedMutations()
    {
        var groupKey = new RecordKeyDefinition<Row, int>("group", static value => value.Group);
        var idKey = new RecordKeyDefinition<Row, int>("id", static value => value.BusinessId);
        var definition = new RecordStoreDefinition<Row, int>(
            [idKey, groupKey],
            new PrimaryKeyDefinition<Row, int>(["id"], static value => value.BusinessId));
        var sorted = new RecordStore<Row, int>(definition)
        {
            KeyIndexMode = RecordStoreKeyIndexMode.SortedTable
        };
        var hash = new RecordStore<Row, int>(definition)
        {
            KeyIndexMode = RecordStoreKeyIndexMode.Hash
        };
        var activeIds = new List<StoreRecordId>();
        var nextBusinessId = 1;
        var random = new Random(1729);

        for (var index = 0; index < 256; index++) AddOne(random.Next(32));
        AssertEquivalent();

        for (var operation = 0; operation < 2_000; operation++)
        {
            var action = random.Next(100);
            if (action < 35 || activeIds.Count == 0)
            {
                AddOne(random.Next(32));
            }
            else if (action < 70)
            {
                var id = activeIds[random.Next(activeIds.Count)];
                Assert.True(sorted.TryGetRecord(id, out var sortedValue));
                Assert.True(hash.TryGetRecord(id, out var hashValue));
                Assert.Equal(sortedValue, hashValue);
                var newGroup = random.Next(32);
                Assert.Equal(
                    sorted.TryUpdate(sortedValue with { Group = newGroup }),
                    hash.TryUpdate(hashValue with { Group = newGroup }));
            }
            else
            {
                var position = random.Next(activeIds.Count);
                var id = activeIds[position];
                Assert.Equal(sorted.TryDeprecate(id), hash.TryDeprecate(id));
                activeIds.RemoveAt(position);
            }

            if (operation % 50 == 0) AssertEquivalent();
        }
        AssertEquivalent();

        void AddOne(int group)
        {
            var value = new Row { BusinessId = nextBusinessId++, Group = group };
            var sortedId = sorted.Add(value);
            var hashId = hash.Add(value);
            Assert.Equal(sortedId, hashId);
            activeIds.Add(sortedId);
        }

        void AssertEquivalent()
        {
            Assert.Equal(hash.Count, sorted.Count);
            Assert.Equal(hash.GetKeys(groupKey), sorted.GetKeys(groupKey));
            Assert.Equal(hash.GetKeyCount(groupKey), sorted.GetKeyCount(groupKey));
            for (var group = 0; group < 32; group++)
            {
                Assert.Equal(hash.GetValueCount(groupKey, group), sorted.GetValueCount(groupKey, group));
                Assert.Equal(
                    hash.GetValues(groupKey, group).Select(static value => value.StoreRecordId),
                    sorted.GetValues(groupKey, group).Select(static value => value.StoreRecordId));
            }
        }
    }

    private record struct Row : IRecordStoreValue
    {
        public StoreRecordId StoreRecordId { get; set; }
        public int BusinessId { get; init; }
        public int Group { get; init; }
    }
}
