# Runtime Fixed Value Instruction FIFO Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the active command/state string FIFO with fixed-width atomic shared queues, one global controller inbox and one inbox per registered unit, with asynchronous wake/dispatch and unified state-transition notification.

**Architecture:** `Iwesun.Runtime.Data` owns the blittable instruction and bounded atomic ring layout. `Iwesun.Runtime.Diagnostics` owns anonymous Windows shared-memory/wake handles, dispatchers, registry integration and managed-process handle bootstrap. State transitions publish `RuntimeState.Code`; controller commands publish the integer `RuntimeManagedCommandKind`; details remain on the diagnostics pipe.

**Tech Stack:** C#/.NET 10, Windows `CreateFileMappingW`/`MapViewOfFile`/anonymous event handles, `Interlocked`, `ThreadPool.RegisterWaitForSingleObject`, existing diagnostics pipe and functional-test executable.

---

## File map

- Create `Iwesun.Runtime.Data/RuntimeValueInstruction.cs`: fixed protocol, entity kinds, queue header/slot layouts.
- Create `Iwesun.Runtime.Diagnostics/RuntimeSharedAtomicFifo.cs`: anonymous shared mapping, fixed bounded atomic queue and inherited-handle descriptor.
- Create `Iwesun.Runtime.Diagnostics/RuntimeInstructionDispatcher.cs`: asynchronous wake, drain-to-empty and handler dispatch.
- Create `Iwesun.Runtime.Diagnostics/RuntimeUnitInbox.cs`: per-registration FIFO lifecycle and command dispatch.
- Modify `Iwesun.Runtime.Diagnostics/RuntimeManagedRegistry.cs`: global controller inbox, unit inbox descriptors, send/receive APIs and shutdown commands.
- Modify `Iwesun.Runtime.Diagnostics/RManagedState.cs`: unified post-transition publication.
- Modify `Iwesun.Runtime.Diagnostics/RProcess.cs`, `RThread.cs`, `RTask.cs`: use unified transition and unit-inbox paths.
- Modify `Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`: activate controller dispatcher and process bootstrap.
- Modify `Iwesun.Runtime.Diagnostics/DiagnosticSharedFifoBus.cs` and `DiagnosticSwitchboard.cs`: remove command/state from the legacy string transport.
- Modify `Iwesun.Runtime.FunctionalTests/Program.cs`: focused fixed FIFO, dispatch, overflow and cross-process scenarios.
- Modify `Iwesun.Runtime.FunctionalTests/SampleHostCliFullScenario.cs`: verify coordinated shutdown through unit FIFOs.
- Modify `docs/05-runtime-tooling/RUNTIME_INTEGRATION_GUIDE.md` and `docs/RUNTIME_DIAGNOSTICS.md`: publish the final API and transport boundary.

### Task 1: Fixed instruction contract and atomic ring behavior

**Files:**
- Create: `Iwesun.Runtime.Data/RuntimeValueInstruction.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add a failing fixed-layout functional scenario**

Add scenario `value-instruction-layout` which asserts `Marshal.SizeOf<RuntimeValueInstruction>() == 48`, stable field round-trip, and distinct `RuntimeInstructionEntityKind` values for controller/process/thread/task/business.

- [ ] **Step 2: Run the scenario and verify failure**

Run: `dotnet run --project Iwesun.Runtime.FunctionalTests -c Debug -- --child --scenario value-instruction-layout`

Expected: build failure because `RuntimeValueInstruction` does not exist.

- [ ] **Step 3: Add the fixed blittable types**

Define `RuntimeValueInstruction` with `long Sequence`, five `int` identity/value fields, `long Arg0`, `long Arg1`, and an explicit protocol/version `int` so the packed size is 48 bytes. Define `RuntimeInstructionEntityKind : int`. Define internal header and slot layouts using only integral fields; each slot includes an atomic sequence marker plus one instruction.

- [ ] **Step 4: Run the layout scenario**

Expected: PASS and exact 48-byte instruction size.

- [ ] **Step 5: Commit the protocol contract**

Commit only the data contract and focused scenario with message `feat: add fixed runtime instruction contract`.

### Task 2: Anonymous shared-memory bounded FIFO

**Files:**
- Create: `Iwesun.Runtime.Diagnostics/RuntimeSharedAtomicFifo.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add failing queue tests**

Add scenario `value-instruction-fifo` covering depth 64 and 128, FIFO ordering, concurrent producers, one-time consumption, full queue returning `false`, dropped counter increments, drain-to-empty, and idempotent disposal.

- [ ] **Step 2: Verify the scenario fails**

Expected: build failure because `RuntimeSharedAtomicFifo` does not exist.

- [ ] **Step 3: Implement anonymous shared storage**

Use `CreateFileMappingW` with a null name and inheritable security attributes, `MapViewOfFile`, `UnmapViewOfFile`, and `CloseHandle`. Store header and fixed slots directly in the mapped view. Do not use `MemoryMappedFile.CreateOrOpen`, `Mutex`, strings, JSON, Base64, or variable-length entries.

- [ ] **Step 4: Implement atomic enqueue/dequeue**

