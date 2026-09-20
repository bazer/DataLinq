> [!WARNING]
> This is an interim nine-row checkpoint, not W1 performance acceptance. Allocation increases and variable timing warnings remain open. The other 81 W0 benchmark rows, internal async coordination costs and the final W1 audit are not covered by this capture.

# W1 Initial Performance Checkpoint

**2026-09-20 follow-up:** the [mutation preflight reduction](W1%20Mutation%20Preflight%20Allocation%20Reduction.md) adds full 48-stage evidence and measures a reduction in update/CRUD allocation after the read-diagnostics changes. It preserves this earlier checkpoint and its unresolved warnings; final performance acceptance remains open.

**Date:** 2026-09-19. Follows [scoped failure attribution](W1%20Scoped%20Failure%20Attribution.md) and the [W1 completion audit](W1%20Completion%20Audit.md). The frozen [W0 performance baseline](W0%20Baseline%20Evidence.md) remains `7e36614b5f26f1dd199ff93bc60620f4b0714d5f`; the published compatibility baseline remains 0.9.2.

## Scope and provenance

Three sequential `--allocation-regression --profile heavy --release-evidence` invocations captured the same nine SQLite-memory workloads, with the default `--filter "*"`, fresh harness builds, unchanged operation counts and per-row telemetry. Each used the W0 history as `--baseline`, with separate `--history-json` and `--comparison-json` outputs. No local tests or other benchmarks ran alongside them, and neither measured checkout changed during its run.

| Capture | Runtime commit | Run ID |
| --- | --- | --- |
| Current, first | `ec9c7e34bae5bbd6aab6d5e57f7a5891155feee2` | `20260919-181536690-ac2de7cbeba94bb9a98659eae1cabdd9` |
| Before attribution | `e179f6cf5352ab9e9b17da9554bbf4ad3e5e41c8` | `20260919-182634281-83817055cd724b74aee9f9522ce2c04f` |
| Current, repeat | `ec9c7e34bae5bbd6aab6d5e57f7a5891155feee2` | `20260919-183335041-343aaf34da504e97993ea06ed719e8fc` |

