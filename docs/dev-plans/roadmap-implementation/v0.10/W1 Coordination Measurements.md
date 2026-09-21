> [!WARNING]
> Diagnostic measurements of internal coordination, not native-provider latency or W1 performance acceptance. F19 and F21 remain open.

# W1 Coordination Measurements

**Date:** 2026-09-21. Follows the [telemetry allocation reduction](W1%20Telemetry%20Allocation%20Reduction.md), integrated through #220 at `79840b1b41c07f005df76adfc723d8954cbe1f43`. Measured code: `94908aa42fdd84806c75732e2233ea8bde864bd4`. No production runtime or public API changes are made in this slice. Published compatibility remains 0.9.2; the frozen [W0 baseline](W0%20Baseline%20Evidence.md) and its six canonical lanes are unchanged.

## Workload boundaries

[W1CoordinationBenchmarks](../../../../src/DataLinq.Benchmark/W1CoordinationBenchmarks.cs) adds thirteen controlled workloads using the real internal scope, admission, helper and reader coordinators. The [W0 plan](W0%20Baseline%20and%20Evidence%20Plan.md#w0-p5-map-io-and-behavior-now-prove-async-feasibility-in-w1w2) separates internal controllable evidence from native feasibility. These measurements do not invent a 0.9 async baseline.

- Five primitive workloads batch 256 cycles and normalize to one cycle: root diagnostic scope; outer plus inner scopes; enter/release of a reused enumerator gate; enter/validate/release of a transaction lease and private step on a reused gate; and a fresh gate/helper/failure collector closed with no active work. The helper row includes its completion task/lease and amortized benchmark task, but no callback, database transaction, commit or recovery. Gate construction is excluded from the two admission rows.
- Eight reader workloads complete one whole cursor per operation. A minimal direct cursor and the real [AsyncReaderEnumerable](../../../../src/DataLinq/Execution/AsyncReaderEnumerable.cs) use the same controlled source and checksum/resource checks. They cover one row, sixteen rows and a one-row cursor with suspended advancement. Two additional coordinated rows use one shared token or two distinct tokens. Token sources are created outside measurement. Reader/source/task/capture allocations and the verification driver are included; there is no managed transaction, database I/O or logical-query telemetry context.
- Suspended rows create a pending completion source and release it only after `MoveNextAsync` has returned incomplete. Both the row and EOF advance must suspend. No timers, sleeps, `Task.Run` or assumed `Task.Yield` ordering provide the suspension. Continuation scheduling is included in the measured cost; it is not simulated database latency.

Every reader invocation requires one validation, one open, N+1 reads, N value accesses, one asynchronous disposal, checksum N(N+1)/2, no pending read and the expected provider token shape. Synchronous read/disposal throws. Global cleanup repeats the selected protocol outside measurement and writes its counts into a separate `DiagnosticEvidence` block. Ordinary database/model telemetry remains zero; synthetic reads are not reported as database queries. The direct cursor is a measurement control, not a production fallback or an implementation of all coordination guarantees.

## Measured costs

The clean heavy run uses .NET SDK 10.0.401/runtime 10.0.12, BenchmarkDotNet 0.15.8, Windows 10.0.26200 x64, Intel Family 6 Model 140 Stepping 1 and eight logical processors. `heavy` selects MediumRun: two launches, ten warmups and fifteen measurement iterations. Duration: 596.201026 seconds. Values below are the normalized history values; error is BenchmarkDotNet's reported mean error. This class uses byte display units without changing the frozen lanes' display configuration.

| Workload | Mean µs/op | Error µs/op | B/op |
| --- | ---: | ---: | ---: |
| Enumerator admission | 0.0062 | 0.0002 | 0 |
| Diagnostic scope | 0.0287 | 0.0019 | 96 |
| Direct reader one row | 0.0616 | 0.0021 | 192 |
| Nested diagnostic scopes | 0.0804 | 0.0060 | 264 |
| Transaction admission | 0.1019 | 0.0033 | 96 |
| Empty helper lifetime | 0.1060 | 0.0045 | 448 |
| Direct reader sixteen rows | 0.2592 | 0.0164 | 264 |
| Coordinated reader one row | 0.4166 | 0.0115 | 1344 |
| Coordinated reader shared token | 0.4610 | 0.0330 | 1344 |
| Coordinated reader linked tokens | 0.4909 | 0.0086 | 1424 |
| Coordinated reader sixteen rows | 1.3804 | 0.1452 | 2856 |
| Direct reader suspended | 1.6679 | 0.0600 | 864 |
| Coordinated reader suspended | 2.3002 | 0.1188 | 2176 |

Enumerator admission is allocation-free in this reused-gate measurement. The complete transaction admission cycle allocates 96 bytes. A root diagnostic scope costs 96 bytes; the nested row measures both scopes and must not be described as the inner scope alone. The complete coordinated reader adds 1,152 bytes over the one-row control, 2,592 bytes over the sixteen-row control and 1,312 bytes over the suspended control. Sharing a token adds no measured allocation; linking two distinct tokens adds 80 bytes. These differences include the coordinators' different object/state-machine lifetimes, not just one isolated gate or scope. They cannot be directly summed into production query costs.

The raw log retains multimodal-distribution warnings for transaction admission and shared-token enumeration. History uncertainty ranges from 1.8% to 10.5%; the sixteen-row coordinated case has the largest reported relative error. Point estimates do not establish fixed latency or precise relative timing of the token cases. This is one diagnostic capture, not evidence that a runtime optimization improved performance.

## Verification and receipts

Release builds of the benchmark CLI, benchmark harness and unit project pass with zero warnings/errors. Five new [evidence-gate cases](../../../../src/DataLinq.Tests.Unit/BenchmarkEvidenceReporterTests.cs) pass both focused and within **3,858/3,858 unit cases**, with no failures/skips. `CoordinationDiagnostic_CompleteFilterCannotClaimCanonicalReleaseEvidence` checks both ordinary and release-intent invocations; `CoordinationDiagnostic_MalformedRowsRemainIncomplete` rejects wrong operation counts, missing telemetry and a mismatched telemetry provider. Memory, generator and SQLite suites were not rerun locally for this benchmark-only change; #220's broader receipts remain historical evidence, not newly claimed passes.

The earlier thirteen-row Dry smoke run is complete but uses a dirty checkout and the earlier KiB display. It proves protocol execution, not useful timing/allocation estimates. The two functional reports also have stable dirty-checkout provenance at `79840b1b`, complete invocation/artifact records and matching runner assemblies; their `ValidForEvidence=false` is retained. An initial unit build failed two nullable-analysis errors in the new tests; the corrected build and actual test runs pass. No product failure is claimed from that build error.

The heavy run is complete and artifact-complete with thirteen measured/telemetry rows, zero invalid rows and exit zero. Runner, DevTools and benchmark assemblies match clean, unchanged commit `94908aa4`; the benchmark assembly SHA-256 also matches its recorded receipt. No edits, builds, tests or other benchmarks overlap measurement. All **19 raw receipts** match their byte lengths and SHA-256, and all thirteen separate protocol blocks were checked. The smoke run's 19 raw receipts also match.

This remains a custom filter with no canonical expected target set: top-level `ValidForEvidence=false`, `ReviewRequired=true`, expected target count zero. Clean assembly provenance does not override that boundary. It is deliberately absent from the release-lane registry; `--release-evidence` cannot promote it into a strict W0 lane.

| Report under `artifacts/` | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-coordination-diagnostic-focused.json` | 5 / 5 | `55aec342701a838709fb5d50608dbb226860ad57ee4b87894934df5f2b0cb82a` |
| `w1-coordination-diagnostic-unit.json` | 3858 / 3858 | `4d6bda130c53a9405a6c783250e6b6c818e94ecf46257c0f6b8e5a3d1cdeb527` |

| History under `artifacts/benchmarks/history/` | SHA-256 |
| --- | --- |
| `w1-coordination-diagnostic-smoke.json` | `ec7eebafb4945b2e53c57c4b13ae77590f404c0f3a0cccfb84e27cbb1ed1e7bd` |
| `w1-94908aa4-coordination-diagnostic.json` | `8787c7d1b88fb45bfef9b945d6036bcacee7e63f50cd27610a378a6df0b93728` |

Heavy run ID: `20260921-201209254-7f481996e3454672b7650b53a0d9677f`. Smoke run ID: `20260921-195637821-8c3bc52fd0f74e9c8e6cfe726b8186cd`. Reproduce through the Benchmark CLI with `run --filter '*W1CoordinationBenchmarks*' --profile heavy --history-json <new-path>` after building a clean committed runner. Do not add release intent or overwrite these receipts.

## Remaining work

F19 now has bounded measurements of internal scope, admission, empty-helper and reader/token costs. It still needs reduction/explanation and explicit disposition of remaining costs, plus all six strict W0 lanes on the final committed integration. The five end-to-end allocation warnings from #220 are unchanged; these diagnostic rows neither resolve nor waive them. Next, use the existing allocation-stage lane to locate remaining synchronous costs before the final six-lane capture. F21 still owns the final I/O/integration reconciliation and broad evidence review. Native provider behavior, public/packed consumers, DI and release acceptance remain later gates; the [limited W0-F1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception) is unchanged.

Planning pages remain excluded from DocFX. Local links/anchors, receipt values and named tests are checked before integration; final-head CI and expected-head merge/tree verification belong in this change's PR.
