# RecordStore integration boundary

Runtime uses one data assembly and one namespace. `Iwesun.Runtime.Data.dll` / `Iwesun.Runtime.Data` owns both
Runtime-specific data contracts and `RecordStore<TValue,TPrimaryKey>` with its definitions, record IDs,
constraints, cloning, publication, views, serialization, and optional access gate.

Source consumers reference `D:\Git Space\Runtime\modules\Data\src\Iwesun.Runtime.Data\Iwesun.Runtime.Data.csproj`. Installed
consumers use:

```text
C:\Program Files\Iwesun\Runtime\lib\Iwesun.Runtime.Data\Iwesun.Runtime.Data.dll
```

Do not copy the DLL from another host output. The release verifier and installed samples use the library from the
Runtime installation tree. RecordStore is the only active implementation; use the public type name
`RecordStore` directly and do not introduce a version-suffixed compatibility alias in consumer code or documentation.

`Iwesun.Data.dll`, the `Iwesun.Data` namespace, DList, and RecordStore V1 are retired without forwarding types or
aliases. Their ignored archive is historical evidence only and never participates in builds or releases. Migration rules:

1. define all indexed keys and the business primary key in `RecordStoreDefinition<TValue,TPrimaryKey>`;
2. keep mutable Filter, Limit, Merge, unique-constraint, and Publish policies on the Store instance;
3. retain `StoreRecordId` and use read-modify-write for Update or explicit deprecation;
4. provide a complete `IDeepCloneStrategy<TValue>` when a struct contains mutable references;
5. keep Source mutations in one logical writer; other flows consume completed Snapshots;
6. use `AccessGate` leases only for exceptional cooperative cross-flow access, not as an implicit lock;
7. use only the RecordStore public API documented by `RECORD_STORE_PUBLIC_API.md`.

Key semantics:

- ordinary Keys are indexed and may repeat;
- the business primary key is one or more Keys and is unique unless `AllowPrimaryKeyDuplicate` is enabled;
- `RecordStore` detects actual primary-key and unique-constraint conflicts; when `AutoMerge` is enabled it
  calls the derived store's `MergeAdd(incoming)` once;
- `MergeAdd` owns candidate selection and business completeness; `MergeResolver` only produces
  `C = Merge(A,B)`, and the derived store uses the protected atomic commit primitive to preserve indexes;
- a successful `MergeAdd` must return an active `StoreRecordId` from the same Source; the base validates only
  that ownership boundary and does not reinterpret derived-table business logic;
- Publish conflict aggregation uses `ConflictAggregator(IReadOnlyList<TValue>)`, not the Add merge delegate;
- Filter and Limit are general Store constraints, not Add-only callbacks;
- stable order, `StoreRecordId`, value ownership, and Snapshot boundaries must remain intact.
