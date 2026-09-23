> [!WARNING]
> All six canonical lanes are captured, but W1 performance is not accepted. Allocation attribution, timing follow-up and final integration remain F19/F21 work.

# W1 Six-Lane Performance Checkpoint

**Date:** 2026-09-23. Measured integration: `726810c64f82212e568dba76bc6ffdcc59410543`, the verified merge of [PR #223](https://github.com/bazer/DataLinq/pull/223). This follows the [activity-scope reduction](W1%20Activity%20Scope%20Allocation%20Reduction.md) and [allocation-stage review](W1%20Allocation%20Stage%20Review.md). The frozen [W0 performance baseline](W0%20Baseline%20Evidence.md) remains `7e36614b5f26f1dd199ff93bc60620f4b0714d5f`; published compatibility remains 0.9.2.

## Capture and validity

Six sequential canonical `heavy --release-evidence` runs capture **90 rows** against the corresponding frozen W0 histories. All histories and comparisons are complete, artifact-complete and valid for evidence, with exit zero, no invalid rows and no missing or mismatched targets. All comparisons remain **ReviewRequired**. Valid capture is not performance acceptance.

The environment is .NET SDK 10.0.401/runtime 10.0.12, BenchmarkDotNet 0.15.8, Windows 10.0.26200 x64, Intel Family 6 Model 140 Stepping 1 and eight logical processors. The heavy profile selects MediumRun, two launches, ten warmups and fifteen measurement iterations. Runner, DevTools and benchmark assemblies match the clean, unchanged integration commit. Each lane's live benchmark assembly was hashed before the next build; all six recorded `eb61054d051699ea0ceb524293d63b7c693ff28576d811fa1c0c6870eac63e23`.

No local source edits, tests, profiling or other benchmarks overlapped these measurements. After the final process exited, a separate verification pass checked all **126 raw files** against their byte lengths and SHA-256, plus all six history/comparison/baseline hash triples. The frozen histories were not replaced.

## Complete comparison

| Lane | Rows | Stable | Improved | Warning | Noisy | Allocation warnings | Latency warnings | Telemetry changes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| phase2-watch | 6 | 0 | 4 | 0 | 2 | 0 | 0 | 0 |
| phase3-query-hotpath | 6 | 0 | 1 | 5 | 0 | 4 | 3 | 0 |
| v09-query-backend | 12 | 6 | 3 | 3 | 0 | 2 | 2 | 0 |
| v09-memory-read | 9 | 9 | 0 | 0 | 0 | 0 | 0 | 0 |
| allocation-regression | 9 | 1 | 3 | 5 | 0 | 5 | 2 | 0 |
| allocation-stages | 48 | 22 | 12 | 12 | 2 | 2 | 12 | 0 |

Overall row statuses total **38 stable, 23 improved, 25 warning and 4 noisy**. The independent axes contain **13 allocation warnings, 19 latency warnings and zero telemetry changes**. Six rows have noisy latency; two of those retain an overall warning because their allocation increased. Do not add the independent warning counts as though they were disjoint workloads.

All nine Memory-read rows have unchanged allocation and stable latency. The query-backend parsing, binding, template-freezing and capability-preparation rows also have unchanged allocation. Within the 48-stage lane, 42 rows allocate exactly the same amount as W0; both mutation-preflight rows fall from 716.80 B/op to zero, both warm typed-ID rows fall from 1,607.68 to 1,597.44 B/op, and both cold typed-ID rows increase.

These controls narrow the cost investigation toward complete execution/coordination paths. They do not prove an exact object-level explanation, and improvements in one workload do not offset another workload's regression.

## Allocation findings

| Lane / workload / provider | W0 B/op | W1 B/op | Allocation delta |
| --- | ---: | ---: | ---: |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-file | 27770.88 | 35624.96 | 28.28% |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-memory | 28528.64 | 36229.12 | 26.99% |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-file | 21760.00 | 27422.72 | 26.02% |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-memory | 22292.48 | 27955.20 | 25.40% |
| v09-query-backend / SQL adapter scalar Any / sqlite-file | 7587.84 | 8663.04 | 14.17% |
| v09-query-backend / SQL adapter scalar Any / sqlite-memory | 7956.48 | 9041.92 | 13.64% |
| allocation-regression / CRUD workflow batch / sqlite-memory | 65884.16 | 78438.40 | 19.06% |
| allocation-regression / CRUD workflow small / sqlite-memory | 65904.64 | 78448.64 | 19.03% |
| allocation-regression / Cold primary-key fetch / sqlite-memory | 6256.64 | 7546.88 | 20.62% |
| allocation-regression / Cold relation traversal / sqlite-memory | 13352.96 | 17203.20 | 28.83% |
| allocation-regression / Update employees / sqlite-memory | 15626.24 | 17623.04 | 12.78% |
| allocation-stages / Cold typed-ID exact terminal / sqlite-file | 5027.84 | 6062.08 | 20.57% |
| allocation-stages / Cold typed-ID exact terminal / sqlite-memory | 5099.52 | 6420.48 | 25.90% |

Smaller increases remain part of the review: repeated scalar Any is +6.45% for SQLite file and +4.12% for SQLite memory; the calibrated 60,000-operation warm-key row is +2.91%. The 10% warning threshold is triage, not permission to ignore a material change. The histories normalize the frozen lanes' displayed allocation values, including KiB rounding; they are not exact object-size measurements.

Recorded query, reader, row-cache, relation, mutation and transaction telemetry is unchanged in all 90 comparisons. This rules out a changed *recorded* workload shape, not all possible uninstrumented work. The earlier [coordination measurements](W1%20Coordination%20Measurements.md) and allocation sampling identify diagnostic scopes, execution-context bookkeeping and lifetime coordination as leads. The subsequent [paired allocation diagnostics](W1%20Paired%20Allocation%20Diagnostics.md) reproduce these costs against the actual W0 source with matching checksums and telemetry; complete byte attribution and timing disposition remain open.

## Timing findings

The table retains every latency warning and noisy latency row, including those whose overall status is an allocation warning. Error is the history's reported mean error; it is not a new statistical test.

| Lane / workload / provider | W0 mean +/- error, us | W1 mean +/- error, us | Time delta | Maximum noise | Latency status |
| --- | ---: | ---: | ---: | ---: | --- |
| phase2-watch / Warm primary-key fetch / sqlite-file | 6.7130 +/- 1.4090 | 7.2500 +/- 1.0190 | 8.00% | 20.99% | noisy |
| phase2-watch / Warm primary-key fetch / sqlite-memory | 5.4910 +/- 1.3460 | 7.1030 +/- 1.2900 | 29.36% | 24.51% | noisy |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-file | 123.9000 +/- 26.8200 | 131.4200 +/- 22.7700 | 6.07% | 21.65% | noisy |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-memory | 141.6800 +/- 3.3160 | 158.9600 +/- 20.8600 | 12.20% | 13.12% | warning |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-file | 128.5700 +/- 11.9980 | 137.4600 +/- 28.6500 | 6.91% | 20.84% | noisy |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-memory | 154.2500 +/- 20.7870 | 187.3300 +/- 32.8500 | 21.45% | 17.54% | warning |
| phase3-query-hotpath / Repeated scalar Any / sqlite-file | 88.2500 +/- 10.5440 | 97.1600 +/- 13.7000 | 10.10% | 14.10% | warning |
| v09-query-backend / Expression parse/structural template / sqlite-memory | 8.3210 +/- 0.3054 | 9.3934 +/- 0.7179 | 12.89% | 7.64% | warning |
| v09-query-backend / SQL adapter scalar Any / sqlite-file | 20.4909 +/- 0.7423 | 23.1748 +/- 1.5791 | 13.10% | 6.81% | warning |
| allocation-regression / CRUD workflow batch / sqlite-memory | 400.3167 +/- 29.8896 | 473.7704 +/- 82.2306 | 18.35% | 17.36% | warning |
| allocation-regression / CRUD workflow small / sqlite-memory | 397.6687 +/- 32.3900 | 480.7090 +/- 70.0887 | 20.88% | 14.58% | warning |
| allocation-stages / Binary canonical-key propagation / sqlite-file | 0.2136 +/- 0.0151 | 0.2392 +/- 0.0177 | 11.99% | 7.40% | warning |
| allocation-stages / Canonical provider-row decoding / sqlite-file | 0.5309 +/- 0.0097 | 0.6443 +/- 0.1025 | 21.36% | 15.91% | warning |
| allocation-stages / Cold typed-ID exact terminal / sqlite-file | 54.4879 +/- 5.0030 | 75.3347 +/- 13.5051 | 38.26% | 17.93% | warning |
| allocation-stages / Cold typed-ID exact terminal / sqlite-memory | 73.8988 +/- 8.7885 | 98.5530 +/- 11.7927 | 33.36% | 11.97% | warning |
| allocation-stages / Known-miss materialization/publication / sqlite-memory | 1.9337 +/- 0.0587 | 2.5613 +/- 0.1525 | 32.46% | 5.95% | warning |
| allocation-stages / Mutation command preparation / sqlite-file | 2.7729 +/- 0.3632 | 3.3237 +/- 0.3540 | 19.86% | 13.10% | warning |
| allocation-stages / Mutation command preparation / sqlite-memory | 3.0750 +/- 0.3910 | 4.0010 +/- 0.5986 | 30.11% | 14.96% | warning |
| allocation-stages / Mutation execution preflight / sqlite-memory | 1.5030 +/- 0.2171 | 1.5327 +/- 0.3129 | 1.98% | 20.41% | noisy |
| allocation-stages / Mutation final drift validation / sqlite-memory | 0.0827 +/- 0.0071 | 0.1110 +/- 0.0186 | 34.22% | 16.76% | warning |
| allocation-stages / Mutation state-change capture / sqlite-memory | 0.0766 +/- 0.0066 | 0.0988 +/- 0.0118 | 28.98% | 11.94% | warning |
| allocation-stages / Source cache result publication / sqlite-memory | 0.1951 +/- 0.0114 | 0.2706 +/- 0.0260 | 38.70% | 9.61% | warning |
| allocation-stages / Source result validation / sqlite-memory | 1.6288 +/- 0.0658 | 2.3273 +/- 0.2717 | 42.88% | 11.67% | warning |
| allocation-stages / Typed-ID canonical-key propagation / sqlite-file | 0.2171 +/- 0.0077 | 0.2497 +/- 0.0447 | 15.02% | 17.90% | warning |
| allocation-stages / Warm typed-ID exact terminal / sqlite-memory | 5.3633 +/- 1.3383 | 6.4611 +/- 2.1608 | 20.47% | 33.44% | noisy |

The CRUD timing warnings were absent from the preceding activity-scope candidate capture. Their reported error ranges overlap W0, as do the query hot-path warnings, so a fixed slowdown is not established by those point estimates. Conversely, interval overlap is not proof of equivalence or permission to waive the warning. Structural parsing in SQLite memory and the SQL scalar adapter in SQLite file have non-overlapping reported ranges in this capture and require direct follow-up.

The stage lane now has twelve latency warnings, versus two in the earlier stage checkpoint. Several concern tiny local stages with unchanged allocations. Its raw log retains seventeen minimum-iteration warnings and a multimodal-distribution warning: final drift validation reaches only 63.5–78.3 microseconds per iteration, command preparation 2.642–3.202 milliseconds, and known-miss publication 1.244–2.327 milliseconds. Source result validation, publication, cold typed-ID execution and the other flagged rows remain visible above. Neither short duration nor unchanged source/allocation alone establishes a timing pass.

The phase2 warm-key rows use 1,000 operations per invocation; the calibrated allocation-regression warm-key row uses 60,000. Their timing and uncertainty are separate receipts, not interchangeable results for a single run.

## Receipts and reproduction

All paths below are under `artifacts/benchmarks/history/`. The baseline pattern is `w0-7e36614b-<lane>.json`; candidate history is `w1-726810c6-<lane>.json`; comparison is `w1-726810c6-vs-w0-<lane>.json`. Baseline hashes remain those sealed in the W0 evidence record.

| Lane / run ID | Seconds | Raw receipts | History SHA-256 | Comparison SHA-256 |
| --- | ---: | ---: | --- | --- |
| phase2-watch / 20260923-121132795-806cb8e20c2047bf960e3ef82cde3d37 | 257.4458627 | 12 | fe745a1c2f8ef853df2afd67feb52a816139c58054d5f2e8aa70da94cb532d1a | b2950e646c246e2cf600e578a92c7c782256dc42f6b2ab7a0a5fc7d4ecdce037 |
| phase3-query-hotpath / 20260923-121551031-c3032c0f1d35443287a0812b42eef018 | 144.3525548 | 12 | dc10b265ab63b3ca3cf76d361539db07bfd82769fa933724b78d56ee729fcec2 | 67794a7ab9b4dddd5f3397e838f36f21128e39cc595e08954a4fba46747757b6 |
| v09-query-backend / 20260923-121816078-8ad6deb50fcc47889e0c3b7645a042ce | 438.9419546 | 18 | a8ea8ca0d21a3c4b81ddd803796ce38461896bb5ce022ab8282f61566411d69f | dd7b106c923be7d3201d7534b196b2901e6cdfb7498793f3a9d2a704f1ba9036 |
| v09-memory-read / 20260923-122535719-5a48ca3956af46089cad635e705d7e16 | 291.5257583 | 15 | 9265ac3712a554e93aea02da3b989520886d4883686af74c68bb5ec6aed562ef | 2aa88c20616bd892ff212fe3bf814ba3073bf414b70e1a17aca021c29f05bada |
| allocation-regression / 20260923-123027928-ed843c068c554c4384a8c420dbdc47a7 | 251.8371952 | 15 | 03e0b5fab01a89b97259d5894dcf0427883d89027f852fd0327d5f6e5d7a13f2 | 73f822527ce1c2110ff466112fed59bc7f859689032a17c776bf04d3a70664c6 |
| allocation-stages / 20260923-123440602-b4b27f654df34fb687dd12f9937625f3 | 1693.5134409 | 54 | aec8d51f8d55929cf271690cc37e76626e0b8b11d47b4bb3ddaa92e597144887 | 04ed6915aba4fa0a8a60627a26294862fe9af03d80dcdb78bc9045560a3b08b0 |

Reproduce each lane from a clean committed runner with `run --<lane> --profile heavy --release-evidence --history-json <fresh-path> --baseline artifacts/benchmarks/history/w0-7e36614b-<lane>.json --comparison-json <fresh-path>`. Use separate invocations and new output paths; do not overwrite the frozen baseline or this checkpoint. The capture's per-lane verification records are `artifacts/w1-726810c6-<lane>-verification.json`.

## Remaining work

F19 now has a complete current six-lane capture, rather than an outstanding capture request. It remains open for paired allocation attribution and timing disposition. Rebuild the exact frozen W0 source in its own clean worktree for supplemental diagnostics; the existing published-0.9.2 target is a different commit and must not be relabeled as W0. Preserve all original warnings when adding repeats.

Any proposed reduction must retain diagnostic isolation, independent cleanup, transaction admission and cache/publication semantics, with meaningful correctness evidence. Speculative pooling, plan caching or shared provider state is not justified by these totals. If production code changes, refresh the affected evidence and the final complete capture before acceptance.

F21 still requires final broad-evidence reconciliation, the complete I/O/requirement audit, exact-head CI and verified PR integration. The [functional audit](W1%20Functional%20and%20IO%20Audit.md) remains the requirement matrix. The [limited W0-F1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception), native W2 acceptance, public/packed W3 consumers, issue #26 and release approval are unchanged. No package is published or public async support claimed here.
