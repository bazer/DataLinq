# W2 Functional And Performance Checkpoint

The [revised candidate checkpoint](W2%20Revised%20Candidate%20Checkpoint.md) records the later `76781c89` runtime after the lifecycle factory allocation correction. This page preserves the original `49176d7a` evidence.

> [!WARNING]
> W2 remains in progress. This checkpoint records passing native functional evidence and measured costs; it is not SQLite acceptance, performance approval or public async API availability.

**Runtime candidate:** `49176d7a50d51a4de1848b501652e82215793b98`, 2026-09-24. The [native operation audit](W2%20Native%20Provider%20Audit.md) maps the bindings and test boundaries; the [execution record](W2%20Native%20Provider%20Async%20Execution.md) retains earlier failures and development receipts.

The subsequent [cost attribution and lifecycle fast-path follow-up](W2%20Performance%20Cost%20Disposition.md) preserves these original results, explains the mutation allocation families and removes an avoidable 64-byte factory allocation from repeated provider access. Its working-source checks do not replace this clean-candidate record or close the remaining cold-query timing review.

## Clean Functional Evidence

The explicit Release full plan with `--batch-size 1` passes **8,539/8,539**, zero failures/skips. It includes generators 71, unit 3,961, Memory 221, compliance 3,411 and server-specific tests 875. All eight SQL targets have individual result rows. Invariant tests run only in their named-plan anchor shard.

Run `20260924T035335382Z-d8ef819917da4c2b82065dff2f7e5b1f` identifies clean, unchanged candidate source and matching clean Testing CLI/DevTools assemblies. **ValidForEvidence=true**, including complete raw artifacts and per-target coverage. Independent TRX inspection confirms every passing result and all eight new native telemetry cases; `artifacts/w2-49176d7a-functional-verification.json` records 69 artifact hashes. Summary `artifacts/w2-49176d7a-full-release.json` has SHA-256 `30ebabcb26965314f6a71c231bca24452d49910798cfb04d9886d2cc6f9188b5`.

