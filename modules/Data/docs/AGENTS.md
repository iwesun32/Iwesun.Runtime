# Runtime Data validation repository — AI Agent Instructions

This file is the entry point for AI coding agents working in this repository.

## Quick Start

- **Build**: `dotnet build modules/Data/Iwesun.Runtime.Data.slnx -c Release`
- **Test**: `dotnet test modules/Data/tests/Iwesun.Runtime.Data.Tests/Iwesun.Runtime.Data.Tests.csproj -c Debug`
- **Scale test**: `dotnet run --project modules/Data/tools/Iwesun.Runtime.Data.ScaleTest/Iwesun.Runtime.Data.ScaleTest.csproj -c Release -- --target-mb 128`
- **Language**: C# (.NET 10), `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`
- **Namespace**: `Iwesun.Runtime.Data`
- **Assembly**: `Iwesun.Runtime.Data` (source: `D:\Git Space\Runtime\modules\Data\src\Iwesun.Runtime.Data`)
- **Private fields**: `_camelCase` with underscore prefix
- **Language policy**:
  - Code identifiers, comments, XML docs → **English**
  - Design docs (`docs/`) → **中文 (Chinese)**
  - AI instructions (`AGENTS.md`) → **English**

## Purpose

This repository validates and documents `Iwesun.Runtime.Data.RecordStore<TValue,TPrimaryKey>`.
The active source and only released data assembly live in the Runtime repository. DList and RecordStore V1
are retired in the ignored `archive/recordstore-v1` tree and must never re-enter a project or release.

## Critical Rules

1. **Zero business coupling.** This library must depend only on `System.*`. Never add a
   `ProjectReference` or `using` to DDNS Snap (or any consumer) types. Consumers reference this
   library, not the other way around.
2. **Never write files via terminal** (no `>`, `>>`, `| Out-File`, `Set-Content`). Use editor tools.
3. **Preserve RecordStore semantics.** Append never sorts; global and within-key insertion order are
   stable; group keys may repeat; precise mutation uses `StoreRecordId`; analysis is explicit.
4. **Preserve value ownership.** `TValue` is a struct. Any mutable reference inside it requires a
   complete `IDeepCloneStrategy<TValue>` at input, output, clone, view, and publication boundaries.
5. **Preserve publication boundaries.** Publishing freezes the source, atomically installs a same-type
   read-only snapshot, and never serializes runtime delivery markers or locks.
6. **Do not restore removed DList compatibility code.** `DList`, its JSON converter, and their tests
   were removed on 2026-07-17. Consumers must migrate to RecordStore instead of reintroducing aliases.
7. **Keep RecordStore single-writer.** Source mutations and Publish belong to one logical execution
   flow. Other threads consume completed read-only Snapshots. `SourceAccess` is an optional cooperative
   gate for exceptional cross-flow coordination; do not add implicit locking to the default path.
8. **Keep storage conflicts and business merge separate.** RecordStore detects primary-key and unique-
   constraint conflicts. Ordinary Add may invoke `MergeAdd(incoming)` only when an actual conflict exists
   and `AutoMerge` is enabled. Candidate selection and business completeness belong to the derived store;
   the base store only owns index-safe commit primitives. Publication aggregation remains an independent
   output policy.

## Project Structure

```text
modules/Data/
├── Iwesun.Runtime.Data.slnx  -> focused solution entrypoint
├── docs/
│   ├── README.md             -> 中文 documentation index
│   ├── 01-design/            -> RecordStore design and technical contracts
│   ├── 02-api/               -> RecordStore guide/examples and compatibility API
│   └── 03-reference/         -> reference layer (source map, logical contracts)
├── src/Iwesun.Runtime.Data/          -> released implementation
├── tests/Iwesun.Runtime.Data.Tests/  -> Runtime Data unit and contract tests
└── tools/Iwesun.Runtime.Data.ScaleTest/ -> configurable large-memory/index/publication test program
```

## Core API

- `RecordStore<TValue,TPrimaryKey>` — ordered structural record store and publication root.
- `RecordStoreDefinition<TValue,TPrimaryKey>` — keys, primary key, and schema definition.
- `StoreRecordId` — stable identity inside one store instance.
- `RecordStoreSchemaRegistry<TKey,TValue>` — schema recovery for serialization.
- `DList` and its JSON converter are not part of the assembly.

## Relationship to Consumers

Consumers reference `D:\Git Space\Runtime\modules\Data\src\Iwesun.Runtime.Data\Iwesun.Runtime.Data.csproj` or the released
`lib\Iwesun.Runtime.Data\Iwesun.Runtime.Data.dll`. Never add an `Iwesun.Data` compatibility surface.
