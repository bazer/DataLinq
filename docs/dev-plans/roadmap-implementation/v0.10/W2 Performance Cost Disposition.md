# W2 Performance Cost Disposition

This follows the [clean functional and performance checkpoint](W2%20Functional%20And%20Performance%20Checkpoint.md). The paired controls below measure W1 `a435d0b428937040bfd02210e67eb1219c9ed6f7` and W2 `49176d7a50d51a4de1848b501652e82215793b98`. The [revised candidate checkpoint](W2%20Revised%20Candidate%20Checkpoint.md) records the subsequent clean `76781c89` runtime, its full matrix and all six canonical lanes. This is an internal W2 cost review, not final release approval or corrected-SQLite acceptance.

## Mutation Allocation Attribution

The existing W1 allocation probe was reused unchanged against both already-built benchmark assemblies, in separate processes. Each of three workloads uses its actual harness setup/cleanup and 20 warmups followed by 10 measured invocations. Setup/cleanup is outside the exact synchronous thread-allocation measurement and trace markers. The update workload declares 2,000 operations per invocation; each CRUD workload declares 350. All six observations preserve those counts, equal result checksums and identical normalized telemetry.

| SQLite memory workload | W1 exact managed B/op | W2 exact managed B/op | Additional B/op | Additional sampled scope/context B/op |
| --- | ---: | ---: | ---: | ---: |
| Update employees | 17564.14 | 20317.70 | 2753.56 | 1781.60 |
| CRUD workflow small | 78353.25 | 86332.66 | 7979.41 | 5057.85 |
| CRUD workflow batch | 78339.50 | 86220.21 | 7880.72 | 6275.94 |

The exact workload-thread increases reproduce the scale of the canonical +2,754.56 / +8,038.40 / +7,966.72 B/op observations. EventPipe allocation ticks independently identify `ExecutionFailureScope`, `ExecutionContext` and the `AsyncLocal` value-map family as the largest source of added allocation. The six traces contain 3,289/3,808 update ticks, 2,571/2,835 small-CRUD ticks and 2,571/2,832 batch-CRUD ticks for W1/W2 respectively.

These type totals are **sampled estimates**, not exact per-type byte accounting. A tick is assigned to the object that crosses the sampling threshold, and a tick inside the work interval can include preceding setup allocation. Do not subtract the sampled scope estimate from the exact total and label the remainder an exact unexplained cost. The complete per-type tables and raw traces are retained, including negative differences and changes in unrelated object families.

The source paths explain the added work:

- [Native transaction commands](../../../../src/DataLinq.SQLite/SQLiteDatabaseTransaction.Commands.cs) now bind synchronous calls through the same native transaction resource used by async first use. Each captured command has its own `SyncRawCommand` and native adapter. Dispatch, the logging callback and detachment have separate occurrence scopes so a reused exception or callback failure cannot inherit an earlier provider diagnosis.
- [Native readers](../../../../src/DataLinq.SQLite/SQLiteAsyncDataLinqDataReader.cs) preserve the command/connection ownership boundary through reader disposal. The wrapper, value reader and admission gate have separate lifetimes. The synchronous path still calls the synchronous driver directly; the class name does not imply `Task.Run` or synchronous waiting on async work.
- [Lazy native initialization](../../../../src/DataLinq.SQLite/SQLiteDatabaseTransaction.Resource.cs) allocates a private resource bundle and coordinates the initial open, visibility command, deferred begin and publication. Initialization and status notification have distinct diagnostic boundaries. The same resource survives switching between synchronous and asynchronous use.
- [Diagnostic scopes](../../../../src/DataLinq/Execution/ExecutionFailureScope.cs) have immutable ancestry in `AsyncLocal`. The [W1 primitive measurement](W1%20Coordination%20Measurements.md) already quantified their managed cost. Replacing this identity with mutable shared state would break the tested exception-occurrence contract; primitive costs cannot simply be multiplied into an exact production-query total.

The samples also identify the new `SyncRawCommand`, native adapter, native reader, enumerator gate and associated delegate/closure families. This is additional coordination around unchanged work: the update/CRUD checksums, query counts, mutation counts, transaction counts, rows, materializations and cache behavior agree. No `ExecutionFailures` collector is intentionally allocated on the successful native-command path.

**Allocation disposition:** retain the measured native ownership and diagnostic cost for internal W2 integration. It is a real increase of about 15.7% for the update workload and 10.1–10.2% for these CRUD workloads versus W1, in addition to W1's already recorded overhead. Preserve it in W9's final-release comparison. This decision does not claim every allocation is irreducible; it rejects removing validated ownership, callback isolation or cleanup guarantees merely to recover the old number.

