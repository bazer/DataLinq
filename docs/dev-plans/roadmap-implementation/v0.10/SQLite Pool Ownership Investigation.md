> [!WARNING]
> This is diagnostic evidence for W0-F1. The correction is tested locally in Microsoft.Data.Sqlite, but DataLinq still references the unchanged published driver. W0-F1 is not closed and W1 has not started.

# SQLite Pool Ownership Investigation

**Recorded:** 2026-09-17.

**Finding:** Microsoft.Data.Sqlite can lend one pooled internal connection to two distinct, still-open outer connections. Starting independent transactions then produces SQLite error 1, `cannot start a transaction within a transaction`, matching the W0 failure.

**Disposition:** preserve the tested upstream correction and reproducer; submit upstream after authorization, seek a 10.0 servicing fix, adopt a corrected official package through a separate DataLinq PR, and rerun the affected evidence. No dependency replacement, lock, retry, or pooling configuration change is included here.

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

- [Prepared upstream issue and standalone reproducer](evidence/sqlite-pool/upstream-issue.md): complete project and program using published packages, observed output, mechanism and validation. This is a draft, not a submitted issue.
- [Upstream correction and regression test](evidence/sqlite-pool/0001-Publish-SQLite-pooled-connection-ownership-before-ac.patch): applicable to upstream base `961b3cb6fe78daf720068a5166218138c6c04f73` using `git am` in an EF Core checkout. It does not apply to DataLinq.
- Local upstream commit: `4198aba3e42cf76a436edb08070e7e32da44bcd2`. It has not been pushed to an upstream fork or submitted as a PR.

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

## Remaining Integration Work

1. Submit the prepared report and correction to `dotnet/efcore` with maintainer review and a request to consider 10.0 servicing. Upstream submission is outside DataLinq and awaits explicit authorization; any contributor agreement must be handled by the contributor personally.
2. Obtain and pin a corrected official package. Record the actual version, upstream fix and package hashes; do not assume that the next version contains the correction.
3. Make the DataLinq dependency update on `master`, test it, then merge it forward into `v0.10` under the accepted branch workflow. Preserve the published 0.9.2 compatibility baseline and original pre-async evidence.
4. Rerun the published-package ownership/transaction reproducer, focused parallel transaction coverage, quick plan and full supported provider matrix. Rebuild and recapture affected package/consumer/performance evidence against a clean frozen candidate with the new dependency. Record the new dependency graph and exact evidence target rather than relabeling W0's old results.
5. Close W0-F1 only when the dependency actually used by DataLinq is corrected and the required evidence passes. W1 remains gated in the meantime.

A DataLinq-only checkout lock would not protect raw Microsoft.Data.Sqlite callers sharing the same pool. Disabling pooling globally would change connection behavior and performance. Neither is silently introduced as a substitute for the driver fix. A temporary mitigation would require a separately reviewed implementation and measured consequences.
