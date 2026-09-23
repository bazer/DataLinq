> [!WARNING]
> Supplemental timing controls, not performance acceptance. Original canonical histories and warnings remain unchanged. F19/F21 are still open.

# W1 Timing Controls

**Date:** 2026-09-23. Follows the [paired timing follow-up](W1%20Paired%20Timing%20Follow-up.md), [paired allocation diagnostics](W1%20Paired%20Allocation%20Diagnostics.md) and [six-lane checkpoint](W1%20Six-Lane%20Performance%20Checkpoint.md). This slice changes no runtime, benchmark workload/count, evidence validator, package or public API.

## Scope and protocol

The clean candidate/runner is `e7660b5b3c6a36aac0864a35d6eb7d6e8cfe4bf7`, merged [PR #225](https://github.com/bazer/DataLinq/pull/225). Its `src` and `scripts` trees are identical to the canonical candidate `726810c64f82212e568dba76bc6ffdcc59410543`. The historical runtime is the actual W0 `7e36614b5f26f1dd199ff93bc60620f4b0714d5f`, not the published-0.9.2 target. Both use .NET 10.0.12 / SDK 10.0.401 on the same Windows 10.0.26200 x64 host, Intel Family 6 Model 140 Stepping 1, eight logical processors.

Each experiment runs **W0-A, W1-B, W1-C, W0-D**, serially. No local build, test, profiler, competing benchmark or tracked edit overlaps sampling. Read-only source/artifact inspection continued; this is not an exclusive-CPU laboratory run. The order balances the runtime comparison but is not randomized. Live binaries and loaded managed dependencies were hashed before subsequent builds; the clean checkouts remained unchanged.

There are two distinct protocols; their absolute timings must not be spliced together:

- **Scalar Any:** existing Benchmark CLI / BenchmarkDotNet 0.15.8, `heavy`/MediumRun, SQLite memory, the original 1,000 operations per invocation, two launches, **50 warmups and 30 measurement iterations per launch**. All four raw logs independently confirm those counts (100 warmup and 60 actual rows each). The four histories are complete, artifact-complete, exit zero and have zero invalid rows. Their custom selection/options correctly leave `ValidForEvidence=false`; clean runner/target provenance is valid. All **28 raw receipts** match lengths/hashes. Minimum-iteration warnings remain.
- **Stage/read/mutation probes:** delegates to the actual harness methods, verified operation counts and actual iteration setup/cleanup before/after **every invocation**, outside measurement. Each case has at least 64 warmup invocations and two elapsed warmup seconds, then at least 64 measured invocations and 250 ms accumulated measured work. The maximum is one million calls per phase. Cold caches and transaction preparation are not reused to make the workload faster. Cases have separate global setup/cleanup but share the group process; A/B use forward case order, C/D reverse it. Stopwatch and thread-allocation samples are retained without trimming, forced GC or timing-overhead subtraction. These are descriptive controls, not BenchmarkDotNet confidence intervals or strict release evidence.

The **12 completed probe reports contain 132 rows**, 465,691 measured invocations and 3,055,863 warmup invocations. A separate verification pass checks hashes, loaded identities, sample counts, normalization, checksums and unchanged normalized telemetry. Many short-stage samples share one process and are not independent experimental replicates; do not derive statistical significance by treating every invocation as an independent run.

The probe uses the [Employees harness](../../../../src/DataLinq.Benchmark/EmployeesBenchmarks.cs), [typed terminal harness](../../../../src/DataLinq.Benchmark/TypedIdExactTerminalBenchmarks.cs) and [calibrated allocation harness](../../../../src/DataLinq.Benchmark/AllocationRegressionBenchmarks.cs). Stage/read counts are 1,000; updates use 2,000; each calibrated CRUD invocation uses 350. The phase2 warm-key controls here therefore do not substitute the allocation lane's 60,000-operation row.

## Ordinary scalar Any

| Run | Mean +/- error, us/op | B/op |
| --- | ---: | ---: |
| w0-a | 59.3900 +/- 3.5670 | 17561.60 |
| w1-b | 55.8900 +/- 0.9080 | 18636.80 |
| w1-c | 59.3900 +/- 3.0900 | 18636.80 |
| w0-d | 58.2200 +/- 2.4170 | 17561.60 |

The forward comparison is **-5.89%** and the reverse comparison **+2.01%**, with overlapping reported error ranges in both pairs. The earlier **+54.02%** warning does not reproduce under this longer-warmup/order control. That limits the claim of a consistent large slowdown; it does not erase the old result, prove equivalence, or establish a particular JIT/host cause. No samples were manually removed.

Allocation is consistently **+1,075.20 B/op / +6.12%** in these controls. One scalar query per operation and the rest of the recorded telemetry are identical. The allocation cost remains regardless of the latency result.

## Short-stage controls

Values are mean microseconds per operation. Forward delta compares B/A; reverse delta compares C/D. All rows, including newly positive differences, are retained.

| Workload / provider | W0-A, us | W1-B, us | W1-C, us | W0-D, us | Forward delta | Reverse delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| BinaryCanonicalKeyPropagation / sqlite-file | 0.2026 | 0.1943 | 0.2127 | 0.2116 | -4.09% | 0.49% |
| BinaryCanonicalKeyPropagation / sqlite-memory | 0.2440 | 0.2102 | 0.2309 | 0.2869 | -13.87% | -19.51% |
| TypedIdCanonicalKeyPropagation / sqlite-file | 0.4949 | 0.2631 | 0.2254 | 0.2261 | -46.85% | -0.32% |
| TypedIdCanonicalKeyPropagation / sqlite-memory | 0.2398 | 0.3098 | 0.2921 | 0.2432 | 29.19% | 20.12% |
| CanonicalProviderRowDecoding / sqlite-file | 0.5972 | 0.5519 | 0.5354 | 0.5070 | -7.59% | 5.61% |
| CanonicalProviderRowDecoding / sqlite-memory | 0.6483 | 0.5787 | 0.5270 | 0.6279 | -10.73% | -16.06% |
| KnownMissMaterializationPublication / sqlite-file | 0.3780 | 0.4221 | 0.3362 | 0.3311 | 11.69% | 1.53% |
| KnownMissMaterializationPublication / sqlite-memory | 0.3678 | 0.4171 | 0.3365 | 0.2998 | 13.41% | 12.24% |
| MutationCommandPreparation / sqlite-file | 1.0733 | 0.7430 | 1.9458 | 0.8554 | -30.77% | 127.48% |
| MutationCommandPreparation / sqlite-memory | 0.7998 | 0.7922 | 0.7843 | 0.6585 | -0.95% | 19.11% |
| MutationExecutionPreflight / sqlite-file | 0.3323 | 0.1647 | 0.1435 | 0.3357 | -50.43% | -57.26% |
| MutationExecutionPreflight / sqlite-memory | 0.3598 | 0.1355 | 0.1195 | 0.3113 | -62.35% | -61.61% |
| MutationFinalDriftValidation / sqlite-file | 0.0045 | 0.0060 | 0.0051 | 0.0046 | 34.17% | 11.33% |
| MutationFinalDriftValidation / sqlite-memory | 0.0057 | 0.0047 | 0.0046 | 0.0064 | -16.90% | -28.14% |
| MutationStateChangeCapture / sqlite-file | 0.1233 | 0.0980 | 0.0794 | 0.0823 | -20.51% | -3.60% |
| MutationStateChangeCapture / sqlite-memory | 0.0991 | 0.1297 | 0.1457 | 0.0792 | 30.89% | 84.06% |
| SourceCacheResultPublication / sqlite-file | 0.1778 | 0.1730 | 0.1686 | 0.1859 | -2.70% | -9.29% |
| SourceCacheResultPublication / sqlite-memory | 0.1981 | 0.1750 | 0.2079 | 0.1636 | -11.66% | 27.12% |
| SourceResultValidation / sqlite-file | 2.5675 | 2.1486 | 1.4076 | 2.7494 | -16.32% | -48.80% |
| SourceResultValidation / sqlite-memory | 2.1682 | 2.1665 | 1.4279 | 1.4482 | -0.08% | -1.40% |
| WarmPrimaryKeyFetch / sqlite-file | 3.7909 | 2.8505 | 2.8179 | 2.8429 | -24.81% | -0.88% |
| WarmPrimaryKeyFetch / sqlite-memory | 2.9886 | 2.9843 | 2.9463 | 3.2133 | -0.14% | -8.31% |

The thirteen original cases left without a supplemental control in #225 are now covered. Coverage is not acceptance:

- Both phase2 warm-key warnings and the original file-backed binary/typed propagation warnings do not recur in these pairs. Provider decoding changes direction on file and is lower on memory. Source-result validation is lower on both providers in both pairs.
- Known-miss materialization on memory remains **+13.41% / +12.24%**, approximately 49 / 37 ns per operation, with unchanged 416 B/op in these probe means. [InstanceFactory](../../../../src/DataLinq/Instances/InstanceFactory.cs) now takes a per-constructor occurrence checkpoint and catches factory failures to prevent stale diagnostic reuse; [the functional audit](W1%20Functional%20and%20IO%20Audit.md#model-factory-occurrence-correction) records the reproduced correctness need. This is a concrete added path, but this whole-stage measurement does not isolate its exact latency.
- Command preparation on file reverses from -30.77% to +127.48%. The latter mean is 1.9458 us, median 1.3244 us and standard deviation 4.1985 us, with a 36.9191 us maximum. Those tails are retained. Memory preparation is -0.95% / +19.11%; source publication on memory also changes direction. These are inconclusive as fixed slowdowns, not passes.
- Mutation preflight is faster in all four pairs and allocates zero bytes, consistent with the [earlier preflight reduction](W1%20Mutation%20Preflight%20Allocation%20Reduction.md). Final drift validation is lower on memory, but file remains positive by about 1.53 / 0.52 ns. Both allocate zero. State-change capture is lower on file but **+30.89% / +84.06%** on memory, with unchanged 344 B/op. Typed-key propagation on memory is also positive (+29.19% / +20.12%). These positive controls remain visible and need a bounded disposition; an unchanged method is not proof of unchanged performance.

Source inspection narrows those local-stage questions. [MutationSnapshot](../../../../src/DataLinq/Mutation/MutationSnapshot.cs), [DataLinqKey](../../../../src/DataLinq/Instances/DataLinqKey.cs), the [key propagation fixture](../../../../src/DataLinq.Benchmark/CanonicalKeyPropagationAllocationFixture.cs) and [source-loading fixture](../../../../src/DataLinq.Benchmark/SourceLoadingAllocationFixture.cs) are unchanged from W0. The exercised StateChange constructor and finalized-version check are also unchanged, despite added async execution members elsewhere. Key propagation uses a Memory fixture under both SQLite labels; its file/memory labels do not identify separate provider I/O paths. Source-cache publication is a local dictionary-fill fixture, not TableCache publication. Source-result validation uses the existing builder plus its added optional original-request branch. None of these facts explains all timing variation or authorizes removing guards.

## Entity reads and typed terminals

| Workload / provider | W0-A, us | W1-B, us | W1-C, us | W0-D, us | Forward delta | Reverse delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| RepeatedInPredicateFetch / sqlite-file | 86.9728 | 96.6771 | 105.2192 | 89.9817 | 11.16% | 16.93% |
| RepeatedInPredicateFetch / sqlite-memory | 151.4239 | 151.2614 | 157.4348 | 137.4529 | -0.11% | 14.54% |
| RepeatedNonPrimaryKeyEqualityFetch / sqlite-file | 67.6840 | 77.6341 | 82.5517 | 85.8393 | 14.70% | -3.83% |
| RepeatedNonPrimaryKeyEqualityFetch / sqlite-memory | 110.4796 | 121.9199 | 120.1832 | 113.9526 | 10.36% | 5.47% |
| ColdTypedIdExactTerminal / sqlite-file | 15.5379 | 16.9590 | 15.5720 | 13.8976 | 9.15% | 12.05% |
| ColdTypedIdExactTerminal / sqlite-memory | 39.4724 | 45.5802 | 46.3188 | 37.6510 | 15.47% | 23.02% |
| WarmTypedIdExactTerminal / sqlite-file | 2.8385 | 2.9160 | 3.2208 | 2.8376 | 2.73% | 13.50% |
| WarmTypedIdExactTerminal / sqlite-memory | 3.2213 | 2.9444 | 2.6647 | 2.5780 | -8.60% | 3.37% |

IN on file remains **+11.16% / +16.93%**; non-key equality on memory is **+10.36% / +5.47%**. The other entity comparisons vary by order. Cold typed-ID memory is still **+15.47% / +23.02%**; cold file is +9.15% / +12.05%. Warm typed-ID memory no longer reproduces the earlier +60.40% point estimate, while warm file remains order-sensitive. These controls retain a real read-path cost question rather than declaring all query warnings environmental.

Thread-allocation means still show the larger query increases: IN adds 7,704–7,968 B per operation, non-key equality roughly 5,663–5,775 B, and cold typed terminals **1,416.10 B**. Warm typed terminals add approximately **48 B** here. These probe values are not substituted for the canonical allocation values, including the earlier slightly lower warm-typed allocation result. The verification index retains all four allocation means per case.

The [allocation traces](W1%20Paired%20Allocation%20Diagnostics.md#source-attribution-and-limits) and current source identify immutable diagnostic lineage, execution-context changes, guarded read-sequence closures/iterators/gates, logical-query enumeration and owned cleanup. [DataSourceAccess.ReadSequence](../../../../src/DataLinq/Mutation/DataSourceAccess.cs) and [GuardedEnumerable](../../../../src/DataLinq/Execution/GuardedEnumerable.cs) add real lifetime objects even for root reads; [SyncQueryEnumerable](../../../../src/DataLinq/Execution/SyncQueryEnumerable.cs) supplies the logical-query boundary. Cold typed lookup uses [ExecuteTerminalPrimaryKeyLookup](../../../../src/DataLinq/Linq/Planning/Sql/SqlQueryPlanBackend.cs) plus the owned cold row-loading path. Sampled type totals cannot be subtracted from net allocation as an exact residual equation, and compiler-generated type names alone do not identify a new object family. The earlier metrics/activity reductions also remove allocations. A source-grounded trade-off decision is still required.

## Updates and CRUD

| Workload / provider | W0-A, us | W1-B, us | W1-C, us | W0-D, us | Forward delta | Reverse delta |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| UpdateEmployees / sqlite-memory | 50.0795 | 52.8702 | 53.2379 | 50.7028 | 5.57% | 5.00% |
| CrudWorkflowSmall / sqlite-memory | 358.9068 | 374.6713 | 361.6786 | 348.8068 | 4.39% | 3.69% |
| CrudWorkflowBatch / sqlite-memory | 383.1355 | 396.2901 | 390.4036 | 355.5154 | 3.43% | 9.81% |

Update latency is **+5.57% / +5.00%**, versus the earlier +18.19% warning. CRUD small is +4.39% / +3.69%; batch +3.43% / +9.81%. These are smaller positive differences, not zero-cost claims. Added managed allocation remains **1,886–1,942 B/op for updates** and **12,250–12,364 B/op for CRUD**. The existing traces identify substantial diagnostic-scope/context costs across command, hydration, mutation, completion and independent cleanup. Query/mutation/transaction/cache telemetry remains identical across these controls.

## Checksum rules and retained failed attempts

Ordinary deterministic cases have identical integer checksums across runs. Two cases require explicit rules:

- Binary canonical keys use [DataLinqKey](../../../../src/DataLinq/Instances/DataLinqKey.cs)'s `System.HashCode`, whose random seed is process-local ([.NET documentation](https://learn.microsoft.com/en-us/dotnet/api/system.hashcode?view=net-10.0)). Each invocation is checked against `unchecked(4 * 1000 * DataLinqKey.FromValue(bytes 00..0f).GetHashCode())` inside that process. Different process hashes are not a workload mismatch.
- [CRUD](../../../../src/DataLinq.Benchmark/BenchmarkContext.cs) includes every generated employee ID in its checksum. Each invocation inserts/deletes 350 rows; SQLite AUTOINCREMENT advances the next invocation's sum by **350 × 350 = 122,500**. Cleanup restores updates but does not reset that sequence. Probe v2 retains every warmup/measurement checksum and checks the unchecked Int32 progression; first checksums match across the four runs. The verifier independently recomputes the progression. No workload or database-reset behavior was changed to manufacture constant checksums.

The following local logs remain under `artifacts/`. None contributes accepted rows to the tables:

| Log | Failure / limit | SHA-256 |
| --- | --- | --- |
| w0-a-e7660b5b-timing-probe-stages-bootstrap-failed.log | Reflection called obsolete BenchmarkDotNet Target getter; no workload sampled | 50feb01b8f0d0c48850041f189fbe1bc5c3ef17c2fc47c0e69a8ef3ad1f2af7a |
| w0-a-e7660b5b-timing-probe-stages-limit-pilot.log | 100,000-call warmup limit reached at final drift validation; partial stdout only, no complete report | 690baa86804eb46c6769130e8f7d075f7c81bcb489208f078246a5a2116336da |
| w0-a-e7660b5b-timing-probe-stages-binary-reflection-failed.log | Looked for non-generic FromValue; failed before sampling | 043507540ee6b37c57bb3acc7a209dbba789e4523650eb0194fc9fc51a7ff74c |
| w0-a-e7660b5b-timing-probe-mutations-checksum-pilot.log | v1 incorrectly required constant CRUD checksums; update stdout only, no complete mutation report | 71e98152482cd790c45c6898758c531c40100632a4d827f437ad5a409599a813 |

The failed binary-reflection process remained alive after printing its exception; a premature rebuild failed with a locked Probe.dll. The process settled before the successful rebuild. The later failed mutation process was explicitly interrupted and confirmed terminal before v2 started. These are diagnostic-tool failures, not product regressions. Completed v1 stage/read reports are preserved; v2 reruns the entire mutation group, including updates. The original scalar script had a pre-launch PowerShell interpolation error; it was corrected before any capture began.

## Receipts and reproduction

The independent index is `artifacts/w1-timing-controls-verification.json`, SHA-256 **4143767dd96642b2bec5e0311292439b74c9f2fa43d1c5d452db5d7f120a2750**. It contains each group's report hashes, runtime/probe identities, all four timing/allocation means per case and the verified sample counts. Scalar index: `artifacts/w1-e7660b5b-scalar-order-control.json`, SHA-256 `f8781710c9a560a055b070ec06049a13bd0b93ad9e5fc0e9bdf61f6c8bddcfe4`.

| Label | History SHA-256 |
| --- | --- |
| w0-a | c8f379a5c95013a061eeeabc5d6d31d6301b72213bbcbc406f3488b455d43dc6 |
| w1-b | 8f261da1ad542ec733b310364256ff2f3427864ffae73814a27dd4c098bbd30c |
| w1-c | 76ffdfc937e579e9431486a3358da4115c7d21e0be90685ed8825d9adf95e83e |
| w0-d | 5f7f052845e90c2b710e385561d1be92fd030204d37d67a82d95beaede32410a |

Scalar history paths are `artifacts/benchmarks/history/<label>-e7660b5b-scalar-order-control.json`. Reproduce through the [Benchmark CLI](../../../contributing/DataLinq.Benchmark.CLI.md): set `DATALINQ_BENCHMARK_PROVIDERS=sqlite-memory`, then `run --filter '*RepeatedScalarAny*' --profile heavy --history-json <fresh-path> -- --warmupCount 50 --iterationCount 30`. Add `--benchmark-target-root artifacts/benchmarks/targets/w0-7e36614b-net10` before the `--` separator for W0. Run in ABBA order from verified clean checkouts.

| Group / label | JSON SHA-256 |
| --- | --- |
| stages / w0-a | 3780cd85a974a69c8ac5535a4b32aef040b8588923e87c82100e43e3155c0a17 |
| stages / w1-b | 42536e7367348c171897ef701b301a240e59c5a6a7987f771df5897c116b2d50 |
| stages / w1-c | 17b53d4fc5f768e5b8ff8f04d7ea1456533308ca70d0c3345be27eb4a12cd349 |
| stages / w0-d | fc2d52c497e3d4da66c74a029661b1d78f95f3da3a2874ec9a12810b4ccc5e36 |
| reads / w0-a | 443474b7cffb95e08ee9a58531d0152a81518d4424ee069aca242eb000ff2c4e |
| reads / w1-b | 1218c2768bc95c200f66c0084dd4837cb191a4354376e897300fb3a9fad08a81 |
| reads / w1-c | e525a8fbdb3b3e35ed78948bd4e7b1ecd5e092379e715bc4dd9c90d049ac68fc |
| reads / w0-d | e33c6e228fc3c4dc62c3939d2b88579ee015ace3c0bfc8d4e98c27d34da21ee6 |
| mutations / w0-a | f9bba8eae32925e41c2b91d3952390eb94218a60d49b5d3d4249b4a1803adf46 |
| mutations / w1-b | b3bcc259b47add117439e2031bd85d5e9b9d207ca1e168876faa7fdd16129332 |
| mutations / w1-c | 2c429c3f352799118ddae01826ea3195c463ea1afeb5fc5275f17c8e06b9143c |
| mutations / w0-d | b4ff1f4e84a3d305840902c9d16593b16d81441bc58a1a5874864c2dc8004aba |

Probe paths are `artifacts/<label>-e7660b5b-timing-probe-<group>.json` for stages/reads, and `artifacts/<label>-e7660b5b-timing-probe-v2-mutations.json` for mutations. Group indexes:

| Group index under artifacts | SHA-256 |
| --- | --- |
| w1-e7660b5b-timing-probe-stages-verification.json | 2aaef7610c1c3d3887120b868b36296c9a73fa1d854d7d0fcdbde59629390051 |
| w1-e7660b5b-timing-probe-reads-verification.json | 423e3fa924e46c739fa838c0b636c86b6183ab3d7bf82e4bb4f2c1ab13c10265 |
| w1-e7660b5b-timing-probe-v2-mutations-verification.json | 818d29793439b612b546ed5abc85bcb4b720c76a26f43a16b03e51a7bf227c77 |

The [v1 source snapshot](W1%20Timing%20Probe%20v1.cs.txt) is the executed stage/read probe; its constant-checksum rule is **not valid for CRUD**. The [v2 snapshot](W1%20Timing%20Probe%20v2.cs.txt) is the executed mutation probe with the explicit progression rule. Both use this [project snapshot](W1%20Timing%20Probe.csproj.txt). Snapshots preserve source after repository LF normalization; hashes below identify the actual captured files, including their original line endings. Restore/build as a separate net10.0 executable under `artifacts`, then invoke `Probe.dll <actual-benchmark.dll> <stages|reads|mutations> <forward|reverse> <fresh-output.json>` through `scripts/dotnet-sandbox.ps1 exec`. Use v1 only for stages/reads, or v2 for a fresh experiment with newly recorded provenance.

| Probe | Captured source SHA-256 | Captured binary SHA-256 |
| --- | --- | --- |
| v1 | f65f14b34548d6de83e867d7e26bbb723ae636f49a9ffc9b3d8a0310de4a007b | 17c8a3627cfe8ed8392947e5a900ea27c9795b0d6daba9d03dd08256d8f24453 |
| v2 | 56acc8a8dce96626efaef8452a66833acd1cc6d59ad45f9022967c696fd64efa | f8b7b65e1f9fb9d322f7d5f1376e7ee56bc527558e7219351211438ee105774e |

The W0 benchmark binary is `b3bd7a1c14da40552936a2c77144734ff561e393dea06aad0187d087f21d7054`; W1 is `87d2eecf511dce00790483e13ec68fcbafc68a2d24129487f4aa95afebb43148`. A rebuild is a new binary receipt, not a recreation of these timings.

## Next bounded step

F19 now has controls for every original flagged/noisy latency case, but the read-path costs and positive/inconclusive controls above still need disposition. The next concrete reduction is to investigate diagnostic allocations on already-finished `GuardedEnumerable` calls. `MoveNext` and `Dispose` currently enter a scope even when they cannot invoke the underlying iterator. Any change must retain the call gate before checking completion, keep actual move/cleanup scopes, preserve helper closure/draining and prove overlap and failure isolation with meaningful tests. No such runtime change is made here.

After that measured reduction, reconcile the remaining scope/lifetime trade-offs against [D10-6](Implementation%20Order%20and%20Integration%20Plan.md#d10-6-performance-evidence-policy) and [RE10-5](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md#re10-5-performance-and-telemetry), refreshing affected and final canonical evidence if runtime code changes. Exact object-by-object agreement with sampled allocation ticks is not a valid acceptance criterion, but unexplained material costs and undispositioned meaningful latency are not waived. F21 still requires final broad/I/O/integration reconciliation. Planning pages remain excluded from DocFX; source links, tables, snapshots and receipts are verified instead.

The published compatibility baseline remains 0.9.2. The [limited W0-F1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception), native W2, public/packed W3, issue #26 and release approval remain unchanged. No W1 completion or package publication is claimed.
