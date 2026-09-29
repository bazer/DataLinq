# W3 Closeout

**Closed, 2026-09-29:** the user confirmed that the reviewer is satisfied and explicitly authorized closing W3 and merging [PR #230](https://github.com/bazer/DataLinq/pull/230) into `v0.10`. Integration uses a merge commit to preserve the single-PR implementation and review history.

## Completed Scope

W3 exposes the accepted public and generated async query, lookup, relation, mutation, transaction, raw/fluent, administrative, diagnostic and options families over W1/W2. XML documentation, usage/migration examples, generated consumers, compiled signatures and B01-B15 compatibility evidence are recorded in the [execution record](W3%20Public%20Async%20Surface.md) and [compatibility evidence](W3%20Compatibility%20Evidence.md).

## Review Corrections And Final Verification

The reviewed implementation head is `2988aeace7b9168d8e4b9793aa340d22427a0f76`.

- `3536df46` retains transaction admission and helper draining through buffered relation enumeration and enforces the recovery restrictions reported after standalone completion failure.
- `52ed5394` fixes torn scalar reads by atomically publishing generated getter caches. The Linux regression exposed 3,014 incorrect reads before the fix and none afterward.
- `2988aeac` validates relation source capability/lifecycle before either cancellation token, including warm snapshots. The validated plan transfers to transaction admission without rebinding SQL or recapturing provider policy. Sixteen regression cases cover validation precedence, deferred construction, absence of pre-canceled dispatch and continued transaction usability.

At that implementation head, all **4,971 Release quick-plan tests** passed: 101 generator, 4,079 unit, 223 Memory and 568 SQLite-file compliance cases. Core Release builds passed on .NET 8/9/10 with zero warnings/errors. All **twelve required CI checks** passed in [run 36494201672](https://github.com/bazer/DataLinq/actions/runs/36494201672), including both SQLite targets and the latest MySQL/MariaDB lanes. The final documentation-only closeout receives its own PR checks before integration; PR #230 records the merge receipt.

The earlier 17-shard full matrix, exact packages, unchanged old binaries and consumer checks remain evidence for `db2b71df`, as recorded in the compatibility page. They are not relabeled as executions against the review fixes or the eventual merge commit. Review fixes add no public signatures. The thirteen individually dispositioned raw ApiCompat findings remain visible; wave closure does not turn that raw result into a blanket compatibility pass.

## Carried Forward

- **W4:** DI, hosting and unit-of-work integration are the next wave.
- **W5:** runtime schema validation, including A05-A07 and supporting types/behavior, remains in W5. B16 remains W4/W5 integration evidence.
- **W8/W9:** performance investigation, optimization and acceptance remain deferred until all feature waves are implemented, before candidate freeze and final release evidence.
- **SQLite:** official corrected-package adoption, affected reruns and W0-F1/release acceptance remain separate obligations under the existing policy.

No W3 implementation or review finding remains open. This closes the development wave; it neither releases 0.10 nor authorizes package publication.