Use per-slot sequence numbers and `Interlocked.CompareExchange` on write/read positions. Publish a slot only after its instruction is fully copied; release a consumed slot by advancing its sequence to the next ring generation. Keep capacity immutable and reject non-power-of-two or out-of-range depths.

- [ ] **Step 5: Run the FIFO scenario repeatedly**

Run the scenario 20 times and require zero ordering corruption, duplicates or deadlocks.

- [ ] **Step 6: Commit the queue**

Commit with message `feat: add anonymous atomic instruction fifo`.

### Task 3: Asynchronous wake and drain dispatcher

**Files:**
- Create: `Iwesun.Runtime.Diagnostics/RuntimeInstructionDispatcher.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeSharedAtomicFifo.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add failing dispatch tests**

Add scenario `value-instruction-dispatch` asserting that enqueue returns before handlers execute, one wake drains all queued instructions in sequence order, a write racing with drain/reset is not lost, and dispose prevents further callbacks.

- [ ] **Step 2: Verify failure**

Expected: build failure because the dispatcher is missing.

- [ ] **Step 3: Add anonymous wake event and commit generation**

Create an unnamed inheritable auto-reset event per FIFO. After slot publication, increment a shared commit generation and signal the event. Expose safe wait and signal operations without exposing raw ownership to business callers.

- [ ] **Step 4: Add dispatcher**

Register the event with `ThreadPool.RegisterWaitForSingleObject`. The callback drains until empty, dispatches outside producer threads, compares the observed generation after draining, and immediately drains again when generation changed. Serialize callbacks so only one consumer drains a FIFO.

- [ ] **Step 5: Run dispatch race tests**

Expected: PASS under at least 10,000 writes and repeated dispose races.

- [ ] **Step 6: Commit dispatch support**

Commit with message `feat: dispatch fixed fifo instructions asynchronously`.

### Task 4: Registry topology and unified controller/unit APIs

**Files:**
- Create: `Iwesun.Runtime.Diagnostics/RuntimeUnitInbox.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeManagedRegistry.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeManagedCommandTarget.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add failing topology scenario**

Add `managed-instruction-topology`: register process/thread/task/business units, assert one controller FIFO of depth 128, assert each registration has a distinct depth-64 inbox, send commands by unit ID, verify target-only dispatch, and unregister/dispose every inbox.

- [ ] **Step 2: Verify failure**

Expected: assertions fail because registrations currently own `ConcurrentQueue<RuntimeManagedCommand>` rather than fixed inboxes.

- [ ] **Step 3: Add registry-owned controller inbox**

Create one depth-128 `RuntimeSharedAtomicFifo` and dispatcher in `RuntimeManagedRegistry`. Expose `TryPublishStateCode(unitId, entityKind, stateCode, arg0 = 0, arg1 = 0)` and a controller instruction event/handler registration. Controller callbacks resolve `EntityIdHash` against the registration table; collisions require pipe/registry resolution rather than adding strings to FIFO.

- [ ] **Step 4: Add unit inbox lifecycle**

Each registration creates/attaches a depth-64 `RuntimeUnitInbox`. Add `TrySendCommand(unitId, RuntimeManagedCommandKind kind, long arg0 = 0, long arg1 = 0)`. Keep compatibility command methods as thin adapters only when they contain no string payload; reject complex payload with an explicit result directing callers to the pipe.

- [ ] **Step 5: Route shutdown broadcasts**

Make Stop/Wakeup use `TrySendCommand`. On full inbox, signal it, retry a bounded number of times, and let `RuntimeShutdownCoordinator` continue periodic resend until unregister or timeout.

- [ ] **Step 6: Run managed and shutdown tests**

Run `managed`, `thread`, `task`, and the new topology scenario. Expected: all pass and final registration count is zero.

- [ ] **Step 7: Commit registry topology**

Commit with message `feat: add controller and per-unit instruction inboxes`.

### Task 5: Unified state-transition business interface

