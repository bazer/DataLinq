> [!IMPORTANT]
> W1 concerns internal orchestration and controllable-provider evidence. It does not establish native async support, freeze the public API, close W0-F1 or approve the 0.10 release.

# W1 Closeout

**Recorded:** 2026-09-23. Runtime under review: `a435d0b428937040bfd02210e67eb1219c9ed6f7`, based on merged `3bc97df2feb3d17d7a05f965398cb67989b09edc` ([PR #226](https://github.com/bazer/DataLinq/pull/226)). [PR #227](https://github.com/bazer/DataLinq/pull/227) contains the final iterator reduction, this evidence and the integration receipt. Its final body records the exact reviewed head, all required CI jobs, guarded merge and tree comparison. The [functional audit](W1%20Functional%20and%20IO%20Audit.md) supplies the detailed assertion review; this page reconciles its remaining F19/F21 work.

## Finished iterator correction

[GuardedEnumerable](../../../../src/DataLinq/Execution/GuardedEnumerable.cs) previously opened an [ExecutionFailureScope](../../../../src/DataLinq/Execution/ExecutionFailureScope.cs) on every `MoveNext` and `Dispose`, including calls that could no longer invoke the underlying iterator. The final change enters the call gate first, checks completion, then opens a diagnostic scope for actual work. Helper-drained disposal returns without another scope. No provider state, cache, pooling, failure policy or public declaration changes.

The gate must remain ahead of the finished check: `DisposeCore` marks the iterator finished before invoking cleanup, so a second call during that cleanup must still reject overlap. Actual moves and cleanup retain their diagnostic scopes; helpers retain closed admission and once-only draining. Restoring the scope before releasing the call gate keeps restoration inside the admitted call.

[Six new TUnit cases](../../../../src/DataLinq.Tests.Unit/Core/CompletedGuardedEnumerableTests.cs) cover both naturally exhausted and explicitly disposed iterators, helper-drained disposal, overlap during terminal movement/disposal, and separate real move/cleanup diagnostic occurrences. Against the previous runtime, exactly three allocation controls fail and all three overlap/isolation controls pass. After the change all six pass. The tests warm the calls, then measure 10,000 repetitions without asynchronous assertions inside the measured interval. Finished-call pairs and helper-drained disposal allocate zero managed bytes. The old controls measured 9,120,000 bytes per 10,000 call pairs and 4,560,000 per 10,000 disposals under TUnit's ambient context; those absolute sizes are test-context-specific, not a production B/op claim.

## Functional evidence

The initial broad run is **Debug**, not Release: its JSON configuration and executed DLL paths are authoritative. It passes 5,226 cases with zero failures or skips: 71 generator, 3,878 unit, 221 Memory, 528 SQLite-file and 528 SQLite-memory. The negative and focused runs are also Debug. The initial draft PR's Release label was corrected before integration. The provider Release builds separately pass for .NET 8, 9 and 10 with zero warnings/errors.

The subsequent explicit **Release** run on clean `a435d0b4` also passes **5,226/5,226**, with zero failures/skips and the same suite totals. Its run ID is `20260923T164555226Z-86d025d2f3764b9387c2990f64275b71`; configuration, clean matching runner assemblies and unchanged start/end checkout are verified. Independent TRX checks find all six new controls in both broad runs, and confirm the three intended negative-control failures. All four summaries retain `ValidForEvidence=false`: these bounded local runs are not full release-matrix evidence.

| Evidence | Result | SHA-256 |
| --- | --- | --- |
| `artifacts/w1-finished-iterator-negative.json` | 3 passed / 3 failed; expected old-runtime allocation failures | `41207f491a6328edebc4b4b5a2e7077883d7e7812ef6860b370737560c5f77f1` |
| `artifacts/w1-finished-iterator-focused.json` | 6 passed | `a842c7dd2dc3f44530703eaef9b3584292e422fac6fd26da51e74dd66be5229e` |
| `artifacts/w1-finished-iterator-quick.json` | 5,226 passed, Debug | `e9f606ea04aa620aadfac9abf4c5c90864e734bccc23a66a62f11f06ef05f183` |
| `artifacts/w1-finished-iterator-quick-release.json` | 5,226 passed, Release | `4518ed6e25fcafd2172ceb4abf6ca5086e6940af2a10040830d4b5011a2fdc48` |

These are bounded functional receipts, not a full release-matrix approval. The original working-source runs have `ValidForEvidence=false`; that flag is preserved. A first unquoted comma-separated target argument failed selection before tests and produced no summary. Its log, `artifacts/w1-finished-iterator-quick-selection-failed.log`, remains a failed invocation, not a product test failure or a counted test run.

## Final canonical performance evidence

All **90 canonical rows and 126 raw artifact receipts** are verified on clean runtime commit `a435d0b4`. Normalized telemetry and operation counts match both W0 and the preceding W1 checkpoint. There are **13 allocation warnings, one latency warning and seven noisy latency rows**. The overall row-status table has six noisy rows because the IN/file row is classified as an allocation warning while its latency is noisy. `ReviewRequired` flags remain unchanged; the disposition below is separate from evidence validity.

All six lanes use the frozen `w0-7e36614b-<lane>.json` baseline and the current benchmark harness with `--profile heavy --release-evidence`, on .NET 10.0.12 / SDK 10.0.401 / BenchmarkDotNet 0.15.8, Windows x64, Intel Family 6 Model 140 Stepping 1, eight logical processors. Histories and comparisons are complete and valid, with clean matching runner/target/assembly provenance. Each live benchmark DLL was checked before the next build, and every retained raw artifact's length and SHA-256 was verified. No local test/build/profiler overlapped timing measurement; the controller's between-lane harness builds are part of the recorded protocol.

| Lane | Rows | Stable | Improved | Warning | Noisy | Allocation warnings | Latency warnings | Telemetry changes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| phase2-watch | 6 | 0 | 4 | 0 | 2 | 0 | 0 | 0 |
| phase3-query-hotpath | 6 | 1 | 1 | 4 | 0 | 4 | 0 | 0 |
| v09-query-backend | 12 | 4 | 5 | 2 | 1 | 2 | 1 | 0 |
| v09-memory-read | 9 | 6 | 3 | 0 | 0 | 0 | 0 | 0 |
| allocation-regression | 9 | 2 | 2 | 5 | 0 | 5 | 0 | 0 |
| allocation-stages | 48 | 20 | 23 | 2 | 3 | 2 | 0 | 0 |

Allocation warnings against W0 remain explicit:

| Lane / workload / provider | W0 B/op | W1 B/op | Allocation delta |
| --- | ---: | ---: | ---: |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-file | 27770.88 | 34877.44 | 25.59% |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-memory | 28528.64 | 35624.96 | 24.87% |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-file | 21760.00 | 26890.24 | 23.58% |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-memory | 22292.48 | 27361.28 | 22.74% |
| v09-query-backend / SQL adapter scalar Any / sqlite-file | 7587.84 | 8663.04 | 14.17% |
| v09-query-backend / SQL adapter scalar Any / sqlite-memory | 7956.48 | 9041.92 | 13.64% |
| allocation-regression / CRUD workflow batch / sqlite-memory | 65884.16 | 78346.24 | 18.92% |
| allocation-regression / CRUD workflow small / sqlite-memory | 65904.64 | 78264.32 | 18.75% |
| allocation-regression / Cold primary-key fetch / sqlite-memory | 6256.64 | 7608.32 | 21.60% |
| allocation-regression / Cold relation traversal / sqlite-memory | 13352.96 | 17111.04 | 28.14% |
| allocation-regression / Update employees / sqlite-memory | 15626.24 | 17561.60 | 12.39% |
| allocation-stages / Cold typed-ID exact terminal / sqlite-file | 5027.84 | 6246.40 | 24.24% |
| allocation-stages / Cold typed-ID exact terminal / sqlite-memory | 5099.52 | 6461.44 | 26.71% |

Flagged or noisy latency rows remain explicit:

| Lane / workload / provider | W0 mean +/- error, us | W1 mean +/- error, us | Time delta | Maximum noise | Latency status |
| --- | ---: | ---: | ---: | ---: | --- |
| phase2-watch / Warm primary-key fetch / sqlite-file | 6.7130 +/- 1.4090 | 5.5440 +/- 1.1080 | -17.41% | 20.99% | noisy |
| phase2-watch / Warm primary-key fetch / sqlite-memory | 5.4910 +/- 1.3460 | 5.4200 +/- 1.1470 | -1.29% | 24.51% | noisy |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-file | 123.9000 +/- 26.8200 | 114.6700 +/- 12.1300 | -7.45% | 21.65% | noisy |
| v09-query-backend / Invocation bind scalar/local sequence / sqlite-memory | 0.1652 +/- 0.0074 | 0.1885 +/- 0.0444 | 14.10% | 23.55% | noisy |
| v09-query-backend / SQL adapter scalar Any / sqlite-file | 20.4909 +/- 0.7423 | 24.0015 +/- 1.7795 | 17.13% | 7.41% | warning |
| allocation-stages / Mutation command preparation / sqlite-file | 2.7729 +/- 0.3632 | 3.1285 +/- 0.6578 | 12.82% | 21.03% | noisy |
| allocation-stages / Warm typed-ID exact terminal / sqlite-file | 7.9779 +/- 1.2873 | 7.4632 +/- 1.4941 | -6.45% | 20.02% | noisy |
| allocation-stages / Warm typed-ID exact terminal / sqlite-memory | 5.3633 +/- 1.3383 | 6.1115 +/- 2.1353 | 13.95% | 34.94% | noisy |

BenchmarkDotNet's minimum-iteration-duration and multimodal warnings remain in the histories and raw reports. Neither the harness nor the comparator threshold is changed to improve this presentation.

Histories are `artifacts/benchmarks/history/w1-a435d0b4-<lane>.json`; comparisons are `w1-a435d0b4-vs-w0-<lane>.json`. Verification receipts are `artifacts/w1-a435d0b4-<lane>-verification.json`. The corresponding frozen W0 hashes are unchanged from the [previous canonical checkpoint](W1%20Six-Lane%20Performance%20Checkpoint.md). Raw paths are enumerated in each history rather than inferred from nearby files.

| Lane / run ID | Seconds | Raw receipts | History SHA-256 | Comparison SHA-256 |
| --- | ---: | ---: | --- | --- |
| phase2-watch / 20260923-155511531-c3b9db046de647de9299a375e5db9de9 | 233.5667028 | 12 | 95c8d9d24091f5d4e51f230caa0728e9068c966a8a2af2c72618956e1f6501da | c232dabf97cd777e8c81c97cf6fbb22e870b0d63115f52dd0fd7b28bc6850572 |
| phase3-query-hotpath / 20260923-155905849-5d8ddd8d49cb404d906f2580ecd0ec04 | 127.6311751 | 12 | 7a97b996f7db43a88b4d9b5d044a54ffd677074c7d454be59f6c41793c427dfe | 9fb28a7eb70bf00781d3b536ccfc28bf69823d86672b250a10aad88b0b7dc34a |
| v09-query-backend / 20260923-160114097-e1f8f097d85b4985bef00d22b2ae943a | 451.1591061 | 18 | a944ecd875c4a26825b7ac292854853debf6187b5058949b015fa91ca1000e00 | dd749e859310ed9406884f5148f6e86285b744422728214042cc3b869aac80e2 |
| v09-memory-read / 20260923-160846037-dc0153226ffc4e85ae18366e5eeb82a7 | 276.6117527 | 15 | 42705c0f628fb7582a5a5afca33b5cbaef9101f95a032613b496a866a93feeda | a47b11322958741ecd51f564b741be85e61bf02f8204159c6564805b5c92a144 |
| allocation-regression / 20260923-161323426-088f030c62c9444c8eaca92e941cc0da | 244.0823138 | 15 | 456c4aaa5cddb55a322da54bcd69975ac46da98402e4bc4f9879906760b8c2d9 | 280625cb4b09ec164c99f80fcfe1898160556bd521104b449e4fabc8c1330b31 |
| allocation-stages / 20260923-161728158-c7819be9457b453988edd6857671b7b0 | 1684.1597142 | 54 | e088e8750b06cf18a8845ac2f2510a9736185288ac517b58654b281f52fc65c4 | a2ae01a384f352a32846d250bce7a6ef68d77d3fc29b7680f92009d0212591eb |

The [earlier 90-row checkpoint](W1%20Six-Lane%20Performance%20Checkpoint.md), [nine-workload allocation traces](W1%20Paired%20Allocation%20Diagnostics.md), [paired timing follow-up](W1%20Paired%20Timing%20Follow-up.md) and [ABBA timing controls](W1%20Timing%20Controls.md) retain their original identities and every warning. They are complementary evidence, not replacements for unfavorable current rows. The previous-runtime binary archive, `artifacts/benchmarks/bin-snapshots/w1-e7660b5b`, preserves all 92 files/94,108,677 bytes with per-file checks in `artifacts/w1-e7660b5b-binary-snapshot.json` (SHA-256 `2d29dd8b7e8360cccbbdd7dd230c13e4e5735ffd7d74fc39bad53a1a05fd78e5`). It is an archive, not a newly measured baseline.

## F19 cost disposition

The accepted [D10-6 policy](Implementation%20Order%20and%20Integration%20Plan.md#d10-6-performance-evidence-policy) and [RE10-5 criteria](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md#re10-5-performance-and-telemetry) require material costs to be explained and meaningful latency to be dispositioned. They do not require zero overhead or exact reconciliation of sampled allocation ticks with net B/op. The following dispositions retain the measured costs for internal W1 integration; they are not a claim that the costs are free, that all timings are equivalent, or that release sign-off has occurred.

The retained work is visible in [command telemetry](../../../../src/DataLinq/Database/DatabaseAccess.CommandTelemetry.cs), [guarded source reads and private owned services](../../../../src/DataLinq/Mutation/DataSourceAccess.cs), [logical query enumeration](../../../../src/DataLinq/Execution/SyncQueryEnumerable.cs) and [independent reader/command cleanup](../../../../src/DataLinq/Execution/ReadCommandResources.cs). The per-model occurrence checkpoint is in [InstanceFactory](../../../../src/DataLinq/Instances/InstanceFactory.cs). The functional audit and six final controls establish why those boundaries cannot simply be removed from live callbacks.

| Workload family | Evidence and cause | W1 disposition |
| --- | --- | --- |
| Ordinary/scalar-adapter Any | Command startup, dispatch, owned cleanup and outer query scopes isolate custom getters, provider and observer failures; SQLite visibility setup uses its own command telemetry. The allocation probes identify the scope/context family. The earlier +54.02% ordinary Any timing warning does not recur under 50-warmup/30-iteration ABBA controls; the adapter's paired +1.85/+2.64 us is a real retained cost, not excused by the comparator's threshold. | Retain the tested diagnostic boundaries and their bounded scalar cost. No scalar result/command-count reduction is used to obtain a faster result. The old large warning remains documented as protocol-sensitive, not proof of equivalent runtime performance. |
| IN/non-PK entity reads | `DataSourceAccess.ReadSequence` now wraps database-root and private iterators; `GuardedEnumerable`, `SyncQueryEnumerable` and `ReadCommandResources` isolate advancement, conversion, reporting and owned cleanup. Allocation traces contain scope/ExecutionContext/AsyncLocal maps plus iterator, delegate and gate families. Telemetry matches. ABBA controls retain meaningful read-path increases, including IN/file +11.16%/+16.93%. | Retain explicit lifetime and diagnostic isolation. This is additional managed work, not fewer database rows or an assumed driver regression. Completed no-op scopes are removed; scopes around live callbacks remain. Retained read costs must stay visible in later native/public and final-release comparisons. |
| Cold primary/typed keys and cold relations | Owned row services carry explicit operation identity; loader/materialization/cache wrappers cannot be shared between owners. Read scopes and enumerator lifetimes add allocation. Cold typed-ID/memory controls retain +15.47%/+23.02%; cold key/relation probes retain additional managed allocation. Cache/row/materialization telemetry remains identical. | Accept the cost of the internal ownership/recovery contract, preserving cache identity, generation-safe publication and cancellation behavior. No speculative service sharing or plan cache is introduced. |
| Update and CRUD | Command, private hydration, finalization and transaction failure boundaries add scoped diagnostic work. Paired sampled scope/context bytes are of the same scale as the net allocation increases, with savings in other families; their difference is not an unexplained exact byte balance. ABBA timing controls retain updates +5.57%/+5.00%, small CRUD +4.39%/+3.69% and batch CRUD +3.43%/+9.81%. | Retain the measured correctness trade-off. Earlier preflight and unobserved-activity reductions remain in place, as does the finished-iterator reduction. No retry, weakened failure attribution or changed mutation/transaction counts is accepted. |
| Warm cache hits, Memory, provider initialization and startup | Compare the calibrated warm-key lane separately from the shorter watch lane. Memory's accepted in-process read subset has no provider facade. Current and prior receipts retain operation-count/telemetry identity and the previously removed preflight allocation. | Preserve these controls and their actual results. No SQL/provider-wide speed claim is inferred from Memory or startup improvements. |
| Tiny stage timing and order-sensitive controls | Canonical key and mutation-state fixtures execute unchanged local work across the provider labels; this does not prove identical timing. ABBA typed-key/memory changes are about 49–70 ns, state capture about 31–66 ns, and final-drift/file about 0.5–1.5 ns. Known-miss materialization adds a real allocation-free per-factory occurrence checkpoint; controls retain about 37–49 ns. Command-preparation/file has a large reverse-order tail (median 1.3244 us, standard deviation 4.1985 us, maximum 36.9191 us). | Retain the small bounded checkpoint cost. Treat other tiny/provider-label-sensitive stage differences and tail-dominated preparation results as unresolved micro-timing, not established material end-to-end regressions. Keep them on the watch list; do not manufacture statistical significance from non-independent probe invocation counts or trim unfavorable samples. |

The final non-noisy latency warning is SQL-adapter Any/file: 20.4909 +/- 0.7423 us at W0 versus 24.0015 +/- 1.7795 us now, +3.5106 us / +17.13%. Retain this conservative current cost under the scalar diagnostic-boundary disposition above; do not attribute it to the finished-iterator change. The seven noisy cases are warm primary-key watch/file and memory, IN/file, invocation binding/memory, mutation command preparation/file, and warm typed-ID terminals/file and memory. All seven reported mean-plus/minus-error ranges overlap W0; neither regression nor equivalence is established by those rows. The earlier positive ABBA read/mutation controls remain part of the accepted cost record even where the final canonical run is faster or stable. No new stage latency warning remains in the 48-row run.

The [internal coordination measurements](W1%20Coordination%20Measurements.md) cover 13 workloads, including uncontended gates, admission, helper closure, immediate/suspended calls and linked tokens. Their no-I/O figures are not whole-query overhead. Scope identity remains immutable, diagnostic-only and separate from admission authority; pooling or replacing it with mutable ambient state would invalidate the tested occurrence contract. F19 is discharged by these recorded explanations, retained trade-offs and complete comparisons, not by changing warning thresholds or declaring every row green. Issue #26 remains open. Release notes and the final release manifest must carry the retained trade-offs when W9 evaluates the completed feature.

## F21 requirement and I/O reconciliation

The [functional audit](W1%20Functional%20and%20IO%20Audit.md) records the actual assertion reviews for F01–F18 and F20. The final iterator change rechecks F05/F20 with the six controls above, and all reviewed families remain in the broad test run. The current core/Memory source scan finds no `Task.Run` provider facade: `ThreadWorker` and `CacheCleanupScheduler` own maintenance work, while `ImmutableRelation` and `ImmutableForeignKey` synchronously wait for their shared load slot. Those matches are explicitly classified, not hidden by an empty search claim.

| W0 inventory boundary / accepted contract | W1 expression and reviewed proof | Responsibility remaining after W1 |
| --- | --- | --- |
| Open, scalar/non-query and reader acquisition; F02–F06/F10/F20 | Explicit `IAsyncDatabaseAccess` and reader capabilities; I/O-free validation before cancellation; borrowed/owned command distinction; lazy private resource publication; command-specific dispatch evidence and immutable failure collection. Functional-audit capability, initialization, raw and diagnostic reviews cover each boundary. | W2 binds real opens/setup/begin/commands/readers and establishes native cancellation, connection trust and cleanup. Merely inheriting ADO.NET async members is insufficient. |
| Row advancement, decoding, eager/buffered results, early cleanup; F05/F10/F11/F20 | `AsyncReaderEnumerable`, owned readers/commands, captured fluent/raw/model reads, combined tokens and cleanup before complete-result publication. Tests cover actual suspension, early disposal, reader/command failures, buffer ownership and stale exception reuse. | W2 actual provider reader behavior; W3 emitted public sequence/terminal declarations and packed consumers. |
| Expression/prepared/fluent/raw capture; F10/F11 | Private captured query state, prepared invocation snapshots, provider/model keys, projections/terminals and tracked-vs-raw recovery distinctions. The query review corrects projected-array snapshots and proves warm transaction-local cache hits. | W3 complete public signature/compatibility verification; unsupported fluent mutations stay unsupported. |
| Key loading, materialization and relation graphs; F01/F11/F12 | Canonical key/row batches, owned buffers, source-specific holders, coordinated sync/async loads, independent waiter cancellation, generation checks, cardinality and complete cache/index publication. Converted/binary/composite keys and keyless relation targets are covered. | W2 native row loading; W3 generated required/optional async navigation and packed relation consumers. Existing accepted synchronous relation breaks remain recorded. |
| Mutation capture, private hydration, transaction completion and callbacks; F03/F04/F06–F09 | One explicit owner, captured values and finite batches, reservations, single-attempt mutation lifecycle, once-only callbacks, borrowed helper handles, atomic closure/draining, native-outcome-neutral internal completion and independent cooperative rollback budget. No implicit retry or abandonment. | W2 native first-use/reentrancy/completion certainty and rollback feasibility; W3 callback/mutation/completion bindings; W4 host options. Existing transaction attachment retains consuming ownership. |
| Metadata and availability probes; F13/F14 | Captured registry/options/identity, fresh owned sessions, sequential commands with timeout/token propagation, complete buffered publication and explicit availability-only mapping. Empty-runtime metadata is distinct from legacy import comparison policy. | W2 actual parsers, effective database/keeper identity, no-creation behavior, all native command timeouts and failure classification. W5 comparison/Include/readable-empty result policy; W3 public factories. |
| Provisioning, journal mode and constructor setup; F15/F16 | Captured scripts/registration, explicit capability and owned resource handoff, confirmed-command cancellation, independent cleanup and partial-effect handling. SQLite keeper/WAL and MariaDB constructor-version probes retain their accepted synchronous exclusions. | W2 DDL/keeper/actual mode/interruption evidence and server metadata owning lifetimes. W3 factory/default compatibility. No destructive cleanup or async construction promise. |
| Owning-root disposal; F17 | Shared sync/async once-only disposal state, ordered independent cleanup and settlement of owned maintenance. Self-join rejects; dependent application readers/transactions are not implicitly drained or canceled. | W2 native resources; W3 public inherited interface slots and custom providers; W4 DI ownership. |
| Memory, local composition and legacy/custom extensions; F02/F11/F18 | Immediate, cooperatively cancelable supported Memory reads; validation before cancellation; explicit missing capability. Local encoding, SQL construction, mutable editing, cache clearing and notifications retain synchronous contracts. Existing public virtual sync dispatch has compatibility controls. | W3 real old/custom packed consumers, generated overloads and Memory-only package graph. No new Memory writes, transactions, prepared SQL, navigation or SeedAsync scope. |
| Cross-cutting reporting, performance and integration; F19–F21 | F20's immutable identities, occurrence checkpoints, primary/secondary ordering, no-dispatch restrictions and observer-failure preservation are reviewed across every preceding family. This page supplies F19 disposition and the final broad/CI/merge integration receipt. | Native cause proof remains W2, public diagnostic mapping remains W3, final performance/telemetry and release documentation remain W9. |

No inventoried family is silently dropped or called native-ready. The 0.9.2 package compatibility baseline remains distinct from W0's frozen development runtime. The [limited W0-F1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception) authorizes this internal work only; the upstream SQLite limitation and every native/public acceptance gate remain open.

## Complete current row record

The previous W1 column is the clean `726810c6` six-lane checkpoint. Its production runtime is unchanged through the documentation-only `3bc97df2` integration. Current-minus-previous allocation differences are descriptive observations, not a new paired causal experiment: coarse end-to-end B/op can move slightly on unaffected workloads. The focused zero-allocation controls prove the no-op reduction. All W0 comparisons, including unfavorable and noisy rows, appear below.

| Lane / workload / provider | Previous W1 B/op | Current W1 B/op | Change B/op | W0 mean, us | Current mean +/- error, us | W0 delta | Status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| phase2-watch / Warm primary-key fetch / sqlite-memory | 1812.48 | 1812.48 | 0.00 | 5.4910 | 5.4200 +/- 1.1470 | -1.29% | allocation=stable; latency=noisy |
| phase2-watch / Warm primary-key fetch / sqlite-file | 1812.48 | 1812.48 | 0.00 | 6.7130 | 5.5440 +/- 1.1080 | -17.41% | allocation=stable; latency=noisy |
| phase2-watch / Startup primary-key fetch / sqlite-file | 53483.52 | 53504.00 | 20.48 | 284.6420 | 230.5860 +/- 5.2110 | -18.99% | allocation=improved; latency=improved |
| phase2-watch / Startup primary-key fetch / sqlite-memory | 57384.96 | 57384.96 | 0.00 | 414.5200 | 328.5960 +/- 8.5660 | -20.73% | allocation=improved; latency=improved |
| phase2-watch / Provider initialization / sqlite-file | 349132.80 | 349122.56 | -10.24 | 641.1500 | 545.6040 +/- 15.5170 | -14.90% | allocation=stable; latency=improved |
| phase2-watch / Provider initialization / sqlite-memory | 352757.76 | 352389.12 | -368.64 | 734.3020 | 625.2810 +/- 12.3850 | -14.85% | allocation=stable; latency=improved |
| phase3-query-hotpath / Repeated scalar Any / sqlite-file | 18421.76 | 18421.76 | 0.00 | 88.2500 | 84.5700 +/- 14.3500 | -4.17% | allocation=stable; latency=stable |
| phase3-query-hotpath / Repeated scalar Any / sqlite-memory | 18636.80 | 18636.80 | 0.00 | 117.6400 | 98.7600 +/- 17.0500 | -16.05% | allocation=stable; latency=improved |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-file | 35624.96 | 34877.44 | -747.52 | 123.9000 | 114.6700 +/- 12.1300 | -7.45% | allocation=warning; latency=noisy |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-file | 27422.72 | 26890.24 | -532.48 | 128.5700 | 129.2400 +/- 21.7200 | 0.52% | allocation=warning; latency=stable |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-memory | 36229.12 | 35624.96 | -604.16 | 141.6800 | 145.5100 +/- 16.7200 | 2.70% | allocation=warning; latency=stable |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-memory | 27955.20 | 27361.28 | -593.92 | 154.2500 | 147.4600 +/- 19.2100 | -4.40% | allocation=warning; latency=stable |
| v09-query-backend / Invocation bind scalar/local sequence / sqlite-file | 307.20 | 307.20 | 0.00 | 0.1778 | 0.1467 +/- 0.0106 | -17.49% | allocation=stable; latency=improved |
| v09-query-backend / Invocation bind scalar/local sequence / sqlite-memory | 307.20 | 307.20 | 0.00 | 0.1652 | 0.1885 +/- 0.0444 | 14.10% | allocation=stable; latency=noisy |
| v09-query-backend / SQL request/capability preparation / sqlite-file | 0.00 | 0.00 | 0.00 | 0.2323 | 0.1920 +/- 0.0077 | -17.35% | allocation=stable; latency=improved |
| v09-query-backend / SQL request/capability preparation / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.2409 | 0.1937 +/- 0.0103 | -19.59% | allocation=stable; latency=improved |
| v09-query-backend / Template freeze/validation / sqlite-memory | 2560.00 | 2560.00 | 0.00 | 0.9124 | 0.7807 +/- 0.0132 | -14.43% | allocation=stable; latency=improved |
| v09-query-backend / Template freeze/validation / sqlite-file | 2560.00 | 2560.00 | 0.00 | 1.1444 | 0.8088 +/- 0.0569 | -29.33% | allocation=stable; latency=improved |
| v09-query-backend / Expression parse/template/initial bind / sqlite-file | 9533.44 | 9533.44 | 0.00 | 8.6850 | 7.8831 +/- 0.4448 | -9.23% | allocation=stable; latency=stable |
| v09-query-backend / Expression parse/structural template / sqlite-memory | 9472.00 | 9472.00 | 0.00 | 8.3210 | 8.2153 +/- 0.4978 | -1.27% | allocation=stable; latency=stable |
| v09-query-backend / Expression parse/structural template / sqlite-file | 9472.00 | 9472.00 | 0.00 | 8.6626 | 8.4096 +/- 0.8612 | -2.92% | allocation=stable; latency=stable |
| v09-query-backend / Expression parse/template/initial bind / sqlite-memory | 9533.44 | 9533.44 | 0.00 | 8.8812 | 8.9211 +/- 0.6957 | 0.45% | allocation=stable; latency=stable |
| v09-query-backend / SQL adapter scalar Any / sqlite-file | 8663.04 | 8663.04 | 0.00 | 20.4909 | 24.0015 +/- 1.7795 | 17.13% | allocation=warning; latency=warning |
| v09-query-backend / SQL adapter scalar Any / sqlite-memory | 9041.92 | 9041.92 | 0.00 | 51.5673 | 45.0620 +/- 2.8896 | -12.62% | allocation=warning; latency=improved |
| v09-memory-read / Memory primary-key miss / memory | 20.48 | 20.48 | 0.00 | 0.0888 | 0.0791 +/- 0.0024 | -10.92% | allocation=stable; latency=improved |
| v09-memory-read / Memory primary-key hit / memory | 20.48 | 20.48 | 0.00 | 0.1544 | 0.1440 +/- 0.0040 | -6.74% | allocation=stable; latency=stable |
| v09-memory-read / Memory database construction / memory | 1628.16 | 1628.16 | 0.00 | 0.3449 | 0.2990 +/- 0.0102 | -13.31% | allocation=stable; latency=improved |
| v09-memory-read / Memory typed-ID equality count / memory | 7608.32 | 7608.32 | 0.00 | 11.0260 | 9.7210 +/- 0.3052 | -11.84% | allocation=stable; latency=improved |
| v09-memory-read / Memory direct-Guid equality count / memory | 7598.08 | 7598.08 | 0.00 | 9.8923 | 9.8489 +/- 0.5432 | -0.44% | allocation=stable; latency=stable |
| v09-memory-read / Memory repeated entity identity / memory | 7669.76 | 7669.76 | 0.00 | 23.8986 | 23.0041 +/- 0.2287 | -3.74% | allocation=stable; latency=stable |
| v09-memory-read / Memory filter order page / memory | 16076.80 | 16076.80 | 0.00 | 36.1112 | 33.9253 +/- 0.5113 | -6.05% | allocation=stable; latency=stable |
| v09-memory-read / Memory scalar scan / memory | 4505.60 | 4505.60 | 0.00 | 53.8955 | 52.7560 +/- 1.6755 | -2.11% | allocation=stable; latency=stable |
| v09-memory-read / Memory construct and seed / memory | 448092.16 | 448092.16 | 0.00 | 385.4299 | 353.1485 +/- 12.0288 | -8.38% | allocation=stable; latency=stable |
| allocation-regression / Warm relation traversal / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.1226 | 0.1159 +/- 0.0058 | -5.46% | allocation=stable; latency=stable |
| allocation-regression / Warm primary-key fetch / sqlite-memory | 1812.48 | 1812.48 | 0.00 | 2.1786 | 2.0228 +/- 0.0746 | -7.15% | allocation=stable; latency=stable |
| allocation-regression / Update employees / sqlite-memory | 17623.04 | 17561.60 | -61.44 | 49.4950 | 49.2208 +/- 2.4860 | -0.55% | allocation=warning; latency=stable |
| allocation-regression / Cold primary-key fetch / sqlite-memory | 7546.88 | 7608.32 | 61.44 | 94.8760 | 97.8328 +/- 9.4851 | 3.12% | allocation=warning; latency=stable |
| allocation-regression / Cold relation traversal / sqlite-memory | 17203.20 | 17111.04 | -92.16 | 193.9262 | 177.2389 +/- 4.3480 | -8.60% | allocation=warning; latency=stable |
| allocation-regression / Startup primary-key fetch / sqlite-memory | 57384.96 | 57384.96 | 0.00 | 386.9108 | 347.3029 +/- 15.6329 | -10.24% | allocation=improved; latency=improved |
| allocation-regression / CRUD workflow small / sqlite-memory | 78448.64 | 78264.32 | -184.32 | 397.6687 | 413.8806 +/- 45.1220 | 4.08% | allocation=warning; latency=stable |
| allocation-regression / CRUD workflow batch / sqlite-memory | 78438.40 | 78346.24 | -92.16 | 400.3167 | 418.8335 +/- 56.8951 | 4.63% | allocation=warning; latency=stable |
| allocation-regression / Provider initialization / sqlite-memory | 352757.76 | 352757.76 | 0.00 | 717.2022 | 622.5788 +/- 19.9966 | -13.19% | allocation=stable; latency=improved |
| allocation-stages / Source batch slice creation / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.0026 | 0.0023 +/- 0.0000 | -11.54% | allocation=stable; latency=improved |
| allocation-stages / Source batch slice creation / sqlite-file | 0.00 | 0.00 | 0.00 | 0.0026 | 0.0025 +/- 0.0001 | -3.85% | allocation=stable; latency=stable |
| allocation-stages / Singular source argument validation / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.0109 | 0.0098 +/- 0.0003 | -10.09% | allocation=stable; latency=improved |
| allocation-stages / Singular source argument validation / sqlite-file | 0.00 | 0.00 | 0.00 | 0.0104 | 0.0102 +/- 0.0005 | -1.92% | allocation=stable; latency=stable |
| allocation-stages / Singular source result validation / sqlite-file | 0.00 | 0.00 | 0.00 | 0.0109 | 0.0104 +/- 0.0006 | -4.59% | allocation=stable; latency=stable |
| allocation-stages / Singular source result validation / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.0123 | 0.0107 +/- 0.0004 | -13.01% | allocation=stable; latency=improved |
| allocation-stages / Source loader result construction / sqlite-memory | 737.28 | 737.28 | 0.00 | 0.0349 | 0.0303 +/- 0.0015 | -13.18% | allocation=stable; latency=improved |
| allocation-stages / Source loader result construction / sqlite-file | 737.28 | 737.28 | 0.00 | 0.0347 | 0.0328 +/- 0.0022 | -5.48% | allocation=stable; latency=stable |
| allocation-stages / Mutation state-change capture / sqlite-memory | 348.16 | 348.16 | 0.00 | 0.0766 | 0.0620 +/- 0.0039 | -19.06% | allocation=stable; latency=improved |
| allocation-stages / Mutation state-change capture / sqlite-file | 348.16 | 348.16 | 0.00 | 0.0701 | 0.0668 +/- 0.0047 | -4.71% | allocation=stable; latency=stable |
| allocation-stages / Mutation final drift validation / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.0827 | 0.0731 +/- 0.0033 | -11.61% | allocation=stable; latency=improved |
| allocation-stages / Mutation final drift validation / sqlite-file | 0.00 | 0.00 | 0.00 | 0.0870 | 0.0748 +/- 0.0055 | -14.02% | allocation=stable; latency=improved |
| allocation-stages / Scalar canonical-key propagation / sqlite-file | 235.52 | 235.52 | 0.00 | 0.1147 | 0.0970 +/- 0.0056 | -15.43% | allocation=stable; latency=improved |
| allocation-stages / Scalar canonical-key propagation / sqlite-memory | 235.52 | 235.52 | 0.00 | 0.1225 | 0.1027 +/- 0.0061 | -16.16% | allocation=stable; latency=improved |
| allocation-stages / Singular source SQL preparation / sqlite-file | 256.00 | 256.00 | 0.00 | 0.1388 | 0.1245 +/- 0.0024 | -10.30% | allocation=stable; latency=improved |
| allocation-stages / Singular source SQL preparation / sqlite-memory | 256.00 | 256.00 | 0.00 | 0.1426 | 0.1320 +/- 0.0059 | -7.43% | allocation=stable; latency=stable |
| allocation-stages / Source request construction / sqlite-memory | 51.20 | 51.20 | 0.00 | 0.1687 | 0.1448 +/- 0.0061 | -14.17% | allocation=stable; latency=improved |
| allocation-stages / Source request construction / sqlite-file | 51.20 | 51.20 | 0.00 | 0.1535 | 0.1468 +/- 0.0077 | -4.36% | allocation=stable; latency=stable |
| allocation-stages / Composite canonical-key propagation / sqlite-file | 276.48 | 276.48 | 0.00 | 0.1674 | 0.1496 +/- 0.0050 | -10.63% | allocation=stable; latency=improved |
| allocation-stages / Composite canonical-key propagation / sqlite-memory | 276.48 | 276.48 | 0.00 | 0.1613 | 0.1548 +/- 0.0067 | -4.03% | allocation=stable; latency=stable |
| allocation-stages / Converter-backed canonical-key propagation / sqlite-memory | 348.16 | 348.16 | 0.00 | 0.1748 | 0.1570 +/- 0.0038 | -10.18% | allocation=stable; latency=improved |
| allocation-stages / Source cache result publication / sqlite-memory | 880.64 | 880.64 | 0.00 | 0.1951 | 0.1690 +/- 0.0076 | -13.38% | allocation=stable; latency=improved |
| allocation-stages / Converter-backed canonical-key propagation / sqlite-file | 348.16 | 348.16 | 0.00 | 0.1777 | 0.1723 +/- 0.0100 | -3.04% | allocation=stable; latency=stable |
| allocation-stages / Source cache result publication / sqlite-file | 880.64 | 880.64 | 0.00 | 0.1941 | 0.1772 +/- 0.0083 | -8.71% | allocation=stable; latency=stable |
| allocation-stages / Typed-ID canonical-key propagation / sqlite-file | 337.92 | 337.92 | 0.00 | 0.2171 | 0.1905 +/- 0.0099 | -12.25% | allocation=stable; latency=improved |
| allocation-stages / Binary canonical-key propagation / sqlite-memory | 307.20 | 307.20 | 0.00 | 0.2157 | 0.1934 +/- 0.0071 | -10.34% | allocation=stable; latency=improved |
| allocation-stages / Binary canonical-key propagation / sqlite-file | 307.20 | 307.20 | 0.00 | 0.2136 | 0.1987 +/- 0.0090 | -6.98% | allocation=stable; latency=stable |
| allocation-stages / Typed-ID canonical-key propagation / sqlite-memory | 337.92 | 337.92 | 0.00 | 0.2144 | 0.2065 +/- 0.0105 | -3.68% | allocation=stable; latency=stable |
| allocation-stages / Provider-row model materialization / sqlite-file | 143.36 | 143.36 | 0.00 | 0.2407 | 0.2178 +/- 0.0027 | -9.51% | allocation=stable; latency=stable |
| allocation-stages / Provider-row model materialization / sqlite-memory | 143.36 | 143.36 | 0.00 | 0.2642 | 0.2271 +/- 0.0082 | -14.04% | allocation=stable; latency=improved |
| allocation-stages / Composite key reconstruction baseline / sqlite-file | 348.16 | 348.16 | 0.00 | 0.2739 | 0.2367 +/- 0.0052 | -13.58% | allocation=stable; latency=improved |
| allocation-stages / Composite key reconstruction baseline / sqlite-memory | 348.16 | 348.16 | 0.00 | 0.2801 | 0.2503 +/- 0.0095 | -10.64% | allocation=stable; latency=improved |
| allocation-stages / Canonical provider-row decoding / sqlite-file | 296.96 | 296.96 | 0.00 | 0.5309 | 0.5055 +/- 0.0213 | -4.78% | allocation=stable; latency=stable |
| allocation-stages / Canonical provider-row decoding / sqlite-memory | 296.96 | 296.96 | 0.00 | 0.7320 | 0.5058 +/- 0.0163 | -30.90% | allocation=stable; latency=improved |
| allocation-stages / Provider-row decode/materialization pipeline / sqlite-memory | 440.32 | 440.32 | 0.00 | 0.7909 | 0.7280 +/- 0.0125 | -7.95% | allocation=stable; latency=stable |
| allocation-stages / Mutation execution preflight / sqlite-file | 0.00 | 0.00 | 0.00 | 1.2340 | 0.7624 +/- 0.1077 | -38.22% | allocation=improved; latency=improved |
| allocation-stages / Mutation execution preflight / sqlite-memory | 0.00 | 0.00 | 0.00 | 1.5030 | 0.7635 +/- 0.0813 | -49.20% | allocation=improved; latency=improved |
| allocation-stages / Provider-row decode/materialization pipeline / sqlite-file | 440.32 | 440.32 | 0.00 | 0.8207 | 0.7780 +/- 0.0260 | -5.20% | allocation=stable; latency=stable |
| allocation-stages / Known-miss materialization/publication / sqlite-file | 419.84 | 419.84 | 0.00 | 1.7172 | 1.3090 +/- 0.0670 | -23.77% | allocation=stable; latency=improved |
| allocation-stages / Source result validation / sqlite-memory | 737.28 | 737.28 | 0.00 | 1.6288 | 1.4897 +/- 0.0700 | -8.54% | allocation=stable; latency=stable |
| allocation-stages / Source result validation / sqlite-file | 737.28 | 737.28 | 0.00 | 1.5657 | 1.4963 +/- 0.0526 | -4.43% | allocation=stable; latency=stable |
| allocation-stages / Known-miss materialization/publication / sqlite-memory | 419.84 | 419.84 | 0.00 | 1.9337 | 1.9199 +/- 0.1089 | -0.71% | allocation=stable; latency=stable |
| allocation-stages / Mutation command preparation / sqlite-memory | 2232.32 | 2232.32 | 0.00 | 3.0750 | 2.9700 +/- 0.4899 | -3.41% | allocation=stable; latency=stable |
| allocation-stages / Mutation command preparation / sqlite-file | 2232.32 | 2232.32 | 0.00 | 2.7729 | 3.1285 +/- 0.6578 | 12.82% | allocation=stable; latency=noisy |
| allocation-stages / Warm typed-ID exact terminal / sqlite-memory | 1597.44 | 1597.44 | 0.00 | 5.3633 | 6.1115 +/- 2.1353 | 13.95% | allocation=stable; latency=noisy |
| allocation-stages / Warm typed-ID exact terminal / sqlite-file | 1597.44 | 1597.44 | 0.00 | 7.9779 | 7.4632 +/- 1.4941 | -6.45% | allocation=stable; latency=noisy |
| allocation-stages / Cold typed-ID exact terminal / sqlite-file | 6062.08 | 6246.40 | 184.32 | 54.4879 | 53.8307 +/- 10.4401 | -1.21% | allocation=warning; latency=stable |
| allocation-stages / Cold typed-ID exact terminal / sqlite-memory | 6420.48 | 6461.44 | 40.96 | 73.8988 | 76.2517 +/- 13.1892 | 3.18% | allocation=warning; latency=stable |
