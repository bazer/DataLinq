> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 10 Measurement Baseline

Workstream A baseline was captured on 2026-05-12 after adding the focused Phase 10 key-foundation benchmark lane.

Baseline context:

- Branch: `codex-phase10-workstream-a`
- Commit: `5433729e706b126704bb6a5468c9463063c0ff51`
- Provider scope: `sqlite-memory`
- Benchmark profile: `default`
- BenchmarkDotNet job: `ShortRun`

The short version is blunt: the allocation columns are the useful baseline. Local latency means are extremely noisy in several rows, so Phase 10 should not use this baseline to claim speedups unless later runs are much cleaner or repeated history confirms the movement.

## Artifact Set

Local benchmark artifacts:

- `artifacts/benchmarks/history/phase10-baseline-phase2-watch.json`
- `artifacts/benchmarks/history/phase10-baseline-phase3-query-hotpath.json`
- `artifacts/benchmarks/history/phase10-baseline-key-foundation.json`

Supporting summary artifacts:

- `artifacts/benchmarks/results/20260512-131917580-54e3956d12bd4b7ab60fe8cae4258f1a-summary.json`
- `artifacts/benchmarks/results/20260512-132038342-bb9206f6125e4695a041cce69ec078b0-summary.json`
- `artifacts/benchmarks/results/20260512-132200539-0f1707990845439684e14f376e704ca9-summary.json`

The artifact directory is intentionally ignored by git. This page records the baseline evidence that should survive local cleanup.

## Commands

```powershell
$env:DATALINQ_BENCHMARK_PROVIDERS = 'sqlite-memory'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase2-watch --profile default --history-json artifacts\benchmarks\history\phase10-baseline-phase2-watch.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase3-query-hotpath --profile default --history-json artifacts\benchmarks\history\phase10-baseline-phase3-query-hotpath.json
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Benchmark.CLI -- run --phase10-key-foundation --profile default --history-json artifacts\benchmarks\history\phase10-baseline-key-foundation.json
```

## Phase 2 Watch Baseline

| Scenario | Allocated | Mean | Noise |
| --- | ---: | ---: | ---: |
| Provider initialization | 310.91 KB | 657.9 us | 220.1% |
| Startup primary-key fetch | 54.03 KB | 639.7 us | 111.6% |
| Warm primary-key fetch | 14.86 KB | 224.1 us | 16.6% |

The provider initialization allocation baseline is materially lower than the older Phase 9A closeout number, so future comparisons should use this Phase 10 artifact rather than stale May 10 allocation-audit rows.

## Phase 3 Query Hot-Path Baseline

| Scenario | Allocated | Mean | Noise |
| --- | ---: | ---: | ---: |
| Repeated non-PK equality fetch | 31.61 KB | 398.7 us | 95.2% |
| Repeated scalar `Any` | 25.20 KB | 461.2 us | 28.9% |
| Repeated `IN` predicate fetch | 45.46 KB | 690.4 us | 578.5% |

These rows remain useful for allocation deltas and query telemetry shape. They are not clean latency gates.

## Phase 10 Key-Foundation Baseline

| Scenario | Allocated | Mean | Noise | Telemetry |
| --- | ---: | ---: | ---: | --- |
| Warm generated static `Get` | 0.05 KB | 1.048 us | 1830.9% | Row cache hit: 1.0/op |
| Scalar row-cache add/get/remove | 0.15 KB | 1.092 us | 165.5% | Direct row-cache probe |
| Warm relation traversal | 0 KB | 1.504 us | 2359.4% | Relation hits: 2.0/op |

This lane exists to isolate Phase 10's key/cache target surface:

- generated scalar primary-key fetch after row-cache warmup
- warm generated relation traversal
- direct scalar primary-key row-cache add/get/remove without SQL execution noise

The tiny means make the default-profile error percentages ugly. That is expected for micro probes this small. Treat allocation and telemetry as the main signal unless a heavier local profile is run specifically for latency analysis.

## Workstream A Status

Satisfied:

- refreshed Phase 2 watch and Phase 3 query hot-path allocation baselines
- added a focused Phase 10 key-foundation benchmark lane
- confirmed warm generated static `Get`, warm relation traversal, and scalar row-cache add/get/remove probes
- captured commit SHA, date, profile, provider, and artifact paths

Not claimed:

- no latency improvement
- no provider-key cache implementation yet
- no deletion of `IKey` paths yet
