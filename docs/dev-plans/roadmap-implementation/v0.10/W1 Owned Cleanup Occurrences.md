> [!WARNING]
> Internal W1 correction and bounded functional evidence. This does not close W1 or establish native/public async support.

# W1 Owned Cleanup Occurrences

**Date:** 2026-09-21. Review base: `0aab7737049f4551dba60bf1ce4a077ab1eee2aa`, merged [PR #221](https://github.com/bazer/DataLinq/pull/221). This follows the [cross-family diagnostic review](W1%20Functional%20and%20IO%20Audit.md#cross-family-diagnostic-review) under F05/F10/F20. Its bounded review did not cover the successful-reader/failed-command sequence below; the earlier legacy owned-reader controls do not exercise this internal owner.

## Reproduced failure and correction

[OwnedAsyncDataReader](../../../../src/DataLinq/Execution/OwnedAsyncDataReader.cs) disposes its reader and DataLinq-created command through [AsyncCommandCleanup](../../../../src/DataLinq/Execution/AsyncCommandCleanup.cs). Both callbacks shared one diagnostic scope. Reader disposal could report an exception, handle it and return successfully. If command disposal then threw that same exception without a new report, the collector imported the earlier cause, operation and nested failures. The cleanup boundary still supplied its own completion/recovery facts; the bug was stale classification and secondary attribution, not an actual committed transaction.

Eight new cases use the real [OwnedCommandExecution](../../../../src/DataLinq/Execution/OwnedCommandExecution.cs) handoff and controlled native resources. Against the unchanged runtime, **5/8 passed and 3/8 failed**: synchronous, immediately completed async and suspended async cleanup all incorrectly reported `Timeout` instead of `Unknown`. Each failure was the expected cause assertion. The fresh-command-report and two-resource-failure controls passed before the correction.

The fix captures the existing allocation-free report sequence immediately before each disposal. A catch discards a direct report from before that callback before adding its cleanup failure. This retains the existing outer scope, independent reader-then-command attempts, original exception, immutable captured snapshots and concurrent newer reports. It adds no scope object, retry, cancellation dependency, synchronous fallback or resource authority. A report created during the failing disposal still supplies its cause and operation. Previously captured execution failures remain in the collector even if the provider reuses their exception during cleanup.

The [new tests](../../../../src/DataLinq.Tests.Unit/Core/OwnedAsyncCleanupOccurrenceTests.cs) are:

- `OwnedCleanup_CommandFailureCannotBorrowSuccessfulReaderCleanupReport`: six stale/fresh cases across synchronous, completed async and deliberately suspended async disposal. They check original exception identity, cleanup classification, completion/recovery/transaction facts, absence of phantom secondaries, unchanged earlier snapshots, reader-before-command ordering during suspension and terminal once-only disposal across repeated sync/async calls.
- `OwnedCleanup_PreservesBothFreshResourceReportsInOrder`: two sync/async controls retain reader failure as primary and command failure as secondary, with their fresh causes/operations and cleanup stages, then verify once-only disposal.

The controlled reader fixture gains an optional async-disposal callback, parallel to its existing synchronous callback. No provider adapter or public API changes are made.

## Verification

The corrected focused run passes **8/8**. Broad Release / .NET 10 results pass **3,866 unit + 71 generator + 221 Memory + 528 SQLite file + 528 SQLite memory = 5,214 cases**, no failures/skips. Core, Memory, MySQL and SQLite Release builds cover .NET 8/9/10; these and the affected test/dependency builds have zero warnings/errors. The broad unit run also covers the existing owned-command acquisition, dispatch and cleanup controls.

Reports are complete for their invocations/artifacts, with matching runner/DevTools assemblies and stable dirty-checkout provenance at the stated base. `ValidForEvidence=false` is retained: these are bounded functional results, not canonical full-provider or strict performance evidence. No source edits or benchmarks overlapped builds/tests. The [allocation-stage capture](W1%20Allocation%20Stage%20Review.md) completed and its assembly/raw receipts were verified before these changes or test builds began; it does not measure this correction.

| Report under `artifacts/` | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-owned-cleanup-occurrences-negative.json` | 5 / 8 | `77c0f9381865dc04a7202dafea1dff30b353b6612ece788187b55f70c81431bb` |
| `w1-owned-cleanup-occurrences-focused.json` | 8 / 8 | `3dc3b1aa592e06856f7172dcab58fd0efa70c6f6cc79290813fc722e64b46f09` |
| `w1-owned-cleanup-occurrences-unit.json` | 3866 / 3866 | `2ca9c2379d690ca6ba3020796da4a9a539cd712be3ea8824c878016f88d156af` |
| `w1-owned-cleanup-occurrences-generators.json` | 71 / 71 | `45af0b77f73361ca03250b36de793c123f6b8d235d4c27d43bddefe7a9f7c948` |
| `w1-owned-cleanup-occurrences-memory.json` | 221 / 221 | `df5826b90d37ca42c5cc949da1c8943e277408346f38d7f20bfc14f5582a834b` |
| `w1-owned-cleanup-occurrences-sqlite-file.json` | 528 / 528 | `1efbe6b81bc4f48c2b3dbe9174b6a867b1948dd736df311483c5ded75b9b0c25` |
| `w1-owned-cleanup-occurrences-sqlite-memory.json` | 528 / 528 | `1c93d792dc9cea8c3d7c5827cab9d75a6396578de8b55d9f39e505e82d7928a0` |

F20 remains reviewed only within its stated internal boundary, now including this correction. F19/F21 still require remaining cost review, final six-lane evidence and I/O/integration reconciliation. W0-F1's limited exception and W2/W3 native/public gates are unchanged. Local links/anchors, named tests and unmodified report counts/hashes are checked before integration. Planning pages remain excluded from DocFX; final-head CI and expected-head merge/tree verification belong in the PR.