[CI #623](https://github.com/bazer/DataLinq/actions/runs/35953248630) passes every lane on the same commit. This establishes the functional checkpoint against the pinned dependencies, not acceptance of an unreleased SQLite correction.

```powershell
$env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
.\scripts\dotnet-sandbox.ps1 build src/DataLinq.Testing.CLI/DataLinq.Testing.CLI.csproj -c Release -v minimal
.\scripts\dotnet-sandbox.ps1 exec src/DataLinq.Testing.CLI/bin/Release/net10.0/DataLinq.Testing.CLI.dll run --plan full --batch-size 1 --configuration Release --output failures --summary-json <fresh-artifact-path>
```

## First Canonical Performance Capture

All **90 rows and 126 raw artifact receipts** are verified across the six canonical lanes. Histories and comparisons have clean matching source/runner/target identities, complete exact workload sets and **ValidForEvidence=true**. Each live benchmark DLL was checked before the next build; raw file lengths and SHA-256 hashes were checked. No local test, build or profiler overlapped timing measurement; the controller's between-lane builds are part of the protocol.

Runs use .NET 10.0.12 / SDK 10.0.401 / BenchmarkDotNet 0.15.8, Windows x64, Intel Family 6 Model 140 Stepping 1, eight logical processors, `--profile heavy --release-evidence`. The W0 baseline remains `7e36614b5f26f1dd199ff93bc60620f4b0714d5f`; W1 comparison remains `a435d0b428937040bfd02210e67eb1219c9ed6f7`. Actual operation counts and normalized telemetry match both baselines in every row.

The initial W0 comparison reports **13 allocation warnings, 20 latency warnings and 12 noisy latency rows**. Row-status totals differ from latency-status totals when an allocation warning takes priority. W1's thirteen allocation-warning identities persist, but their magnitudes must be reviewed again: the added W2 mutation costs are not covered merely by retaining the same warning count. BenchmarkDotNet minimum-iteration and distribution warnings remain in the raw histories. No thresholds, samples or result flags are rewritten.

| Lane | Rows | Stable | Improved | Warning | Noisy | Allocation warnings | Latency warnings | Telemetry changes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| phase2-watch | 6 | 1 | 3 | 0 | 2 | 0 | 0 | 0 |
| phase3-query-hotpath | 6 | 0 | 0 | 4 | 2 | 4 | 1 | 0 |
| v09-query-backend | 12 | 7 | 2 | 3 | 0 | 2 | 2 | 0 |
| v09-memory-read | 9 | 5 | 0 | 4 | 0 | 0 | 4 | 0 |
| allocation-regression | 9 | 2 | 1 | 6 | 0 | 5 | 4 | 0 |
| allocation-stages | 48 | 28 | 6 | 10 | 4 | 2 | 9 | 0 |

Notable additional synchronous mutation allocations versus W1:

| Workload / SQLite memory | W1 B/op | W2 B/op | Added B/op |
| --- | ---: | ---: | ---: |
| Update employees | 17561.60 | 20316.16 | 2754.56 |
| CRUD workflow small | 78264.32 | 86302.72 | 8038.40 |
| CRUD workflow batch | 78346.24 | 86312.96 | 7966.72 |

These increases remain open for attribution and disposition. W2 now binds synchronous transaction commands through the shared capture/initialization and native diagnostic paths, but that code change alone does not identify every allocated byte or explain the measured latency. Removing lifecycle or diagnostic guarantees to improve a benchmark is not an accepted resolution.

### Canonical Receipts

Histories are `artifacts/benchmarks/history/w2-49176d7a-<lane>.json`; comparisons are `w2-49176d7a-vs-w0-<lane>.json`. Verification receipts are `artifacts/w2-49176d7a-<lane>-verification.json`. Frozen W0 hashes are checked before and after capture. The full normalized comparison with W1 is retained in `artifacts/w2-49176d7a-runtime-comparison.md` and `.json`.

| Lane / run ID | Seconds | Raw receipts | History SHA-256 | Comparison SHA-256 |
| --- | ---: | ---: | --- | --- |
| phase2-watch / 20260924-040515802-f09b596927de4c81995afb7cfa7319c3 | 234.392 | 12 | `615fd584b3f39235b08a4af275519190c14a9c7cc2293946863db02e9c94763a` | `2c75c68bfb3d85dd9c529972d50916cecc2557e90db4ac34101072ef53a0a06e` |
| phase3-query-hotpath / 20260924-040911011-9df00d54722a4fc991df83f16a13d4f5 | 156.666 | 12 | `7ad13c1dfd6ee593527c2735fd13e74e4f6776f78766bcb8c42117bf2d0ab9b7` | `d849971fc7a2400418418dad49541525d38b2754fb93f21a816e8dc1121f9642` |
| v09-query-backend / 20260924-041148336-a37adc24c5d1438c97bde551b0889fea | 478.949 | 18 | `237a6478ca0d999826b5a0e13ddde5b5faedd9f117b3820c8c7ba7169549e7ca` | `595e4b48a0ee8f7fe658ca1af239abb41ba9d03fe6f6be97bdfc68c9377649f6` |
| v09-memory-read / 20260924-041948011-d3a32fd9eb53401788cdc40c79726714 | 315.211 | 15 | `855cd6fd979bb78521afec1332e761c485a8922cd920a24eda8352541bbedc43` | `0b779fc9420708913f1e9378a96d3fdb11c1ce2007082db92290e5a0595fd5bd` |
| allocation-regression / 20260924-042503910-cc52a81b661840eab4cb2ca51f913b9c | 292.697 | 15 | `44759d918e9fc5a51c458826b7e5551ba7549eb2aa0a944518c40593367f788a` | `479b04b9f620d07c74978da2689e387a44ff61e66d834ec876d778ec4f680509` |
| allocation-stages / 20260924-042957308-5b888d5cf3794606894a991c6e212e93 | 1413.785 | 54 | `acbd05d21cb5d67a7d3424c457e21eb1c5090f707dd2d409c9729993631df217` | `0e565c42ebd33abfa194f2478df96825a950a3bc3b850da630ab286d047e3f53` |

Reproduction uses the Benchmark CLI `run --<lane> --profile heavy --release-evidence --history-json <fresh-history> --baseline artifacts/benchmarks/history/w0-7e36614b-<lane>.json --comparison-json <fresh-comparison>`. Build the Release runner from the clean candidate first. Do not overlap local test/build/profiler work with measurement.

## Bounded Timing Controls

The first canonical pass prompted a fixed, one-pass sequence: W1 hot paths → W2 hot paths → W1 Memory reads → W2 Memory reads. These four additional heavy-profile runs preserve the original histories. W1 uses an isolated detached checkout at `a435d0b4` beneath `artifacts/benchmarks/targets/w1-w2-timing-control`; the clean runner remains `49176d7a`. Historical build hooks, assembly identities and actual runtime source are recorded by the Benchmark CLI.

All **30 control rows and 54 raw receipts** are verified with valid histories/comparisons, matching clean runner/runtime provenance, live DLL checks and unchanged operation counts/telemetry. W1 controls compare with the original W1 history; W2 controls compare with the newly captured W1 control for the same lane. The fixed order is recorded, not randomized, and this is not a repeated-until-pass procedure.

| Lane | W1 control vs original W1: latency warnings / noisy rows | W2 control vs fresh W1: latency warnings / noisy rows | Allocation warnings / telemetry changes in either comparison |
| --- | --- | --- | --- |
| phase3-query-hotpath | 2 / 3 | 0 / 6 | 0 / 0 |
| v09-memory-read | 7 / 0 | 0 / 0 | 0 / 0 |

Fresh W1 itself is slower than its original capture in several cases. All nine fresh Memory comparisons are stable; Memory implementation and benchmark sources are unchanged between these runtime commits. This provides evidence against assigning the original Memory slowdown solely to W2. It does not identify the external or runtime cause.

**Every fresh hot-path latency comparison is noisy.** Zero warning flags there do not establish parity or absence of a regression. These controls do not close timing review for the other canonical lanes, and they do not explain W2's additional mutation allocation. Original warnings, noise classifications and `ReviewRequired` flags remain unchanged.

| Workload / provider | Original W1, us | Initial W2, us | Fresh W1, us | Fresh W2, us | Fresh W2 delta | Fresh latency status |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Repeated IN predicate fetch / sqlite-file | 114.6700 | 117.9000 | 145.4000 | 160.3000 | 10.25% | noisy |
| Repeated IN predicate fetch / sqlite-memory | 145.5100 | 166.0000 | 158.6000 | 184.6000 | 16.39% | noisy |
| Repeated non-PK equality fetch / sqlite-file | 129.2400 | 153.1000 | 156.1000 | 164.5000 | 5.38% | noisy |
| Repeated non-PK equality fetch / sqlite-memory | 147.4600 | 186.8000 | 211.3000 | 174.4000 | -17.46% | noisy |
| Repeated scalar Any / sqlite-file | 84.5700 | 128.8000 | 110.8000 | 130.5000 | 17.78% | noisy |
| Repeated scalar Any / sqlite-memory | 98.7600 | 151.0000 | 119.7000 | 106.1000 | -11.36% | noisy |
| Memory construct and seed / memory | 353.1485 | 439.4452 | 426.5354 | 406.7832 | -4.63% | stable |
| Memory database construction / memory | 0.2990 | 0.3302 | 0.3256 | 0.3114 | -4.36% | stable |
| Memory direct-Guid equality count / memory | 9.8489 | 13.5167 | 12.1704 | 10.9577 | -9.96% | stable |
| Memory filter order page / memory | 33.9253 | 37.2620 | 46.0058 | 41.5061 | -9.78% | stable |
| Memory primary-key hit / memory | 0.1440 | 0.1662 | 0.1710 | 0.1647 | -3.68% | stable |
| Memory primary-key miss / memory | 0.0791 | 0.1006 | 0.1003 | 0.0931 | -7.18% | stable |
| Memory repeated entity identity / memory | 23.0041 | 27.0214 | 28.2049 | 30.0440 | 6.52% | stable |
| Memory scalar scan / memory | 52.7560 | 59.1906 | 60.8506 | 65.3299 | 7.36% | stable |
| Memory typed-ID equality count / memory | 9.7210 | 10.1891 | 10.4654 | 10.8836 | 4.00% | stable |

Control histories are `artifacts/benchmarks/history/w2-49176d7a-control-<w1|w2>-<lane>.json`; comparisons insert `-comparison-` before the lane. Verification receipts use `artifacts/w2-49176d7a-control-<w1|w2>-<lane>-verification.json`. Commands use the same heavy profile and evidence checks as the canonical pass, adding `--benchmark-target-root artifacts/benchmarks/targets/w1-w2-timing-control` only for W1.

| Runtime / lane / run ID | Seconds | History SHA-256 | Comparison SHA-256 |
| --- | ---: | --- | --- |
| W1 / phase3-query-hotpath / 20260924-045404917-cdec35d7470a46d6a737e01fdc972b54 | 207.064 | `dc76f1f02d304eb697f600c34fc236c91c948d9cc5b917971330ec17d7f25f7d` | `68a1131c89898849e07459dbab7134446e12961a29838140eeb02f62f5fbd67a` |
| W2 / phase3-query-hotpath / 20260924-045733413-071809d0080d4a25950b350a431bfd56 | 161.362 | `bceb132846f5e9b55b9180119ad32dc14381c90eb42c8bdcb9debec4ceed34a1` | `0e8d5fd2500e7eecfe3b2719b271fadefb3a708bccbd337527c5f966827d4eca` |
| W1 / v09-memory-read / 20260924-050015606-cf477fd33f054a8aa43de5a937f36206 | 318.999 | `180acd7cb7dc5ba167f770f6aba9c2cf25c7874d0f982a2e7420766ca726de06` | `ed4623ef917dcb228d32370e48e8a8f9abe235e047598679b92718260a31bddd` |
| W2 / v09-memory-read / 20260924-050535997-00ae17a53d6d446199ab64d0dc3dde23 | 312.481 | `1f95954234fd66b80edd739115fa51f6c9f1f6b09cb4d72be92d731bf26ef6d5` | `56a96769aa7bb0ec270eac982b6e1e71f2ec53fbb29dffdba8f6e5628c268ba4` |

## Generated Binary Getter Cost

The canonical binary/key stages do not directly measure repeated access to the generated binary property corrected in W2. A [supplemental probe](evidence/binary-getter/Program.cs), with its [project](evidence/binary-getter/Probe.csproj), invokes the actual generated `ImmutableCanonicalKeyBenchmarkBinaryRow.Id` getter from each runtime's benchmark assembly. It uses the existing in-process fixture and generated factory; reflection/delegate binding occurs outside measurement. This is a thread-allocation and ownership observation, not a latency benchmark, native-I/O measurement or release-valid benchmark history.

The same synchronous helper warms 10,000 getter calls, then measures 10,000 calls with exact length checksums. It also mutates an exposed array and checks a later read. W1 reuses the public array and exposes that edit. W2 returns independent arrays and preserves the underlying value. Only nonempty payloads of the three stated sizes are measured; this does not claim a cost for null/empty values or every generated property shape.

| Payload | W1 warmed B/read | W2 B/read | W1 caller mutation isolated | W2 caller mutation isolated |
| --- | ---: | ---: | --- | --- |
| 32 bytes | 0 | 56 | false | true |
| 4096 bytes | 0 | 4120 | false | true |
| 65536 bytes | 0 | 65560 | false | true |

That additional array allocation restores the existing defensive-copy contract. It must not be advertised as a performance-neutral change or removed by returning a mutable cached array. Native value-integration regressions cover both nullable and non-null properties, binary keys and repeated access.

Both observations use the same probe DLL SHA-256 `015eaa39c775c0d7ea608a1c10ab3b7911d91f25cc735b4ea81e25f5a4ce4dbb`. Loaded DataLinq libraries identify the expected source commit and retain their DLL hashes. The benchmark harness reports clean provenance; public library projects do not emit that build-state metadata, and the report keeps their value null. The initial probe incorrectly looked up the metadata key and then expected every library to emit the harness-only field; those assertions were corrected to the actual provenance schema. A failed rebuild while the first apphost remained alive is retained in `artifacts/w2-binary-getter-probe/identity-initial-failure.log`; it is not a product failure or a passing measurement.

| Observation | SHA-256 | Benchmark DLL SHA-256 |
| --- | --- | --- |
| `artifacts/w2-binary-getter-probe/w1-a435d0b4.json` | `ed163c526862e151b37d5526fb309b88ba6be1787de16588e7f1a0d8815fe656` | `aaf3174b42cd179cffe81231e0c7babb8df477d278c96ca4ac33f12dad6e2375` |
| `artifacts/w2-binary-getter-probe/w2-49176d7a.json` | `386922af9134a726cc87c31f6798d2937b6048ce3b06dbe9d094fc28e0f94178` | `04184d6a4e9aeb3487c00aef6d7f9cf42c66d60c0b5196d65887e5447ea33f0d` |

The baseline DLL hash matches the retained W1 canonical verification; the candidate hash matches this W2 capture. Run the probe in separate processes for each previously built runtime, providing its benchmark DLL path, full expected commit and a fresh output path. Use the Benchmark CLI to build a historical target with its provenance hooks first.

```powershell
.\scripts\dotnet-sandbox.ps1 restore docs/dev-plans/roadmap-implementation/v0.10/evidence/binary-getter/Probe.csproj
.\scripts\dotnet-sandbox.ps1 run --project docs/dev-plans/roadmap-implementation/v0.10/evidence/binary-getter/Probe.csproj -c Release -- <benchmark.dll> <full-expected-commit> <fresh-output.json>
```

## Remaining W2 Gates

- Attribute and disposition the additional synchronous mutation allocations and unresolved timing observations. D10-6 blocks on new unexplained regressions; valid receipts are not approval of their results.
- Adopt the official SQLite ownership correction through the established branch flow and rerun affected evidence. DataLinq still pins 10.0.11.
- Resolve the separate [failed-rollback file-pool integrity finding](SQLite%20Failed%20Rollback%20Pool%20Reuse%20Investigation.md). Successful ADO disposal or the ownership-order correction cannot establish safe reuse for that distinct failure.
- Retain earlier automatic-concurrency timeout and allocation observations with their stated limits. Public/generated API work remains W3; W2 is not release approval.

## Complete Canonical Row Record

All values below are the initial capture, not replacements selected from later controls. Latency and allocation statuses are against W0; W1 columns expose incremental W2 cost. Later controls, supplemental probes and unresolved gates are recorded separately.

| Lane / workload / provider | W1 B/op | W2 B/op | W0 mean, us | W1 mean, us | W2 mean +/- error, us | W0 time delta | Allocation / latency status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| phase2-watch / Warm primary-key fetch / sqlite-memory | 1812.48 | 1812.48 | 5.4910 | 5.4200 | 7.1390 +/- 1.2490 | 30.01% | stable / noisy |
| phase2-watch / Warm primary-key fetch / sqlite-file | 1812.48 | 1812.48 | 6.7130 | 5.5440 | 7.1750 +/- 1.0950 | 6.88% | stable / noisy |
| phase2-watch / Startup primary-key fetch / sqlite-file | 53504.00 | 54384.64 | 284.6420 | 230.5860 | 307.8960 +/- 23.4020 | 8.17% | improved / stable |
| phase2-watch / Startup primary-key fetch / sqlite-memory | 57384.96 | 58777.60 | 414.5200 | 328.5960 | 443.0300 +/- 51.3910 | 6.88% | improved / stable |
| phase2-watch / Provider initialization / sqlite-file | 349122.56 | 349562.88 | 641.1500 | 545.6040 | 573.7530 +/- 59.3730 | -10.51% | stable / improved |
| phase2-watch / Provider initialization / sqlite-memory | 352389.12 | 353720.32 | 734.3020 | 625.2810 | 725.0260 +/- 137.2740 | -1.26% | stable / stable |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-file | 34877.44 | 35000.32 | 123.9000 | 114.6700 | 117.9000 +/- 20.1700 | -4.84% | warning / noisy |
| phase3-query-hotpath / Repeated scalar Any / sqlite-file | 18421.76 | 18421.76 | 88.2500 | 84.5700 | 128.8000 +/- 29.1800 | 45.95% | stable / noisy |
| phase3-query-hotpath / Repeated scalar Any / sqlite-memory | 18636.80 | 18769.92 | 117.6400 | 98.7600 | 151.0000 +/- 36.2400 | 28.36% | stable / noisy |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-file | 26890.24 | 27064.32 | 128.5700 | 129.2400 | 153.1000 +/- 35.6300 | 19.08% | warning / noisy |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-memory | 35624.96 | 35747.84 | 141.6800 | 145.5100 | 166.0000 +/- 23.0100 | 17.17% | warning / warning |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-memory | 27361.28 | 27443.20 | 154.2500 | 147.4600 | 186.8000 +/- 53.0300 | 21.10% | warning / noisy |
| v09-query-backend / Invocation bind scalar/local sequence / sqlite-file | 307.20 | 307.20 | 0.1778 | 0.1467 | 0.1460 +/- 0.0053 | -17.89% | stable / improved |
| v09-query-backend / Invocation bind scalar/local sequence / sqlite-memory | 307.20 | 307.20 | 0.1652 | 0.1885 | 0.1827 +/- 0.0351 | 10.59% | stable / warning |
| v09-query-backend / SQL request/capability preparation / sqlite-memory | 0.00 | 0.00 | 0.2409 | 0.1937 | 0.2251 +/- 0.0217 | -6.56% | stable / stable |
| v09-query-backend / SQL request/capability preparation / sqlite-file | 0.00 | 0.00 | 0.2323 | 0.1920 | 0.2264 +/- 0.0231 | -2.54% | stable / stable |
| v09-query-backend / Template freeze/validation / sqlite-memory | 2560.00 | 2560.00 | 0.9124 | 0.7807 | 0.8344 +/- 0.0519 | -8.55% | stable / stable |
| v09-query-backend / Template freeze/validation / sqlite-file | 2560.00 | 2560.00 | 1.1444 | 0.8088 | 0.8635 +/- 0.0853 | -24.55% | stable / improved |
| v09-query-backend / Expression parse/structural template / sqlite-file | 9472.00 | 9472.00 | 8.6626 | 8.4096 | 7.9349 +/- 0.2850 | -8.40% | stable / stable |
| v09-query-backend / Expression parse/structural template / sqlite-memory | 9472.00 | 9472.00 | 8.3210 | 8.2153 | 8.1472 +/- 0.5635 | -2.09% | stable / stable |
| v09-query-backend / Expression parse/template/initial bind / sqlite-file | 9533.44 | 9533.44 | 8.6850 | 7.8831 | 8.4749 +/- 0.6187 | -2.42% | stable / stable |
| v09-query-backend / Expression parse/template/initial bind / sqlite-memory | 9533.44 | 9533.44 | 8.8812 | 8.9211 | 9.2060 +/- 1.0926 | 3.66% | stable / stable |
| v09-query-backend / SQL adapter scalar Any / sqlite-file | 8663.04 | 8724.48 | 20.4909 | 24.0015 | 23.0787 +/- 1.6589 | 12.63% | warning / warning |
| v09-query-backend / SQL adapter scalar Any / sqlite-memory | 9041.92 | 9103.36 | 51.5673 | 45.0620 | 56.3596 +/- 6.6973 | 9.29% | warning / stable |
| v09-memory-read / Memory primary-key miss / memory | 20.48 | 20.48 | 0.0888 | 0.0791 | 0.1006 +/- 0.0135 | 13.29% | stable / warning |
| v09-memory-read / Memory primary-key hit / memory | 20.48 | 20.48 | 0.1544 | 0.1440 | 0.1662 +/- 0.0143 | 7.64% | stable / stable |
| v09-memory-read / Memory database construction / memory | 1628.16 | 1628.16 | 0.3449 | 0.2990 | 0.3302 +/- 0.0284 | -4.26% | stable / stable |
| v09-memory-read / Memory typed-ID equality count / memory | 7608.32 | 7608.32 | 11.0260 | 9.7210 | 10.1891 +/- 0.4561 | -7.59% | stable / stable |
| v09-memory-read / Memory direct-Guid equality count / memory | 7598.08 | 7598.08 | 9.8923 | 9.8489 | 13.5167 +/- 2.1511 | 36.64% | stable / warning |
| v09-memory-read / Memory repeated entity identity / memory | 7669.76 | 7669.76 | 23.8986 | 23.0041 | 27.0214 +/- 1.5507 | 13.07% | stable / warning |
| v09-memory-read / Memory filter order page / memory | 16076.80 | 16076.80 | 36.1112 | 33.9253 | 37.2620 +/- 0.8744 | 3.19% | stable / stable |
| v09-memory-read / Memory scalar scan / memory | 4505.60 | 4505.60 | 53.8955 | 52.7560 | 59.1906 +/- 4.0512 | 9.82% | stable / stable |
| v09-memory-read / Memory construct and seed / memory | 448092.16 | 448092.16 | 385.4299 | 353.1485 | 439.4452 +/- 58.4306 | 14.01% | stable / warning |
| allocation-regression / Warm relation traversal / sqlite-memory | 0.00 | 0.00 | 0.1226 | 0.1159 | 0.1142 +/- 0.0047 | -6.85% | stable / stable |
| allocation-regression / Warm primary-key fetch / sqlite-memory | 1812.48 | 1812.48 | 2.1786 | 2.0228 | 2.3780 +/- 0.2544 | 9.15% | stable / stable |
| allocation-regression / Update employees / sqlite-memory | 17561.60 | 20316.16 | 49.4950 | 49.2208 | 65.5024 +/- 8.5777 | 32.34% | warning / warning |
| allocation-regression / Cold primary-key fetch / sqlite-memory | 7608.32 | 7680.00 | 94.8760 | 97.8328 | 144.3064 +/- 23.7318 | 52.10% | warning / warning |
| allocation-regression / Cold relation traversal / sqlite-memory | 17111.04 | 17233.92 | 193.9262 | 177.2389 | 193.9687 +/- 14.3702 | 0.02% | warning / stable |
| allocation-regression / Startup primary-key fetch / sqlite-memory | 57384.96 | 58777.60 | 386.9108 | 347.3029 | 440.5813 +/- 41.7385 | 13.87% | improved / warning |
| allocation-regression / CRUD workflow small / sqlite-memory | 78264.32 | 86302.72 | 397.6687 | 413.8806 | 536.9698 +/- 75.2153 | 35.03% | warning / warning |
| allocation-regression / CRUD workflow batch / sqlite-memory | 78346.24 | 86312.96 | 400.3167 | 418.8335 | 557.3324 +/- 128.0308 | 39.22% | warning / noisy |
| allocation-regression / Provider initialization / sqlite-memory | 352757.76 | 353843.20 | 717.2022 | 622.5788 | 626.7082 +/- 49.3694 | -12.62% | stable / improved |
| allocation-stages / Source batch slice creation / sqlite-file | 0.00 | 0.00 | 0.0026 | 0.0025 | 0.0027 +/- 0.0002 | 3.85% | stable / stable |
| allocation-stages / Source batch slice creation / sqlite-memory | 0.00 | 0.00 | 0.0026 | 0.0023 | 0.0029 +/- 0.0004 | 11.54% | stable / warning |
| allocation-stages / Singular source argument validation / sqlite-file | 0.00 | 0.00 | 0.0104 | 0.0102 | 0.0108 +/- 0.0005 | 3.85% | stable / stable |
| allocation-stages / Singular source argument validation / sqlite-memory | 0.00 | 0.00 | 0.0109 | 0.0098 | 0.0118 +/- 0.0018 | 8.26% | stable / stable |
| allocation-stages / Singular source result validation / sqlite-memory | 0.00 | 0.00 | 0.0123 | 0.0107 | 0.0120 +/- 0.0006 | -2.44% | stable / stable |
| allocation-stages / Singular source result validation / sqlite-file | 0.00 | 0.00 | 0.0109 | 0.0104 | 0.0127 +/- 0.0011 | 16.51% | stable / warning |
| allocation-stages / Source loader result construction / sqlite-file | 737.28 | 737.28 | 0.0347 | 0.0328 | 0.0351 +/- 0.0038 | 1.15% | stable / stable |
| allocation-stages / Source loader result construction / sqlite-memory | 737.28 | 737.28 | 0.0349 | 0.0303 | 0.0363 +/- 0.0039 | 4.01% | stable / stable |
| allocation-stages / Mutation state-change capture / sqlite-memory | 348.16 | 348.16 | 0.0766 | 0.0620 | 0.0596 +/- 0.0005 | -22.19% | stable / improved |
| allocation-stages / Mutation state-change capture / sqlite-file | 348.16 | 348.16 | 0.0701 | 0.0668 | 0.0659 +/- 0.0051 | -5.99% | stable / stable |
| allocation-stages / Mutation final drift validation / sqlite-memory | 0.00 | 0.00 | 0.0827 | 0.0731 | 0.0858 +/- 0.0030 | 3.75% | stable / stable |
| allocation-stages / Mutation final drift validation / sqlite-file | 0.00 | 0.00 | 0.0870 | 0.0748 | 0.0871 +/- 0.0077 | 0.11% | stable / stable |
| allocation-stages / Scalar canonical-key propagation / sqlite-file | 235.52 | 235.52 | 0.1147 | 0.0970 | 0.1002 +/- 0.0041 | -12.64% | stable / improved |
| allocation-stages / Singular source SQL preparation / sqlite-memory | 256.00 | 256.00 | 0.1426 | 0.1320 | 0.1213 +/- 0.0029 | -14.94% | stable / improved |
| allocation-stages / Singular source SQL preparation / sqlite-file | 256.00 | 256.00 | 0.1388 | 0.1245 | 0.1220 +/- 0.0055 | -12.10% | stable / improved |
| allocation-stages / Scalar canonical-key propagation / sqlite-memory | 235.52 | 235.52 | 0.1225 | 0.1027 | 0.1359 +/- 0.0271 | 10.94% | stable / warning |
| allocation-stages / Composite canonical-key propagation / sqlite-memory | 276.48 | 276.48 | 0.1613 | 0.1548 | 0.1559 +/- 0.0131 | -3.35% | stable / stable |
| allocation-stages / Source request construction / sqlite-memory | 51.20 | 51.20 | 0.1687 | 0.1448 | 0.1610 +/- 0.0051 | -4.56% | stable / stable |
| allocation-stages / Source request construction / sqlite-file | 51.20 | 51.20 | 0.1535 | 0.1468 | 0.1701 +/- 0.0110 | 10.81% | stable / warning |
| allocation-stages / Converter-backed canonical-key propagation / sqlite-file | 348.16 | 348.16 | 0.1777 | 0.1723 | 0.1703 +/- 0.0148 | -4.16% | stable / stable |
| allocation-stages / Converter-backed canonical-key propagation / sqlite-memory | 348.16 | 348.16 | 0.1748 | 0.1570 | 0.1706 +/- 0.0101 | -2.40% | stable / stable |
| allocation-stages / Composite canonical-key propagation / sqlite-file | 276.48 | 276.48 | 0.1674 | 0.1496 | 0.1760 +/- 0.0244 | 5.14% | stable / stable |
| allocation-stages / Source cache result publication / sqlite-file | 880.64 | 880.64 | 0.1941 | 0.1772 | 0.1864 +/- 0.0173 | -3.97% | stable / stable |
| allocation-stages / Source cache result publication / sqlite-memory | 880.64 | 880.64 | 0.1951 | 0.1690 | 0.1934 +/- 0.0293 | -0.87% | stable / stable |
| allocation-stages / Typed-ID canonical-key propagation / sqlite-memory | 337.92 | 337.92 | 0.2144 | 0.2065 | 0.2122 +/- 0.0196 | -1.03% | stable / stable |
| allocation-stages / Typed-ID canonical-key propagation / sqlite-file | 337.92 | 337.92 | 0.2171 | 0.1905 | 0.2164 +/- 0.0188 | -0.32% | stable / stable |
| allocation-stages / Binary canonical-key propagation / sqlite-file | 307.20 | 307.20 | 0.2136 | 0.1987 | 0.2376 +/- 0.0367 | 11.24% | stable / warning |
| allocation-stages / Provider-row model materialization / sqlite-file | 143.36 | 143.36 | 0.2407 | 0.2178 | 0.2488 +/- 0.0210 | 3.37% | stable / stable |
| allocation-stages / Composite key reconstruction baseline / sqlite-file | 348.16 | 348.16 | 0.2739 | 0.2367 | 0.2626 +/- 0.0211 | -4.13% | stable / stable |
| allocation-stages / Provider-row model materialization / sqlite-memory | 143.36 | 143.36 | 0.2642 | 0.2271 | 0.2740 +/- 0.0349 | 3.71% | stable / stable |
| allocation-stages / Binary canonical-key propagation / sqlite-memory | 307.20 | 307.20 | 0.2157 | 0.1934 | 0.2865 +/- 0.0827 | 32.82% | stable / noisy |
| allocation-stages / Composite key reconstruction baseline / sqlite-memory | 348.16 | 348.16 | 0.2801 | 0.2503 | 0.2952 +/- 0.0400 | 5.39% | stable / stable |
| allocation-stages / Canonical provider-row decoding / sqlite-memory | 296.96 | 296.96 | 0.7320 | 0.5058 | 0.5166 +/- 0.0105 | -29.43% | stable / improved |
| allocation-stages / Canonical provider-row decoding / sqlite-file | 296.96 | 296.96 | 0.5309 | 0.5055 | 0.6270 +/- 0.0758 | 18.10% | stable / warning |
| allocation-stages / Mutation execution preflight / sqlite-memory | 0.00 | 0.00 | 1.5030 | 0.7635 | 0.8156 +/- 0.0796 | -45.74% | improved / improved |
| allocation-stages / Provider-row decode/materialization pipeline / sqlite-memory | 440.32 | 440.32 | 0.7909 | 0.7280 | 0.8579 +/- 0.0654 | 8.47% | stable / stable |
| allocation-stages / Provider-row decode/materialization pipeline / sqlite-file | 440.32 | 440.32 | 0.8207 | 0.7780 | 0.8708 +/- 0.0953 | 6.10% | stable / stable |
| allocation-stages / Mutation execution preflight / sqlite-file | 0.00 | 0.00 | 1.2340 | 0.7624 | 1.0116 +/- 0.2248 | -18.02% | improved / noisy |
| allocation-stages / Source result validation / sqlite-memory | 737.28 | 737.28 | 1.6288 | 1.4897 | 1.6762 +/- 0.1115 | 2.91% | stable / stable |
| allocation-stages / Source result validation / sqlite-file | 737.28 | 737.28 | 1.5657 | 1.4963 | 1.6867 +/- 0.1309 | 7.73% | stable / stable |
| allocation-stages / Known-miss materialization/publication / sqlite-file | 419.84 | 419.84 | 1.7172 | 1.3090 | 1.7873 +/- 0.1575 | 4.08% | stable / stable |
| allocation-stages / Known-miss materialization/publication / sqlite-memory | 419.84 | 419.84 | 1.9337 | 1.9199 | 2.7243 +/- 0.3625 | 40.89% | stable / warning |
| allocation-stages / Mutation command preparation / sqlite-file | 2232.32 | 2232.32 | 2.7729 | 3.1285 | 2.7842 +/- 0.2363 | 0.41% | stable / stable |
| allocation-stages / Mutation command preparation / sqlite-memory | 2232.32 | 2232.32 | 3.0750 | 2.9700 | 3.4863 +/- 0.2791 | 13.38% | stable / warning |
| allocation-stages / Warm typed-ID exact terminal / sqlite-memory | 1597.44 | 1597.44 | 5.3633 | 6.1115 | 5.2137 +/- 1.5023 | -2.79% | stable / noisy |
| allocation-stages / Warm typed-ID exact terminal / sqlite-file | 1597.44 | 1597.44 | 7.9779 | 7.4632 | 7.7299 +/- 1.6274 | -3.11% | stable / noisy |
| allocation-stages / Cold typed-ID exact terminal / sqlite-file | 6246.40 | 6174.72 | 54.4879 | 53.8307 | 68.4417 +/- 12.4764 | 25.61% | warning / warning |
| allocation-stages / Cold typed-ID exact terminal / sqlite-memory | 6461.44 | 6481.92 | 73.8988 | 76.2517 | 71.5642 +/- 10.7342 | -3.16% | warning / stable |
