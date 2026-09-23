> [!WARNING]
> Supplemental allocation diagnostics, not latency measurements, strict release evidence or W1 performance acceptance. The complete canonical checkpoint remains authoritative for the release-lane comparisons.

# W1 Paired Allocation Diagnostics

**Date:** 2026-09-23. Follows the [six-lane checkpoint](W1%20Six-Lane%20Performance%20Checkpoint.md), with no production or benchmark-source changes. The actual frozen W0 commit is `7e36614b5f26f1dd199ff93bc60620f4b0714d5f`; the W1 integration is `726810c64f82212e568dba76bc6ffdcc59410543`. The existing published-0.9.2 benchmark target at `1894d53d25511a3581e8923deda1b74d0f76ee25` is a different before-state and was not used or relabeled.

## Method and provenance

A new clean detached worktree at `artifacts/benchmarks/targets/w0-7e36614b-net10` supplies the W0 runtime. The current Benchmark CLI builds it through the existing historical-target hooks. Both sides use .NET 10.0.12 and SQLite memory. All profiles run sequentially after the six canonical lanes have finished; no build, test, competing benchmark or source edit overlaps sampling.

The bounded probe loads each side's real benchmark assembly and delegates directly to nine audited methods in [EmployeesBenchmarks](../../../../src/DataLinq.Benchmark/EmployeesBenchmarks.cs) and [AllocationRegressionBenchmarks](../../../../src/DataLinq.Benchmark/AllocationRegressionBenchmarks.cs). Required setup methods must exist; the declared BenchmarkDotNet operation count must equal the audited count. Each workload uses twenty setup/work/cleanup warm-ups, followed by twenty sampled invocations with setup and cleanup outside the measured region. Counts are 3,000 for the pre-bound scalar adapter, 1,000 for the three repeated query and two cold-read methods, 2,000 for updates and 350 for each CRUD method.

`GC.GetAllocatedBytesForCurrentThread` records exact managed bytes allocated on the synchronous workload thread during each invocation. It is not process-wide allocation and includes the real synchronous workload, not only one internal primitive. Separately, EventPipe allocation ticks estimate allocated types on that thread between paired start/end markers. Allocation ticks are sampled; an interval crossing a marker may include preceding setup allocation. Type totals are therefore approximate and are not exact object counts or an allocation-stack decomposition.

All eighteen runs finish with twenty matched marker pairs, a nonempty sample, matching checksums within each W0/W1 pair and identical normalized telemetry replays. The replay uses each harness's own telemetry routine after the trace stops. JSON records every invocation's bytes, warm-up bytes, sample counts, runtime, probe binary hash and loaded dependency identities/hashes before and after capture. Every recorded dependency was rehashed; all loaded DataLinq assemblies identify the corresponding W0 or W1 commit. Both checkouts were clean throughout profiling.

The W1 benchmark SHA-256 is `eb61054d051699ea0ceb524293d63b7c693ff28576d811fa1c0c6870eac63e23`, matching the canonical capture. The separately rebuilt W0 benchmark SHA-256 is `b3bd7a1c14da40552936a2c77144734ff561e393dea06aad0187d087f21d7054`; it is not presented as the frozen history's original binary.

## Paired results

| SQLite-memory workload | Operations per side | W0 B/op | W1 B/op | Added B/op | W1 scope-family sampled B/op |
| --- | ---: | ---: | ---: | ---: | ---: |
| SqlAdapterScalarAny | 60000 | 7960.37 | 9040.32 | 1079.95 | 1294.82 |
| RepeatedNonPrimaryKeyEqualityFetch | 20000 | 22293.11 | 27956.13 | 5663.03 | 3811.81 |
| RepeatedInPredicateFetch | 20000 | 28524.49 | 36221.53 | 7697.04 | 5963.73 |
| RepeatedScalarAny | 20000 | 17661.60 | 18672.68 | 1011.08 | 1199.06 |
| ColdPrimaryKeyFetch | 20000 | 6254.38 | 7617.67 | 1363.28 | 1790.76 |
| ColdRelationTraversal | 20000 | 13353.65 | 17164.19 | 3810.54 | 3430.61 |
| UpdateEmployees | 40000 | 15677.94 | 17563.94 | 1886.00 | 2798.79 |
| CrudWorkflowSmall | 7000 | 65898.85 | 78359.23 | 12460.38 | 12669.79 |
| CrudWorkflowBatch | 7000 | 65887.49 | 78442.42 | 12554.93 | 12806.81 |

