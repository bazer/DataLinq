> [!WARNING]
> Internal 0.10 command ownership with controllable providers. Native command factories, complete query capture and public async APIs are not implemented by this slice. W1 and W0-F1 remain open.

# W1 Owned Async Commands

**Recorded:** 2026-09-17. Follows merged [initialization-handoff PR #159](https://github.com/bazer/DataLinq/pull/159), under the [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception). See the full [completion audit](W1%20Completion%20Audit.md).

## Command and Reader Lifetimes

`OwnedCommandExecution` adds internal owned reader/scalar/non-query orchestration alongside the existing borrowed-command capability. It consumes an explicit `IAsyncOwnedCommandFactory` bound to captured invocation inputs and the selected provider. Factory validation/construction cannot do provider I/O; partial construction remains the factory's responsibility until an owned command is transferred. The factory must validate supported execution and cleanup before pre-cancellation. Deriving from `DbCommand` or implementing `IAsyncDisposable` is not evidence of actual async support.

Each invocation is single-attempt after work starts. Validation/pre-cancellation does not construct a command, so a rejected pre-canceled call does not consume the invocation. A deferred sequence captures a fresh invocation per enumerator; unused enumerators create no command. Complete SQL/parameter/builder snapshots and native factory implementations remain separate work, not implied by this interface contract.

| Boundary | Implemented internal behavior |
| --- | --- |
| Factory validation and pre-cancellation | No command construction or dispatch |
| Command construction/validation fails after transfer | Dispose the owned command, preserving the original error |
| Cancellation after command construction | Observe it before dispatch, then await independent cleanup |
| Reader acquisition fails or returns null | Clean up all transferred resources; do not retry |
| Reader acquisition succeeds despite a cancellation request | Transfer reader and command before the enumerator's next cancellation checkpoint |
| Reader lifetime | Retain the command until reader cleanup finishes; attempt command cleanup even if reader cleanup fails |
| Scalar/non-query execution | Await command cleanup before returning; cleanup failure prevents a normal result; do not retroactively cancel confirmed execution success |
| Repeated disposal | Shared sync/async terminal state; report each cleanup attempt once |
| Concurrent advance/dispose | Reject overlap before changing state or releasing command ownership |

`OwnedAsyncDataReader` preserves synchronous getters, direct synchronous advancement/disposal, explicit async advancement/disposal, and the optional `IDataLinqOwnedBinaryBufferReader` capability only when the wrapped reader supplies it. There is no synchronous fallback on its async cleanup path. The wrapper owns the command, not its surrounding transaction. A standalone provider access still owns its connection lifetime; this does not prove native connection cleanup.

The source composes with `InitializingTransactionReaderSource` and the existing managed enumerator. Deterministic integration tests keep transaction admission across reader and command cleanup, prove that initialization completes before command creation, and drain unfinished helper readers before transaction cleanup. Borrowed-command behavior remains unchanged.

## Failure Evidence and Materialization

Reader and command cleanup are attempted independently and in that order. Original exceptions remain primary; structured secondary failures preserve encounter order and identity. `ExecutionFailures.AddCleanup` records the fact of a direct cleanup failure even when that exception already carries older execution context or is the same instance as the primary. An older attachment cannot turn a failed cleanup into evidence for continued transaction use. Reader, initialization and direct synchronous read cleanup use this boundary distinction; helper draining still imports the reported work's original stage.

Tests run the owned async reader through the actual `ProviderRowDecoder` and `ProviderRowMaterializer`. They verify conversion after awaited advancement, typed decoding failures, independent model-valued row results, defensive copying for ordinary binary readers, and exclusive-buffer transfer for readers that implement the optional capability. This exercises the common materializer; it does not complete ordinary/prepared/fluent query capture or generated model/API integration.

## Development Evidence

Thirty-nine new TUnit cases cover command validation/construction, canceled and failed dispatch, suspended independent cleanup, cleanup failure after successful scalar/non-query execution, reader ownership transfer, sync/async disposal state, overlap, acquisition failures, unused/repeated enumeration, actual transaction/helper/initialization integration, and canonical decoding/conversion/binary ownership.

Local Release / .NET 10:

- `artifacts/w1-owned-commands-focused.json`: **26/26 passed**, owned command and cleanup tests.
- `artifacts/w1-owned-commands-transactions.json`: **284/284 passed**, transaction-focused tests at maximum parallelism 16.
- `artifacts/w1-owned-commands-decoding-final.json`: **23/23 passed**, decoder tests including five new async ownership/materialization cases.
- `artifacts/w1-owned-commands-unit.json`: **2,144/2,144 passed**, full unit suite at CI parallelism 16.
- `artifacts/w1-owned-commands-sqlite-file.json` and `artifacts/w1-owned-commands-sqlite-memory.json`: **519/519 passed each**, compliance anchor shards at maximum parallelism 8.
- Unit/dependency, compliance and core .NET 8/9/10 builds: zero warnings/errors, recorded in adjacent `w1-owned-commands-*-build.log` files.

The first decoder run, `artifacts/w1-owned-commands-decoding.json`, was **22 passed / 1 failed**. Its ordinary-reader test branch accidentally constructed the binary-ownership fixture through target-typed construction. Naming the ordinary fixture explicitly corrected the test setup; the final run above verifies both capability branches. This failed run is not passing evidence.

These are development checks, not frozen release evidence. Exact-head CI is recorded in the PR. Planning pages are excluded from DocFX; relative links and whitespace are checked separately.

## Remaining Integration

The factories are explicit internal contracts exercised by controllable implementations. Actual owned string/generated commands still need captured SQL/parameter/materializer state and native factory binding. Scalar/non-query ownership primitives still need the complete managed operation, mutation and metadata orchestration around them. The remaining W1 query capture, mutation/batch/hydration, relation/cache publication, metadata/root lifecycle, telemetry/correlation, I/O-map and performance gates stay open. Native/provider-specific dispatch and connection feasibility remain W2; public declarations and consumers remain W3.

The compatibility baseline stays 0.9.2, benchmark runtime stays .NET 10, and the SQLite dependency limitation is unchanged.
