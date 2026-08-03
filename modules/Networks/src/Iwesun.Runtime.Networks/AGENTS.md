# Iwesun.Runtime.Networks — AI Agent Instructions

This file is the entry point for AI coding agents working in this repository.

## Quick Start

- **Build**: `dotnet build modules/Networks/Iwesun.Runtime.Networks.slnx -c Release`
- **Language**: C# (.NET 10), `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`
- **Namespace**: `Iwesun.Runtime.Networks` (flat — all types live in this single namespace)
- **Assembly**: `Iwesun.Runtime.Networks`
- **Private fields**: `_camelCase` with underscore prefix
- **Language policy**:
  - Code identifiers, comments, XML docs → **English**
  - Design docs (`docs/`) → **中文 (Chinese)**
  - AI instructions (`AGENTS.md`) → **English**

## Purpose

`Iwesun.Runtime.Networks` is a standalone, business-agnostic network access library extracted from the
DDNS Snap project. It provides an **event-driven, value-type FIFO** asynchronous network endpoint
model. See [`modules/Networks/docs/README.md`](../../docs/README.md) for the full documentation index.

## Critical Rules

1. **Zero business coupling.** This library must depend only on `System.*`. Never add a
   `ProjectReference` or `using` to DDNS Snap (or any consumer) types. Consumers reference this
   library, not the other way around.
2. **Never write files via terminal** (no `>`, `>>`, `| Out-File`, `Set-Content`). Use editor tools.
3. **`TSend` / `TReceive` are value types** (`struct` constraint). Keep new endpoint request/result
   types as `readonly record struct` or `struct`.
4. **Preserve the tracked FIFO model.** New request/reply endpoints inherit
   `TrackedRequestReplyEndpointBase<TRequest, TResponse, TKey>`. `Guid RequestId` is the framework
   identity; `TKey` is caller query data only and must never key the internal pending table or
   select a completion. Match, retry, remove, and await exclusively by `RequestId`. Duplicate keys
   are valid; expose them only through snapshots, terminal records, and read-only key queries.
   Networks 3.0 protocol requests require a non-empty RequestId before enqueue; convenience factories
   may create it synchronously, but the low-level tracked base never replaces `Guid.Empty`.
5. **Defensive execution** — validate inputs in `OnFilterSend`; skip invalid items gracefully via
   error events instead of throwing across the send thread.
6. **One public request/reply standard.** Do not restore removed V1 HTTP, DoH, TCP, or Ping types,
   aliases, wrappers, or compatibility assemblies. PowerShell, process execution, and local
   snapshots may retain the legacy FIFO base only because they are not duplicate request/reply APIs.

## Project Structure

```text
modules/Networks/
├── Iwesun.Runtime.Networks.slnx -> focused solution entrypoint
├── docs/
│   ├── README.md             -> 中文 documentation index
│   ├── 01-design/            -> design layer (overview, state machine, FIFO model)
│   ├── 02-endpoints/         -> endpoint layer (base API, binary primitives, concrete endpoints)
│   └── 03-reference/         -> reference layer (source map, logical contracts)
├── tests/                    -> unit and contract tests
├── samples/                  -> runnable public examples
├── validation/               -> Windows/WFP real-network validation
└── src/Iwesun.Runtime.Networks/
    ├── Iwesun.Runtime.Networks.csproj       -> RootNamespace + AssemblyName = Iwesun.Runtime.Networks
    ├── NetworkAsyncEndpointBase.cs  -> abstract base + event args + enums
    ├── IpAddressValue.cs            -> 17-byte canonical pure IP value
    ├── MacAddressValue.cs           -> 7-byte canonical EUI-48 value
    ├── BinaryNetworkPrimitives.cs   -> fixed binary network error
    └── *Endpoint*.cs                -> concrete endpoints (Tcp/Udp/Ping/Dns/NetBios/Arp/Ipv6Neighbor/Process/PowerShell)
```

## Core API

- `TrackedRequestReplyEndpointBase<TRequest,TResponse,TKey>` — the only public request/reply base.
  Key surface: `TrySend`, `Send`, `TryReadReceived`, GUID pending queries, retry/failure/completion
  events, T1/T2/T3 budgets, Branch planning, and bounded multi-response collection.
- `NetworkAsyncEndpointBase<TSend, TReceive>` — exception base retained for PowerShell, process, and
  local snapshot capabilities. Key surface: `Send`, `TryReadReceived`,
  `Start`/`Stop`/`Dispose`, `SignalHardwareReady`/`SignalHardwareBlocked`/`ResetHardware`, abstract
  `OnSend`, virtual hooks (see below), protected `PublishReceived`/`PublishReceivedRaw`.
- Events: `SendStarted`, `SendCompleted`, `ReceiveCompleted`, `HardwareStateChanged`, `HardwareError`,
  `Error`, `SendQueueAvailable`.
- `NetworkSendResult` (`Sent`/`HardwareBlocked`/`Failed`) + enums `NetworkSendStatus`,
  `NetworkHardwareStatus`, `NetworkEndpointErrorKind`.

