> [!WARNING]
> This is a measured W1 cost reduction, not performance acceptance or W1 completion. Cold-relation allocation, smaller increases, variable timing warnings and the remaining implementation/evidence gates stay open.

# W1 Mutation Preflight Allocation Reduction

**Date:** 2026-09-20. Follows the [initial performance checkpoint](W1%20Initial%20Performance%20Checkpoint.md) and [synchronous read diagnostics](W1%20Synchronous%20Read%20Diagnostics.md#remaining-work-and-cost). [PR #192](https://github.com/bazer/DataLinq/pull/192) removes repeated successful-path operation-label allocation without removing admission, recovery or diagnostic boundaries. The frozen [W0 performance baseline](W0%20Baseline%20Evidence.md) remains `7e36614b5f26f1dd199ff93bc60620f4b0714d5f`; published compatibility remains 0.9.2.

## Measured problem and change

The complete 48-row allocation-stage capture at the integrated read-diagnostics commit `89fbcbbc38c3fa62c04bdb6b9844851e061d0546` reports mutation execution preflight at 839.68 B/op on both SQLite targets, against W0's 716.80 B/op (+17.1%). This stage repeatedly validates an already captured state change without executing provider commands. The source shows seven independently formatted operation descriptions across `EnsureMutationCommitOutcomeKnown` and `EnsureMutationNotPoisoned`, even when every guard succeeds.

[Transaction](../../../../src/DataLinq/Mutation/Transaction.cs) now supplies constant descriptions for insert, update and delete. Each guard remains in its original order with its original message and requested operation kind; the fallback for an unknown enum value retains the prior formatting. The statement description does not replace the separately supplied requested `Save` identity. No diagnostic scope, ownership validation, cleanup stage, cache rule, cancellation or recovery decision changes.

| Mutation preflight allocation | W0 | Before this change | After this change |
| --- | ---: | ---: | ---: |
| SQLite file | 716.80 B/op | 839.68 B/op | 0 B/op |
| SQLite memory | 716.80 B/op | 839.68 B/op | 0 B/op |

Zero is the benchmark's reported allocation for this isolated validation stage, not a claim that mutations or their orchestration allocate nothing. Its candidate time delta against W0 is -47.8% for file and -26.7% for memory; the memory timing is classified noisy. Do not turn those point estimates into a fixed latency guarantee.

## End-to-end checkpoint

The nine-row candidate allocation-regression comparison is strict valid evidence. The preceding runtime column comes from the retained [read-diagnostics checkpoint](W1%20Synchronous%20Read%20Diagnostics.md#remaining-work-and-cost); it measures the runtime subsequently integrated at `89fbcbbc`.

| Workload | Before allocation delta vs W0 | Candidate allocation delta vs W0 | Candidate time delta vs W0 |
| --- | ---: | ---: | ---: |
| CRUD batch | +9.2% | +4.3% | +0.9% |
| CRUD small | +9.2% | +3.9% | -6.9% |
| Cold primary key | +7.0% | +7.0% | +6.5% |
| Cold relation | +14.6% | +14.6% | -5.4% |
| Provider initialization | -4.1% | -4.1% | -17.7% |
| Startup primary key | -19.6% | -19.6% | -13.5% |
| Update | +12.8% | +2.1% | -3.0% |
| Warm primary key | 0.0% | 0.0% | -4.8% |
| Warm relation | zero bytes | zero bytes | -15.9% |

Update allocation falls from 17,633.28 to 15,953.92 B/op; W0 is 15,626.24 B/op. Cold relation remains 15,298.56 versus W0's 13,352.96 B/op. The candidate comparison has one allocation warning, no latency warnings and no recorded telemetry changes. Its outcome remains **ReviewRequired**. Sub-threshold allocation increases are not waived; improvements in unrelated workloads do not offset them.

The candidate stage comparison has five latency warnings: binary canonical-key propagation (memory, +17.2%), cold typed-ID terminal (file, +21.6%), known-miss publication (memory, +26.3%), mutation final drift validation (memory, +16.8%) and source result validation (memory, +15.9%). It has no allocation warnings and no recorded telemetry changes, but remains **ReviewRequired**. Cold typed-ID allocation is still +8.6% on file and +6.6% on memory versus W0. The before capture had 16 latency warnings and two allocation warnings; that difference does not prove this mutation-only change improved unrelated stages. Short-iteration, distribution and timing-noise warnings remain in the histories. No particular environmental cause is established for the variation, including the changed file cold-terminal allocation between captures.

## Validation and provenance

- Release core builds for .NET 8/9/10 and unit/Memory/compliance builds: zero warnings and errors.
- Unit: 3,143/3,143; Memory: 221/221; SQLite file: 528/528; SQLite memory: 528/528. All 4,420 pass, none skipped. Reports `artifacts/w1-coordination-costs-{unit,memory,sqlite-file,sqlite-memory}.json` are complete with complete artifacts and exit zero; `ValidForEvidence=false` correctly identifies these local functional invocations as development verification, not release qualification. Existing tests exercise mutation lifecycle, admission, recovery, explicit operation identity and the failure-provenance/cleanup contracts.
- Runtime commit `8c114f8574bb65ab08c455952b7744341585ff5d` passed all 12 Latest CI jobs in [run 35513016517](https://github.com/bazer/DataLinq/actions/runs/35513016517). The later evidence-documentation commit does not change runtime or benchmark source; its final-head CI must pass before merge.

All three new benchmark histories use `--profile heavy --release-evidence`, the unfiltered canonical selector and its exact provider/operation-count set, with fresh harness builds. Each uses its corresponding `w0-7e36614b-<selector>.json` as `--baseline` and separate `--history-json`/`--comparison-json` outputs. Both candidate captures run sequentially after all local tests/builds finish. No source edits, other benchmarks or tests overlap a measured run. Checkout start/end are clean and unchanged; runner, DevTools and benchmark assemblies match the recorded commit and clean build state.

| Capture | Runtime commit | Run ID |
| --- | --- | --- |
| Before, 48 allocation stages | `89fbcbbc38c3fa62c04bdb6b9844851e061d0546` | `20260920-104102728-f49f8ac843d34be083b4d2e981e0a905` |
| Candidate, nine allocation-regression rows | `8c114f8574bb65ab08c455952b7744341585ff5d` | `20260920-130435605-66d9f5beca03459785c5ce3b16c90468` |
| Candidate, 48 allocation stages | `8c114f8574bb65ab08c455952b7744341585ff5d` | `20260920-130917706-28ee84ee73ad479d9689f7963f95f74c` |

Each history and comparison is valid, complete and artifact-complete with exit zero; each comparison is comparable to W0. The candidate stage status counts are 29 stable, 11 improved, five warning and three noisy. The nine-row counts are five stable, three improved and one warning. All 123 raw artifact receipts (54 before, 15 macro candidate, 54 stage candidate) were independently checked for recorded byte length and SHA-256. The runner retains W0's recorded Windows 10.0.26200 x64, eight logical processors, Intel Family 6 Model 140 Stepping 1, .NET 10.0.12 and BenchmarkDotNet 0.15.8 identity. This establishes the comparison contract, not identical machine load or byte-identical generated fixture dates.

Histories/comparisons remain local under `artifacts/benchmarks/history/`, with referenced logs and telemetry under `artifacts/benchmarks/runs/<Run ID>/`. No raw files were uploaded or earlier captures overwritten.

| Artifact filename | SHA-256 |
| --- | --- |
| `w1-89fbcbbc-allocation-stages.json` | `4e65ad8c730503e2a9a1b5025a27f6f6714ef1c79f4c2f42ef5d110d1a75bdf2` |
| `w1-89fbcbbc-vs-w0-allocation-stages.json` | `a796e84912ff6f90bac356cf38d3ba371344cf5f17c6062e870d25d5c466b91c` |
| `w1-8c114f85-allocation-regression.json` | `825c4a1e733ca4df8c45787174a83bdf4ede3a5ce9c5ce49626c79487f9becea` |
| `w1-8c114f85-vs-w0-allocation-regression.json` | `068b870b1411276b3a0b48b7aa054a8377b71466dc53ce81ab7ae4cd8c91204f` |
| `w1-8c114f85-allocation-stages.json` | `fdcd50be14de0524557836d22cb44a2d3dd01fc707c7412ae77e05a3db425db0` |
| `w1-8c114f85-vs-w0-allocation-stages.json` | `79c1645d1175039a7a764da9b621f70946c1d8060e5b3e9f90f3fd54c51b26ea` |

## Remaining work

The [W1 completion audit](W1%20Completion%20Audit.md) remains open. Preserve the cold-relation and smaller allocation increases for further attribution/reduction, and measure internal async coordination separately. All six W0 lanes must still be captured after final integration; these two lanes do not close the other 33 rows or final acceptance. Existing final-0.8 allocation debt and the limited SQLite W0-F1 exception remain unchanged.

The next code audit has a concrete telemetry gap: `Select.CaptureModels`, async scalar execution and `SqlQueryPlanBackend.Async` do not yet reproduce the query-level activity/counter boundaries used by synchronous `Select` and exact-key terminals. Complete that internal integration with controllable cancellation, partial-completion and listener-failure evidence, alongside the still-open synchronous/local diagnostic paths, adapter readiness and final I/O mapping. Native adapter binding and public APIs remain W2/W3 work.
