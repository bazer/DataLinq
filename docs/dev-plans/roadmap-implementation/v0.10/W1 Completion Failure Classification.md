> [!WARNING]
> Internal 0.10 completion diagnostics. W1 is not complete. Native provider classification, public diagnostic declarations and performance acceptance remain separate gates.

# W1 Completion Failure Classification

**Recorded:** 2026-09-21, after [synchronous transaction telemetry](W1%20Synchronous%20Transaction%20Telemetry.md). This implements further internal evidence for [AAPI-92 through AAPI-95](Async%20Public%20API%20Decisions.md#aapi-92-failure-classification-uses-independent-public-enums). The [completion audit](W1%20Completion%20Audit.md) retains all wider requirements.

## Known causes and separate stages

Async completion previously classified some known cache-finalization failures as Unknown. Notification errors shared the broader Finalization stage, and cache recovery was flattened into generic local finalization or resource cleanup. The initial paired synchronous/asynchronous cases reproduced nine failures out of ten.

[Completion diagnostics](../../../../src/DataLinq/Mutation/Transaction.CompletionDiagnostics.cs) now capture local failures as immutable owned observations. Known local finalization uses LocalFinalizationError. Internal Notification and CacheRecovery stages distinguish reporting callbacks from cache recovery; genuine mutable/cache publication still uses Finalization. Native errors without sufficient evidence remain Unknown. This is classification by the executing boundary, not a blanket mapping from exception types.

The [shared activity helper](../../../../src/DataLinq/Execution/ExecutionActivity.cs) preserves a current, more specific nested failure instead of overwriting its cause and stage with a notification fallback. Plain status, activity and meter failures use Notification. Async scalar/reader/mutation startup also uses that stage; physical command counts, activity lifetimes and dispatch semantics do not change. Existing reporting assertions were updated to require the accepted distinction.

The [synchronous](../../../../src/DataLinq/Mutation/Transaction.cs) and [asynchronous](../../../../src/DataLinq/Mutation/Transaction.AsyncCompletion.cs) completion boundaries retain original primary exceptions, ordered secondaries, actual transaction identity and native completion certainty. TransactionCommitFinalizationException retains its original InnerException and CleanupFailures and receives the same structured facts at construction. A confirmed commit is never downgraded by recovery or reporting errors.

## Capture at the cache boundary

Capturing only after a batch of cache callbacks returned was insufficient. Four new cases proved that a later notification could replace an earlier exception object's direct diagnostic lookup and incorrectly change its reported cause and stage.

[DatabaseCache](../../../../src/DataLinq/Cache/DatabaseCache.cs) now supplies an internal, optional failure observer for best-effort transaction removal, recovery clearing and notification discard. The managed completion owner captures each failure at its catch, under a separate diagnostic scope, before another cache callback runs. The legacy exception lists and public signatures remain intact. Callers without that private observer do not acquire the additional observation scopes. No admission authority is stored in diagnostic context or conferred on callbacks.

Cache recovery failure remains a cleanup safety fact even when its exception is deduplicated against the primary, or a more specific nested admission failure keeps Validation/InvalidOperation attribution. After uncertain commit and failed cache recovery, both synchronous and asynchronous paths advertise disposal only. Disposal advances the transaction's recovery snapshot without rewriting an earlier exception context.

The callback tests separately prove that ordinary notification failure uses Notification, while a callback's rejected nested Commit/Rollback keeps the attempted operation, Validation stage, InvalidOperation cause and active operation. The outer owner still supplies confirmed completion and valid terminal recovery.

## Verification

[22 new TUnit cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.CompletionDiagnostics.cs) cover:

| Cases | Boundary |
| ---: | --- |
| 4 | Confirmed commit with cache-publication failure, with/without recovery failure, sync/async |
| 4 | Plain commit/rollback status notifications, sync/async |
| 2 | Uncertain native commit retains its primary and local cache-recovery secondary |
| 4 | Specific nested admission failure escaping a completion notification |
| 2 | Reused native/cache exception retains cleanup restriction without duplication |
| 4 | Earlier cache occurrence survives a later callback replacing its direct lookup, confirmed/uncertain sync/async |
| 2 | Cache recovery preserves a nested rollback-admission failure and independent cleanup safety fact |

The initial **1/10** capture became **10/10**, then **16/16**. The first full unit run was **3,451/3,457**: six async query/mutation telemetry-start paths still used Finalization. Their runtime stages were corrected, after which all **298/298** reporting cases and **3,457/3,457** unit cases passed.

The cache-occurrence expansion reproduced **2/6**, exposing four delayed-capture errors. Capturing at the actual cache boundary fixes those errors: all **22/22** focused and **3,463/3,463** unit cases pass on the final runtime, including the 298 reporting cases. Memory passes **221/221**, and SQLite file/memory each pass **528/528**: **4,740** broad local passes, no skips. Core, SQLite and MySQL Release builds cover .NET 8/9/10; unit, Memory and compliance builds also have zero warnings/errors.

All listed reports retain complete invocation/count/artifact information. Passing captures exit zero; failed captures exit two. ValidForEvidence=false denotes bounded local verification, not release qualification. Reports remain unmodified under local artifacts, unuploaded. No source edits or benchmark runs overlapped local builds/tests. Exact-head Latest CI and the expected-head merge are recorded in the PR.

| Report filename | Passed / total | SHA-256 |
| --- | ---: | --- |
| `w1-completion-diagnostics-negative.json` | 1 / 10 | `34b33c8d2b6d6f63f4361f5a3f31a42ca16a3dca416207f7d8b4c17bbb7217b8` |
| `w1-completion-diagnostics-first.json` | 10 / 10 | `a79250bf301ee82837d213df8e82dee15c0e0199c749dc7a012c2c4bfc4024b9` |
| `w1-completion-diagnostics-expanded.json` | 16 / 16 | `0995112c286e4b94f2ebc8f887fbb7335fa4be386c5a8d73d3bc347cbbd8728e` |
| `w1-completion-diagnostics-unit-first.json` | 3451 / 3457 | `e5c2bf6246b8ecd97ea55a8cde9de72915939dec9af48aaca1fbd1280e20da90` |
| `w1-completion-diagnostics-reporting.json` | 298 / 298 | `8bb1ffef9ad4820e8ef14ee43e4880095a240976ed2905db3ebc6efbe981a614` |
| `w1-completion-diagnostics-unit-verified.json` | 3457 / 3457 | `6b15d48aa1ba45e3bf257fb68851dcf382343164c1c749bb3f1acbc7deb289ab` |
| `w1-completion-diagnostics-occurrences-negative.json` | 2 / 6 | `f16771780a058c410b6a8d8fe21e6a233cf29b3e5bdf53c95af76936f1c28393` |
| `w1-completion-diagnostics-captured.json` | 22 / 22 | `24c797183026544f46de4b8f5c3144ea0cf57e4f1ce2e67271aada6b3fc7f32a` |
| `w1-completion-diagnostics-unit-captured.json` | 3463 / 3463 | `e53d572d5ad1457cc23dc3e7b536fc05df6c9f2f09431cbb5ac3f7a20105cee4` |
| `w1-completion-diagnostics-memory.json` | 221 / 221 | `198c33fbefb748754f63debccbd14dfd4884f89b4a5fbdee0f1b2a784310f671` |
| `w1-completion-diagnostics-sqlite-file.json` | 528 / 528 | `b1de17a16f5620440bb6cb5dafff379a8157c0e36a84e471ab62df2d168d1d31` |
| `w1-completion-diagnostics-sqlite-memory.json` | 528 / 528 | `f6d00f291fdc72e01cf4deb4d98e3ff829e02005e51405f115eb2c45a969d0c0` |

## Remaining requirements and cost

These are internal stages, not new public declarations or a numeric serialization contract. The public inventory and consumer mapping remain W3; native provider evidence remains W2. Higher local/preflight diagnostics, administrative/standalone-provider attribution, complete cause/stage mapping across the I/O inventory and internal adapter readiness still require the final W1 audit.

Failure snapshots are created only when these local failures occur, but private recovery observation scopes/delegates also have successful-path costs. No per-mutable observation scopes were added to the concrete local invalidation loop. Neither fact substitutes for measurement. Attributable coordination-cost analysis, reductions or explanations and all six strict .NET 10 W0 comparison lanes remain required. W0-F1 and the 0.9.2 compatibility baseline are unchanged. No packages are published.
