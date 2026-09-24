# W2 Revised Candidate Checkpoint

**Runtime:** `76781c890d3486316de00d4c3cffd5dc1e4f6680`, captured 2026-09-24. The only product change since the [original checkpoint](W2%20Functional%20And%20Performance%20Checkpoint.md) is the published-coordinator fast path in the SQLite and shared MySQL/MariaDB `RootDisposal` getters. This report and the SQLite reproduction project do not change that runtime.

W2's internal native bindings and planned verification captures are complete. **Final W2 acceptance remains open.** The official SQLite ownership correction is still pending package adoption and affected reruns; the separate failed-rollback pool defect needs a disposition; performance results retain real costs and unresolved timing attribution. Public/generated async APIs remain W3 work.

## Functional Verification

The clean full Release run passes **8,539/8,539**, with zero failures or skips. Checkout, runner identity and unchanged source match; **ValidForEvidence=true**. The full plan uses its existing eight-concurrent-test limit and batch size one to retain individual provider totals. Independent TRX inspection confirms all results Passed, including all eight native sync/async telemetry parity cases.

| Suite | Passing tests |
| --- | ---: |
| Generators | 71 |
| Unit | 3,961 |
| Memory | 221 |
| Compliance: SQLite file / memory | 547 / 404 |
| Compliance: each of six server targets | 410 each |
| Server-specific: MySQL 9.7 / 8.4 | 207 / 132 |
| Server-specific: each of four MariaDB targets | 134 each |

