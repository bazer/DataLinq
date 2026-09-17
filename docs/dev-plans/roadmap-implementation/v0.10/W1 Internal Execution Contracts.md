> [!WARNING]
> This records internal implementation work for 0.10. No public async API or native provider support is delivered by this slice. W0-F1 and the blocked SQLite/release gates remain open.

# W1 Internal Execution Contracts

**Started:** 2026-09-17, under the user's [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception).

The [implementation order](Implementation%20Order%20and%20Integration%20Plan.md) and accepted AAPI decisions remain authoritative. W1 establishes internal orchestration contracts with deterministic tests; W2 must prove actual provider execution, and W3 owns emitted public declarations and packed-consumer compatibility.

## W1.1: Command, Reader And Deferred Acquisition Foundation

This first slice adds internal types under `DataLinq.Execution`; existing `DatabaseAccess`, public interfaces, providers, transactions, generators and package dependencies are unchanged.

| Contract | Implemented boundary |
| --- | --- |
| `IAsyncDatabaseAccess` | Reader, scalar and non-query command execution; explicit required tokens; `Task` results; supplied `IDbCommand` remains borrowed |
| `AsyncDatabaseAccess` | Shared order: null argument check, provider-supplied I/O-free lifecycle/capability validation, pre-cancellation, then direct dispatch to an implemented async core |
| `IAsyncDataReader` | Internal companion to the synchronous current-row reader, with `Task<bool>` advancement and `ValueTask` async disposal, matching AAPI-57's selected shape without exposing its public declaration |
| `IAsyncReaderSource` / `BorrowedCommandReaderSource` | I/O-free source construction and explicit deferred reader acquisition; command identity is retained, not cloned or treated as a generated-query parameter snapshot |

`AsyncDatabaseAccess.Require` rejects an access object without the explicit capability. It does not infer support from the presence of a `DbCommand` or a framework async method. Providers must validate the actual command and operation before I/O; their native dispatch evidence remains W2 work. There is no synchronous fallback, automatic retry, `Task.Run`, blocking wait, or post-success cancellation check in these helpers.

Provider-supplied validation must not acquire an execution lease, mutate the borrowed command, or perform I/O. Transaction admission and release across suspension are deliberately left to W1.2, after validation and pre-cancellation. This slice does not retrofit the existing transaction guard or claim to enforce overlap restrictions yet.

The deferred source only acquires a reader and transfers it to its caller. It is not yet an async row enumerator: combined method/enumerator tokens, automatic disposal on every enumeration exit, owned string-command construction and primary/secondary cleanup-failure composition remain later work. The source does not own or dispose the supplied command, transaction or connection. A provider implementation must clean up resources it acquired if reader creation fails before transfer.

## Controllable Test Infrastructure

`ControlledAsyncDatabaseAccess`, `ControlledAsyncDataReader` and `AsyncCheckpoint` live only in the existing TUnit unit-test project. They can pause command dispatch, reader advancement and async cleanup, or inject a specific exception. Continuations are asynchronous, tests await explicit entry signals with bounded timeouts, and no scheduler delays or databases are used by these new cases.

The command probes derive from `DbCommand` but intentionally do not override its async defaults. Any accidental synchronous command/reader execution throws and is counted. This lets tests catch inappropriate fallback without mistaking the fixture for a real provider.

The 29 focused cases cover:

- null, lifecycle and unsupported-command/operation validation before pre-cancellation across all three command families;
- valid pre-cancellation preventing dispatch without disposing borrowed resources;
- original token and command identity reaching suspended dispatch, and cancellation during the controlled wait;
- provider failure identity without replay or exception wrapping;
- unchanged scalar/non-query results and already-completed success surviving later cancellation;
- source construction without dispatch, deferred reader transfer, null-input rejection, sequential opens without a cached reader, and borrowed-command ownership/stability;
- a controllable ephemeral reader position, asynchronous advancement and cleanup, cleanup independent of an already-canceled operation token, and injected cleanup failures.

The reader tests validate the test fixture's ability to represent later orchestration cases. They do not establish native cancellation, automatic enumerator cleanup, connection trust, transaction outcomes, or combined execution/cleanup failure precedence in production.

## Validation For This Slice

- Unit project Release build: zero warnings/errors, `artifacts/w1-contract-build-final.log`.
- Core Release build on .NET 8/9/10: zero warnings/errors, `artifacts/w1-contracts-core-build.log`.
- Focused Testing CLI invocation: **29 passed, 0 failed**, `artifacts/w1-contracts-focused-final.json`.
- Full unit suite: **1,864 passed, 0 failed**, `artifacts/w1-contracts-unit-final.json`.

The Testing CLI summaries record the actual test configuration (Debug, .NET 10), resolved commands, runner identity and referenced raw results. The CLI launcher and separate explicit builds used Release; do not relabel the test-host configuration from the launcher's `-c` argument. These are development checks from a modified checkout, not a clean strict release capture. The broader unit suite contains existing provider-backed fixtures; the new 29 cases themselves use only controllable doubles. Earlier 28-case / 1,863-test runs remain in the adjacent non-final reports; the additional case verifies sequential source opens and borrowed-command ownership after reader disposal.

No package was published, no driver dependency changed, and no native-provider acceptance, performance improvement, packed API compatibility or W0-F1 closeout is claimed. The new core types are internal and are not wired into existing production execution. Planning pages are excluded from DocFX.

## Remaining W1 Slices

| Next slice | Required work and evidence |
| --- | --- |
| W1.2 operation ownership and initialization | Extend the existing transaction guard with internal ownership across suspended commands/readers, private nested dispatch and lazy initialization; deterministic overlap, early failure and disposal tests; no lock held across arbitrary awaits |
| W1.3 cancellation, completion and recovery | Validation/cancellation ordering throughout orchestration, interrupted reads versus writes, completion certainty, independent recovery budget, and preservation of original failures with ordered cleanup failures; no implicit retries or abandoned operations |
| W1.4 invocation/source capture and handoff | Owned command/reader lifetimes and async enumeration exits, combined tokens, ordinary/prepared/mutation snapshots at their accepted boundaries, relation/cache publication races and mapping of every I/O family to the internal contracts |

Transaction completion, typed materialization, relation coordination, mutation orchestration, metadata operations and owning-root disposal are not implemented by W1.1. Additional internal contracts must be driven by those slices and the [I/O map](W0%20IO%20Execution%20Map.md), not a new public provider protocol. W1 is complete only when its full contract/controllable-test gate is met; this first PR does not claim that gate.

Native SQLite integration acceptance, final provider feasibility/public API freeze and release approval still require verified adoption of a corrected official SQLite dependency and recapture of the affected evidence. The frozen pre-async baseline and 0.9.2 compatibility baseline remain unchanged.
