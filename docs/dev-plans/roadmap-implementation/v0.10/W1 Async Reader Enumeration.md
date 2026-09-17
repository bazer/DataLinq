> [!WARNING]
> Internal 0.10 groundwork verified with controllable readers. No native provider or public async query API is wired to this enumerator yet. W1 and W0-F1 remain open.

# W1 Async Reader Enumeration

**Recorded:** 2026-09-17. Follows the synchronous [query and relation ownership integration](W1%20Query%20and%20Relation%20Ownership.md), merged in [PR #152](https://github.com/bazer/DataLinq/pull/152), under the accepted [limited W1 exception](SQLite%20Pool%20Ownership%20Investigation.md#accepted-limited-w1-exception).

This implements the internal single-reader portion of AAPI-18 through AAPI-21 and AAPI-34 through AAPI-37. It uses the existing async reader/source contracts and the same transaction admission gate as synchronous managed execution. It is a building block for later query, relation, materializer and provider integration, not an alternative public query protocol.

## Capture, Acquisition And Ownership

`AsyncReaderEnumerable<T>` invokes an I/O-free capture function once per `GetAsyncEnumerator`. That function supplies the source for this invocation. Sequence construction, enumerator construction and disposal of an unused enumerator do not open a reader or acquire transaction admission. Repeated enumeration captures again and does not permanently cache rows. The capture hook does not itself implement the full query-builder, prepared-query or mutation snapshot policies.

The first move validates transaction lifecycle and the source's reader capability, checks cancellation, acquires transaction ownership and then awaits reader acquisition. The new internal `ValidateReader`/source `Validate` entry points permit I/O-free capability checks before admission; dispatch still validates again. Borrowed commands keep their identity and must remain stable for the operation and reader lifetime.

Once acquired, the enumerator owns the reader through every move, current-row conversion, time between rows and asynchronous cleanup. The original transaction is retained and never migrated to a committed source. `TransactionReadScope` keeps its explicit private step across suspension; no ambient permission, thread affinity or lock across provider/application work is introduced. The enumerator neither commits nor disposes the caller's transaction or borrowed command. Failed acquisition remains responsible for cleaning up resources before reader transfer; owned generated-command construction is still future work.

Each move, current-value access and disposal first enters the per-enumerator call guard. Overlapping or reentrant calls fail before altering position or resources. The same transaction also rejects synchronous reads, commit, rollback and disposal while the async enumerator owns it. Independent root enumerators acquire no transaction gate.

## Cancellation And Cleanup

Default, method-only, enumerator-only and equal-token cases avoid a linked source. Distinct cancellable tokens are linked for this enumeration, and either can cancel it. Owned linking is disposed on every terminal path, including unused disposal. Cancellation is observed during acquisition, advancement and buffered iteration, before and after current-row conversion. Conversion remains synchronous local work; arbitrary user code is not forcibly interrupted.

Acquisition success transfers the reader before checking a newly requested cancellation, ensuring the reader is still disposed. Cleanup is awaited without the enumeration token. Cancellation never becomes a hard deadline: an uncooperative pending provider call is awaited to actual completion before its resources can be cleaned up or admission released. No task abandonment, automatic retry, synchronous fallback, `Task.Run` or artificial scheduling yield is added.

Exhaustion, early `await foreach` exit, cancellation, acquisition/read/conversion failure and explicit disposal all finish owned cleanup. A cleanup-only failure is thrown once; repeated disposal does not replay it. When DataLinq owns both the failed move and cleanup, the original primary exception is rethrown and an immutable internal `AsyncEnumerationFailure` record retains the original cleanup exception separately. This narrowly proves precedence and identity; it does **not** deliver the public failure-context accessor, provider classification or a connection-trust verdict. An application exception from an `await foreach` body can still be replaced by a throwing disposal under ordinary C# scope semantics.

## Development Evidence

The new tests use controllable command/reader doubles, explicit entry signals and bounded test waits. They do not contact a native database. Transaction cases combine those doubles with the existing scripted transaction fixture to verify shared sync/async admission. Production code has no timeout-based abandonment.

The 32 new cases cover cold and repeated capture, unused disposal, token combinations and unlinking, cancellation at acquisition/advancement/buffered boundaries, late acquisition success, early exit, empty results, exception identity and cleanup precedence, root concurrency, reentrancy, lifecycle validation and shared transaction admission through paused acquisition/read/cleanup. An explicitly uncooperative acquisition proves that cancellation cannot abandon work or release ownership early.

Local Release / .NET 10 verification:

- `artifacts/w1-async-enumerator-focused.json`: **22/22 passed**, standalone enumerator tests.
- `artifacts/w1-async-enumerator-transactions.json`: **140/140 passed**, transaction-focused tests including ten new async cases.
- `artifacts/w1-async-enumerator-unit.json`: **1,951/1,951 passed**, full unit suite.
- `artifacts/w1-async-enumerator-build.log`: unit project build, zero warnings/errors.
- `artifacts/w1-async-enumerator-core-build.log`: core builds on .NET 8/9/10, zero warnings/errors.
- All 70 local links in the changed planning pages resolve; `git diff --check` is clean. These pages are excluded from DocFX.

These are modified-checkout development checks, not frozen release evidence; PR CI is recorded separately against its head commit. The existing full suite includes provider-backed fixtures, but the new cases use only controllable/scripted doubles. No local native async acceptance is claimed.

## Remaining Gates And Next Slice

The subsequent [read failure/recovery slice](W1%20Read%20Failure%20and%20Recovery.md) adds explicit evidence requirements for reuse, ordered secondary failures and immutable completion/recovery snapshots. Independent automatic rollback budgets and native classification/confirmation remain open. Gate release alone is still not proof that a native connection can continue after an interrupted operation or failed cleanup.

Owned string-command construction/disposal, native initialization, async private hydration, helper callback admission/draining, full ordinary/prepared/mutation capture, relation coordination/publication and routing all inventoried I/O families remain open. Public diagnostic access and async declarations still require W3 consumer evidence. Native adapters and actual provider interruption/trust evidence remain W2 work.

Coordination and allocation costs are unmeasured. Provider packages, pooling configuration and existing synchronous dispatch are unchanged by this slice. The 0.9.2 compatibility baseline and .NET 10 benchmark target remain fixed. W0-F1 still requires verified corrected official-package adoption and affected evidence; no SQLite acceptance, API freeze, release approval or package publication is implied.