The generated binary-getter allocation is separate. The checkpoint's actual-getter probe measures one owned array per nonempty access (payload plus 24 bytes on the measured runtime). That restores the defensive-copy contract and is retained as a correctness correction, not attributed to these integer-key employee workloads.

## Timing Controls

The mutation follow-up has a fixed W1-forward → W2-forward → W2-reverse → W1-reverse order. It reuses the W1 timing probe unchanged. Every invocation has its own actual harness setup and cleanup; cold state is not reused to inflate the apparent sample count. Each case has at least 64 warmup calls and two elapsed warmup seconds, then at least 64 measured calls and 250 ms accumulated measured work. No sample is trimmed and no timing overhead is subtracted. Work checksums, including SQLite's advancing generated IDs, are validated.

These are descriptive process controls, not additional BenchmarkDotNet histories, independent statistical samples or an equivalence test. They preserve the original canonical warnings. The probe's storage is preallocated outside measurement. No local build, test or profiler overlaps the timing runs.

| Workload / SQLite memory | W1 forward, us | W2 forward, us | Forward change | W1 reverse, us | W2 reverse, us | Reverse change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Update employees | 63.1547 | 65.5048 | +3.72% | 59.7978 | 60.3404 | +0.91% |
| CRUD workflow small | 459.0657 | 408.7243 | -10.97% | 358.5296 | 407.1327 | +13.56% |
| CRUD workflow batch | 428.6372 | 412.7255 | -3.71% | 359.1571 | 417.2176 | +16.17% |

The updated path has a small mean increase in both orders. CRUD reverses direction with order: its additional allocation is reproducible, but these means do not isolate the size of its latency contribution. Fresh W1 itself is slower than its original canonical capture for several cases. This bounds the attribution claim; it does not make the original W2 means disappear or prove equivalent performance.

**Mutation latency disposition:** retain the native coordination cost and the unfavorable original/controlled results for internal W2 integration. Carry the canonical update +32.34%, small CRUD +35.03% and noisy batch CRUD +39.22% versus W0 alongside these fresh W1 controls. Do not present the favorable forward pair alone, combine dependent invocations into a significance claim or attribute all change to the environment. The reverse-order +13.56%/+16.17% CRUD observations remain explicit costs to revisit with final release evidence.

A second fixed ABBA sequence covers eleven remaining cold-key/startup and flagged stage cases, using the same protocol. All 44 rows have matching operation counts and normalized telemetry. Binary-key checksums use the process-local hash seed and are checked against the actual fixture formula, not numerically compared across processes.

| Workload / provider | W1 forward, us | W2 forward, us | Forward change | W1 reverse, us | W2 reverse, us | Reverse change |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| ColdPrimaryKeyFetch / sqlite-memory | 47.9338 | 54.9581 | 14.65% | 63.0793 | 87.0997 | 38.08% |
| StartupPrimaryKeyFetch / sqlite-memory | 526.0015 | 727.1148 | 38.23% | 406.1805 | 426.1290 | 4.91% |
| ColdTypedIdExactTerminal / sqlite-file | 15.8731 | 20.5711 | 29.60% | 17.9649 | 27.6278 | 53.79% |
| KnownMissMaterializationPublication / sqlite-memory | 0.3940 | 0.4381 | 11.18% | 0.5111 | 0.3845 | -24.77% |
| MutationCommandPreparation / sqlite-memory | 0.7172 | 0.7537 | 5.09% | 0.7397 | 0.9697 | 31.10% |
| CanonicalProviderRowDecoding / sqlite-file | 0.6157 | 1.3454 | 118.52% | 0.9655 | 0.5904 | -38.85% |
| SourceBatchSliceCreation / sqlite-memory | 0.0028 | 0.0066 | 136.91% | 0.0024 | 0.0024 | 1.60% |
| SingularSourceResultValidation / sqlite-file | 0.0112 | 0.0119 | 6.53% | 0.0301 | 0.0285 | -5.48% |
| ScalarCanonicalKeyPropagation / sqlite-memory | 0.1762 | 0.1680 | -4.66% | 0.2375 | 0.2403 | 1.17% |
| SourceRequestConstruction / sqlite-file | 0.1566 | 0.1650 | 5.37% | 0.2715 | 0.3933 | 44.85% |
| BinaryCanonicalKeyPropagation / sqlite-file | 0.2303 | 0.3296 | 43.11% | 0.2131 | 0.1892 | -11.19% |