### Virtual Hooks (override points)

| Method                                                             | Purpose                                                        | Default           |
| ------------------------------------------------------------------ | -------------------------------------------------------------- | ----------------- |
| `abstract OnSend(TSend item)`                                      | **Required** — perform the actual send/start async op          | —                 |
| `virtual OnStarted()`                                              | Initialize resources (e.g., start UDP receive loop)            | no-op             |
| `virtual OnStopping()`                                             | Cleanup when `Stop()` called                                   | no-op             |
| `virtual OnResetHardware()`                                        | Reset resources (recreate UdpClient, clear active ops)         | no-op             |
| `virtual OnFilterSend(item, out reason)`                           | Validate/drop send items before analysis                       | accept all        |
| `virtual OnAnalyzeSend(item, out analyzed, out reason)`            | Mutate/normalize (assign RequestId, default timeout)           | pass-through      |
| `virtual OnFilterReceive(item, out reason)`                        | Validate/drop received items                                   | accept all        |
| `virtual OnAnalyzeReceive(raw, Action<TReceive> emit, out reason)` | Translate raw callback into 0..N TReceive values (1→1 default) | cast raw directly |

### Protected Publish Methods

- `PublishReceived(TReceive item)` — single-item publish (runs `OnFilterReceive` then enqueues to receive FIFO).
- `PublishReceivedRaw(object raw)` — raw-object publish (runs `OnAnalyzeReceive` which calls `emit` for each item, then each goes through `PublishReceived`). Use for 1→N translation (e.g., ARP table parsing).

### Hardware State Machine (`NetworkHardwareStatus`)

| Value                | Meaning                                                            |
| -------------------- | ------------------------------------------------------------------ |
| `Uninitialized (0)`  | Before `OnStarted` completes                                       |
| `Ready (1)`          | Can send                                                           |
| `Busy (2)`           | Hardware blocked — send thread parks until `SignalHardwareReady()` |
| `BufferOverflow (3)` | FIFO full; overflow error raised                                   |
| `Resetting (4)`      | `ResetHardware()` called; `OnResetHardware` running                |
| `Faulted (5)`        | `OnStarted` or `OnResetHardware` threw                             |
| `Stopped (6)`        | After `Stop()`/`Dispose()`                                         |

`IsHardwareBlock(ex)` detects `SocketException` with `NetworkDown/Unreachable/HostDown/HostUnreachable/NoBufferSpaceAvailable/WouldBlock/IOPending`.

## Concrete Endpoints

| Endpoint Class                        | TSend                                | TReceive                    | Purpose                                                                      |
| ------------------------------------- | ------------------------------------ | --------------------------- | ---------------------------------------------------------------------------- |
| `NetworkTcpConnectEndpoint<TKey>`     | `NetworkTcpConnectRequest<TKey>`     | `NetworkTcpConnectResponse<TKey>` | TCP connect with explicit access plan and Branch evidence              |
| `NetworkUdpDatagramEndpoint<TKey>`    | `NetworkUdpDatagramRequest<TKey>`    | `NetworkUdpDatagramResponse<TKey>` | UDP exchange through a private socket/NAT mapping                       |
| `NetworkPingEndpoint<TKey>`           | `NetworkPingRequest<TKey>`           | `NetworkPingResponse<TKey>` | Unified IPv4/IPv6 and bounded multi-response Ping                            |
| `NetworkDnsReverseLookupEndpoint<TKey>` | `NetworkDnsReverseLookupRequest<TKey>` | `NetworkDnsReverseLookupResponse<TKey>` | PTR with separate resolver access plan                      |
| `NetworkNetBiosNameEndpoint<TKey>`    | `NetworkNetBiosNameRequest<TKey>`    | `NetworkNetBiosNameResponse<TKey>` | Native NBNS unicast/broadcast node-status query                         |
| `NetworkHttpGetEndpoint<TKey>`        | `NetworkHttpGetRequest<TKey>`        | `NetworkHttpGetResponse<TKey>` | Stable-semantic HTTP connection pooling                                    |
| `NetworkDohEndpoint<TKey>`            | `NetworkDohRequest<TKey>`            | `NetworkDohResponse<TKey>` | DoH wire/JSON with separated HTTP security and resolver identities             |
| `WindowsArpTableEndpoint`             | `WindowsArpTableRequest`             | `WindowsArpTableRecord`     | ARP table snapshot via `arp -a` (1→N emit)                                   |
| `WindowsIpv6NeighborSnapshotEndpoint` | `WindowsIpv6NeighborSnapshotRequest` | `WindowsIpv6NeighborRecord` | IPv6 neighbor table via PowerShell                                           |
| `ProcessCommandEndpoint`              | `ProcessCommandRequest`              | `ProcessCommandResult`      | One-process-per-send with stdout/stderr capture and timeout-kill             |
| `PowerShellSingleCommandEndpoint`     | Various binary structs               | Various binary structs      | PowerShell with `FixedBytes*` inline arrays to avoid string heap allocations |

## Fixed Value Primitives