The last column combines sampled `ExecutionFailureScope`, `System.Threading.ExecutionContext` and `OneElementAsyncLocalValueMap` bytes, normalized by the sampled operation count. Those types receive no samples in the W0 workload regions. This does not prove zero W0 allocation of every unsampled type. It also does not mean the new scope-family estimate equals the net increase: other allocations can shrink, additional wrapper objects can grow, and sampling has the limitations above.

The pre-bound scalar adapter is particularly stable: all W0 samples are 7,960.22–7,961.89 B/op and all W1 samples 9,040.30–9,040.37 B/op. The paired mean difference is 1,079.95 B/op. Non-key equality and IN also reproduce the canonical allocation increases with unchanged recorded query/reader/cache work.

The longer fixed warm-up does not make every row constant. Repeated scalar Any spans 17,554.90–17,992.21 B/op at W0 and 18,636.34–18,796.34 at W1; cold key reads span 6,131.54–6,515.54 and 7,547.57–7,740.83 respectively. Preserve those per-invocation ranges rather than treating the table's averages as exact steady-state object counts. These diagnostic values must not replace or be numerically spliced into BenchmarkDotNet's canonical rows.

## Source attribution and limits

[ExecutionFailureScope](../../../../src/DataLinq/Execution/ExecutionFailureScope.cs) installs immutable diagnostic lineage through an AsyncLocal. [Direct command telemetry](../../../../src/DataLinq/Database/DatabaseAccess.CommandTelemetry.cs) isolates the overall command, startup callbacks/custom getters and provider dispatch. [Select scalar execution](../../../../src/DataLinq/Query/Select.cs) adds the outer logical-query boundary and [owned read resources](../../../../src/DataLinq/Execution/ReadCommandResources.cs) isolates cleanup. SQLite's connection-visibility setup also dispatches through command telemetry. These are real paths in the scalar workload, which explains why execution-context and scope allocations appear despite unchanged parsing/binding allocations.

Entity queries additionally traverse [guarded read sequences](../../../../src/DataLinq/Mutation/DataSourceAccess.cs), [enumerator admission](../../../../src/DataLinq/Execution/GuardedEnumerable.cs) and [logical-query enumeration](../../../../src/DataLinq/Execution/SyncQueryEnumerable.cs). Relations and CRUD also include shared-load coordination, private transaction-owned services and finalization. The samples identify a substantial diagnostic/lifetime cost, but do not assign every remaining byte to an exact source object.

The preceding [metrics-factory and activity reductions](W1%20Telemetry%20Allocation%20Reduction.md) and [activity-scope follow-up](W1%20Activity%20Scope%20Allocation%20Reduction.md) remove some other allocations. Consequently, summing newly sampled types and expecting equality with the net W0 delta is invalid. This evidence is a stronger attribution lead than aggregate percentages alone, not a waiver of the unexplained remainder.

## Retained setup and pilot limitations

The first historical bootstrap used the canonical backend selector with a scalar-only filter and a Dry profile. Restore/build and the two selected benchmarks ran, but the history correctly reports **Incomplete**, exit one, two observed targets out of the selector's twelve expected targets, and `ValidForEvidence=false`. Its clean matching assembly/checkout provenance establishes the built runtime's identity; the partial run is not counted as passing benchmark evidence. A future focused diagnostic should use a custom filter without claiming the complete canonical selector.

A first scalar probe with three warm-ups retained changing early-invocation allocation, including 9,440.30 B/op at W1 before later samples settled near 9,040.30. Both pilot traces/JSON are preserved under `w0-7e36614b-probe-scalar-adapter` and `w1-726810c6-probe-scalar-adapter`. The table above uses only the subsequent fixed-twenty-warm-up protocol; it does not silently discard early samples from either protocol.

## Receipts and next work