The cold primary-key and typed-ID means increase in both orders. Their size is not explained by unchanged operation counts, and the micro-stage controls vary strongly with order even where source and exact allocation are unchanged. In particular, the known-miss stage changes sign, decoding changes from +118.52% to -38.85%, and batch slicing changes from +136.91% to +1.60%. This is evidence of measurement sensitivity, not a license to dismiss the cold-query results. These results remain open for the revised candidate below.

The already captured Memory controls are stable against fresh W1. The six hot-path comparisons remain noisy and inconclusive. The two initial query-backend warnings are lower than W1 (scalar binding 0.1827 vs 0.1885 us; SQL-adapter Any/file 23.0787 vs 24.0015 us), so they retain W1's existing cost disposition. The original ninety-row table remains the canonical record; none of these controls changes its thresholds or status flags.

## Avoidable Lifecycle Factory Allocation

Source inspection found a separate avoidable cost in both provider families: `RootDisposal` passed a capturing factory to `LazyInitializer.EnsureInitialized` on every access, even when the coordinator already existed. A volatile read of the published reference now handles that fast path; `LazyInitializer` still owns first publication. `EnsureUsable` is still called, and the coordinator, cleanup steps, lifecycle state and error isolation are unchanged.

The [actual provider-access probe](evidence/lifecycle-access/Program.cs) warms 10,000 calls and measures another 10,000 through the public `DatabaseAccess` getter on each real SQLite provider. It checks that every access returns the same object and that access after provider disposal throws. Reflection and fixture setup/cleanup are outside allocation measurement; the probe does not claim getter latency or server-provider allocation evidence.

| Provider | Previous runtime B/access | Working fast-path B/access | Stable access identity | Disposed access rejected |
| --- | ---: | ---: | --- | --- |
| SQLite file | 64 | 0 | yes | yes |
| SQLite memory | 64 | 0 | yes | yes |

Both observations use probe DLL SHA-256 `4235cc60e390de61937236dadfb145414d3ee6986625309a6465514d7fe6c493`. The previous runtime is the archived clean `49176d7a` benchmark DLL. The revised runtime is a **dirty working build based on `3faea4ef`**, benchmark DLL SHA-256 `372b222b19de48739b92998887e1464f244a4cfc3b4593311294d400c5584870`. Reports retain all loaded DataLinq DLL versions/hashes; these are not clean-candidate acceptance receipts.

`artifacts/w2-lifecycle-access-before.json` has SHA-256 `29eb747559a4b96b59d1914b8b4afc70bbc5e67905fe9da2644bdafb79ccb205`; `artifacts/w2-lifecycle-access-after.json` has SHA-256 `f944cc48342ec5cd595f1dd5fb76ea88898c975651fbd687e09d16290060ca1a`. Reproduce with the [standalone project](evidence/lifecycle-access/Probe.csproj), invoking its DLL with `<benchmark.dll> <fresh-output.json>` after building it. The two provider changes share the same volatile fast path, but only the SQLite getters are quantified by this probe.

Release builds of the benchmark harness, unit suite, server suite and all three tracked probe projects pass with zero warnings/errors. Focused native SQLite coverage passes **79/79**; native administration coverage passes **45/45** across all six server targets at maximum concurrency eight. These are filtered working-source runs, not clean-candidate acceptance. The first unit run attempted to rebuild the still-running Testing CLI through a project reference and hit a Windows DLL lock (`MSB3021`/`MSB3027`); prebuilding the unit project before launching the CLI with `--no-build` resolves that tooling conflict. The failed run remains `artifacts/w2-lifecycle-fastpath-sqlite.json`.

Passing unit receipt `artifacts/w2-lifecycle-fastpath-sqlite-verified.json` (run `20260924T054320356Z-b0fbff6340e14b7ea4518abdcf5b0467`) has SHA-256 `111e8bdc14fc1f693a3b5bcdd97914b7188189f009d6fd34ba83b76eb429bc1a`. Passing server receipt `artifacts/w2-lifecycle-fastpath-servers.json` (run `20260924T054406215Z-5792ad1d1ac84aa6a599ca671a1e4ca0`) has SHA-256 `ecc1254d9c9c3ad75f68e7aedac39358e2245310e6f725943422eae537b77491`.

This removes one identified allocation source without weakening the lifecycle contract. It does **not** establish that the cold-query latency increases are solved, or eliminate the native coordination costs described above. The [subsequent clean candidate capture](W2%20Revised%20Candidate%20Checkpoint.md) verifies the same zero-allocation getter result, 8,539 passing tests and 90 canonical rows. Performance acceptance remains open with the measured costs and timing uncertainty explicit. Neither SQLite acceptance gate is changed.