**Files:**
- Modify: `Iwesun.Runtime.Diagnostics/RManagedState.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeManagedUnitBase.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RProcess.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RThread.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RTask.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add failing transition-publication tests**

For process/thread/task/business states, subscribe to controller instructions, perform `TransitionTo`, assert `Value == RuntimeState.Code`, correct entity kind/hash, and exactly one publication. Assert failed `TryTransitionTo` publishes nothing.

- [ ] **Step 2: Verify failure**

Expected: current state frame path does not provide unified dispatcher behavior.

- [ ] **Step 3: Centralize post-transition behavior**

Make `RManagedState` accept its entity kind and use a single private `PublishTransition(RuntimeState state)` method: update registration snapshot, then call `TryPublishStateCode`. `TransitionTo` and successful `TryTransitionTo` both call it once. Details and subtask changes update the snapshot but do not publish a state Code unless the current state actually changes.

- [ ] **Step 4: Remove wrapper bypasses**

Ensure all internal transitions in `RProcess`, `RThread`, and `RTask` call their `IRManagedState` transition API. Remove direct state-manager calls and duplicate state FIFO publication.

- [ ] **Step 5: Run transition and wrapper scenarios**

Expected: process/thread/task/business all emit identical fixed frames and unregister cleanly.

- [ ] **Step 6: Commit unified transitions**

Commit with message `feat: publish state codes through unified transition api`.

### Task 6: Managed cross-process handle bootstrap

**Files:**
- Create: `Iwesun.Runtime.Diagnostics/RuntimeInstructionHandleBootstrap.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RProcess.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeHostTemplate.cs`
- Modify: `Iwesun.Runtime.FunctionalTests/Program.cs`

- [ ] **Step 1: Add failing cross-process scenario**

Parent creates an `RProcess` child through the standard factory. Assert child state Code reaches the parent's controller FIFO, parent Stop reaches the child's unit FIFO, child dispatcher wakes without polling, child unregisters, and an independently launched probe without inherited handles cannot attach.

- [ ] **Step 2: Verify failure**

Expected: the child cannot attach to anonymous FIFO handles.

- [ ] **Step 3: Add inherited descriptor bootstrap**

Before process start, create the child's unit inbox in the parent, mark mapping/event handles inheritable, and append a versioned fixed bootstrap descriptor through environment variables reserved by Runtime. Pass the controller FIFO mapping/event handles and child inbox mapping/event handles. Clear inheritance for unrelated handles.

- [ ] **Step 4: Attach in child activation**

During `RuntimeHostTemplate.Activate`, validate descriptor version, process ownership and capacities, attach mapped views/events, start dispatchers, then clear bootstrap environment values from the live process. Invalid descriptors fail closed and leave pipe diagnostics available.

- [ ] **Step 5: Verify process behavior**

Run the cross-process scenario repeatedly. Expected: event-driven delivery in both directions, no fixed names, no stale attachment after both processes exit.

- [ ] **Step 6: Commit process bootstrap**

Commit with message `feat: inherit instruction fifo handles across managed processes`.

### Task 7: Retire legacy command/state string FIFO

**Files:**
- Modify: `Iwesun.Runtime.Diagnostics/DiagnosticSharedFifoBus.cs`
- Modify: `Iwesun.Runtime.Diagnostics/DiagnosticSwitchboard.cs`
- Modify: `Iwesun.Runtime.Diagnostics/RuntimeManagedRegistry.cs`
- Modify: `Iwesun.Runtime.Data/RuntimeInjectorTransportModels.cs`

- [ ] **Step 1: Add a source-boundary assertion**

Add a functional/source inspection check that active command/state paths do not call `CreateOrOpen`, named `Mutex`, `EncodeCommandFrame`, `EncodeStateFrame`, or string FIFO enqueue.

- [ ] **Step 2: Remove command/state channels from legacy bus**

Keep legacy monitor/breakpoint behavior only where still required. Delete active `Command` and `State` named-MMF channels and remove `TryEnqueueCommand`, `TryEnqueueState`, `TryDequeueCommand`, and `TryDequeueState` call paths.

- [ ] **Step 3: Remove obsolete codecs when unreferenced**

Delete command/state string prefixes and Base64 codec methods if no remaining compatibility consumer exists. Do not remove fixed structs still required by public compatibility until replacements are documented.

- [ ] **Step 4: Build Debug and Release**

Run `dotnet build Iwesun.Runtime.slnx -c Debug` and `dotnet build Iwesun.Runtime.slnx -c Release`. Expected: zero errors; Release contains no breakpoint implementation and retains FIFO state/control, pipe, output, logging, reflection, hooks and shutdown.

- [ ] **Step 5: Commit legacy removal**

Commit with message `refactor: retire string command and state fifos`.

### Task 8: Full shutdown and documentation validation

**Files:**
- Modify: `Iwesun.Runtime.FunctionalTests/SampleHostCliFullScenario.cs`
- Modify: `docs/05-runtime-tooling/RUNTIME_INTEGRATION_GUIDE.md`
- Modify: `docs/RUNTIME_DIAGNOSTICS.md`
- Modify: `docs/REQUIREMENTS_ACTIVE.md`

- [ ] **Step 1: Strengthen SampleHost assertions**

Before shutdown, assert three registered loop inboxes. Issue `safe-shutdown`, assert Stop/Wakeup delivery through fixed unit FIFOs, state Code notifications on the controller FIFO, all three units unregister, host exits with code 0, and final managed registration count is zero.

- [ ] **Step 2: Add timeout coverage**

Register a deliberately non-draining unit, request a short coordinated shutdown, and assert exit/result code 124 plus the pending unit ID returned through the pipe snapshot.

- [ ] **Step 3: Run focused and full functional coverage**

Run all new FIFO scenarios, wrapper scenarios, `sample-host-cli-full`, then the complete functional parent. Restore diagnostics with `quiet` after CLI tests.

- [ ] **Step 4: Update documentation**

Document FIFO topology, exact 48-byte instruction, state Code versus command enum semantics, depth defaults, overflow rules, inherited-handle process boundary, public `TransitionTo`/`TryTransitionTo`, and the rule that complex content uses the pipe.

- [ ] **Step 5: Final verification and commit**

Run Debug and Release solution builds again. Commit tests/docs with message `test: verify fixed instruction fifo lifecycle`.