The current runtime is the tested head of [PR #189](https://github.com/bazer/DataLinq/pull/189); its tree matches the integration squash `a08b0f8452c030cd1c52215fc35e61836380700b`. The preceding runtime is the squash of [PR #188](https://github.com/bazer/DataLinq/pull/188), run with `--benchmark-target-root artifacts/benchmarks/targets/w1-before-attribution`. Current tooling remained at `ec9c7e34` for all three captures. Benchmark and CLI source files are unchanged between the two measured commits; the historical target still uses the recorded external-harness build/configuration hooks. This is a bounded control, not a claim that every build input is identical.

All three histories and comparisons have `ValidForEvidence=true`, complete artifacts and exit zero; every comparison has `Comparable=true`, nine matched rows and no recorded telemetry changes. **All three outcomes are `ReviewRequired`.** Artifact validity establishes the capture contract, not performance acceptance. After completion, all 45 raw artifact receipts in the three histories were independently checked against their recorded size and SHA-256.

The runner matches W0's recorded Windows 10.0.26200 x64, eight logical processors and Intel Family 6 Model 140 Stepping 1 identity. SDK 10.0.401, runtime .NET 10.0.12, BenchmarkDotNet 0.15.8 and `MediumRun` remain recorded. These checks do not establish identical machine load, thermal state or other unrecorded conditions. No particular environmental cause is established for the timing variation.

## Observed changes

The table preserves both current-commit comparisons against W0. Percentages are rounded from the comparison artifacts; allocation values in those artifacts are normalized reported bytes per operation, not allocation-stack measurements.

| Workload | First time delta | Repeat time delta | First allocation delta | Repeat allocation delta |
| --- | ---: | ---: | ---: | ---: |
| CRUD batch | -0.6% | +2.0% | +4.6% | +4.6% |
| CRUD small | +7.4% | +31.3% | +4.6% | +4.7% |
| Cold primary key | +0.6% | +8.8% | 0.0% | 0.0% |
| Cold relation | +3.2% | +6.3% | +2.7% | +2.7% |
| Provider initialization | -7.0% | +5.9% | -4.0% | -4.0% |
| Startup primary key | -1.3% | +30.1% | -20.3% | -20.2% |
| Update | +12.5% | +6.9% | +7.3% | +7.3% |
| Warm primary key | +2.2% | +2.6% | 0.0% | 0.0% |
| Warm relation | +4.6% | -6.2% | zero bytes | zero bytes |

Update allocation is reported as **15,626.24 B/op at W0 and 16,773.12 B/op in both current runs**. Before attribution it was already 16,824.32 B/op (+7.7%). Thus a comparable increase exists before PR #189; this does not isolate the cost of that PR or prove its scopes are free. The two current update means are 55.6590 and 52.9151 microseconds, against W0's 49.4950. The first current error interval overlaps W0's: 55.6590 +/- 5.5269 versus 49.4950 +/- 3.6105 microseconds. Retain the CLI warning without treating its threshold as a statistical proof of a fixed slowdown.

The first current comparison has one latency warning (update). The repeat has two (CRUD small and startup primary key), with update below the warning threshold. The earlier-commit control has five latency warnings (cold primary key, provider initialization, startup primary key, update and warm primary key), plus noisy CRUD small/batch timings. Preserve all three runs; selecting the fastest result, averaging unrelated workloads or discarding the warning run would conceal uncertainty. Short-iteration and multimodal-distribution warnings remain in the histories.

Allocation increases are not excused by timing variation or by staying below the CLI's 10% warning threshold. Startup allocation reductions likewise do not offset update/CRUD/relation increases. Unchanged recorded telemetry is useful semantic evidence but does not prove uninstrumented work is unchanged or satisfy the outstanding async telemetry requirements.

## Disposition and next work

The performance gate remains **open**. Profile the transaction ownership and materialization paths, measure any proposed reduction, and preserve failure isolation, cache identity and admission safety. The source audit finds per-owner loading/materialization service construction in [DataSourceAccess](../../../../src/DataLinq/Mutation/DataSourceAccess.cs). The exact-key path used by this update benchmark enters [SqlQueryPlanBackend](../../../../src/DataLinq/Linq/Planning/Sql/SqlQueryPlanBackend.cs) directly rather than the guarded iterator. These are investigation leads, not measured attribution of the extra bytes or latency. Do not remove diagnostic lineage on the strength of this aggregate comparison.

Continue the direct synchronous reporting audit through eager scalar/key reads, [ReadCommandResources](../../../../src/DataLinq/Execution/ReadCommandResources.cs) and the direct relation-row path in [TableCache.RowLoading](../../../../src/DataLinq/Cache/TableCache.RowLoading.cs). Complete the remaining W1 code/telemetry and adapter-readiness work, then capture all six W0 lanes against the final integrated candidate and measure the internal coordination paths that these public synchronous workloads do not exercise. The separate final-0.8 allocation budget, native W2 acceptance, public W3 declarations and SQLite W0-F1 remain unchanged.

## Retained artifacts

Histories and comparisons remain local under `artifacts/benchmarks/history/`; their referenced raw output and telemetry remain under `artifacts/benchmarks/runs/<Run ID>/`. The historical worktree is retained. No raw artifacts were uploaded, no earlier evidence was overwritten, and no benchmark threshold, workload or baseline was changed. The following hashes identify the six checkpoint artifacts; the W0 history hash is retained in each comparison.

| File beneath `artifacts/benchmarks/history/` | SHA-256 |
| --- | --- |
| `w1-ec9c7e34-allocation-regression.json` | `5a1e4711deb1ed2b7881a467d8088fbe52f79f1bd6faedcdc4fa508f7bf537e0` |
| `w1-e179f6cf-allocation-regression.json` | `bda1ed59da45141c2021a7c9218fcc9388b075cf558c9220515dd0f9f7c91f57` |
| `w1-ec9c7e34-allocation-regression-repeat.json` | `0fcea2c7797dc597add99225e7caea5781f68d3833be52dfdd711b51a35758e9` |
| `w1-ec9c7e34-vs-w0-allocation-regression.json` | `73d4db96c31e64c77111f4c2014fb186bf8b2b79c93b9f75c27e17bb176ac0b5` |
| `w1-e179f6cf-vs-w0-allocation-regression.json` | `04714acc27e94fda0e589d8e03abe776344c8e64d23ca577701bfff8bff8237b` |
| `w1-ec9c7e34-vs-w0-allocation-regression-repeat.json` | `ef38879d1dc60f69c3de42a223d0cfeea84ee904858127bbd8d53e4e04e1b0a6` |
