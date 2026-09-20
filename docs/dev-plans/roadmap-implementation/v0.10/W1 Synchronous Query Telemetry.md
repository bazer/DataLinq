> [!WARNING]
> W1 synchronous SQL logical-query reporting only. Synchronous mutation/transaction reporting, full recovery/classification integration, performance qualification and W1 closeout remain open.

# W1 Synchronous Query Telemetry

**Recorded:** 2026-09-20, after [command telemetry](W1%20Command%20Telemetry.md). This extends the existing [synchronous read diagnostics](W1%20Synchronous%20Read%20Diagnostics.md) and reuses the [async logical-query telemetry](W1%20Async%20Query%20Telemetry.md) recorder. The [completion audit](W1%20Completion%20Audit.md) remains authoritative for unfinished W1 requirements.

## Failure ownership and query boundaries

Synchronous fluent entity queries, typed/untyped scalar queries and exact-primary-key terminals previously finalized telemetry in ordinary finally blocks. A counter or duration listener could replace the original execution/cleanup error and prevent later reporters from running. Activity-start callbacks could throw before the query owned its activity. Entity enumeration also kept its query activity current across a yielded row, exposing it to consumer code.

[Select](../../../../src/DataLinq/Query/Select.cs) and the [SQL terminal backend](../../../../src/DataLinq/Linq/Planning/Sql/SqlQueryPlanBackend.cs) now use an internal [synchronous query collector](../../../../src/DataLinq/Execution/SyncQueryExecution.cs). Execution and owned cleanup settle before query reporting. Counter, duration, activity finalization and caller restoration are attempted independently, while original exceptions retain their identity and stack. Later failures retain encounter order; telemetry failures are local-finalization failures, and cleanup retains its distinct stage and disposal attribution.

The [shared query recorder](../../../../src/DataLinq/Execution/QueryExecutionTelemetry.cs) accepts the enclosing requested operation. A private Save owner remains Save, and an explicitly Unknown owner remains Unknown. The physical scalar/entity tag does not replace that requested kind. Exact-primary-key terminals now begin a Query admission and diagnostic invocation explicitly, including local Single/SingleOrDefault semantics. Provider and transaction IDs remain attached without retaining live authority in diagnostics.

Typed and untyped scalar calls share one direct synchronous implementation, with separate static dispatch delegates preserving their existing provider overloads and conversion policy. Commands remain owned by the existing read-resource boundary. No async facade, retry, provider capability expansion or public declaration is introduced.

The collector preserves current child completion/recovery facts from the same source when present; cleanup failure restricts recovery. It does not establish new provider trust or settle the remaining synchronous transaction recovery/state integration. In particular, a conversion thrown inside a legacy scalar provider overload retains that boundary's existing classification; this slice does not infer a native cause or rewrite all legacy conversion stages.

## Iterator activity and ownership

The [logical-query enumerator](../../../../src/DataLinq/Execution/SyncQueryEnumerable.cs) lives inside the existing ReadSequence admission and GuardedEnumerable call gate. Its activity starts on the first admitted MoveNext, becomes current only while query work runs, and restores the activity of each individual caller before returning. Resuming or disposing under another caller preserves the original query parent and restores that later caller. If an observer stops the caller during reporting, restoration clears Current instead of reintroducing that stopped activity. Activity.CurrentChanged failures are collected and still allow resource cleanup and query finalization.

The existing cache/key-query algorithms, SQL order replay and keyless materialization remain in Select. Typed entity conversion now happens inside the logical query, so an invalid projection remains primary through reporting. Keyless readers stay open across rows, and early disposal or a later read error disposes the reader and command before reporting. Compiler-iterator cleanup, callback-helper draining, overlap rejection and explicit private admission continue to use the existing owners.

Unused/disposed-before-start, rejected-admission and pre-canceled scalar calls emit no query telemetry. A naturally exhausted enumeration reports success; early disposal reports failure, including helper-drained escaped readers. Cache-only queries still count once without issuing commands. Disposing a completed enumerator does not report twice.

