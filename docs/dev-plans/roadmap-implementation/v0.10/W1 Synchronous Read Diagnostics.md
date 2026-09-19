> [!WARNING]
> Internal W1 read-resource reporting, not complete synchronous diagnostics or W1 acceptance. Remaining boundary classification, telemetry, adapter readiness and measured coordination costs stay open. Native W2, public W3 and SQLite W0-F1 remain separate gates.

# W1 Synchronous Read Diagnostics

**Date:** 2026-09-19. Continues [scoped attribution](W1%20Scoped%20Failure%20Attribution.md) and the earlier [eager read reporting](W1%20Eager%20Read%20Failure%20Reporting.md) under the [completion audit](W1%20Completion%20Audit.md).

## Reproduced gaps

An initial 21-case negative control failed 20 cases on the original implementation: eager calls could import old timeout/cleanup/operation facts from a reused exception, current read identity was incomplete, and matching request cancellation was unclassified. Fresh evidence published during a call must still survive; an earlier attachment on the same exception is not current evidence.

A second, separate two-case regression was reproduced during the work. `Select.ReadRows` converted an ephemeral reader inside a nested `foreach`; if conversion and disposal both failed, disposal replaced the conversion exception before the outer reporting boundary saw it. Root and transaction cases both failed before conversion was moved inside explicit resource ownership. The direct scalar relation-row path also used independent `using` declarations without preserving earlier read/cleanup errors; it now uses the shared failure-preserving owner.

## Implemented behavior

[ReadCommandResources](../../../../src/DataLinq/Execution/ReadCommandResources.cs) carries captured operation/provider identity and the request token. Single/batch key loaders, index loaders, first-row/cache/scalar queries and generated reader/row enumeration supply that identity. Managed private dispatch retains its explicit owner's kind, including an explicitly unknown kind. Matching requested-token cancellation is recognized; foreign-token or unrequested cancellation exceptions remain unclassified. No native timeout/provider cause is guessed.

Eager entry points establish an invocation scope. [DataSourceAccess.ReadSequence](../../../../src/DataLinq/Mutation/DataSourceAccess.cs) now guards database-root and private iterator calls as well as managed outer calls, while only the actual outer transaction owner obtains/registers a lease. Each diagnostic scope ends before returning a row to application code. Current evidence from a later move is observed in that move, not filtered through a marker captured when the reader was constructed.

Reader and command cleanup each get independent diagnostic provenance. The collector remains lazy, preserves original exceptions and encounter order, and deduplicates repeated exception objects without losing the fact that cleanup failed. A cleanup-only failure remains a disposal operation. Root reads report nontransactional completion/recovery; managed reads retain conservative snapshots without introducing a new synchronous transaction trust policy.

[Select.ReadRows](../../../../src/DataLinq/Query/Select.cs) now owns its command/reader while decoding each row, records conversion failures before cleanup and retains both independent cleanup failures. The [direct relation-row path](../../../../src/DataLinq/Cache/TableCache.RowLoading.cs) does the same for string/provider-sensitive scalar relation keys that do not use neutral canonical index loading. Dispatch stays synchronous and uses the existing owner-aware hooks; no public API or provider dependency changes.

## Verification

[29 new TUnit cases](../../../../src/DataLinq.Tests.Unit/Core/TransactionMutationFailureTests.SyncReadDiagnostics.cs) cover root/managed reads, single/batch/index/first/scalar/cache routes, stale and fresh reports, successive invocations, matching/foreign/unrequested cancellation, repeated cleanup exceptions, string-relation read and cleanup-only failures, conversion with both cleanup failures, and scope restoration/later-move evidence across iterator yields.

The original negative report is `artifacts/w1-sync-read-diagnostics-negative.json` (1 passed, 20 failed). The conversion negative report is `artifacts/w1-sync-read-diagnostics-conversion-negative.json` (27 passed, the two new conversion cases failed). Intermediate output is retained: a relation fixture initially supplied NULL for a required column and was corrected to match its scripted row; the first broad unit run caught an attempted fallback for an explicitly unknown owner kind, and that inference was removed. The existing authoritative-owner test is unchanged.

Final local Release results, under `artifacts/w1-sync-read-diagnostics-`: `focused-owner.json` **29/29**, `unit-final.json` **3,143/3,143**, `memory.json` **221/221**, and `sqlite-file.json` / `sqlite-memory.json` **528/528 each**. The broad total is **4,420 passed, zero skipped**; the focused cases are included in the unit total. All five final reports are complete, with complete artifacts and exit zero. Their `ValidForEvidence=false` development scope does not replace the release matrix. Core .NET 8/9/10 and unit/Memory/compliance Release builds passed with zero warnings/errors. Planning links and whitespace are checked before PR creation; these pages are excluded from DocFX.

## Remaining work and cost

The [initial performance checkpoint](W1%20Initial%20Performance%20Checkpoint.md) predates this change. These additional scopes and iterator wrappers add successful-path work; the value-type resource owner and lazy failure collector do not make the complete path allocation-free. Profile and reduce/explain coordination costs, then recapture the complete W0 comparison at final integration.

Complete the higher synchronous orchestration audit: cache/model construction and projection outside this resource owner, relation wrappers, local validation/cancellation before or after resource execution, requested mutation/completion kinds, standalone raw-provider identity, and telemetry-listener failure composition. This slice establishes reporting at the listed read-resource boundaries; it does not claim every outer operation is now correlated or classified. Internal adapter compatibility readiness, telemetry and final requirement/code/test/I/O mapping remain W1 requirements.