The executed probe source is `artifacts/w1-allocation-probe-v3/Program.cs`, SHA-256 `3c9f06a3d63801aa70459ad548c82cafd36c9c6a4f33c220218076b8a5576017`. An identical [source snapshot](W1%20Allocation%20Probe.cs.txt) and its [project snapshot](W1%20Allocation%20Probe.csproj.txt) are retained for review. Copy them to a fresh directory immediately beneath `artifacts` as `Program.cs` and `Probe.csproj`; the project's relative diagnostic-library references resolve against the built current benchmark harness. This is a bounded internal probe, not a new supported CLI or release-lane selector.

The captured probe binary SHA-256 is `f1ded985d2807074d4c4b25ed903457c177f3a694bd3c5372ea92a6155c7cde0`. Reproduce with `Probe.dll <actual-benchmark.dll> <method> 20 <fresh-output-prefix>` using the workspace sandbox wrapper, after restoring/building the probe and verifying both clean runtime identities. A rebuild at a different path may have a different binary hash; record the actual new source, binary and runtime receipts instead of reusing these identities.

Each prefix below lives under `artifacts/` and owns a JSON file plus a nettrace file: `w0-7e36614b-probe-v3-<method>` and `w1-726810c6-probe-v3-<method>`. All eighteen JSON and trace receipts are indexed, including trace lengths/hashes, in `w1-paired-allocation-probe-v3-verification.json`, SHA-256 `2374859b0d51df6a4d66e516c4dc9c6655a9f2d5fb6a7490047712a07b5a8ce6`.

| Method | W0 JSON SHA-256 | W1 JSON SHA-256 |
| --- | --- | --- |
| SqlAdapterScalarAny | 2381c62fcc2840205af99960b5605ee8a5d7fc2af773a1c9c8c092d9ff78687a | ee644f2752f6740a4d19be42cf4b96c8cf5f5f617421cbadf436cc9aa801679e |
| RepeatedNonPrimaryKeyEqualityFetch | 912ccba964ed1289284d068aaccedfbde4eddf5182606bc3b164cb03c76ffc0f | 986317e1d5884759be2c8f6db667aec0c25f28090a9f96656e17368845436a6b |
| RepeatedInPredicateFetch | 229c845f9084b72cb628f460364f9174879057fb9c6f97e8952ae41b8f1f8dbe | 89c64139787fd6e1ba8a0dd17f3afb453c0e845ccfcba91f92edee1f4ba6f039 |
| RepeatedScalarAny | 7cb6a50544b31fc33ae6fd021aca50a2ed30b875eb42b067bc7fce8a32ec3295 | 13d3c8be8a85fb0a779a8dc52d2222cf21de28a621348cc7e71e2c642e92bf4e |
| ColdPrimaryKeyFetch | b63978ba3c49cd2bfa6d5010e073c4383a4f35bce63a485d6f7e1ab645014c38 | 489c6788617b65c4cc300fc40a6791df0e441909dde52d7143862f84933b7fc0 |
| ColdRelationTraversal | 73ba5896f339442effe1788a883704e102dcff00362e5dc11c14786744462d0e | 58e85e2bd9be69994baf3fc2231aeafe0ed865cad69b2d0c7d423afc11de90f9 |
| UpdateEmployees | ca04afdfd4aff819f11cfc5815ce15797d71444b3641dd383c553ffb76bbc49e | 7a2444516b3f1f04ad1b595bb19d7ae31672e292ed60506d5394a86001587510 |
| CrudWorkflowSmall | 337dd09248a634e7b636b21c1a62d775139849f265feb68c077c56ec9f31ab7f | f7faaf727b9bc33a36a2d24469d46bdea497d9d4499d8503ae52a88a50758789 |
| CrudWorkflowBatch | 955885f282f79311f88a5cba2a87e6947af2ded9f17a8186e3dcf3a20f28f216 | 3d31373867ad7ef60e75d35866800db9c4ee5086d8988001e73b10c8b26dc918 |

The retained incomplete bootstrap history is `artifacts/benchmarks/history/w0-7e36614b-scalar-probe-bootstrap.json`, SHA-256 `51b5d68691f256949d541199ce1775c51572dddd30b8bdaf9104c882637fa960`.

Next, finish attribution where the samples leave material residuals and obtain paired timing evidence for every flagged/noisy workload, especially the short isolated stages. No runtime optimization or acceptance decision is made by this diagnostic slice. F19/F21, native provider work, public compatibility and the limited W0-F1 exception remain unchanged.
