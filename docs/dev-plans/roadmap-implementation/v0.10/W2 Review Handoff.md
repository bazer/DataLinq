# W2 Review Handoff

**Status, 2026-09-28:** the full review identified four correctness/compatibility findings on `e4720b61`. All four are addressed in `7b6607b69b9249b70ba362173f1fb221bcd40b7c`, with focused regressions and clean full Debug and Release matrices. The [follow-up review](https://github.com/bazer/DataLinq/pull/229#issuecomment-5870998052) confirms all four resolved with no new actionable issues. The user then authorized [W2 closure and merge-commit integration](W2%20Closeout.md). Public API freeze and release approval remain separate.

**PR:** [#229, Implement W2: native provider async execution](https://github.com/bazer/DataLinq/pull/229), `codex/0.10-w2` → `v0.10`. Preserve the accepted single-PR/merge-commit workflow. Public/generated async APIs remain W3 work.

## Accepted Scheduling Decisions

1. **Defer all further performance work to the end of 0.10, after feature implementation.** Run a focused performance investigation and optimization effort as part of final integration, before freezing the W9 release candidate. Performance acceptance is a final-release obligation, not a remaining W2 review prerequisite. This supersedes earlier W2 requests for another performance pass before review. Existing required CI checks and functional/telemetry correctness assertions remain in place.
2. **Preserve the evidence and measured costs.** Keep the frozen W0 baseline, W1 comparisons, all W2 benchmark rows, profiles, warnings and unresolved timing observations. Deferral does not establish parity, dismiss regressions or declare every allocation unavoidable. The final effort must attribute costs, make justified optimizations with correctness coverage, and record an explicit performance disposition against the completed feature set.
3. **Continue the existing SQLite adoption process.** Both upstream fixes are merged. Adopt suitable official package versions when they become available through `master` → `v0.10` → the active feature branch, and rerun affected ownership/concurrency/rollback/cleanup evidence. Until then, keep the existing dependency pin and pooling policy. Package availability is a tracked follow-up and does not prevent this full W2 review; W0-F1, corrected-package acceptance and final release gates remain open.
4. **Review W2 for correctness, coverage, scope and evidence accuracy.** The user's chosen reviewer reported the findings below, and the user authorized their fixes. Further optimization work belongs to the final performance effort. Any newly identified correctness defect still needs a normal fix and focused validation.

## Review Corrections And Current Functional Evidence

The [review findings](https://github.com/bazer/DataLinq/pull/229#issuecomment-5869959701) are addressed together in `7b6607b6`:

| Finding | Correction and regression boundary |
| --- | --- |
| Direct completion treated failed initialization as an unused transaction | Synchronous completion validates the lazy resource before changing status. Failed setup rejects commit and rollback while remaining disposable; genuinely unused transactions still complete without opening a connection. |
| Borrowed MySQL/MariaDB commands retained stale transaction bindings | Release the connection/transaction bindings after eager execution or reader disposal, including failure cleanup. Caller-owned commands can cross completed transactions; another live transaction's binding remains rejected before initialization. |
| Async exact-type restrictions rejected synchronous SQLite command subclasses | Keep exact-type validation at the async boundary. Synchronous scalar/nonquery overrides and the reader's virtual dispatch remain effective; async subclass execution still fails before I/O. |
| Retained synchronous SQLite access bypassed root disposal | Check the shared lifecycle guard before opening an owned connection for every synchronous scalar/nonquery/reader overload, after both sync and async root disposal. |

The [SQLite regression tests](../../../../src/DataLinq.Tests.Unit/SQLite/SQLiteNativeReviewRegressionTests.cs) and [server regression tests](../../../../src/DataLinq.Tests.MySql/NativeReviewRegressionTests.cs) first reproduced the failures against the reviewed implementation: **5 SQLite cases and 6 server cases** on MySQL 8.4/MariaDB 11.8. After the fixes, the focused native suites pass **84/84 SQLite** and **114/114 server** cases. Existing transaction-lifetime assertions now inspect the native transaction connection while checking that borrowed commands are detached.

Both full matrices pass **8,558/8,558**, zero failures/skips, on clean `7b6607b6`, with all five suites, all eight SQL targets and individual provider totals. Source/runner identities match and remain unchanged; **ValidForEvidence=true**. The Release builds have zero warnings/errors. Independent TRX inspection confirms all 17 result files and all eight native sync/async telemetry parity cases; `artifacts/w2-review-functional-verification.json` retains the Release run's 69 artifact hashes.

| Suite | Release passing tests |
| --- | ---: |
| Generators | 71 |
| Unit | 3,966 |
| Memory | 221 |
| Compliance: SQLite file / memory | 547 / 404 |
| Compliance: each of six server targets | 410 each |
| Server-specific: MySQL 9.7 / 8.4 | 211 / 134 |
| Server-specific: each of four MariaDB targets | 136 each |

Release run `20260928T131034344Z-723d9e80f6c3410788b23f22019ee0bf` is recorded in `artifacts/w2-review-full-release.json`, SHA-256 `02b5363f228c4677bf3a08e11b02571ebd0359e49e78120895ef8604303ff92c`. Debug run `20260928T130105444Z-0cef8213b2b24e9d94182f02d13c52d1` is preserved in `artifacts/w2-review-full-debug.json`, SHA-256 `182a2fa2f515925d4ef0b0ab6ae438c2163ac31a6bb615d79c8c2e820dffa992`. The first invocation's Release option was consumed by `dotnet run`; the saved evidence exposed the Debug configuration. The separate Release run invokes the compiled Release Testing CLI directly with `run --plan full --configuration Release --batch-size 1`, using `DATALINQ_TEST_DB_HOST=127.0.0.1` in the sandbox. Raw artifacts remain local; this report is tracked.

The existing performance captures describe `76781c89`, before these corrections. They remain preserved historical evidence, not performance measurements for `7b6607b6`; no new performance acceptance is claimed.

## Review Starting Points

- [Implementation order and W2 exit criteria](Implementation%20Order%20and%20Integration%20Plan.md#w2-native-provider-async-execution).
- [Native operation and failure audit](W2%20Native%20Provider%20Audit.md): native bindings, ownership, cancellation, cache publication, transaction certainty, diagnostics and driver limits across SQLite and shared MySQL/MariaDB.
- [Execution record](W2%20Native%20Provider%20Async%20Execution.md): implementation history and original failures, including automatic-concurrency pool-reset/catalog timeouts and the Linux allocation observation. Later passes do not establish the causes of those earlier observations; review their disposition without relabeling them as fixed.
- [Revised candidate checkpoint](W2%20Revised%20Candidate%20Checkpoint.md): historical runtime `76781c890d3486316de00d4c3cffd5dc1e4f6680`, **8,539/8,539** passing tests and the preserved performance captures. The current functional evidence above covers the later review corrections. [CI #627](https://github.com/bazer/DataLinq/actions/runs/36414503108) passed on the earlier documentation checkpoint `56f262f4`.
- [Performance cost record](W2%20Performance%20Cost%20Disposition.md): all six canonical lanes, 90 rows and 126 verified receipts, with 13 allocation warnings, 19 latency warnings and 9 noisy latency rows. Approximate W1-relative increases of 2.8 KB/update and 7.8 KB/CRUD remain recorded for the final performance effort.
- [SQLite ownership investigation](SQLite%20Pool%20Ownership%20Investigation.md) and [separate rollback investigation](SQLite%20Failed%20Rollback%20Pool%20Reuse%20Investigation.md): the first correction targets 10.0.13; rollback PR #39083 merged on 2026-09-26 and issue #39082 targets 12.0-preview1. That milestone does not confirm a 10.0.x backport or inclusion in 10.0.13. DataLinq still pins Microsoft.Data.Sqlite 10.0.11.

## Completion Boundaries

The four reported findings are fixed, verified and confirmed by the reviewer. The user authorized W2 closure and merge-commit integration; [W2 Closeout](W2%20Closeout.md) records completion and the carried-forward work. Official SQLite package adoption and affected verification continue when packages become available. Performance investigation, optimization and acceptance resume after all 0.10 features are implemented, followed by the [frozen-candidate release evidence](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md). W2 integration does not mark 0.10 released or authorize publishing it.