Aggregate counts are recorded once for an admitted logical query, including sampling/start failure. Meter outcome labels describe the execution/cleanup result known when reporting begins. A later observer failure can fail the call and mark the activity failed, but cannot retract a measurement already delivered. A stop or final caller-restoration callback can fail after activity tags have been observed. Independent attempts do not guarantee delivery to every BCL listener if an earlier subscriber throws.

Diagnostic scopes end on each iterator call; none remains ambient in consumer code. The shared meter recorder now retains the enclosing scope when creating its failure collector, while each callback uses a separate child scope. Fresh later-read facts survive, while earlier invocations' reused exception facts are rejected.

## Verification

[The 58 new nonparallel TUnit cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.SyncQueryTelemetry.cs) use real activity/meter listeners and controlled synchronous provider resources:

| Cases | Covered boundary |
| ---: | --- |
| 3 | Execution primary survives command cleanup and counter/duration/stop failures |
| 8 | Sampling/start failure across typed/untyped scalar, entity and exact-key terminal routes, before command creation |
| 4 | Natural exhaustion/early disposal under a later caller, including a throwing stop listener |
| 2 | Root/transaction terminal semantics remain primary through reporting |
| 12 | Each query family with no observers, normal observers or a failing counter; cleanup and reporting remain under admission |
| 2 | Root/transaction typed-entity conversion is part of logical-query failure handling |
| 3 | Unused, busy and pre-canceled queries emit nothing |
| 4 | Private Save and explicitly Unknown ownership through scalar/entity reporting |
| 2 | Primary cleanup retains Dispose attribution through stop failure |
| 3 | Throwing activity-current changes while yielding, resuming or disposing still finalize the query |
| 2 | Cache-only query counts with and without observers, without commands |
| 1 | Helper drains an escaped synchronous reader, retains reporting failure and does not commit |
| 2 | Reused reporter exceptions cannot import stale completion/cleanup facts |
| 4 | Root/transaction keyless readers retain later-read and both cleanup failures in order |
| 4 | Scalar conversion and matching/foreign/unrequested cancellation survive reporting |
| 2 | A caller stopped by a completion observer is not restored as current |

The original negative report is **0/6**, but only five cases are valid product reproductions: the iterator case incorrectly primed the provider cache for a transaction read and produced no row. The first implementation passed those five cases (**5/6**); the iterator fixture was corrected to return a real transaction row. The expanded **36/40** run exposed four test-setup errors: it attempted public query creation after taking private ownership. Query capture now precedes that admission. The **55/56** boundary run passed its assertions but the helper case incorrectly attempted public transaction disposal after transferring completion to its controlled helper; the helper remains the disposal owner. These intermediate failures are retained, not counted as product regressions or passing evidence.

The first complete captures passed **56/56** focused, **197/197** combined reporting and **4,617** broad tests. Final review then reproduced a further product edge in **0/2** cases: a completion observer could stop the caller, after which query finalization restored that stopped activity. Restoration now rejects stopped callers. The final `verified` captures pass **58/58** focused and **199/199** combined query/mutation/transaction/command reporting cases. Repeated broad verification passes **3,342 unit + 221 Memory + 528 SQLite file + 528 SQLite memory = 4,619**, with no skips. Focused tests are included in the unit total. Release core builds for .NET 8/9/10 and unit/Memory/compliance builds complete with zero warnings and errors.

