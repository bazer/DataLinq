> [!WARNING]
> Internal 0.10 diagnostic evidence. W1 is not complete. Native provider fidelity, public consumers and final performance acceptance remain separate gates.

# W1 Materialization Failure Occurrences

**Recorded:** 2026-09-21, following [settled failure occurrences](W1%20Settled%20Failure%20Occurrences.md). This extends internal AAPI-26/91–95 evidence in the [completion audit](W1%20Completion%20Audit.md).

## Why invocation scopes were insufficient

A single buffered operation can advance and materialize many rows. An exception reported and caught during an earlier row, reader call or successful cleanup can later be thrown without a new report. Invocation ancestry alone still considers the earlier attachment eligible, so the later local materialization failure could inherit Timeout, Commit and unrelated cleanup details.

The same issue occurs when a query transform completes an inner transform successfully before failing locally, or when single-reference relation completion runs after its buffered reader has already closed. A separate issue affected continuation recovery: final query observers could replace an exception's attachment after the primary had been captured, and the enumerator reread that attachment for completion/recovery policy.

## Report checkpoints and captured recovery

[ExecutionFailureContexts](../../../../src/DataLinq/Execution/ExecutionFailure.cs) now stamps each attachment with an internal process-local sequence. Capturing a work-boundary checkpoint is a volatile integer read, with no per-row object or AsyncLocal write. When that work throws, an attachment from before its checkpoint is retired with compare/exchange. Reports produced during the current work survive; normal invocation-scope filtering still applies. Previously returned contexts and failure collectors retain their immutable snapshots. The sequence is neither public identity nor admission authority.

[Buffered reads](../../../../src/DataLinq/Execution/AsyncBufferedRead.cs) and [async enumeration](../../../../src/DataLinq/Execution/AsyncReaderEnumerable.cs) take checkpoints between acquisition, reader advancement, row conversion and local completion. [Nested query transforms](../../../../src/DataLinq/Execution/AsyncReaderTransform.cs) and [relation completion](../../../../src/DataLinq/Cache/TableCache.AsyncRelations.cs) also separate completed inner work from their next local callback. Retiring an earlier attachment lets the existing reporting boundary classify the new failure; it does not invent provider effects or authorize recovery.

ExecutionFailures retains the primary context captured alongside its exception dispatch information. Enumeration uses that snapshot for an already-reported continuation or pre-admission failure, before cleanup/observers can replace the direct exception lookup. A late observer cannot change NotAttempted to Committed or erase the original rollback-only restriction. Original exception identity, ordered deduplication, cleanup ownership and source assessment remain unchanged.

## Verification

[Materialization cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.MaterializationOccurrences.cs) and [checkpoint controls](../../../../src/DataLinq.Tests.Unit/Core/ExecutionFailureOccurrenceTests.cs) add 30 TUnit cases:

| Cases | Boundary |
| ---: | --- |
| 16 | Buffered read, streaming reader, grouped buffer and continuation row conversion; earlier reader or earlier-row reports; stale/current pairs |
| 4 | Buffer completion after row aggregation and continuation completion after command cleanup |
| 2 | Captured single-reference relation completion after buffered cleanup |
| 2 | Nested transforms, with a successful inner transform followed by a failing outer transform |
| 2 | Late query observer replacement of a continuation failure's report, with and without rethrowing the same primary exception |
| 4 | Earlier/current attachment retirement, unrelated exception isolation, retained collector/snapshot facts, invocation-scope filtering and a zero-allocation checkpoint capture control |

The unmodified runtime passed **12/22** first controls; ten failed the precise MaterializationError assertion because earlier Timeout reports survived. The first correction passed **22/22**. A staged expanded control deliberately retained the old nested-transform behavior and late direct-context lookup while keeping the other corrections: **27/30**, with one stale nested-transform failure and two incorrect completion outcomes. The final correction passes **30/30**.

Final broad local results are **3,737/3,737 unit**, **221/221 Memory**, **528/528 SQLite file** and **528/528 SQLite memory**: **5,014 passes**, no skips. Core, SQLite and MySQL Release builds cover .NET 8/9/10; unit, Memory and compliance builds also have zero warnings/errors.

The test draft needed a missing namespace import corrected before its first behavioral run; failed builds are not counted as reproductions. Reports remain local, unmodified and unuploaded. ValidForEvidence=false denotes bounded local verification, not release qualification. Failed controls exit two and successful runs exit zero. No source edits or benchmarks overlapped local builds/tests. Planning docs are excluded from DocFX; local links, anchors and report counts/hashes are verified. Exact-head CI and expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-materialization-occurrences-expanded-negative.json` | 27 / 30 | `4b50171fc4d9dc575fc5ff463656c3894b9b1b4d3f2a899d2941dbf5907ef5e3` |
| `w1-materialization-occurrences-first.json` | 22 / 22 | `93725a1e9496486894e526850fc123b65c06fe1a52854d15e653d7dc6e05a40e` |
| `w1-materialization-occurrences-focused.json` | 30 / 30 | `7b7bab56241dbff7b45839cf88f97a22f9cde1094c9d6562925a60eca526b9da` |
| `w1-materialization-occurrences-memory.json` | 221 / 221 | `544be20355573278d25b60d27b86e51ee59a35d156d3b82a6169e34d0327e0b6` |
| `w1-materialization-occurrences-negative.json` | 12 / 22 | `fc2f5b969680bdd2aa083b09af20d444f000c5afdf2973eb5a1a39b75cd564a8` |
| `w1-materialization-occurrences-sqlite-file.json` | 528 / 528 | `6fe0bb6a42baa1d1ef6a101711dbfcfedbf649a25111e1ecd6cc7426272bbcc8` |
| `w1-materialization-occurrences-sqlite-memory.json` | 528 / 528 | `c47bcf5dd3b609ec59068a2f7f27a933b5e176d13c684a3dffdcba3ea329b007` |
| `w1-materialization-occurrences-unit.json` | 3737 / 3737 | `83345d085e1af19eabb28e16970a77378155d627a111631a5e06afd941af1f0a` |

## Preflight audit and remaining cost

[AAPI-92](Async%20Public%20API%20Decisions.md#aapi-92-failure-classification-uses-independent-public-enums) does not require context on every argument error. This slice does not add blanket catches around factory binding or source validation, or infer cause from exception type. Existing [scalar pre-cancellation/capability tests](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.AsyncScalar.cs), [relation validation/warm-holder tests](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.AsyncRelations.cs) and [command telemetry preflight tests](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.CommandTelemetry.cs) remain in the passing unit run. This is bounded evidence, not a completed family-wide preflight classification audit.

The microcontrol measures **zero bytes across 10,000 warmed checkpoint captures** on the current test thread. It does not measure complete query throughput, iterator/async-state size or the cost of failure reporting. Each attachment now allocates a small stamped report and increments the sequence; a failure collector retains one additional context reference. Successful execution adds checkpoint reads/state, not a per-row scope allocation. Earlier diagnostic scopes remain in place.

The subsequent [functional and I/O audit](W1%20Functional%20and%20IO%20Audit.md) reproduces and corrects stale attribution between individual model constructors and replaces broad remaining-work descriptions with F01–F21 review states. Cell/higher synchronous boundaries, complete classification/preflight/I/O review and internal readiness remain review obligations; all six strict .NET 10 W0 lanes remain open. Measure and explain/reduce complete coordination costs before accepting performance. Native implementation remains W2 and public/packed consumer evidence remains W3. W0-F1's limited internal exception and the published 0.9.2 compatibility baseline are unchanged. No packages are published.
