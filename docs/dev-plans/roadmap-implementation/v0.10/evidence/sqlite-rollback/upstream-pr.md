# Prevent pooling SQLite connections with unsettled native transactions

**Submitted:** [dotnet/efcore#39083](https://github.com/dotnet/efcore/pull/39083), 2026-09-24. Commit `f5884a502eb82288ee86fe6919fa666bb1d08350` on `bazer/efcore:codex/sqlite-unsettled-transactions`, targeting `dotnet/efcore:main`.

**Merged, verified 2026-09-28:** `AndriySvyryd` merged the PR into `main` on **2026-09-26 at 01:44:30 UTC**, as [`7b0248934f14bbb2159310b224f669c76eb07946`](https://github.com/dotnet/efcore/commit/7b0248934f14bbb2159310b224f669c76eb07946). Issue #39082 is closed as completed with milestone [12.0-preview1](https://github.com/dotnet/efcore/milestone/238); this is not confirmation of a 10.0.x backport. Official-package adoption and affected verification in DataLinq remain open.

The original submitted body follows, including its submission-time checklist and validation limits.

Fixes #39082

When native rollback fails, `SqliteTransaction.RollbackInternal` still completes and detaches its managed transaction wrapper. Closing the connection can then return a handle with an active transaction to the pool. The next borrower can see uncommitted rows and cannot start a new transaction.

Check `sqlite3_get_autocommit` during deactivation, after resetting connection-specific state and inside the existing valid-handle guard. Mark an unsettled handle non-poolable so the existing return path disposes it. This preserves the rollback exception and affects only that physical connection. A connection whose native transaction has successfully ended remains reusable, including one explicitly recovered by the caller after an earlier rollback error.

Add ten regression/control cases covering sync/async rollback and disposal, rejected and successful native rollback, healthy connections in the same pool, caller recovery before close, and raw SQL transactions. Tests use SQLite's authorizer to reject ROLLBACK before execution; the failure is raised by the native engine.

Validation on Windows x64 with the pinned upstream SDK:

- Original production code: five targeted failures and five passing controls.
- Patched code: all ten targeted cases pass, with the identical test DLL.
- Full Microsoft.Data.Sqlite.Tests application: 724 passed, seven existing skips, zero failures, using the standard `category=failing` exclusion.
- Unchanged standalone eight-case reproduction: failed file rollbacks preserve error 23; the next checkout uses a new handle, sees only committed data and can start a transaction. Successful rollback still permits pooling; memory controls are unchanged.

Builds have zero warnings/errors. Alternate native SQLite bundles and upstream CI have not been run. There is no public API change, rollback retry or pool-wide clearing; pool return adds one native autocommit check.

### AI disclosure

The issue was found and the fix, regression tests, and PR were developed with GPT-6 Astra on Extra High, with the repository owner's authorization.

### Checklist

- [ ] Contribution guidelines/walkthrough requirement confirmed by the submitter.
- [x] Issue filed and implementation described in #39082.
- [ ] Maintainer approval obtained.
- [x] Code builds and tests pass locally as detailed above.
- [ ] Validation confirmed by upstream CI.
- [x] Commit message includes the filed issue reference.
- [x] Regression tests added and demonstrated to fail before the production change.
- [x] Code follows the existing provider/pool and test patterns.
