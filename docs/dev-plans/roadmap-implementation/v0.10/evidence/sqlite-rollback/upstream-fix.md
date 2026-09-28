# SQLite Unsettled Transaction Pool Fix

**Status, verified 2026-09-28:** [PR #39083](https://github.com/dotnet/efcore/pull/39083) merged into `main` on **2026-09-26 at 01:44:30 UTC**, by `AndriySvyryd`, as [`7b0248934f14bbb2159310b224f669c76eb07946`](https://github.com/dotnet/efcore/commit/7b0248934f14bbb2159310b224f669c76eb07946). [Issue #39082](https://github.com/dotnet/efcore/issues/39082) is closed as completed with milestone [12.0-preview1](https://github.com/dotnet/efcore/milestone/238). A 10.0.x backport or inclusion in 10.0.13 is not established by this merge. The issue and PR were submitted on 2026-09-24. Submitted commit [`f5884a502eb82288ee86fe6919fa666bb1d08350`](https://github.com/bazer/efcore/commit/f5884a502eb82288ee86fe6919fa666bb1d08350) contains the tested patch unchanged, based on `dotnet/efcore` main at [`cca217cc2d529b8083e804f7334db552f744f536`](https://github.com/dotnet/efcore/commit/cca217cc2d529b8083e804f7334db552f744f536). DataLinq's production dependencies and pooling policy are unchanged.

- [Patch](upstream-fix.patch): two upstream files, six production lines and 151 test lines.
- [PR title and description](upstream-pr.md).
- [Submitted issue](upstream-issue.md) and [published-package investigation](../../SQLite%20Failed%20Rollback%20Pool%20Reuse%20Investigation.md).

## Change

`SqliteConnectionInternal.Deactivate` already resets connection-specific state and excludes closed/invalid handles from pooling. After resetting a valid handle, the patch checks `sqlite3_get_autocommit`. If it is zero, the connection still has a native transaction and is marked non-poolable. The existing pool-return path then disposes that physical connection instead of lending it to another caller.

```csharp
// A failed rollback can detach the managed transaction without ending the native transaction.
if (sqlite3_get_autocommit(_db) == 0)
{
    _canBePooled = false;
}
```

The managed transaction can be detached by `RollbackInternal`'s finally/Complete path even though native rollback failed. Pool eligibility therefore checks the actual engine state at return. A connection that the caller successfully rolls back before closing remains reusable. Other healthy connections in the same pool remain reusable too.

There is no new public API, exception replacement, rollback retry, pool-wide clearing, lock or background work. The existing invalid-handle guard protects the native call. The added operation is one native autocommit check for a valid handle on pool return; no performance benchmark is claimed. Disposal of the excluded handle provides SQLite's physical-close cleanup.

The same invariant also covers a transaction started through raw SQL without a managed transaction wrapper. The correction deliberately prevents uncommitted data and locks from surviving across those pooled checkouts. It does not change the failure or completion semantics seen by the caller before connection close, or attempt to make an unsettled connection usable while it is still checked out.

## Tests And Negative Control

The patch adds ten cases to upstream's existing `SqliteConnectionFactoryTest` xUnit suite. No test framework is added to DataLinq.

- Eight combinations of sync/async, explicit rollback/transaction disposal, and permitted/rejected native rollback. The authorizer rejects only `SQLITE_TRANSACTION` / `ROLLBACK`; no provider method manufactures an error or successful outcome.
- These cases assert error 23 on rejection, detached managed transaction, native autocommit before close, one rollback attempt, physical-handle closure/reuse, committed row visibility and successful use of a fresh transaction. A second healthy connection proves that the fix does not clear the whole pool.
- One case successfully completes a previously rejected rollback before close and verifies that pooling still reuses that now-safe handle.
- One case leaves a raw SQL transaction open and verifies that it cannot cross the pool boundary.

| Verification | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Ten new cases, original upstream production code | 5 | **5** | 0 |
| Same ten cases, patched production code | **10** | 0 | 0 |
| Full upstream Microsoft.Data.Sqlite.Tests application, patched | **724** | 0 | **7** |

The five negative-control failures are exactly the four rejected managed-rollback cases and the unmanaged transaction case. The test DLL has the **same SHA-256 before and after the production change**. The corrected driver is the only changed binary in that comparison. Both successful builds report zero warnings/errors.

The full test run uses upstream's standard `category=failing` exclusion. Six skips reference [dotnet/efcore#35585](https://github.com/dotnet/efcore/issues/35585); one references [SQLitePCL.raw#421](https://github.com/ericsink/SQLitePCL.raw/issues/421). They are retained as skips. This is the bundled `e_sqlite3` application on Windows x64, not the entire EF Core or alternate-native-library matrix.

The first full run inside the sandbox passed 723, skipped seven and failed one existing test because `Path.GetTempFileName` was denied access. The identical binaries passed all 724 runnable tests outside the sandbox. The original failure and rerun are both retained. Initial SDK-download failures and the first test-build overload-ambiguity correction are also preserved in local logs; they are not product failures or passing test receipts.

## Original Standalone Reproduction

The unchanged [published-driver probe](Program.cs) was additionally run against isolated original and patched upstream source-build DLLs. It executes all eight file/memory, permit/deny, sync/async disposal combinations under .NET 10.0.12.

| File database, denied rollback, both disposal forms | Original upstream | Patched upstream |
| --- | --- | --- |
| Original disposal error | SQLite 23 | SQLite 23 |
| Same handle on next checkout | Yes | **No** |
| Engine autocommit on next checkout | 0 | **1** |
| Rows visible on next checkout | 2 | **1** |
| New transaction | Fails | **Succeeds** |
| Original rollback attempts | 1 | **1** |

All six controls preserve their expected behavior: file/permit reuses the handle; memory modes physically close it. Every fixed observation sees only committed data and can begin a transaction. These are explicitly local diagnostic source builds, not official Microsoft packages or a DataLinq dependency replacement.

## Reproduction And Review

The local checkout is `artifacts/investigations/sqlite-rollback-20260924/efcore`, now clean on submitted branch `codex/sqlite-unsettled-transactions`. Before committing, the exported patch passed both `git apply --check --cached` against the base index and `git apply --reverse --check` against the tested working tree. The submission commit changes exactly those two files; the source and all 23 recorded validation receipts still match the tested material.

In a separate EF Core checkout at the base commit:

```powershell
git switch -c codex/sqlite-unsettled-transactions cca217cc2d529b8083e804f7334db552f744f536
git apply --check <path-to-upstream-fix.patch>
git apply <path-to-upstream-fix.patch>
# Use the upstream pinned SDK and restore tooling.
.\eng\common\build.ps1 -restore -build -projects test/Microsoft.Data.Sqlite.Tests/Microsoft.Data.Sqlite.Tests.csproj -configuration Debug
. .\activate.ps1
dotnet exec artifacts/bin/Microsoft.Data.Sqlite.Tests/Debug/net11.0/Microsoft.Data.Sqlite.Tests.dll --filter-method '*native_transaction*' --minimum-expected-tests 10 --output Minimal
dotnet exec artifacts/bin/Microsoft.Data.Sqlite.Tests/Debug/net11.0/Microsoft.Data.Sqlite.Tests.dll --filter-not-trait category=failing --minimum-expected-tests 700 --output Minimal
```

The pinned SDK is `11.0.100-rc.1.26425.128`; the driver targets net10.0, upstream tests net11.0, and SQLitePCLRaw is 3.0.5. Upstream's own restore/build script installed its required toolset. Local verification used DataLinq's sandbox environment wrapper after upstream activation, escalating only the blocked build/full-test commands.

The upstream fix is accepted and merged. Both public submissions retain the disclosure: "The issue was found and the fix, regression tests, and PR were developed with GPT-6 Astra on Extra High, with the repository owner's authorization." Their descriptions omit local sandbox details as requested; the internal validation history remains available. The 2026-09-24 [upstream run](https://github.com/dotnet/efcore/actions/runs/35986941690) passed the Microsoft.Data.Sqlite and EF Core SQLite jobs on Windows and Linux. Its Windows Cosmos job reported 7,054 passing tests, 312 skips and zero failed assertions, then failed because foreground threads remained alive at process shutdown. That original failure remains part of the record; merger acceptance does not convert it into a passing receipt. The existing ownership correction is separate, and the merge does not establish that either fix is present in a released 10.0.x package or close DataLinq's official-package adoption and verification gate.

## Evidence

Raw local evidence is under `artifacts/investigations/sqlite-rollback-20260924/`. The pre-submission `verify-results.ps1` checks individual TRX results, all sixteen before/after native observations, error preservation, driver identities and identical test DLLs; its original checkout-HEAD precondition refers to the tested base before the fix was committed. Its frozen `verification.json` records 23 file receipts and has SHA-256 `72ece6886c0b0384dad81336ed4108f5b5082c41262621b4213e0aa3078827d3`. The later `submission.json` identifies the published issue, PR and commit and confirms that the recorded files are unchanged.

| Binary | SHA-256 |
| --- | --- |
| Original upstream Microsoft.Data.Sqlite.dll | `610ae7c2e700b69c74a241cf3bbad822e1bfa2b9335bc90844fa81b9f559fadb` |
| Patched upstream Microsoft.Data.Sqlite.dll | `78a64053b762fc2d9794fdc0084241c0f6a666f6a138162cdfa32396475c0beb` |
| Identical original/patched test DLL | `4e126ed5e7b043c866b8027080c8e4d93793c1637dadf39ae734d559a43aff4a` |

The patch and reports are kept with the tracked investigation material. Raw logs, binaries and TRX files remain local; no external evidence upload is claimed.
