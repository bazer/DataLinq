# W2 Review Handoff

**Status:** ready for full review, by the user's decision on **2026-09-28**. Implementation and the planned functional/performance captures are complete. The user will arrange another agent to perform the full review; that review has not yet been completed. Review readiness is not a merge, public API freeze or release approval.

**PR:** [#229, Implement W2: native provider async execution](https://github.com/bazer/DataLinq/pull/229), `codex/0.10-w2` → `v0.10`. Preserve the accepted single-PR/merge-commit workflow. Public/generated async APIs remain W3 work.

## Accepted Scheduling Decisions

1. **Defer all further performance work to the end of 0.10, after feature implementation.** Run a focused performance investigation and optimization effort as part of final integration, before freezing the W9 release candidate. Performance acceptance is a final-release obligation, not a remaining W2 review prerequisite. This supersedes earlier W2 requests for another performance pass before review. Existing required CI checks and functional/telemetry correctness assertions remain in place.
2. **Preserve the evidence and measured costs.** Keep the frozen W0 baseline, W1 comparisons, all W2 benchmark rows, profiles, warnings and unresolved timing observations. Deferral does not establish parity, dismiss regressions or declare every allocation unavoidable. The final effort must attribute costs, make justified optimizations with correctness coverage, and record an explicit performance disposition against the completed feature set.
3. **Continue the existing SQLite adoption process.** Both upstream fixes are merged. Adopt suitable official package versions when they become available through `master` → `v0.10` → the active feature branch, and rerun affected ownership/concurrency/rollback/cleanup evidence. Until then, keep the existing dependency pin and pooling policy. Package availability is a tracked follow-up and does not prevent this full W2 review; W0-F1, corrected-package acceptance and final release gates remain open.
4. **Hand W2 to the user's chosen review agent.** The review should assess correctness, coverage, scope and the accuracy of the evidence. Further optimization work belongs to the final performance effort. Any newly identified correctness defect still needs a normal fix and focused validation.

## Review Starting Points

- [Implementation order and W2 exit criteria](Implementation%20Order%20and%20Integration%20Plan.md#w2-native-provider-async-execution).
- [Native operation and failure audit](W2%20Native%20Provider%20Audit.md): native bindings, ownership, cancellation, cache publication, transaction certainty, diagnostics and driver limits across SQLite and shared MySQL/MariaDB.
- [Execution record](W2%20Native%20Provider%20Async%20Execution.md): implementation history and original failures, including automatic-concurrency pool-reset/catalog timeouts and the Linux allocation observation. Later passes do not establish the causes of those earlier observations; review their disposition without relabeling them as fixed.
- [Revised candidate checkpoint](W2%20Revised%20Candidate%20Checkpoint.md): clean product runtime `76781c890d3486316de00d4c3cffd5dc1e4f6680`, **8,539/8,539** passing tests, zero failures/skips, all eight SQL targets, native telemetry parity and matching evidence provenance. Later documentation commits do not change that runtime. [CI #627](https://github.com/bazer/DataLinq/actions/runs/36414503108) passed on documentation checkpoint `56f262f4`.
- [Performance cost record](W2%20Performance%20Cost%20Disposition.md): all six canonical lanes, 90 rows and 126 verified receipts, with 13 allocation warnings, 19 latency warnings and 9 noisy latency rows. Approximate W1-relative increases of 2.8 KB/update and 7.8 KB/CRUD remain recorded for the final performance effort.
- [SQLite ownership investigation](SQLite%20Pool%20Ownership%20Investigation.md) and [separate rollback investigation](SQLite%20Failed%20Rollback%20Pool%20Reuse%20Investigation.md): the first correction targets 10.0.13; rollback PR #39083 merged on 2026-09-26 and issue #39082 targets 12.0-preview1. That milestone does not confirm a 10.0.x backport or inclusion in 10.0.13. DataLinq still pins Microsoft.Data.Sqlite 10.0.11.

## Completion Boundaries

W2 is ready for full review now. Review findings and a subsequent integration decision remain outstanding. Official SQLite package adoption and affected verification continue when packages become available. Performance investigation, optimization and acceptance resume after all 0.10 features are implemented, followed by the [frozen-candidate release evidence](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md). These scheduling decisions do not mark 0.10 released or authorize merging/publishing it.