All listed reports have complete counts, invocations and artifacts. Final runs exit zero; intermediate failed captures exit two. `ValidForEvidence=false` identifies bounded local checks, not release qualification. Reports remain local under `artifacts/`, unmodified and unuploaded. Final-head Latest CI and the expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-sync-query-telemetry-negative.json` | 0 / 6 | `041edd5d37d63850e5e5df150bcdf2f9de62c14b577399491d437b101350a33b` |
| `w1-sync-query-telemetry-first.json` | 5 / 6 | `0f8229147290739283b6582eee5a77bea12f12ee19ddfb0728357905906c43a7` |
| `w1-sync-query-telemetry-expanded.json` | 36 / 40 | `70a0c21123c1f46b1f7fa8b3ed858e53336be7208ead30954ffe055b836fad09` |
| `w1-sync-query-telemetry-boundaries.json` | 55 / 56 | `d90c084567f3b3f1a69318116f0ee81506b48c8faeea5449eed51b40a121716f` |
| `w1-sync-query-telemetry-final.json` | 56 / 56 | `9ca75af5ad52a9c4e7a93fe526143493bdb81bc292babf89d269154089a5eeef` |
| `w1-sync-query-telemetry-all-reporting.json` | 197 / 197 | `c847d7c20f7e5380cdd0254308a6ff23547a263d4795866389fd48ab08c090dd` |
| `w1-sync-query-telemetry-unit.json` | 3340 / 3340 | `9acf62c235dfe6b95f23faf598958a784673395be758ea169595db96b263f66d` |
| `w1-sync-query-telemetry-memory.json` | 221 / 221 | `1c2a31975137fdf7fd70a144d6b1849531ba4a13aca53fb3a0c7134dd8d013ec` |
| `w1-sync-query-telemetry-sqlite-file.json` | 528 / 528 | `5b44a059acea9f1b9cbe105232014098cadb510ad8e2876075f52e31b242dd77` |
| `w1-sync-query-telemetry-sqlite-memory.json` | 528 / 528 | `d321d9981279d8afa0214b6ba097d30a61ff3c7564405783293b78b4f3b2be7b` |
| `w1-sync-query-telemetry-stopped-caller-negative.json` | 0 / 2 | `9763a9a298301b704db719bc0076f7caafffecddb983a66338d3ff0c7222199b` |
| `w1-sync-query-telemetry-verified.json` | 58 / 58 | `000d34fa1b4be5b7ff54e06f28cd255cd8204fced0652b9070283f5b3d22bcfa` |
| `w1-sync-query-telemetry-all-reporting-verified.json` | 199 / 199 | `05e7209014aa698a3215826ce6756916be832450e3cf3755228aa792179ed6b6` |
| `w1-sync-query-telemetry-unit-verified.json` | 3342 / 3342 | `bd53e1bc6e730ad36f611a5b7794cf4d232c20225279b74baf067a97f0a523fc` |
| `w1-sync-query-telemetry-memory-verified.json` | 221 / 221 | `2489447ee14ed9a28671ba04041cb7893bd4ef429c06ebde23f10d6ed8c0a06a` |
| `w1-sync-query-telemetry-sqlite-file-verified.json` | 528 / 528 | `2d37f187be24593fa3c9d89ed21c0e4412e135178d2151f7e6893d3d5d84c2af` |
| `w1-sync-query-telemetry-sqlite-memory-verified.json` | 528 / 528 | `aa36818bc6c23bccc4b4d46bf3a6c69a4bed0dfd4454da4bc2db7c9f447053ac` |

## Remaining work and cost

This slice closes reporting composition for the inventoried synchronous SQL logical-query entry points above. It does not close synchronous mutation/transaction reporting, synchronous consumers of command no-dispatch evidence, complete cause/stage/recovery mappings, administrative telemetry, standalone raw-provider attribution or the final cross-family I/O audit. Native async adoption remains W2; public/packed consumers remain W3.

The new enumerable/enumerator wrapper and diagnostic scopes have successful-path costs. Failure collectors are still allocated only on failure, and no-listener activity restoration avoids creating a restoration scope, but these facts are not performance measurements. The earlier [W1 allocation checkpoint](W1%20Mutation%20Preflight%20Allocation%20Reduction.md) predates this runtime. Coordination-cost attribution, reductions or explicit explanations and all six strict .NET 10 W0 comparison lanes remain required on the final integrated candidate. No performance increase or W0-F1 limitation is waived here.