The server targets are MySQL 9.7/8.4 and MariaDB 10.11/11.4/11.8/12.3. [CI #625](https://github.com/bazer/DataLinq/actions/runs/35961636090) passes all 12 jobs, including the required gate, on this same commit. The local runner build has zero warnings/errors. Earlier automatic-concurrency timeout observations and the original Linux iterator allocation observation remain in the [execution record](W2%20Native%20Provider%20Async%20Execution.md); these passes do not prove their original causes.

Run `20260924T055226608Z-913fa509f4ab434e879770bdb51484f8` is recorded in `artifacts/w2-76781c89-full-release.json`, SHA-256 `4f67388220bdfb74baa9ec2dcdaae3d215d682981ba71bc9a0c8b24fbd91fb9d`. Independent receipt `artifacts/w2-76781c89-functional-verification.json` retains all 69 artifact hashes. Raw evidence remains local; this report is tracked.

## Performance Review

All six canonical lanes were run sequentially after the functional matrix, without overlapping local builds, tests or profilers. All 90 rows and 126 raw receipts verify, with clean matching source/runner identities, unchanged operation counts and normalized telemetry, and unchanged frozen W0 artifacts. Every history and comparison is **ValidForEvidence=true**. This validates the evidence, not performance parity.

The environment is Windows x64, .NET 10.0.12, SDK 10.0.401 and BenchmarkDotNet 0.15.8, Intel Family 6 Model 140 Stepping 1, eight logical processors. Baselines remain W0 `7e36614b5f26f1dd199ff93bc60620f4b0714d5f` and W1 `a435d0b428937040bfd02210e67eb1219c9ed6f7`. The clean benchmark DLL is SHA-256 `9b278bdafe226e9300f89e55b485832e9a581cf4de6dce36f353d7d82bfed776`, independently checked before each next build. Capture spans 06:00:29–06:47:09 UTC. The permitted host-counter retry returned at 08:06:59 UTC, after measurement ended, and cannot establish the cause of timing variation.

### What Changed And What Remains

- The actual SQLite `DatabaseAccess` getter measures **zero bytes per access**, down from 64, on the clean runtime. Both file and memory controls preserve object identity across 10,000 measured accesses and reject access after disposal. Probe DLL SHA-256 `4235cc60e390de61937236dadfb145414d3ee6986625309a6465514d7fe6c493` is unchanged. The clean receipt `artifacts/w2-lifecycle-access-76781c89-clean.json` has SHA-256 `ff692b59130bf393bfbb6a988bbe56bd4d19cefe6e360fd2fa50c71052f8f612`. This quantifies the SQLite getter only; it is not a whole-query or server allocation claim.
- Cold primary-key/memory allocation is **7,608.32 B/op**, equal to W1. Cold typed-ID/file is **6,246.40 B/op**, equal to W1; cold typed-ID/memory is **6,420.48 B/op**, below W1's 6,461.44. Warm primary-key remains 1,812.48 B/op and warm relation remains zero. Canonical allocation values are BDN estimates; the isolated getter probe is an exact workload-thread measurement.
- Mutation allocation remains higher: update **20,316.16 B/op** (+2,754.56 versus W1), small CRUD **86,159.36** (+7,895.04), batch CRUD **86,159.36** (+7,813.12). The [paired traces and source review](W2%20Performance%20Cost%20Disposition.md) identify native command/reader ownership and diagnostic scope/context costs. Retain these measured correctness costs for internal integration and W9 release comparison. The factory fast path does not remove them.
- Current cold primary-key/memory timing is **114.7171 ± 22.8365 us**, versus W1 97.8328 and W0 94.8760: still a W0 warning. Cold typed-ID/file is **58.4770 ± 7.0246 us** and memory **77.2342 ± 10.3738 us**, classified stable versus W0. Earlier fixed-order controls found higher cold-query means in both orders. The recapture does not prove the getter change caused the timing differences, or erase the earlier unfavorable controls.
- Update **64.5539 ± 8.1604 us** and small CRUD **548.0817 ± 80.2813 us** remain W0 timing warnings; batch CRUD **531.7900 ± 110.4121 us** remains noisy. Source attribution establishes added coordination work, but does not quantitatively explain every latency increase. Several unchanged micro-stages also vary with order. No host-contention explanation is established.

The current totals are **13 allocation warnings, 19 latency warnings and 9 noisy latency rows**. The previous totals were 13/20/12; fewer flags alone are not proof of improvement. The full table below retains every row, including unfavorable, noisy and unchanged-source observations. Thresholds and assertions are unchanged.

**Disposition:** the planned capture and cost review are complete. Retain the attributed allocation costs and every latency observation. The evidence does not justify a claim of performance parity, and the performance acceptance gate remains open. Repeating this same local capture until it turns green would not resolve attribution. A further optimization pass needs a concrete hypothesis and bounded workload; final acceptance must explicitly decide how these measured costs fit the release policy. Documentation-only follow-ups do not invalidate this runtime's receipts or require another full matrix/capture.

## SQLite Acceptance

Production pins remain Microsoft.Data.Sqlite 10.0.11 and MySqlConnector 2.6.2. Adopt the official SQLite ownership correction through `master` → `v0.10` → W2 and rerun affected evidence when the published package is available. The separate [failed-rollback pool finding](SQLite%20Failed%20Rollback%20Pool%20Reuse%20Investigation.md) now reproduces directly on published **10.0.11 and 10.0.12**, in both sync and async disposal. A [reviewable upstream draft](evidence/sqlite-rollback/upstream-issue.md) is prepared; nothing has been submitted. No production pool policy or automatic retry is changed. These are distinct acceptance questions.

## Capture Receipts

The lane summary and history/comparison hashes below are generated from the retained verification receipts. Noisy counts refer to the latency field, even when the overall row status is dominated by an allocation warning. Raw files are under `artifacts/benchmarks/`; verifier results are `artifacts/w2-76781c89-<lane>-verification.json`. The normalized complete table is `artifacts/w2-76781c89-runtime-comparison.md` with its JSON companion.

| Lane | Rows | Raw receipts | Allocation warnings | Latency warnings | Noisy latency |
| --- | ---: | ---: | ---: | ---: | ---: |
| phase2-watch | 6 | 12 | 0 | 2 | 2 |
| phase3-query-hotpath | 6 | 12 | 4 | 2 | 4 |
| v09-query-backend | 12 | 18 | 2 | 2 | 0 |
| v09-memory-read | 9 | 15 | 0 | 3 | 0 |
| allocation-regression | 9 | 15 | 5 | 4 | 1 |
| allocation-stages | 48 | 54 | 2 | 6 | 2 |

| Lane | History SHA-256 | W0 comparison SHA-256 |
| --- | --- | --- |
| phase2-watch | `4d1526feaa51cc4e20fb4112a2204470729344d9c3f97cfd394ae255f872eb75` | `eaef0450dc945cf14e2a3fc83c270b9a5e0614cf3f48c0843b4eb3b1736f0bb0` |
| phase3-query-hotpath | `a9f186cfb516ab9ed496f90e2a02014dc285e4e5b0833b2627ad833c530e0e83` | `a22e5464c9da457ae85125ed5e987b27c7d1ba579fe57cd9d8e68e89e7653b8e` |
| v09-query-backend | `3c0b7b94513526e0195e9aee08a502d2c2d96a64bb688264df2640213fd90495` | `33d9c5d52932da27fcab5139ca6eb0e1e79e140d0e341dcb7a0402e1a84d85fd` |
| v09-memory-read | `e34f6b27404745799eabba3dc9d80e2c5099439cde35a0b657731c0e21017ee6` | `9430a7f1cfab03aa102662e1af2e6c5b8c528b204de0ba1e88977de322bed229` |
| allocation-regression | `a3d61f85e1e1bd94c67bbf8fa45ca687eff6046f7107cf960df251d037b6bedd` | `97d3f41bc09dc37b19d4aa41f8977bed8edf8dc2f470fd3fe35cd3687760fc6c` |
| allocation-stages | `1bea2ba818a0f7ef6a02e8e3cd0c62cb49b34e7748b5a4c6220d2470828f21b1` | `7c6c0d131fa23d0e69e588c4985bb27c22910ac322f01d03455c7c8d83f3607b` |

## Complete Canonical Table

Allocation columns compare with W1; latency deltas and classification compare with the frozen W0 lane. Errors are the reported BenchmarkDotNet errors, not standard deviations or proof of equivalence. Duplicate workload/provider names in different lanes keep their distinct recipes and measurements.

| Lane / workload / provider | W1 B/op | W2 B/op | Change B/op | W0 mean, us | Current mean +/- error, us | W0 delta | Status |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| phase2-watch / Warm primary-key fetch / sqlite-memory | 1812.48 | 1812.48 | 0.00 | 5.4910 | 7.7460 +/- 1.7130 | 41.07% | allocation=stable; latency=noisy |
| phase2-watch / Warm primary-key fetch / sqlite-file | 1812.48 | 1812.48 | 0.00 | 6.7130 | 9.7310 +/- 1.2760 | 44.96% | allocation=stable; latency=noisy |
| phase2-watch / Startup primary-key fetch / sqlite-file | 53504.00 | 54312.96 | 808.96 | 284.6420 | 396.9830 +/- 33.8870 | 39.47% | allocation=improved; latency=warning |
| phase2-watch / Startup primary-key fetch / sqlite-memory | 57384.96 | 58705.92 | 1320.96 | 414.5200 | 410.2700 +/- 18.6430 | -1.03% | allocation=improved; latency=stable |
| phase2-watch / Provider initialization / sqlite-file | 349122.56 | 349859.84 | 737.28 | 641.1500 | 554.7930 +/- 46.1710 | -13.47% | allocation=stable; latency=improved |
| phase2-watch / Provider initialization / sqlite-memory | 352389.12 | 354017.28 | 1628.16 | 734.3020 | 929.3750 +/- 172.9380 | 26.57% | allocation=stable; latency=warning |
| phase3-query-hotpath / Repeated scalar Any / sqlite-memory | 18636.80 | 18698.24 | 61.44 | 117.6400 | 129.6000 +/- 29.0100 | 10.17% | allocation=stable; latency=noisy |
| phase3-query-hotpath / Repeated scalar Any / sqlite-file | 18421.76 | 18360.32 | -61.44 | 88.2500 | 135.6000 +/- 30.3400 | 53.65% | allocation=stable; latency=noisy |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-file | 34877.44 | 34877.44 | 0.00 | 123.9000 | 136.0000 +/- 21.6600 | 9.77% | allocation=warning; latency=noisy |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-file | 26890.24 | 26828.80 | -61.44 | 128.5700 | 158.5000 +/- 31.2900 | 23.28% | allocation=warning; latency=warning |
| phase3-query-hotpath / Repeated IN predicate fetch / sqlite-memory | 35624.96 | 35747.84 | 122.88 | 141.6800 | 166.2000 +/- 24.2100 | 17.31% | allocation=warning; latency=warning |
| phase3-query-hotpath / Repeated non-PK equality fetch / sqlite-memory | 27361.28 | 27361.28 | 0.00 | 154.2500 | 187.6000 +/- 44.9500 | 21.62% | allocation=warning; latency=noisy |
| v09-query-backend / Invocation bind scalar/local sequence / sqlite-memory | 307.20 | 307.20 | 0.00 | 0.1652 | 0.1459 +/- 0.0027 | -11.68% | allocation=stable; latency=improved |
| v09-query-backend / Invocation bind scalar/local sequence / sqlite-file | 307.20 | 307.20 | 0.00 | 0.1778 | 0.1543 +/- 0.0112 | -13.22% | allocation=stable; latency=improved |
| v09-query-backend / SQL request/capability preparation / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.2409 | 0.2144 +/- 0.0209 | -11.00% | allocation=stable; latency=improved |
| v09-query-backend / SQL request/capability preparation / sqlite-file | 0.00 | 0.00 | 0.00 | 0.2323 | 0.2153 +/- 0.0135 | -7.32% | allocation=stable; latency=stable |
| v09-query-backend / Template freeze/validation / sqlite-memory | 2560.00 | 2560.00 | 0.00 | 0.9124 | 0.8032 +/- 0.0171 | -11.97% | allocation=stable; latency=improved |
| v09-query-backend / Template freeze/validation / sqlite-file | 2560.00 | 2560.00 | 0.00 | 1.1444 | 0.8962 +/- 0.0912 | -21.69% | allocation=stable; latency=improved |
| v09-query-backend / Expression parse/structural template / sqlite-memory | 9472.00 | 9472.00 | 0.00 | 8.3210 | 8.2393 +/- 0.5008 | -0.98% | allocation=stable; latency=stable |
| v09-query-backend / Expression parse/structural template / sqlite-file | 9472.00 | 9472.00 | 0.00 | 8.6626 | 8.3102 +/- 0.6813 | -4.07% | allocation=stable; latency=stable |
| v09-query-backend / Expression parse/template/initial bind / sqlite-memory | 9533.44 | 9533.44 | 0.00 | 8.8812 | 8.4623 +/- 0.5645 | -4.72% | allocation=stable; latency=stable |
| v09-query-backend / Expression parse/template/initial bind / sqlite-file | 9533.44 | 9533.44 | 0.00 | 8.6850 | 9.8832 +/- 1.1023 | 13.80% | allocation=stable; latency=warning |
| v09-query-backend / SQL adapter scalar Any / sqlite-file | 8663.04 | 8663.04 | 0.00 | 20.4909 | 24.9709 +/- 3.0222 | 21.86% | allocation=warning; latency=warning |
| v09-query-backend / SQL adapter scalar Any / sqlite-memory | 9041.92 | 9041.92 | 0.00 | 51.5673 | 46.1482 +/- 3.8192 | -10.51% | allocation=warning; latency=improved |
| v09-memory-read / Memory primary-key miss / memory | 20.48 | 20.48 | 0.00 | 0.0888 | 0.0941 +/- 0.0074 | 5.97% | allocation=stable; latency=stable |
| v09-memory-read / Memory primary-key hit / memory | 20.48 | 20.48 | 0.00 | 0.1544 | 0.1657 +/- 0.0105 | 7.32% | allocation=stable; latency=stable |
| v09-memory-read / Memory database construction / memory | 1628.16 | 1628.16 | 0.00 | 0.3449 | 0.3287 +/- 0.0324 | -4.70% | allocation=stable; latency=stable |
| v09-memory-read / Memory typed-ID equality count / memory | 7608.32 | 7608.32 | 0.00 | 11.0260 | 10.3704 +/- 0.4421 | -5.95% | allocation=stable; latency=stable |
| v09-memory-read / Memory direct-Guid equality count / memory | 7598.08 | 7598.08 | 0.00 | 9.8923 | 12.1772 +/- 1.5184 | 23.10% | allocation=stable; latency=warning |
| v09-memory-read / Memory repeated entity identity / memory | 7669.76 | 7669.76 | 0.00 | 23.8986 | 26.7516 +/- 3.0442 | 11.94% | allocation=stable; latency=warning |
| v09-memory-read / Memory filter order page / memory | 16076.80 | 16076.80 | 0.00 | 36.1112 | 44.4506 +/- 5.0204 | 23.09% | allocation=stable; latency=warning |
| v09-memory-read / Memory scalar scan / memory | 4505.60 | 4505.60 | 0.00 | 53.8955 | 59.2069 +/- 3.3700 | 9.85% | allocation=stable; latency=stable |
| v09-memory-read / Memory construct and seed / memory | 448092.16 | 448092.16 | 0.00 | 385.4299 | 400.3338 +/- 20.4303 | 3.87% | allocation=stable; latency=stable |
| allocation-regression / Warm relation traversal / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.1226 | 0.1269 +/- 0.0132 | 3.51% | allocation=stable; latency=stable |
| allocation-regression / Warm primary-key fetch / sqlite-memory | 1812.48 | 1812.48 | 0.00 | 2.1786 | 2.2868 +/- 0.2316 | 4.97% | allocation=stable; latency=stable |
| allocation-regression / Update employees / sqlite-memory | 17561.60 | 20316.16 | 2754.56 | 49.4950 | 64.5539 +/- 8.1604 | 30.43% | allocation=warning; latency=warning |
| allocation-regression / Cold primary-key fetch / sqlite-memory | 7608.32 | 7608.32 | 0.00 | 94.8760 | 114.7171 +/- 22.8365 | 20.91% | allocation=warning; latency=warning |
| allocation-regression / Cold relation traversal / sqlite-memory | 17111.04 | 17111.04 | 0.00 | 193.9262 | 209.6830 +/- 25.6511 | 8.13% | allocation=warning; latency=stable |
| allocation-regression / Startup primary-key fetch / sqlite-memory | 57384.96 | 58705.92 | 1320.96 | 386.9108 | 486.5310 +/- 81.7175 | 25.75% | allocation=improved; latency=warning |
| allocation-regression / CRUD workflow batch / sqlite-memory | 78346.24 | 86159.36 | 7813.12 | 400.3167 | 531.7900 +/- 110.4121 | 32.84% | allocation=warning; latency=noisy |
| allocation-regression / CRUD workflow small / sqlite-memory | 78264.32 | 86159.36 | 7895.04 | 397.6687 | 548.0817 +/- 80.2813 | 37.82% | allocation=warning; latency=warning |
| allocation-regression / Provider initialization / sqlite-memory | 352757.76 | 354037.76 | 1280.00 | 717.2022 | 609.3097 +/- 30.9539 | -15.04% | allocation=stable; latency=improved |
| allocation-stages / Source batch slice creation / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.0026 | 0.0025 +/- 0.0001 | -3.85% | allocation=stable; latency=stable |
| allocation-stages / Source batch slice creation / sqlite-file | 0.00 | 0.00 | 0.00 | 0.0026 | 0.0027 +/- 0.0002 | 3.85% | allocation=stable; latency=stable |
| allocation-stages / Singular source argument validation / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.0109 | 0.0112 +/- 0.0011 | 2.75% | allocation=stable; latency=stable |
| allocation-stages / Singular source argument validation / sqlite-file | 0.00 | 0.00 | 0.00 | 0.0104 | 0.0113 +/- 0.0008 | 8.65% | allocation=stable; latency=stable |
| allocation-stages / Singular source result validation / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.0123 | 0.0115 +/- 0.0003 | -6.50% | allocation=stable; latency=stable |
| allocation-stages / Singular source result validation / sqlite-file | 0.00 | 0.00 | 0.00 | 0.0109 | 0.0143 +/- 0.0020 | 31.19% | allocation=stable; latency=warning |
| allocation-stages / Source loader result construction / sqlite-file | 737.28 | 737.28 | 0.00 | 0.0347 | 0.0332 +/- 0.0014 | -4.32% | allocation=stable; latency=stable |
| allocation-stages / Source loader result construction / sqlite-memory | 737.28 | 737.28 | 0.00 | 0.0349 | 0.0343 +/- 0.0025 | -1.72% | allocation=stable; latency=stable |
| allocation-stages / Mutation state-change capture / sqlite-file | 348.16 | 348.16 | 0.00 | 0.0701 | 0.0679 +/- 0.0074 | -3.14% | allocation=stable; latency=stable |
| allocation-stages / Mutation state-change capture / sqlite-memory | 348.16 | 348.16 | 0.00 | 0.0766 | 0.0687 +/- 0.0080 | -10.31% | allocation=stable; latency=improved |
| allocation-stages / Mutation final drift validation / sqlite-file | 0.00 | 0.00 | 0.00 | 0.0870 | 0.0930 +/- 0.0131 | 6.90% | allocation=stable; latency=stable |
| allocation-stages / Mutation final drift validation / sqlite-memory | 0.00 | 0.00 | 0.00 | 0.0827 | 0.0931 +/- 0.0049 | 12.58% | allocation=stable; latency=warning |
| allocation-stages / Scalar canonical-key propagation / sqlite-memory | 235.52 | 235.52 | 0.00 | 0.1225 | 0.1021 +/- 0.0077 | -16.65% | allocation=stable; latency=improved |
| allocation-stages / Scalar canonical-key propagation / sqlite-file | 235.52 | 235.52 | 0.00 | 0.1147 | 0.1025 +/- 0.0073 | -10.64% | allocation=stable; latency=improved |
| allocation-stages / Singular source SQL preparation / sqlite-file | 256.00 | 256.00 | 0.00 | 0.1388 | 0.1225 +/- 0.0080 | -11.74% | allocation=stable; latency=improved |
| allocation-stages / Singular source SQL preparation / sqlite-memory | 256.00 | 256.00 | 0.00 | 0.1426 | 0.1278 +/- 0.0029 | -10.38% | allocation=stable; latency=improved |
| allocation-stages / Composite canonical-key propagation / sqlite-memory | 276.48 | 276.48 | 0.00 | 0.1613 | 0.1511 +/- 0.0029 | -6.32% | allocation=stable; latency=stable |
| allocation-stages / Source request construction / sqlite-file | 51.20 | 51.20 | 0.00 | 0.1535 | 0.1607 +/- 0.0106 | 4.69% | allocation=stable; latency=stable |
| allocation-stages / Composite canonical-key propagation / sqlite-file | 276.48 | 276.48 | 0.00 | 0.1674 | 0.1614 +/- 0.0141 | -3.58% | allocation=stable; latency=stable |
| allocation-stages / Converter-backed canonical-key propagation / sqlite-memory | 348.16 | 348.16 | 0.00 | 0.1748 | 0.1639 +/- 0.0049 | -6.24% | allocation=stable; latency=stable |
| allocation-stages / Source request construction / sqlite-memory | 51.20 | 51.20 | 0.00 | 0.1687 | 0.1683 +/- 0.0122 | -0.24% | allocation=stable; latency=stable |
| allocation-stages / Source cache result publication / sqlite-file | 880.64 | 880.64 | 0.00 | 0.1941 | 0.1810 +/- 0.0115 | -6.75% | allocation=stable; latency=stable |
| allocation-stages / Source cache result publication / sqlite-memory | 880.64 | 880.64 | 0.00 | 0.1951 | 0.1812 +/- 0.0134 | -7.12% | allocation=stable; latency=stable |
| allocation-stages / Converter-backed canonical-key propagation / sqlite-file | 348.16 | 348.16 | 0.00 | 0.1777 | 0.1815 +/- 0.0186 | 2.14% | allocation=stable; latency=stable |
| allocation-stages / Binary canonical-key propagation / sqlite-memory | 307.20 | 307.20 | 0.00 | 0.2157 | 0.1907 +/- 0.0070 | -11.59% | allocation=stable; latency=improved |
| allocation-stages / Binary canonical-key propagation / sqlite-file | 307.20 | 307.20 | 0.00 | 0.2136 | 0.2080 +/- 0.0180 | -2.62% | allocation=stable; latency=stable |
| allocation-stages / Typed-ID canonical-key propagation / sqlite-file | 337.92 | 337.92 | 0.00 | 0.2171 | 0.2111 +/- 0.0095 | -2.76% | allocation=stable; latency=stable |
| allocation-stages / Typed-ID canonical-key propagation / sqlite-memory | 337.92 | 337.92 | 0.00 | 0.2144 | 0.2193 +/- 0.0255 | 2.29% | allocation=stable; latency=stable |
| allocation-stages / Provider-row model materialization / sqlite-file | 143.36 | 143.36 | 0.00 | 0.2407 | 0.2455 +/- 0.0204 | 1.99% | allocation=stable; latency=stable |
| allocation-stages / Composite key reconstruction baseline / sqlite-memory | 348.16 | 348.16 | 0.00 | 0.2801 | 0.2585 +/- 0.0052 | -7.71% | allocation=stable; latency=stable |
| allocation-stages / Provider-row model materialization / sqlite-memory | 143.36 | 143.36 | 0.00 | 0.2642 | 0.2617 +/- 0.0317 | -0.95% | allocation=stable; latency=stable |
| allocation-stages / Composite key reconstruction baseline / sqlite-file | 348.16 | 348.16 | 0.00 | 0.2739 | 0.2822 +/- 0.0255 | 3.03% | allocation=stable; latency=stable |
| allocation-stages / Canonical provider-row decoding / sqlite-file | 296.96 | 296.96 | 0.00 | 0.5309 | 0.5456 +/- 0.0200 | 2.77% | allocation=stable; latency=stable |
| allocation-stages / Canonical provider-row decoding / sqlite-memory | 296.96 | 296.96 | 0.00 | 0.7320 | 0.5967 +/- 0.0650 | -18.48% | allocation=stable; latency=improved |
| allocation-stages / Provider-row decode/materialization pipeline / sqlite-file | 440.32 | 440.32 | 0.00 | 0.8207 | 0.8389 +/- 0.0523 | 2.22% | allocation=stable; latency=stable |
| allocation-stages / Provider-row decode/materialization pipeline / sqlite-memory | 440.32 | 440.32 | 0.00 | 0.7909 | 0.8863 +/- 0.1011 | 12.06% | allocation=stable; latency=warning |
| allocation-stages / Mutation execution preflight / sqlite-file | 0.00 | 0.00 | 0.00 | 1.2340 | 0.9105 +/- 0.1666 | -26.22% | allocation=improved; latency=improved |
| allocation-stages / Mutation execution preflight / sqlite-memory | 0.00 | 0.00 | 0.00 | 1.5030 | 0.9723 +/- 0.1830 | -35.31% | allocation=improved; latency=improved |
| allocation-stages / Source result validation / sqlite-file | 737.28 | 737.28 | 0.00 | 1.5657 | 1.6889 +/- 0.1363 | 7.87% | allocation=stable; latency=stable |
| allocation-stages / Source result validation / sqlite-memory | 737.28 | 737.28 | 0.00 | 1.6288 | 1.7001 +/- 0.1114 | 4.38% | allocation=stable; latency=stable |
| allocation-stages / Known-miss materialization/publication / sqlite-file | 419.84 | 419.84 | 0.00 | 1.7172 | 1.9075 +/- 0.3794 | 11.08% | allocation=stable; latency=warning |
| allocation-stages / Known-miss materialization/publication / sqlite-memory | 419.84 | 419.84 | 0.00 | 1.9337 | 2.6216 +/- 0.4116 | 35.57% | allocation=stable; latency=warning |
| allocation-stages / Mutation command preparation / sqlite-memory | 2232.32 | 2232.32 | 0.00 | 3.0750 | 3.2536 +/- 0.7458 | 5.81% | allocation=stable; latency=noisy |
| allocation-stages / Mutation command preparation / sqlite-file | 2232.32 | 2232.32 | 0.00 | 2.7729 | 3.7553 +/- 0.4689 | 35.43% | allocation=stable; latency=warning |
| allocation-stages / Warm typed-ID exact terminal / sqlite-memory | 1597.44 | 1597.44 | 0.00 | 5.3633 | 4.5214 +/- 1.2845 | -15.70% | allocation=stable; latency=noisy |
| allocation-stages / Warm typed-ID exact terminal / sqlite-file | 1597.44 | 1597.44 | 0.00 | 7.9779 | 7.5600 +/- 0.9852 | -5.24% | allocation=stable; latency=stable |
| allocation-stages / Cold typed-ID exact terminal / sqlite-file | 6246.40 | 6246.40 | 0.00 | 54.4879 | 58.4770 +/- 7.0246 | 7.32% | allocation=warning; latency=stable |
| allocation-stages / Cold typed-ID exact terminal / sqlite-memory | 6461.44 | 6420.48 | -40.96 | 73.8988 | 77.2342 +/- 10.3738 | 4.51% | allocation=warning; latency=stable |
