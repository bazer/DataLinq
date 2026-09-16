# W0 Baseline Evidence

**Status: baseline capture complete; W0-F1 remains an unresolved exit finding.** No async runtime implementation or W1 handoff is claimed. Capture ran on 2026-09-16 UTC (continuing past midnight into 2026-09-17 in Europe/Madrid). The [accepted capture plan](W0%20Baseline%20and%20Evidence%20Plan.md) and [source-linked I/O map](W0%20IO%20Execution%20Map.md) define this checkpoint.

## Frozen Identities

| Identity | Value |
| --- | --- |
| Runtime and tooling checkout | `7e36614b5f26f1dd199ff93bc60620f4b0714d5f`, clean before/after captured commands |
| Capture branch | `codex/0.10-w0-baseline`, based on `v0.10` |
| Published compatibility baseline | NuGet.org **0.9.2**, six packages verified against the [canonical lock](../../../../test-infra/api-compatibility/v0.9.2-packages.json); tag commit `1894d53d25511a3581e8923deda1b74d0f76ee25` |
| Local development packages | `0.10.0-w0.20260916.7e36614b`, PackOnly, six packages plus symbols; this is a local evidence label, not a published version or implemented async release |
| SDK / runtime | `10.0.401` / `.NET 10.0.12` |
| Runner | Windows `10.0.26200`, X64, 8 logical processors; Intel64 Family 6 Model 140 Stepping 1 |
| Tool versions | BenchmarkDotNet `0.15.8`; ApiCompat `10.0.400` |
| SQL package graph | MySqlConnector `2.6.2`; Microsoft.Data.Sqlite `10.0.11`; SQLitePCLRaw bundle/core/provider `3.0.5`; SQLite `3.53.4` |
| Evidence root | `artifacts/release/v0.10/w0-20260916-7e36614b/` |

The baseline keeps the accepted 0.9.2 registration changes. Earlier 0.9.0 comparison reports remain historical diagnostics. The fixed published package baseline and the development performance/test checkout serve different purposes.

## Captured Health And Compatibility

Paths below are relative to the evidence root unless stated otherwise. The final manifest lists individual file lengths and SHA-256 hashes; reports retain commands, UTC timestamps, exit codes and runner provenance.

| Gate | Evidence | Result / disposition |
| --- | --- | --- |
| Doctor | `doctor.log` and command record | Passed, SDK found, writable repository profile; workload auto-import resolver was reported partial and did not prevent the forced build |
| Restore | `restore-sandbox.*`, `restore-unrestricted.*` | Sandbox TLS failed; same restore succeeded outside the sandbox; failed attempt retained |
| Forced solution build | `forced-build-sandbox.*` | Passed, zero errors; two existing WASM0001 groups retained under W0-F2 |
| Frozen test catalog | `test-catalog.*`, `provider-targets.json`, `source-snapshots/` | Eight targets and five suites; catalog expected counts are floors, not measured totals |
| Initial quick plan | `quick.json` | 2,567 passed / 1 failed / 0 skipped; W0-F1 retained |
| Quick repeat | `quick-repeat.json` | 2,568 passed / 0 failed / 0 skipped |
| Focused SQLite repeats | `sqlite-parallel-transaction-repro.json` and `sqlite-parallel-repeat-01.json` through `-10.json` | Eleven isolated passes of the originally failing test; diagnostic, not full-matrix evidence or proof of a fix |
| Full matrix | `full.json` and referenced raw test directories | **5,855 passed / 0 failed / 0 skipped**, all 17 expected entries; `ValidForEvidence=true` |
| Container identity | `container-images.txt` | Six running server images recorded by immutable SHA-256 digest; existing stopped containers were started without reset |
| PackOnly | `pack-sandbox.*`, `pack-unrestricted.*`, `packages/` | Sandbox profile access failed; unrestricted pack passed; nothing published |
| Package inspection | `package-inspection/report.json` | Passed, `ValidForEvidence=true`; exact six-package set, symbols and clean candidate/checkout identity |
| API compatibility | `api-against-0.9.2/report.json` | 36 surfaces, 10 comparisons, zero breaks/framework mismatches/hard failures; two accepted inherited `loadLock` review items (W0-F3) |
| Packed consumer | `consumer-smoke-unrestricted/report.json` | Builds and generated-source checks passed on .NET 8/9/10; .NET 10 execution passed; exact four direct runtime package hashes verified |
| Native-call availability | `provider-api-availability.json`, `provider-api-probe/` | Reflection records signatures and declaring types for nine provider types; no provider interruption/suspension claim |
| Generated model source | `generated-models.*`, `generated-models/net10.0/` | Forced .NET 10 model build passed without warnings; 45 generated source files retained |
| SQLite pool diagnostics | `pool-interleaving.json`, `pool-public.json` and `pool-public-repeat-02.json` / `-03.json` | Controlled interleaving reproduced duplicate checkout; public-API run reproduced duplicate live native handles, followed by two passing repeats; see W0-F1 |

