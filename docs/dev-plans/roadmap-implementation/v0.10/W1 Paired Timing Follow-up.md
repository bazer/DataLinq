> [!WARNING]
> Performance follow-up, not W1 acceptance. The allocation pair is strict canonical evidence; the other four pairs are supplemental filtered diagnostics. Neither replaces the original six-lane checkpoint or removes its warnings.

# W1 Paired Timing Follow-up

**Date:** 2026-09-23. Follows the [six-lane checkpoint](W1%20Six-Lane%20Performance%20Checkpoint.md) and [paired allocation diagnostics](W1%20Paired%20Allocation%20Diagnostics.md). This slice changes no production code, benchmark workload, operation count, evidence validator or public API.

## Scope and provenance

The before-state is the actual frozen W0 commit `7e36614b5f26f1dd199ff93bc60620f4b0714d5f`, rebuilt in the clean historical target `artifacts/benchmarks/targets/w0-7e36614b-net10`. It is not the published-0.9.2 target. The scalar-adapter pair uses clean runner/candidate `41cfb045722f146681d4dc78057f297b79f76381`; the other four pairs use clean integration `3ce130009c62347b398c43ba52eb6ad32813a86e` (merged [PR #224](https://github.com/bazer/DataLinq/pull/224)). Both have identical `src` and `scripts` trees to the canonical candidate `726810c64f82212e568dba76bc6ffdcc59410543`; the intervening changes are evidence documentation.

Every pair runs W0 followed by W1, serially on the same host, using the existing `heavy`/MediumRun protocol on .NET 10.0.12, BenchmarkDotNet 0.15.8 and Windows x64. There is no overlapping local build, test, profiler, competing benchmark or tracked edit during sampling. This is a fixed-order repeat, not a randomized or order-balanced experiment; it cannot rule out host drift or establish the cause of timing variation.

All ten histories and five comparisons finish with exit zero, complete artifacts, matching clean runner/target/assembly identities, unchanged checkout state and zero invalid rows. The nine-row allocation pair has `ValidForEvidence=true`, the exact canonical target set and `--release-evidence`. The four filtered pairs are complete diagnostics with `ValidForEvidence=false` and no canonical expected target set. All **23 comparisons / 106 raw receipts** were independently rechecked, including file lengths/hashes, row matching, time normalization and identical recorded telemetry. Live benchmark binaries were hashed immediately after each capture, before any subsequent build could replace them.

The two earlier scalar receipts retain their own assembly identities. They must not be checked against a later rebuilt W1 binary merely because its filesystem path is the same. The paired verification index records both historical and current identities.

## Results

Every compared row is retained below, not just favorable repeats. Error is the history's reported mean error; it is not a separate significance test. `stable` means the comparator did not raise a latency warning at its configured threshold/noise rules, not equivalence or zero cost. Six rows have noisy latency even when allocation gives the overall row a warning status.

The histories retain the BenchmarkDotNet minimum-iteration and multimodal-distribution warnings. No samples were manually trimmed and no favorable repeat replaces an earlier result.

| Pair / workload / provider | W0 mean +/- error, us | W1 mean +/- error, us | Time delta | Allocation delta | Latency status |
| --- | ---: | ---: | ---: | ---: | --- |
| scalar-adapter / SQL adapter scalar Any / sqlite-file | 19.5800 +/- 0.6010 | 21.4300 +/- 0.8230 | 9.45% | 14.17% | stable |
| scalar-adapter / SQL adapter scalar Any / sqlite-memory | 42.1900 +/- 0.5570 | 44.8300 +/- 0.6140 | 6.26% | 13.64% | stable |
| query-hotpath / Repeated IN predicate fetch / sqlite-file | 119.6500 +/- 29.3600 | 137.7000 +/- 21.2000 | 15.09% | 27.73% | noisy |
| query-hotpath / Repeated IN predicate fetch / sqlite-memory | 142.3100 +/- 13.2300 | 151.4000 +/- 11.1600 | 6.39% | 26.99% | stable |
| query-hotpath / Repeated non-PK equality fetch / sqlite-file | 125.9000 +/- 15.1200 | 127.0000 +/- 20.0900 | 0.87% | 25.08% | stable |
| query-hotpath / Repeated non-PK equality fetch / sqlite-memory | 145.7400 +/- 25.5800 | 166.9000 +/- 34.1100 | 14.52% | 25.91% | noisy |
| query-hotpath / Repeated scalar Any / sqlite-file | 97.7900 +/- 13.0700 | 103.2000 +/- 12.7100 | 5.53% | 6.53% | stable |
| query-hotpath / Repeated scalar Any / sqlite-memory | 107.2600 +/- 16.3700 | 165.2000 +/- 23.2800 | 54.02% | 4.84% | warning |
| allocation / CRUD workflow batch / sqlite-memory | 557.6641 +/- 125.6388 | 585.2294 +/- 116.6184 | 4.94% | 18.75% | noisy |
| allocation / CRUD workflow small / sqlite-memory | 492.5375 +/- 99.6790 | 571.3057 +/- 118.1125 | 15.99% | 18.85% | noisy |
| allocation / Cold primary-key fetch / sqlite-memory | 114.0593 +/- 21.3860 | 112.6332 +/- 24.2543 | -1.25% | 20.62% | noisy |
| allocation / Cold relation traversal / sqlite-memory | 203.1287 +/- 18.8722 | 198.3281 +/- 9.4524 | -2.36% | 28.83% | stable |
| allocation / Provider initialization / sqlite-memory | 695.6654 +/- 32.0785 | 667.7145 +/- 53.6108 | -4.02% | -5.06% | stable |
| allocation / Startup primary-key fetch / sqlite-memory | 519.2250 +/- 83.2356 | 469.3364 +/- 41.6991 | -9.61% | -21.82% | stable |
| allocation / Update employees / sqlite-memory | 50.3276 +/- 3.0196 | 59.4802 +/- 6.4162 | 18.19% | 12.41% | warning |
| allocation / Warm primary-key fetch / sqlite-memory | 2.5897 +/- 0.4280 | 2.4926 +/- 0.2669 | -3.75% | 2.91% | stable |
| allocation / Warm relation traversal / sqlite-memory | 0.1234 +/- 0.0057 | 0.1174 +/- 0.0080 | -4.86% | 0 -> 0 B/op | stable |
| typed-terminal / Cold typed-ID exact terminal / sqlite-file | 58.2200 +/- 3.9890 | 55.8970 +/- 2.6930 | -3.99% | 22.40% | stable |
| typed-terminal / Cold typed-ID exact terminal / sqlite-memory | 66.2010 +/- 10.3000 | 79.6980 +/- 10.3040 | 20.39% | 27.86% | warning |
| typed-terminal / Warm typed-ID exact terminal / sqlite-file | 7.3510 +/- 1.0140 | 7.8910 +/- 1.3160 | 7.35% | -0.64% | stable |
| typed-terminal / Warm typed-ID exact terminal / sqlite-memory | 3.7630 +/- 1.3300 | 6.0360 +/- 1.2590 | 60.40% | 3.31% | noisy |
| structural-parse / Expression parse/structural template / sqlite-file | 8.4760 +/- 0.6093 | 7.9740 +/- 0.3001 | -5.92% | 0.00% | stable |
| structural-parse / Expression parse/structural template / sqlite-memory | 7.6180 +/- 0.1787 | 8.1090 +/- 0.4579 | 6.45% | 0.00% | stable |

There are **13 allocation warnings, three latency warnings, six noisy latency rows and zero telemetry changes**. These counts describe this 23-row set, not the original 90-row matrix.

## Interpretation and outstanding findings

- **Scalar adapter:** both reported timing ranges are non-overlapping despite the comparator's `stable` label: +1.85 us / +9.45% for SQLite file and +2.64 us / +6.26% for SQLite memory. The [allocation probes](W1%20Paired%20Allocation%20Diagnostics.md#source-attribution-and-limits) connect additional scope/context allocation to logical-query, provider-command, SQLite visibility-setup and independent-cleanup boundaries. This is evidence of a real additional cost, not a timing pass obtained by falling below 10%.
- **Ordinary scalar Any:** SQLite memory is 107.26 +/- 16.37 us at W0 versus 165.20 +/- 23.28 us at W1, a +57.94 us / +54.02% warning with non-overlapping reported ranges. Its allocation change is only +4.84%, and recorded work is unchanged. Unlike the pre-bound scalar adapter, [ExecuteScalarAnyBatch](../../../../src/DataLinq.Benchmark/BenchmarkContext.cs) constructs and executes an expression query each time. Raw timings vary substantially; neither that variation nor unchanged telemetry explains away the larger warning. Isolate this path with a prespecified warm-up/order control and inspect the execution stages before assigning its cause or accepting it.
- **Entity queries:** the canonical allocation increases reproduce. IN on SQLite file and non-key equality on SQLite memory remain noisy; their positive point estimates are not waived. The paired [type samples and source paths](W1%20Paired%20Allocation%20Diagnostics.md#source-attribution-and-limits) identify diagnostic scopes plus read-sequence delegates, iterators and admission/lifetime objects. Compiler-generated names and sampled allocation ticks are insufficient to assign every residual byte to an exact object; that attribution remains open.
- **CRUD and updates:** CRUD point estimates are +4.94% and +15.99%, both noisy. The rebuilt W0 alone is +39.31% for batch and +23.86% for small versus the frozen W0 timing; the source and recorded workload remain the same. That demonstrates a material difference between captures, without proving a particular environmental cause. The new update warning is +9.1526 us / +18.19%; its reported ranges overlap narrowly. Preserve it for follow-up alongside the reproduced allocation costs rather than declaring a fixed slowdown or equivalence.
- **Typed exact terminals:** the cold file-backed latency warning does not recur in this pair, but cold SQLite-memory execution remains +20.39%. Warm SQLite-memory execution is +60.40% by the point estimate and still noisy. The original warnings remain; the two cold allocation warnings reproduce. [TypedIdExactTerminalBenchmarks](../../../../src/DataLinq.Benchmark/TypedIdExactTerminalBenchmarks.cs) resets cold/warm state through iteration setup. Increasing invocations without preserving that setup would change the workload.
- **Structural parsing:** the earlier +12.89% SQLite-memory warning does not recur: this pair is +6.45% for memory and -5.92% for file, with unchanged allocation. Both paired reported ranges overlap. This limits the evidence for a consistent parser slowdown; it is not a general guarantee that unchanged source must have unchanged timing.

The calibrated allocation-lane warm-key row uses **60,000** operations per invocation. It does not resolve the two original phase2 warm-key warnings, which use **1,000**. Likewise, no warm-key result substitutes for warm typed-ID execution.

## Retained incomplete attempt

The first CRUD follow-up used `--filter '*AllocationRegressionBenchmarks.CrudWorkflow*'` without the canonical selector. The harness ran both SQLite providers with 350 operations per invocation. Without the allocation category, the reporter checks the ordinary Employees counts (50 for small and 300 for batch), so all four rows are invalid: **Incomplete**, exit one, `ValidForEvidence=false`, with complete artifacts. This is a scope/count mismatch, not a passing diagnostic or evidence of a runtime failure. See [the reporter](../../../../src/DataLinq.Benchmark.CLI/BenchmarkEvidenceReporter.cs) and [calibrated workload](../../../../src/DataLinq.Benchmark/AllocationRegressionBenchmarks.cs).

The attempt remains at `artifacts/benchmarks/history/w0-7e36614b-repeat-crud.json`, SHA-256 `552543ccf9c369d084e86cdf104f49d417ed0c39890af74d62e781486bab5777`; all ten raw receipts were verified. It contributes no rows to the results table. The replacement uses the complete nine-row `--allocation-regression --release-evidence` lane on both sides; no validator, operation count or original artifact was changed.

## Receipts and reproduction

All histories/comparisons are beneath `artifacts/benchmarks/history/`. For query-hotpath, allocation, typed-terminal and structural-parse, the exact stems are `w0-7e36614b-repeat-<pair>`, `w1-3ce13000-repeat-<pair>` and `w1-3ce13000-vs-w0-repeat-<pair>`. Scalar-adapter stems are `w0-7e36614b-scalar-adapter-repeat`, `w1-41cfb045-scalar-adapter-repeat` and `w1-41cfb045-vs-w0-repeat-scalar-adapter`.

| Pair | W0 history SHA-256 | W1 history SHA-256 | Comparison SHA-256 | Rows / raw files |
| --- | --- | --- | --- | ---: |
| scalar-adapter | 49792aec2ff0a98bd033e4f99d009940adc3923901e24d88bd2b07792eb27ea7 | 916956935f04bb14a51a98b2172a6f36e2397ee04f7623864992a937e8015325 | 71e4fb84a29392ec8bcbc784bc285a00a031046266655d814a8b0ea5c0939b57 | 2 / 16 |
| query-hotpath | 5fd9771935060ba9397759e4de931efcb5d6b0c9c4ebecf1798852a1ed955cf1 | 31541f8ba24ae6a84a43dabe6870db5dddcf9c3bc57b7b21ee3af1ca24ce73f4 | 3ac778e58ac7eea748d1a9f2e57707f50af020c04def2b83b99003cc0ef0a4e3 | 6 / 24 |
| allocation | a03551a2912b63af540b17f695141a8e222b926c387896b5833f0ccaac6f8705 | 0d3825067f6f98cd92c527bad022349889a667bf36f205e256a9241c91f2d80e | 0f30968cb64155897104b2b73e4d585a305dbdeada7b93dd202bcdda085072ef | 9 / 30 |
| typed-terminal | 5cd45ae81bec9fd12e22621b29d8f5e062034b670ae6f7cf48928187db8668f2 | 613ba52c66652f56cfed58ffc1cd8ffbe68feebe32fd9ce01294942d84d17252 | 7451622893c7e422c43f85ac16ebd9a3ece83d7d7895117b996a05666c7ae7d8 | 4 / 20 |
| structural-parse | 502937fb93f21d50bcd1d6551bffc54d1b47272d73c137ffa2b53a976b2dd68b | 4068f8f5894c5241d2b1668739601629fc3982639a5f57be3e4552f686c119f1 | 3bd32338b94b894cd2d9df8f5a938b598a85407b407fca7230903de1148e64f2 | 2 / 16 |

The independent index is `artifacts/w1-paired-timing-verification.json`, SHA-256 `e69225660bccc81062d7a89b254c4a2528af8b695b5364177182aa1d004dfc8a`; it records run IDs, exact paths/hashes, commits and benchmark assembly hashes. The capture scripts and verifier remain local diagnostic tooling under `artifacts`, not a new supported release interface. The commands below use the existing [Benchmark CLI](../../../contributing/DataLinq.Benchmark.CLI.md).

From the matching clean runner, build the CLI through `scripts/dotnet-sandbox.ps1`, then run each side using `exec src/DataLinq.Benchmark.CLI/bin/Release/net10.0/DataLinq.Benchmark.CLI.dll run --profile heavy --history-json <fresh-path>`. Add `--benchmark-target-root artifacts/benchmarks/targets/w0-7e36614b-net10` for W0; add `--baseline <new-W0-history> --comparison-json <fresh-path>` for W1. Use the same selection on both sides:

| Pair | Selection |
| --- | --- |
| scalar-adapter | `--filter '*SqlAdapterScalarAny*'` |
| query-hotpath | `--filter '*EmployeesBenchmarks.Repeated*'` |
| allocation | `--allocation-regression --release-evidence` |
| typed-terminal | `--filter '*TypedIdExactTerminalBenchmarks*'` |
| structural-parse | `--filter '*ExpressionParseStructuralTemplate*'` |

Use fresh output paths, no competing workload, unchanged provider configuration and verified clean commits/binaries. Preserve failed attempts and all warnings. Rebuilding a commit does not recreate its original timing or necessarily its binary hash.

## Remaining W1 work

F19 remains open. These repeats cover twelve of the original twenty-five warning/noisy latency cases; they do not close all twelve. Thirteen original cases still need supplemental follow-up: the two phase2 warm-key rows, binary and typed key propagation on file, provider-row decoding on file, known-miss publication on memory, mutation command preparation on both providers, and preflight, final drift validation, state capture, source publication and source validation on memory. The ordinary scalar Any and update warnings above also need disposition. Short-stage measurements must preserve iteration setup/cleanup, cold-cache state and workload counts; simply increasing invocations is not automatically valid.

Residual allocation attribution, meaningful latency disposition, final broad-evidence/I/O reconciliation and exact-head integration remain required by the [functional audit](W1%20Functional%20and%20IO%20Audit.md) and [completion audit](W1%20Completion%20Audit.md). No W1 exit, native provider readiness, public API freeze, final-0.8 parity or release approval is claimed. The [limited W0-F1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception) remains unchanged.