- **`IpAddressValue`** — exact 17-byte readonly struct:
  - stores only Null/IPv4/IPv6 state and 128 pure address bits;
  - never stores IPv6 `ScopeId`; attach the resolved interface index only at an OS API boundary;
  - traits, simplified type, text, and `IPAddress` are computed views, not cached fields;
  - fixed binary representation is `family(1) + high(8) + low(8)`.
- **`MacAddressValue`** — exact 7-byte readonly struct:
  - stores only Null/Value state and 48 EUI-48 bits;
  - OUI, NIC identifier, traits, and text are computed views, not cached fields.
- **`BinaryNetworkError`** — `readonly record struct(byte Kind, int Code, int HResult)`: Kind=None/TimedOut/Cancelled/Exception; factory `FromException(Exception, cancelled, timedOut)`.
- **Fixed-buffer inline arrays**: `FixedBytes32`, `FixedBytes8192`, `FixedBytes32768` using `[InlineArray]`; `FixedBytesCodec` for copy/round-trip.

## Coding Conventions

- **Request/Result types**: `public readonly record struct` (immutable value types, with optional parameters and defaults).
- **RequestId**: protocol request GUIDs are non-empty at construction and rejected when empty.
- **Time**: `DateTime.UtcNow` for timestamps; `Stopwatch.GetTimestamp()`/`GetElapsedTime()` for latency; unix ms (`long`) in binary results.
- **Errors**: `Exception?` in high-level results (TCP/UDP/DNS); `BinaryNetworkError` (value-type) in binary results.
- **Error reason strings**: kebab-case identifiers (e.g., `"tcp-connect-port-invalid"`, `"send-fifo-overflow"`).
- **Argument guards**: `ArgumentNullException.ThrowIfNull()` for public method/constructor parameters.
- **XML doc comments**: English, on public types and members.

### Thread Safety Model

- **Send dequeue thread**: A single dedicated thread owns FIFO dequeue. Tracked endpoints may opt into concurrent attempt starts only when request planning uses immutable request-local snapshots and endpoint state is thread-safe.
- **In-flight tracking**: `ConcurrentDictionary<Guid, T>` for tracking async operations (pings, processes).
- **Lock-free queues**: `ConcurrentQueue<T>` + CAS (`Interlocked.CompareExchange`) for capacity management.
- **Callbacks**: Async completions fire on ThreadPool; `PublishReceived`/`PublishReceivedRaw` are safe from any thread.
- **Event dispatch**: All events go through `SynchronizationContext.Post` (if captured at construction) or `ThreadPool.QueueUserWorkItem` — never raised synchronously on send/callback threads.

### New Request/Reply Endpoint Implementation Pattern

```csharp
public sealed class NetworkXxxEndpoint<TKey>
    : TrackedRequestReplyEndpointBase<NetworkXxxRequest<TKey>, NetworkXxxResponse<TKey>, TKey>
    where TKey : notnull
{
    protected override Guid GetRequestId(NetworkXxxRequest<TKey> request) => request.RequestId;
    protected override TKey GetRequestKey(NetworkXxxRequest<TKey> request) => request.Key;
    protected override Guid GetResponseRequestId(NetworkXxxResponse<TKey> response) => response.Identity.RequestId;
    protected override TKey GetResponseKey(NetworkXxxResponse<TKey> response) => response.Key;
    protected override NetworkExecutionIdentity GetResponseIdentity(NetworkXxxResponse<TKey> response)
        => response.Identity;

    protected override TrackedAttemptStartResult OnStartAttempt(
        NetworkXxxRequest<TKey> request,
        byte retryCount,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        // Request a Branch identity; execute in OnStartBranch and publish with its BranchId.
        return PrecisionSocketEndpointSupport.BranchIdentityRequired();
    }
}

public readonly record struct NetworkXxxRequest<TKey>(Guid RequestId, TKey Key, RequestedAccessPlan AccessPlan);
public readonly record struct NetworkXxxResponse<TKey>(NetworkExecutionIdentity Identity, TKey Key);
```

## Common Pitfalls

1. **TRequest/TResponse must be value types.** Never use classes for tracked contracts.
2. **Never match by TKey.** Complete, retry, remove, and await exclusively by `RequestId`.
3. **Don't throw from `OnStartAttempt`.** Return a rejected attempt with a fixed failure reason.
4. **Don't block the send thread.** Start asynchronous work and return promptly.
5. **Respect the response collection policy.** Single-response branches publish once; bounded multi-response branches may publish multiple unique ResponseIds before one terminal window outcome.
6. **Events are dispatched asynchronously.** Don't assume handlers run on the send thread.
7. **`IpAddressValue` uses network byte order** for address serialization; never copy its packed CLR memory as a wire contract.

## Relationship to Consumers

Referenced by DDNS Snap (`D:\Git Space\Ddns Snap`) through the versioned `Iwesun.Runtime.Networks` package.
DDNS-specific orchestration (scan pipeline, connect-probe service, timing) stays in
the DDNS Snap repo; only the reusable endpoint base classes live here.