The packed consumer's .NET 10 execution exercises Memory and SQLite; MySQL is a compilation probe there. Actual MySQL/MariaDB behavior is covered by the full provider matrix. Its six generated source files cover database metadata and row source on each TFM. The separate 45-file .NET 10 model snapshot includes existing relation/model generation; neither snapshot claims every possible generated declaration or future async emission.

The pool diagnostic restores resolved the pinned packages from cache but reported NU1900 because sandbox access to NuGet vulnerability data failed. Those probes establish behavior of the hashed local driver assembly; they are not dependency-audit evidence.

The full matrix expands as follows:

| Suite/target | Passed |
| --- | ---: |
| Generators, once | 69 |
| Unit, once | 1,835 |
| Memory, once | 149 |
| Compliance / SQLite file | 515 |
| Compliance / SQLite memory | 384 |
| Compliance / each of six server targets | 390 each |
| MySQL suite / MySQL 9.7 | 150 |
| MySQL suite / MySQL 8.4 | 81 |
| MySQL suite / each of four MariaDB targets | 83 each |
| **Total** | **5,855** |

Server targets are MySQL 8.4/9.7 and MariaDB 10.11/11.4/11.8/12.3. Per-target counts differ because the catalog assigns provider-invariant cases to anchor targets; targetless suites run once. The full summary's expected/observed entries and TRX files are authoritative.

## Performance Capture

All six lanes use `--profile heavy --release-evidence` with default `--filter "*"`, a freshly built harness, and sequential execution. No full-provider test or other benchmark runs alongside them. Both baseline and later comparisons must use .NET 10 and preserve the recorded toolchain, runner, corpus/cache states and normalized operation counts.

| Selector | Providers | Expected rows | Status |
| --- | --- | ---: | --- |
| `--phase2-watch` | SQLite file and memory | 6 | Captured; strict evidence valid, timing warnings retained |
| `--phase3-query-hotpath` | SQLite file and memory | 6 | Captured; strict evidence valid, timing warnings retained |
| `--v09-query-backend` | SQLite file and memory | 12 | Captured; strict evidence valid, timing warnings retained |
| `--v09-memory-read` | Memory | 9 | Captured; strict evidence valid, timing warnings retained |
| `--allocation-regression` | SQLite memory | 9 | Captured; strict evidence valid, timing warnings retained |
| `--allocation-stages` | SQLite file and memory | 48 | Captured; strict evidence valid, timing warnings retained |

All **90 rows** are captured, with `ValidForEvidence=true`, exact target sets and per-row telemetry in every lane. History files live at `artifacts/benchmarks/history/w0-7e36614b-<selector>.json`; their raw BDN output, per-operation telemetry and summaries live under the referenced run directories. This is a before-state, not a candidate regression comparison. Website/default/smoke data and old .NET 8 captures do not replace these strict lanes.

The frozen corpus definition is the committed benchmark/fixture source: the SQL benchmark context uses 1,000 employees with Bogus seed `59345922`, its separate startup fixture uses the FullSeeded profile (300 employees), and the Memory context uses 1,024 primitive rows plus 256 Guid rows. Cache/reset/warmup rules and operation counts are retained with the source snapshots and each history row. Bogus date generation includes relative dates: the seed fixes generation choices but does not promise byte-identical dates on a later day. The recorded run timestamps and package versions remain part of comparison context; no full database-byte export is claimed.

