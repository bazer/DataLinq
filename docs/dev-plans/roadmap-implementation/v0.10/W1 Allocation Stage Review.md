> [!WARNING]
> Strict intermediate performance evidence with unresolved warnings, not W1 performance acceptance. F19 and F21 remain open.

# W1 Allocation Stage Review

**Date:** 2026-09-21. Measured clean commit: `0aab7737049f4551dba60bf1ce4a077ab1eee2aa`, merged [PR #221](https://github.com/bazer/DataLinq/pull/221). This follows the [telemetry allocation reduction](W1%20Telemetry%20Allocation%20Reduction.md) and [controlled coordination measurements](W1%20Coordination%20Measurements.md). It precedes the [owned-cleanup correction](W1%20Owned%20Cleanup%20Occurrences.md); that correction requires subsequent final committed performance evidence.

## Capture and results

The unchanged canonical `allocation-stages` lane compares **48 exact rows** against frozen W0 commit `7e36614b5f26f1dd199ff93bc60620f4b0714d5f`. Both SQLite file and memory targets use the same operation counts and telemetry shapes. The run uses .NET SDK 10.0.401/runtime 10.0.12, BenchmarkDotNet 0.15.8, Windows 10.0.26200 x64, Intel Family 6 Model 140 Stepping 1 and eight logical processors. The `heavy` profile selects MediumRun, two launches, ten warmups and fifteen measurement iterations. Duration: **1,558.273479 seconds**.

History and comparison are complete, artifact-complete and `ValidForEvidence=true`, exit zero. Runner, DevTools and benchmark assemblies match the clean, unchanged measured commit; the benchmark DLL hash was checked before later test builds. All **54 raw receipts** match their lengths and SHA-256. No edits, builds, tests or other benchmarks overlapped measurement. There are zero invalid/missing rows, profile/scope mismatches or telemetry changes. `ReviewRequired=true` remains explicit.

The comparison classifies **26 stable, 15 improved, 4 warning and 3 noisy rows**. There are two allocation warnings and two latency warnings. Allocation is unchanged in 42 rows, reduced to zero in both mutation-preflight rows and slightly lower in both warm typed-ID rows. Both cold typed-ID terminal rows allocate more than W0. Values below are the normalized history/comparison values; the frozen lane's KiB display rounding must not be mistaken for exact byte precision.

| Workload | Provider | W0 B/op | Current B/op | Allocation change | Mean change | Latency status |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| Cold typed-ID exact terminal | sqlite-file | 5027.84 | 6062.08 | +20.6% | +3.0% | stable |
| Cold typed-ID exact terminal | sqlite-memory | 5099.52 | 6420.48 | +25.9% | +23.9% | noisy |
| Known-miss materialization/publication | sqlite-memory | 419.84 | 419.84 | 0% | +46.9% | noisy |
| Mutation command preparation | sqlite-memory | 2232.32 | 2232.32 | 0% | +18.4% | warning |
| Mutation execution preflight | sqlite-file | 716.80 | 0 | -100% | -43.8% | improved |
| Mutation execution preflight | sqlite-memory | 716.80 | 0 | -100% | -38.7% | improved |
| Mutation final drift validation | sqlite-memory | 0 | 0 | — | +26.1% | warning |
| Warm typed-ID exact terminal | sqlite-file | 1607.68 | 1597.44 | -0.6% | -20.2% | noisy |
| Warm typed-ID exact terminal | sqlite-memory | 1607.68 | 1597.44 | -0.6% | +0.4% | noisy |

The unchanged-allocation rows cover canonical key propagation, provider-row decoding/materialization, source construction/validation/publication and mutation capture/preparation/final validation. This narrows allocation attribution to the complete cold lookup path rather than demonstrating an increase in each local stage. It does **not** by itself establish exactly which objects account for the cold-path increase; the earlier scope/ExecutionContext sampling and internal coordination measurements are diagnostic leads, not a complete byte decomposition. Improvements in preflight do not offset unrelated cold-lookup or mutation/CRUD regressions.

Timing remains unresolved. The two SQLite-memory latency warnings concern mutation command preparation and final drift validation. Their lane warnings include very short iterations: preparation 2.121–3.467 ms and final validation 66.6–82.1 µs across the two provider rows. Known-miss publication and cold/warm typed-ID rows also have noisy results; maximum comparison noise reaches 34.8% in warm typed-ID memory. The raw log retains 20 minimum-iteration warnings and a multimodal-distribution warning. These are reasons for targeted timing verification, not permission to declare either a regression or a performance pass from point estimates alone. Deterministic allocation increases remain visible regardless of timing noise.

## Receipts and next work

Run ID: `20260921-204254470-e8dea9701254422daa29464d9ee8698c`. Reproduce through the Benchmark CLI after building a clean committed runner: `run --allocation-stages --profile heavy --release-evidence --history-json <new-path> --baseline artifacts/benchmarks/history/w0-7e36614b-allocation-stages.json --comparison-json <new-path>`. Preserve the frozen baseline and original receipts.

| Artifact under `artifacts/benchmarks/history/` | SHA-256 |
| --- | --- |
| `w0-7e36614b-allocation-stages.json` | `2ed9e72ac5795c23d9a322977527f1522e1a0ebe611d1583ce1217e5d4e728c2` |
| `w1-0aab7737-allocation-stages.json` | `196d613a044074d88a06cb389416f6c87cb028eff7427fa2e80a480b9ab36f9f` |
| `w1-0aab7737-vs-w0-allocation-stages.json` | `5f3d6c913da90f1868fa273190f1fe861bafb3867cc30c43870d2fc1a3c8f0bd` |

Next, finish explaining/reducing the remaining synchronous costs and resolve uncertain timing with appropriately bounded diagnostics, then run all six strict W0 lanes against the final committed integration. The five end-to-end allocation warnings from #220 and the two cold typed-ID allocation warnings here are not waived. F21 still requires the final I/O/integration and broad-evidence reconciliation. No native-provider, public/packed-consumer, DI or release gate closes here. Published compatibility stays 0.9.2 and the [limited W0-F1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception) remains unchanged.

Planning pages remain excluded from DocFX. Source links/anchors, table values and receipt hashes are checked before integration; final-head CI and guarded merge/tree verification belong in the PR.
