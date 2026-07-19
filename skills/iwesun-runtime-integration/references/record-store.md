# RecordStore V2 integration boundary

Runtime uses two distinct assemblies:

- `Iwesun.Runtime.Data.dll` owns Runtime-specific DTOs, injector catalogs, and fixed value instructions.
- `Iwesun.Data.dll` owns `RecordStoreV2<TValue,TPrimaryKey>`, definitions, record IDs, constraints,
  cloning, publication, views, serialization, and the optional access gate.

Source consumers reference `D:\Git Space\Data\Iwesun.Data\Iwesun.Data.csproj`. Installed consumers use:

```text
C:\Program Files\Iwesun\Runtime\lib\Iwesun.Data\Iwesun.Data.dll
```

Do not copy the DLL from another host output. The release verifier and installed samples use the library from the
Runtime installation tree. V2 remains an isolated type until its memory and performance promotion gates pass;
do not rename it to `RecordStore` in consumer code or documentation.

Runtime no longer owns `RuntimeDList<T>`. Upstream DList types and their JSON converter are absent from the active
assembly. Migration rules:

1. define all indexed keys and the business primary key in `RecordStoreDefinition<TValue,TPrimaryKey>`;
2. keep mutable Filter, Limit, Merge, unique-constraint, and Publish policies on the Store instance;
3. retain `StoreRecordId` and use read-modify-write for Update or explicit deprecation;
4. provide a complete `IDeepCloneStrategy<TValue>` when a struct contains mutable references;
5. keep Source mutations in one logical writer; other flows consume completed Snapshots;
6. use `AccessGate` leases only for exceptional cooperative cross-flow access, not as an implicit lock;
7. do not recreate DList node references, Insert, positional mutation, implicit keyed deletion, or in-place sorting;
8. follow `DLIST_TO_RECORD_STORE_V2_MIGRATION.md` or `RECORD_STORE_1_0_25_TO_V2_MIGRATION.md`.

Key semantics:

- ordinary Keys are indexed and may repeat;
- the business primary key is one or more Keys and is unique unless `AllowPrimaryKeyDuplicate` is enabled;
- `MergePredicate` decides whether two candidates merge; `MergeResolver` performs the binary merge;
- Publish conflict aggregation uses `ConflictAggregator(IReadOnlyList<TValue>)`, not the Add merge delegate;
- Filter and Limit are general Store constraints, not Add-only callbacks;
- stable order, `StoreRecordId`, value ownership, and Snapshot boundaries must remain intact.