Several lanes report short iterations or multimodal distributions, including noisy warm-primary-key timings in the first lane. Preserve those measurements and warnings; later timing comparisons need repeats where uncertainty overlaps the claimed difference. Allocation changes and semantic telemetry differences cannot be waived by timing noise. Issue [#26](https://github.com/bazer/DataLinq/issues/26)'s separate final-0.8 allocation parity target is unchanged and is not marked resolved by W0.

## Findings And Handoff

**W0-F1 — unresolved SQLite parallel-transaction failure; owner: W0 baseline investigation.** The initial quick plan failed `EmployeesRelationAndThreadingTests.Threading_ParallelTransactionCommits_PersistIndependentUpdates` for SQLite file with an AggregateException containing SQLite error 1 (transaction already active) and error 5 (database locked). The retained stack reaches `SQLiteDatabaseTransaction.DbConnection` during provider BeginTransaction. One direct retry, ten additional isolated repeats, the quick-plan repeat and the full matrix passed unchanged. That establishes intermittency; it does not establish an environmental cause, a driver cause or a fix.

A subsequent standalone **public-API** probe reproduced a concrete driver ownership defect with Microsoft.Data.Sqlite 10.0.11, without loading DataLinq: 16 distinct connections open concurrently, remain strongly held/open until the wave completes, and only then dispose. Before each wave, it clears only its own pool while no connections are open. Wave 774 (zero-based index 773) returned the same native handle to owners 7 and 14. No reflection, forced garbage collection, shared connection operations, SQL statements or concurrent pool clearing are involved. The probe stopped on that failure; two fresh-process repeats each completed 1,000 waves without reproducing it. Reports and exact source are retained in `pool-public-probe/` and the root diagnostic records. The driver assembly SHA-256 is `4abd9c2a61e580eb853e93ca8953a3cef2c05714ae28d2d1859d4dbc5e5700bc`.

The pinned source offers a plausible mechanism: [Activate](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteConnectionInternal.cs#L147) sets its active flag before assigning its weak owner; [pool reclamation](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteConnectionPool.cs#L141) can regard that intermediate state as leaked; [the factory](https://github.com/dotnet/efcore/blob/v10.0.11/src/Microsoft.Data.Sqlite.Core/SqliteConnectionFactory.cs#L27) activates outside the pool's checkout lock. A separate reflection-based controlled-interleaving probe confirms duplicate checkout at that boundary, while its inactive control does not. This is supporting mechanism evidence, not instrumentation of the original DataLinq failure. The [10.0.12 source](https://github.com/dotnet/efcore/blob/v10.0.12/src/Microsoft.Data.Sqlite.Core/SqliteConnectionInternal.cs#L147) retains the same Activate ordering, so a version bump alone is not a verified fix.

**Disposition: not waived; W0 handoff remains blocked.** The driver defect is reproduced; its causal link to the original transaction failure and a safe correction remain unproven. Next work is a focused provider-lifetime follow-up: preserve the standalone reproduction, validate an upstream correction or scoped mitigation, instrument the DataLinq open/begin path, then rerun the affected matrix and benchmark scopes. Passing repeats, suppressing the test, serializing transactions or automatically retrying writes do not establish a correction. Any pooling policy change needs explicit compatibility and performance evaluation. Under the [accepted branch workflow](Branch%20PR%20and%20Benchmark%20Workflow.md), a confirmed defect affecting current consumers is fixed on master and merged forward. If a runtime/dependency fix changes the before-state, record a new identity and recapture affected evidence before W1. No upstream issue/PR or package publication was submitted by this capture.

**W0-F2 — existing SQLite WebAssembly warning debt; owner: release constrained-runtime work.** The forced build reports WASM0001 for `sqlite3_config` and `sqlite3_db_config` varargs entry points in SQLitePCLRaw. These are real unsupported-call warnings, not blanket-suppressed sandbox noise. This checkpoint does not prove those functions are unreachable. New 0.10 package-graph AOT/trim/browser validation remains W8/W9 work; package-consumer success is not a substitute.

**W0-F3 — accepted inherited API differences; owner: W3 compatibility review.** The report preserves the exact canonical-lock dispositions for protected `ImmutableForeignKey<,>.loadLock` and `ImmutableRelation<,>.loadLock`: object on .NET 8, System.Threading.Lock on .NET 9/10. No new suppression or API change was introduced.

**W0-F4 — source-audit ownership gaps; owner: W2 provider/factory work.** Server metadata factories do not explicitly scope their owning information-schema Database, and both provisioning factories create a command without an explicit disposal scope. These source findings are mapped in the I/O inventory; they are not proven active resource leaks or justification for silently changing baseline behavior.

The [I/O map](W0%20IO%20Execution%20Map.md) assigns query, prepared/raw execution, key/relation/materialization, mutation/completion, probes/metadata/timeout, provisioning/setup, owning disposal and unsupported backend boundaries. Its future-only register assigns controllable tests to W1, native feasibility to W2, emitted/packed consumers to W3, hosting to W4, validation to W5, fixture support to W6 and the new package graph to W8/W9.

## Evidence Storage

Sealed at **2026-09-16T22:06:07.083503+00:00**. The local ZIP contains **1,340 files**, including its manifest; the manifest records **1,339 artifact identities**. ZIP integrity, command-log hashes, clean checkout provenance, all strict benchmark artifact hashes/scopes and the complete matrix totals were checked before sealing. The bundle also retains 69 committed corpus/model/configuration inputs and six post-benchmark resolved package graphs.

| Artifact | Repository-relative location | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| Manifest | `artifacts/release/v0.10/w0-20260916-7e36614b/manifest.json` | 430,412 | `9aee9ba7df50f03cdf8fd09c50196af3c3a733f5272cf31d7200d4b2dfe07fb9` |
| Evidence ZIP | `artifacts/release/v0.10/w0-20260916-7e36614b-evidence.zip` | 672,635,108 | `0e83f66c70acc6414e049537f7afa24a7e77c1d9dc1f1ecc2c64b80e83577dc8` |

Raw output and package bytes stay outside Git. Profile/NuGet/temp caches and mutable credential-bearing container state are excluded; immutable image digests are retained. The index and `seal.json` remain outside the archive to avoid self-referential hashes. This is a verified local archive, **not an upload to GitHub or an external backup**. Preserve the ZIP under its recorded hash before deleting local artifacts; later captures must use a new identity and directory.