The same allocation profiler was also run once for each mutation workload against the working build. Updates measure 20,318.08 B/op, small CRUD 86,173.99 and batch CRUD 86,476.36, compared with 20,317.70 / 86,332.66 / 86,220.21 before. Checksums, operations and normalized telemetry still match. This does **not** show a consistent end-to-end mutation allocation reduction; the isolated getter improvement must not be advertised as one. The required native coordination cost remains. Raw JSON/traces are `artifacts/w2-mutation-fastpath-<update|crud-small|crud-batch>.*`; `artifacts/w2-mutation-fastpath-verification.json` retains the six hashes and runtime checks.

## Reproduction And Evidence

The [allocation probe source](evidence/allocation-probe/Program.cs) is the unchanged W1 v3 source, including its event-provider name. Its original DLL hash is `f1ded985d2807074d4c4b25ed903457c177f3a694bd3c5372ea92a6155c7cde0`. Source SHA-256 is `3c9f06a3d63801aa70459ad548c82cafd36c9c6a4f33c220218076b8a5576017` before Git newline normalization. The mutation timing observations use the original W1 v2 source, SHA-256 `56acc8a8dce96626efaef8452a66833acd1cc6d59ad45f9022967c696fd64efa`.

The original mutation timing DLL hash is `f8b7b65e1f9fb9d322f7d5f1376e7ee56bc527558e7219351211438ee105774e`. The tracked [timing probe](evidence/timing-probe/Program.cs) preserves that protocol and adds an explicit `w2-remaining` recipe list for cold-key/startup and flagged stage controls. This extension does not alter the runtime assemblies or canonical harness. Standalone project files resolve the diagnostic libraries from the previously built benchmark output. Build the probes before measurement, not concurrently with it.

```powershell
.\scripts\dotnet-sandbox.ps1 restore docs/dev-plans/roadmap-implementation/v0.10/evidence/allocation-probe/Probe.csproj
.\scripts\dotnet-sandbox.ps1 build docs/dev-plans/roadmap-implementation/v0.10/evidence/allocation-probe/Probe.csproj -c Release
.\scripts\dotnet-sandbox.ps1 restore docs/dev-plans/roadmap-implementation/v0.10/evidence/timing-probe/Probe.csproj
.\scripts\dotnet-sandbox.ps1 build docs/dev-plans/roadmap-implementation/v0.10/evidence/timing-probe/Probe.csproj -c Release
# Use separate processes, the actual previously built benchmark.dll and fresh output paths.
.\scripts\dotnet-sandbox.ps1 exec <allocation-probe.dll> <benchmark.dll> UpdateEmployees 10 <fresh-prefix>
.\scripts\dotnet-sandbox.ps1 exec <timing-probe.dll> <benchmark.dll> mutations forward <fresh-output.json>
.\scripts\dotnet-sandbox.ps1 exec <timing-probe.dll> <benchmark.dll> w2-remaining forward <fresh-output.json>
```

Allocation artifacts are `artifacts/w2-mutation-<w1|w2>-<update|crud-small|crud-batch>.json` and `.nettrace`. Mutation timing artifacts are `artifacts/w2-mutation-timing-<w1|w2>-<forward|reverse>.json`. Loaded dependency versions and hashes identify the measured runtime, rather than inferring it from the current documentation checkout. W1 uses the clean historical target produced by the Benchmark CLI; W2 uses the existing clean candidate output. Raw evidence remains local; the runnable sources and this report are tracked.

The remaining 44 timing rows are in `artifacts/w2-remaining-timing-<w1|w2>-<forward|reverse>.json`. [Verification](evidence/verify-performance-attribution.ps1), run from the repository root against these retained local artifacts, checks dependency hashes, runtime versions, clean harness identity, exact allocation arithmetic, checksums, operation counts and telemetry parity. It records all 20 raw JSON/trace lengths and hashes in `artifacts/w2-performance-attribution-verification.json`, SHA-256 `7bf1c093a4bddff8aeece570b5024c8d808ff854d766ecc6db09b10dba7d1906`.

Before rebuilding the runtime, all 92 benchmark output files (94,296,057 bytes) were archived under `artifacts/benchmarks/bin-snapshots/w2-49176d7a` and independently compared with the live originals. Manifest `artifacts/w2-49176d7a-binary-snapshot.json` has SHA-256 `5cfe5cc38295ccee630125feb60cc440f295fc53eeb1b41ac00b1c6d5e425ea1`. Verification now resolves the original W2 dependency paths against that archive; it does not compare them with the replacement working build or rewrite the captured identities.
