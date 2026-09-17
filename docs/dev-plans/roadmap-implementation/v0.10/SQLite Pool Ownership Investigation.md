> [!WARNING]
> This is diagnostic evidence for W0-F1. The correction is tested locally in Microsoft.Data.Sqlite, but DataLinq still references the unchanged published driver. W0-F1 is not closed. The accepted limited exception below permits W1 internal contracts and controllable-provider tests; SQLite integration and release approval remain blocked.

# SQLite Pool Ownership Investigation

**Recorded:** 2026-09-17.

**Finding:** Microsoft.Data.Sqlite can lend one pooled internal connection to two distinct, still-open outer connections. Starting independent transactions then produces SQLite error 1, `cannot start a transaction within a transaction`, matching the W0 failure.

**Disposition:** submitted [upstream issue #39008](https://github.com/dotnet/efcore/issues/39008) and [fix PR #39009](https://github.com/dotnet/efcore/pull/39009) on 2026-09-17 with the user's authorization, including a request to consider 10.0 servicing. The user has posted CLA acceptance; upstream acceptance, a corrected official package and DataLinq adoption remain pending. No dependency replacement, lock, retry, or pooling configuration change is included here.

## Accepted Limited W1 Exception

**Accepted by the user, 2026-09-17:** continue the 0.10 implementation without waiting for EF Core's merge or package schedule. This explicitly replaces the earlier blanket stop on all W1 implementation; it does not waive W0-F1 or mark W0 fully closed.

- Permit internal async provider/access/source contracts, deterministic controllable-provider tests, and W1 operation ownership, cancellation/recovery and invocation-capture work on feature PRs targeting `v0.10`.
- Keep these changes isolated from production provider wiring and public async support claims. Retain direct synchronous execution; do not introduce sync-over-async, `Task.Run` provider substitutes, or unsupported synchronous fallbacks.
- Keep SQLite integration acceptance, W0-F1 closeout, final provider feasibility/public API freeze and release approval blocked until a corrected official dependency is adopted and the affected evidence is rerun. Passing unrelated CI or controllable tests does not satisfy those gates.
- Preserve the original frozen baseline and failed runs. New W1 results identify their actual commit and scope; a test double is orchestration evidence, not native-driver evidence. The original 0.9.2 compatibility baseline and .NET 10 performance policy are unchanged.
- No temporary pooling policy, retry workaround, unofficial driver distribution or package publication is authorized by this exception. A corrected dependency still lands on `master` and merges forward.

The exception allows scoped W1 progress only. It does not automatically remove other wave dependencies or approve unrelated provider/runtime work.

## Root Cause And Correction

The driver marks a connection active before publishing its weak reference to the owning `SqliteConnection`:

```csharp
_active = true;
_outerConnection.SetTarget(outerConnection);
```

`_active` is volatile. `Leaked` checks whether it is active and has no live owner. Activation occurs after the pool checkout lock has been released. Between these writes, another checkout can therefore mistake an activation in progress for a leaked connection and return the same internal connection to another owner.

Publish the owner first, then publish the volatile active flag:

```csharp
_outerConnection.SetTarget(outerConnection);
_active = true;
```

This prevents leak reclamation from observing an active connection before its owner has been published. It preserves pooling and adds no locks, retries or public API changes. The production patch is this ordering change plus an explanatory comment; the accompanying upstream regression test keeps every owner open while checking handle uniqueness.

Source references: [v10.0.11 activation and leak predicate](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteConnectionInternal.cs), [checkout and activation](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteConnectionFactory.cs), and [pool reclamation](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteConnectionPool.cs). The same activation ordering was inspected in v10.0.12 and in the pinned upstream main commit below; published-package reproduction in this investigation is specifically against 10.0.11.

## Evidence And Limits

| Check | Observed result | Scope |
| --- | --- | --- |
| Published Microsoft.Data.Sqlite 10.0.11, public APIs only | Wave 312: two open owners share a native handle; independent deferred serializable transactions reproduce the exact error | No DataLinq, EF Core runtime, reflection, forced GC or concurrent pool clearing |
| Controlled interleaving, original v10.0.11 source | Duplicate ownership and the same transaction error | Isolated unsigned driver build with a diagnostic scheduling callback |
| Same controlled interleaving, corrected source | Distinct owners; both transaction begins succeed | Same diagnostic callback; not included in the proposed production patch |
| Uninstrumented original source stress | Duplicate ownership on wave 351 | 16 concurrent opens per wave; owners remain open until inspection |
| Uninstrumented corrected source stress | 50,000 waves / **800,000 opens**, no duplicates or other errors | Correctness stress, not a performance benchmark or proof against every possible race |
| New upstream regression test, original main source | Fails on duplicate ownership | Negative control changes only the production source back to the upstream base |
| New upstream regression test, corrected main source | Passes | Actual upstream test project |
| Corrected upstream SQLite test application | **714 passed, 7 skipped, 0 failed** | 721 total; normal upstream `category=failing` exclusion; not the full EF Core matrix |
| Corrected upstream project build | Zero warnings, zero errors | Upstream's own toolchain |

The published reproducer and isolated v10.0.11 builds ran on Windows x64 / .NET 10.0.12 with SQLitePCLRaw.bundle_e_sqlite3 3.0.5. The actual upstream test application targets .NET 11 and was built with its pinned SDK, 11.0.100-rc.1.26420.103. These are distinct evidence sets, not a claim that DataLinq's full .NET 8/9/10 matrix has been rerun with a corrected dependency.

Six upstream skips reference [dotnet/efcore#35585](https://github.com/dotnet/efcore/issues/35585); the collation-isolation skip references [ericsink/SQLitePCL.raw#421](https://github.com/ericsink/SQLitePCL.raw/issues/421). Skips and the upstream category exclusion are retained, not relabeled as passes.

The failure mechanism and exact error are independently reproduced with the same published driver bytes used for W0. We cannot reconstruct the original W0 failure's precise thread schedule retrospectively. Earlier passing reruns do not disprove this race, and the original failed run remains failed evidence.

## Reviewable Patch And Reproducer

- [Submitted upstream issue and standalone reproducer](evidence/sqlite-pool/upstream-issue.md): complete project and program using published packages, observed output, mechanism, validation and AI disclosure; published as [dotnet/efcore#39008](https://github.com/dotnet/efcore/issues/39008).
- [Upstream correction and regression test](evidence/sqlite-pool/0001-Publish-SQLite-pooled-connection-ownership-before-ac.patch): applicable to upstream base `961b3cb6fe78daf720068a5166218138c6c04f73` using `git am` in an EF Core checkout. It does not apply to DataLinq.
- Upstream fix commit: [`4198aba3e42cf76a436edb08070e7e32da44bcd2`](https://github.com/bazer/efcore/commit/4198aba3e42cf76a436edb08070e7e32da44bcd2), pushed to `bazer/efcore:codex/sqlite-pool-owner-publication` and submitted to upstream `main` in [dotnet/efcore#39009](https://github.com/dotnet/efcore/pull/39009). The submitted source and test are unchanged from the tested patch.

Both the issue and PR disclose: "This fix was found, reproduced and posted with GPT-6 Astra on Extra High, with the repository owner's authorization." The PR leaves maintainer approval and upstream automated checks unchecked. The user personally posted [CLA acceptance](https://github.com/dotnet/efcore/pull/39009#issuecomment-5715068889); no agreement was accepted on the user's behalf.

The upstream regression uses upstream's existing xUnit test project. No xUnit project or dependency is added to DataLinq; new DataLinq tests still belong in its TUnit projects.

## Artifact Identity

The original W0 capture remains frozen at DataLinq `7e36614b5f26f1dd199ff93bc60620f4b0714d5f`, with compatibility baseline 0.9.2. See [W0 evidence PR #144](https://github.com/bazer/DataLinq/pull/144). This investigation does not replace or alter that archive.

New local artifact root: `artifacts/investigations/sqlite-pool-20260917/`. The command sidecars retain timestamps, exits and actual Git state; upstream runs record the modified checkout before the local fix commit. The two final direct build logs lack capture sidecars and are identified as such in the artifact README. Failed diagnostic restore/build attempts remain included.

| Artifact | Identity |
| --- | --- |
| `artifacts/investigations/sqlite-pool-20260917-evidence.zip` | 10,342,796 bytes; SHA-256 `ac28cdc5e52048f28449d5a673a940f33a2a1aca1d122a7c02ccd4e73f5f83ec` |
| `manifest.json` inside that archive | 27,468 bytes; SHA-256 `0579ffcbbf6c3ff53d26589148d3019756d298ff48c9041061099c2a832b3f8d` |
| Published 10.0.11 driver assembly used by reproducer | SHA-256 `4abd9c2a61e580eb853e93ca8953a3cef2c05714ae28d2d1859d4dbc5e5700bc` |
| Pinned v10.0.11 upstream source archive | SHA-256 `2fe343eef7b0c06649d0f92de720444278d3b56e3b985bbd8464aedd40274434` |

All 142 archive entries, including the manifest, passed ZIP integrity checking; every one of the 141 evidence files was reread from the archive and checked against its recorded length and SHA-256. The archive excludes downloaded SDKs, caches and the upstream Git clone. It includes the explicitly preserved diagnostic binaries, sources, patch, logs and TRX results. Diagnostic source-build DLLs are unsigned and must not be distributed as official Microsoft packages.

The raw archive is local only. The patch and standalone reproducer are tracked here, but no external backup or upload of the raw evidence is claimed.

The sealed archive predates upstream submission and retains the original draft without the subsequently requested AI disclosure. Its submission status is historical; the archive and its hashes remain unchanged. The tracked report above contains the submitted disclosure and current upstream links.

## Remaining Integration Work

1. Follow up on submitted issue #39008 and PR #39009 for maintainer review and 10.0 servicing consideration. The user has posted CLA acceptance. Submission is complete; approval, CI acceptance and a release are not implied.
2. Obtain and pin a corrected official package. Record the actual version, upstream fix and package hashes; do not assume that the next version contains the correction.
3. Make the DataLinq dependency update on `master`, test it, then merge it forward into `v0.10` under the accepted branch workflow. Preserve the published 0.9.2 compatibility baseline and original pre-async evidence.
4. Rerun the published-package ownership/transaction reproducer, focused parallel transaction coverage, quick plan and full supported provider matrix. Rebuild and recapture affected package/consumer/performance evidence against a clean frozen candidate with the new dependency. Record the new dependency graph and exact evidence target rather than relabeling W0's old results.
5. Close W0-F1 only when the dependency actually used by DataLinq is corrected and the required evidence passes. Only the scoped W1 work authorized above may proceed in the meantime.

A DataLinq-only checkout lock would not protect raw Microsoft.Data.Sqlite callers sharing the same pool. Disabling pooling globally would change connection behavior and performance. Neither is silently introduced as a substitute for the driver fix. A temporary mitigation would require a separately reviewed implementation and measured consequences.
