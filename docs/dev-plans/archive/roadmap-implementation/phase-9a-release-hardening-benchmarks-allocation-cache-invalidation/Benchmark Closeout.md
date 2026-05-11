> [!WARNING]
> This document is roadmap execution material. It is not normative product documentation, and it should not be treated as a shipped support claim.
# Phase 9A Benchmark Closeout

Phase 9A closeout was measured on 2026-05-10 with the default benchmark profile and the published-history provider scope `sqlite-memory`.

The short version is blunt: the allocation story is defensible, the latency story is not. The local `ShortRun` measurements are too noisy to claim speedups or regressions, so release wording should describe lower allocation pressure, cache invalidation hardening, richer telemetry, and better benchmark-history evidence rather than faster queries.

## Artifact Set

Local benchmark artifacts:

- `artifacts/benchmarks/history/phase9a-final-phase2-watch.json`
- `artifacts/benchmarks/history/phase9a-final-phase2-watch-comparison.json`
- `artifacts/benchmarks/history/phase9a-final-phase3-query-hotpath.json`
- `artifacts/benchmarks/history/phase9a-final-phase3-query-hotpath-comparison.json`
- `artifacts/benchmarks/history/phase9a-final-crud-workflows.json`

Comparison baselines:

- `artifacts/benchmarks/history/allocation-audit-phase2-watch-20260510.json`
- `artifacts/benchmarks/history/allocation-audit-phase3-query-hotpath-20260510.json`

The artifact directory is intentionally ignored by git. This page records the release evidence that should survive local cleanup.

## Before And After Allocation

| Scenario | Baseline allocated | Final allocated | Allocation delta | Final mean | Noise status |
| --- | ---: | ---: | ---: | ---: | --- |
| Provider initialization | 899.41 KB | 835.22 KB | -7.1% | 2,531.8 us | noisy |
| Startup primary-key fetch | 145.86 KB | 153.66 KB | +5.3% | 876.0 us | noisy |
| Warm primary-key fetch | 15.75 KB | 15.30 KB | -2.9% | 259.5 us | noisy |
| Repeated non-PK equality fetch | 33.30 KB | 32.45 KB | -2.6% | 431.5 us | noisy |
| Repeated scalar `Any` | 25.73 KB | 25.19 KB | -2.1% | 463.0 us | noisy |
| Repeated `IN` predicate fetch | 47.91 KB | 47.81 KB | -0.2% | 645.6 us | noisy |

No row crossed the comparison warning threshold as a non-noisy regression. Startup primary-key fetch moved up by 5.3% allocation, but both baseline and candidate timings were noisy and the allocation change is below the configured 10% warning threshold. This does not need an owner beyond normal follow-up key/cache monitoring.

## CRUD Macro Evidence

| Scenario | Category | Operations per invoke | Allocated | Mean | Noise |
| --- | --- | ---: | ---: | ---: | ---: |
| CRUD workflow small | `macro-readwrite` | 50 | 111.54 KB | 1,553.0 us | 46.5% |
| CRUD workflow batch | `macro-bulk` | 300 | 110.65 KB | 1,524.0 us | 278.9% |

Both CRUD workflows report the same per-operation telemetry shape: two entity queries, one transaction start, one commit, one insert, one update, one delete, three affected rows, one row-cache hit, five row-cache misses, five row-cache stores, five database rows, five materializations, zero relation hits, and two relation loads.

These scenarios are useful for invalidation telemetry and website trend visibility. They are not yet useful as latency regression gates.

## Release Claims

Safe claims:

- Phase 9A cleaned warning noise and made DataLinq-owned warnings fail the Debug build.
- Benchmark history now preserves profile, commit, runner, category, uncertainty, allocation, telemetry deltas, and same-profile comparison context.
- The website can show profile-separated benchmark trends, compact telemetry details, commit links, and smoothed/outlier-filtered charts.
- Metadata, key, generated metadata, query rendering, relation preload, and cache-internal changes reduced or stabilized allocation in the measured default-profile `sqlite-memory` slices.
- Cache invalidation is now characterized and hardened around update/delete, relation/index changes, commit/rollback boundaries, cache notifications, lazy cache-history snapshots, reverse index mappings, and row-cache byte accounting.

Unsafe claims:

- Do not claim Phase 9A made hot-path latency faster. Every closeout comparison row is noisy.
- Do not claim CRUD macro timings are stable enough for regression enforcement.
- Do not imply Phase 9A shipped provider-key cache cleanup, external invalidation hooks, adaptive cache policy, dependency-tracked result-set caching, or global deduplication. Those remain Phase 10-12 or later work.

## Follow-Up Cache Starting Point

The follow-up key/cache phases can start from a better foundation: invalidation behavior is covered by targeted tests, cache maintenance emits telemetry that can explain work performed, and benchmark history can show whether future cache-semantics work changes allocation, noise, or invalidation shape.
