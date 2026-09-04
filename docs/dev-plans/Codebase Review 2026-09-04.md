# Codebase review — 2026-09-04

## Scope and review plan

Review target: clean working tree at `57aa3dd3cc99e1e214dfae6cae99527e3ae0386e`.

This is a repository-wide, risk-based review of the current implementation for correctness, security, performance, and operational reliability. It is not a claim of exhaustive path coverage or a penetration test. Product code is not being changed as part of this review.

The review proceeds through these areas:

1. **Inventory and baseline:** identify projects, shared source, build/package configuration, active tests, and existing contracts; run the supported local feedback plan.
2. **Query and SQL boundaries:** expression translation, parameterization, identifiers, raw SQL, null/type semantics, pagination, prepared queries, and provider parity.
3. **Mutation and transactions:** atomicity, commit/rollback outcomes, external transactions, disposal, concurrency, identity handling, and failure recovery.
4. **Cache and memory storage:** consistency, key identity, synchronization, retention, indexes, query complexity, and snapshots.
5. **Providers and serialization:** MySQL/MariaDB and SQLite connections, metadata, conversions, resource ownership, schema input, and cancellation/timeouts.
6. **Generation and public tooling:** generated source, configuration, filesystem boundaries, credentials/logging, process execution, and CLI error behavior.
7. **Supporting code:** developer/test/benchmark tools, sample applications, smoke projects, CI, dependencies, packaging, and test coverage.
8. **Validation and prioritization:** trace each candidate through callers and tests, reproduce where practical, reject false positives, and document recommended repairs and regression checks.

## Classification

- **P0 — critical:** immediate, broadly reachable compromise or destructive failure; stop release/use until addressed.
- **P1 — high:** credible data loss, incorrect writes/results, serious security exposure, or severe reliability failure in supported usage.
- **P2 — medium:** narrower correctness/security issues, material performance costs, or operational failures with a specific trigger.
- **P3 — low:** limited-impact issues and defense-in-depth improvements.

Evidence is classified separately from priority:

- **Reproduced:** exercised against this checkout, with an observed result.
- **Confirmed by code:** a complete implementation path establishes the defect; no runtime reproduction claimed.
- **Potential:** a credible concern whose impact, reachability, or workload dependence needs further validation.

Every retained finding identifies its category, affected code, trigger and impact, evidence, suggested remediation, and a regression or validation check. Intentional raw SQL escape hatches and documented limitations are not automatically vulnerabilities.

## Results

**29 findings: 4 high priority, 21 medium priority, and 4 low priority. No P0 finding was established.** Fourteen findings have direct reproductions or measurements, eight are established by implementation paths, and seven remain potential risks requiring additional validation.

The strongest release concerns are **F01–F04**: entity selection can bypass a restrictive join; ordinary identifier APIs permit SQL structure injection; an in-flight read can repopulate an invalidated cache with stale data; and generated-file cleanup can delete an original file while reporting successful restoration.

The existing test coverage is substantial and green. The generator, unit, Memory, complete compliance matrix, and complete MySQL/MariaDB-specific matrix account for **6,026 passing case executions**. Nevertheless, focused probes exposed behavior those suites do not currently protect. Passing tests do not override the reproductions below.

The full solution built successfully with **two `WASM0001` warnings and zero errors**. The live NuGet advisory check reported no vulnerable direct or transitive packages across its 27 solution projects. Neither result establishes that every execution path or deployment is safe.

Product and permanent test source are unchanged. Temporary probes were removed after execution; their source and logs are retained locally under `artifacts/codebase-review-2026-09-04/`. This document is the only intended versioned change. No commit, package publication, or production change was made.

### Prioritized register

Evidence: **R** = reproduced/measured; **C** = confirmed by code; **V** = potential, further validation needed. A reproduced security-sensitive behavior can still require an application-specific exposure condition.

| ID | Priority | Category | Problem | Evidence |
| --- | --- | --- | --- | --- |
| [F01](#f01) | P1 | Correctness / security | Primary-key shortcut bypasses joins and pagination | R |
| [F02](#f02) | P1 | Security | Identifier arguments can change SQL structure | R |
| [F03](#f03) | P1 | Cache consistency | In-flight reads restore stale rows after invalidation | R |
| [F04](#f04) | P1 | Data loss / tooling | Backup cleanup triggers destructive rollback | R |
| [F05](#f05) | P2 | Resource ownership | Logging failure leaks a MySQL reader connection | R |
| [F06](#f06) | P2 | Provider correctness | MySQL `TIME` durations silently wrap to time of day | R |
| [F07](#f07) | P2 | Provider correctness | Multi-bit SQL values are generated as `bool` | R |
| [F08](#f08) | P2 | Provider correctness | Escaped enum members are split and renumbered | R |
| [F09](#f09) | P2 | Query correctness / performance | Entity materialization replaces database ordering | R |
| [F10](#f10) | P2 | Concurrency | Cache-history snapshots race with updates | R |
| [F11](#f11) | P2 | Cache policy / retention | Old expiration entries evict newer index entries | R |
| [F12](#f12) | P2 | Performance | Row eviction repeatedly scans the entire cache | R |
| [F13](#f13) | P2 | Security / observability | Debug SQL logging includes complete parameter values | R |
| [F14](#f14) | P2 | Security / correctness | Existence checks interpolate SQL literals and use wildcard matching | C |
| [F15](#f15) | P2 | Query correctness | Empty membership lists throw; null-only rendering is malformed | R |
| [F16](#f16) | P2 | Concurrency | Relation value publication and clearing are not one snapshot | C |
| [F17](#f17) | P2 | Cache consistency | Concurrent first subscriptions can create different notification managers | C |
| [F18](#f18) | P2 | Tooling reliability | Sequential process-pipe reads can deadlock | C |
| [F19](#f19) | P2 | Tooling / data protection | `OverwriteExistingModels=false` is ignored | C |
| [F20](#f20) | P2 | Resource ownership | Internally created commands are not deterministically disposed | V |
| [F21](#f21) | P2 | Development security | Test databases bind broadly with public default credentials | V |
| [F22](#f22) | P3 | Public API | Callable APIs immediately throw `NotImplementedException` | C |
| [F23](#f23) | P3 | Dependency security | Routine verification disables NuGet auditing | V |
| [F24](#f24) | P3 | Build performance | Compilation changes invalidate all generator execution inputs | V |
| [F25](#f25) | P3 | Memory backend performance | Ordered small pages buffer and sort the full matching set | V |
| [F26](#f26) | P2 | Schema tooling correctness | A missing view is rendered as `CREATE TABLE` | C |
| [F27](#f27) | P2 | Schema tooling correctness | Missing-table scripts omit constraint review comments | C |
| [F28](#f28) | P2 | Startup concurrency | Provider registration writes shared dictionaries without synchronization | V |
| [F29](#f29) | P2 | Platform compatibility | SQLite WebAssembly build exposes unsupported native signatures | V |

## Findings

Line references describe the reviewed commit. Repository-relative links remain useful if later edits move the lines.

<a id="f01"></a>
### F01 — Primary-key selection bypasses restrictive joins and pagination

**P1 · Correctness / conditional authorization impact · Reproduced**

**Location:** [Select.cs](../../src/DataLinq/Query/Select.cs), lines 313–355; [SqlQuery.cs](../../src/DataLinq/Query/SqlQuery.cs), lines 582–614.

`Select.Execute()` takes its scalar/general primary-key shortcut before checking whether the query has joins. The key recognizer checks the predicate shape but does not establish that bypassing the remaining query operations is equivalent. The shortcut returns the row directly from the cache/provider key lookup.

**Observed:** against SQLite, the SQL reader returned zero rows for an inner join to an empty `allowed` table, but entity `Select()` returned one. A primary-key query with `Limit(0)` likewise produced SQL count 0 versus entity count 1. The public LINQ path `Where(row => row.DeptNo == "d001").Take(0)` also returned one row. These are three probes of one underlying eligibility error.

**Impact:** incorrect results; a restrictive join used by an application to express authorization can be bypassed. No production authorization system was tested, and arbitrary predicates are not claimed to be bypassed.

**Repair:** centralize shortcut eligibility around the complete query shape. Require semantic equivalence for joins, grouping, derived sources, offset/limit, and other result-shaping operations; handle `Take(0)` before reading. Keep SQL execution when equivalence is uncertain.

**Regression:** compare SQL and entity execution with cold/warm caches, restrictive inner joins, zero limit, offset, composite keys, and the public LINQ equivalent on all providers.

<a id="f02"></a>
### F02 — Identifier arguments can inject SQL structure

**P1 · Security · Reproduced; application reachability is conditional**

**Location:** [Operand.cs](../../src/DataLinq/Query/Operand.cs), lines 28–59. Audit the related identifier rendering in [SqlQuery.cs](../../src/DataLinq/Query/SqlQuery.cs), [Select.cs](../../src/DataLinq/Query/Select.cs), and provider table-name construction as part of the repair.

`ColumnOperand` surrounds the name with the dialect delimiter without escaping embedded delimiters, and emits the alias directly. Bound values do not protect these identifier positions.

**Observed:** passing `dept_no" = 'd001' OR "dept_no` as the column argument to `Where(...).EqualTo("d002")` produced an executable SQLite predicate equivalent to `"dept_no" = 'd001' OR "dept_no" = @value`, returning both rows. This used an ordinary identifier-taking API, not a `Raw` SQL method.

**Impact:** SQL injection if an application forwards untrusted column/alias/table choices into these APIs. Even trusted schema names containing delimiters can break SQL. This is not evidence that normal parameterized value predicates are injectable.

**Repair:** provide one dialect-aware identifier writer that escapes each identifier component; validate metadata-backed names where appropriate. Separate identifier arguments from SQL expressions instead of guessing whether strings contain SQL. Applications should allowlist externally selectable fields as well.

**Regression:** delimiter, dot, whitespace, reserved-word, alias, and malicious identifier cases across SQLite/MySQL/MariaDB; assert that they are treated as identifiers or rejected and cannot expand the selected rows.

<a id="f03"></a>
### F03 — In-flight reads can republish stale data after invalidation

**P1 · Cache consistency · Reproduced**

**Location:** [TableCache.RowLookup.cs](../../src/DataLinq/Cache/TableCache.RowLookup.cs), lines 64–101; [TableCache.RowStorage.cs](../../src/DataLinq/Cache/TableCache.RowStorage.cs), publication and `TryAddRow` paths; [TableCache.Maintenance.cs](../../src/DataLinq/Cache/TableCache.Maintenance.cs), lines 25–34.

A cache miss fetches data and later materializes/publishes it without a generation check spanning the fetch and publication. Clearing the current dictionary does not invalidate work already in flight.

**Observed:** a controlled model-construction barrier paused a read after the old value was fetched. Another connection updated the row, then explicit `provider.State.ClearCache()` completed. Releasing the first read allowed it to cache the old value. A subsequent primary-key read returned `old`, while direct SQL returned `new`.

**Impact:** stale values can survive a completed invalidation until a later eviction/clear. Security impact depends on applications caching authorization-relevant data. This reproduction uses a supported explicit-clear workflow; it does not establish the outcome of every transaction interleaving.

**Repair:** capture an invalidation generation before loading and reject/retry publication if it changed. Apply the protocol consistently to single rows, batches, index entries, and relation loading/subscription. Do not hold a global cache lock during database I/O.

**Regression:** deterministic barriers around fetch/materialization/publication; update/delete plus explicit clear and committed mutation; verify subsequent reads cannot observe a stale row republished after invalidation.

<a id="f04"></a>
### F04 — Backup cleanup can delete an original file and falsely report restoration

**P1 · Data loss · Reproduced on Windows**

**Location:** [SafeGeneratedFileWriter.cs](../../src/DataLinq.Tools/SafeGeneratedFileWriter.cs), lines 60–155.

Successful replacements and backup deletion occur inside one `try`. If deleting a later backup fails, rollback runs even though earlier backups have already been deleted. Rollback deletes a committed target before checking that a recoverable backup still exists. It also stops processing remaining files after the first rollback exception.

**Observed:** three disposable files were replaced; the second backup was made read-only before cleanup. Deleting the first backup succeeded, deleting the second failed, and rollback removed the first target with no backup left to restore. `one.cs` disappeared. The returned failure nevertheless said **“Existing files were restored.”** Only uniquely named review scratch files were used.

**Repair:** make successful replacement a commit boundary separate from best-effort backup cleanup. A cleanup error must not trigger rollback after recovery material has been discarded. Before deleting a target during rollback, establish that its original can be restored. Attempt rollback per file and accurately aggregate restoration failures.

**Regression:** injected failures during staging, each move, each backup deletion, and restoration; verify original/new contents and recovery artifacts for every file. Include the Windows read-only-backup case and verify diagnostic truthfulness.

<a id="f05"></a>
### F05 — Logging failure before a MySQL reader is returned leaves its connection open

**P2 · Resource ownership · Reproduced**

**Location:** [SqlDbAccess.cs](../../src/DataLinq.MySql/Shared/SqlDbAccess.cs), lines 59–74. Compare the exception-safe [SQLiteDbAccess.cs](../../src/DataLinq.SQLite/SQLiteDbAccess.cs), lines 94–119.

The MySQL path opens a connection, assigns it to the command, and invokes logging before transferring lifetime to a reader. There is no enclosing failure cleanup.

**Observed:** a configured logger that threw during SQL logging caused `ExecuteReader` to fail while the connection remained `Open`. The probe explicitly disposed it afterward. A separate ordinary SQL-syntax-error probe passed: the current MySQL driver closed that connection. Therefore this finding is deliberately narrower than “every failed query leaks.”

**Impact:** logging/formatting failures, and potentially other failures before reader ownership is established, can consume pooled connections. Pool exhaustion under sustained failures was not load-tested.

**Repair:** dispose the opened connection on every exception before successful reader handoff, including logging and telemetry setup. Preserve caller ownership of an externally supplied command.

**Regression:** throwing logger, command setup failure, telemetry callback failure where applicable, SQL error, successful reader disposal, and early enumeration disposal; assert pool/connection recovery.

<a id="f06"></a>
### F06 — MySQL duration values silently wrap into a different value

**P2 · Data fidelity · Reproduced**

**Location:** [SqlDataLinqDataReader.cs](../../src/DataLinq.MySql/Shared/SqlDataLinqDataReader.cs), lines 63–71; [MetadataFromSqlFactory.cs](../../src/DataLinq.MySql/Shared/MetadataFromSqlFactory.cs), lines 743–744.

Schema generation maps SQL `TIME` to `TimeOnly`; the reader reduces the returned duration modulo one day and adjusts negative values into a positive day.

**Observed on MySQL 9.7:** `25:00:00` became `01:00:00`, and `-01:00:00` became `23:00:00`. MySQL `TIME` can represent signed durations exceeding a day, so these are valid database values, not invalid input. See the [MySQL TIME contract](https://dev.mysql.com/doc/refman/8.4/en/time.html).

**Repair:** support a duration-preserving mapping, or make time-of-day intent explicit and reject out-of-range values. Silent modulo conversion should not be the default recovery behavior.

**Regression:** negative, zero, fractional, over-24-hour, and boundary durations; generation plus read/write round trips for MySQL and MariaDB. A hand-authored time-of-day model should fail clearly for a duration it cannot represent.

<a id="f07"></a>
### F07 — `BIT(n)` ignores its width when generating the CLR type

**P2 · Data fidelity · Reproduced**

**Location:** [MetadataFromSqlFactory.cs](../../src/DataLinq.MySql/Shared/MetadataFromSqlFactory.cs), lines 721–722; existing coverage in [ServerTypeMappingTests.cs](../../src/DataLinq.Tests.MySql/ServerTypeMappingTests.cs) covers `BIT(1)`.

The `bit` mapping returns `bool` without checking the declared bit count.

**Observed:** live metadata for a `BIT(8)` column generated `bool`. That type cannot preserve its 256 possible bit patterns. This probe establishes the lossy model contract; it does not claim a complete generated-model round trip was run for all bit widths.

**Repair:** restrict boolean mapping to `BIT(1)` and choose an explicit width-preserving integral/binary representation for larger widths, including compatible reader/writer behavior.

**Regression:** `BIT(1)`, `BIT(8)`, and `BIT(64)` metadata and round trips with zero, multiple set bits, and the highest bit.

<a id="f08"></a>
### F08 — Escaped enum literals are split and their numeric identities change

**P2 · Provider metadata correctness · Reproduced**

**Location:** [MetadataFromSqlFactory.cs](../../src/DataLinq.MySql/Shared/MetadataFromSqlFactory.cs), lines 576–605.

`ParseEnumType` uses `'([^']*)'`, which cannot distinguish the end of a literal from an escaped quote. It assigns consecutive numeric values to the resulting regex matches.

**Observed:** a real MySQL column declared `ENUM('can''t','fine')` became `(can, 1); (t, 2); (fine, 3)`. The original has two members; the generated metadata has three and changes the ordinal of `fine`.

**Repair:** tokenize the provider's enum literal syntax correctly, respecting escaping/SQL mode, then separately generate valid, unique C# names without changing database values or ordinals.

**Regression:** embedded quotes, backslashes, commas, empty strings, C# keywords, punctuation, and name collisions; verify metadata and generated code against live MySQL and MariaDB values.

<a id="f09"></a>
### F09 — Cached entity loading replaces server ordering with CLR ordering

**P2 · Correctness / performance · Reproduced**

**Location:** [Select.cs](../../src/DataLinq/Query/Select.cs), `GetCacheOrderings` around line 244; [TableCache.RowLoading.cs](../../src/DataLinq/Cache/TableCache.RowLoading.cs), lines 315–384.

The low-level column-backed `SqlQuery.OrderBy` path merges cached/fetched rows and sorts them again using `IComparable` and the default CLR comparer. It does not preserve the order returned by the database.

**Observed:** with SQLite binary ordering and CLR culture `en-US`, SQL returned IDs `d001,d002` for names `z,ä`; entity selection returned `d002,d001`.

**Impact:** collation-dependent result differences, possible unsupported comparison for converted model types, and redundant sorting/allocations. The reproduction concerns low-level `SqlQuery`; do not generalize it to every LINQ ordering path, which can render ordering differently.

**Repair:** preserve the SQL-returned key sequence while materializing cache hits/misses, including duplicate/join semantics where supported. Avoid trying to emulate an arbitrary server collation in CLR code.

**Regression:** partial/full/no cache, ascending/descending/multiple columns, non-ASCII strings, nulls, converters, and paging under explicit provider collations.

<a id="f10"></a>
### F10 — Cache-history readers and clearing bypass the writer lock

**P2 · Concurrency · Reproduced**

**Location:** [CacheHistory.cs](../../src/DataLinq/Cache/CacheHistory.cs), lines 17–49.

`Add` locks the linked list, but `GetHistory`, `GetLatest`, and `Clear` do not share that synchronization. `Count` can also diverge during concurrent clear/add operations.

**Observed:** concurrent additions/clears and `GetHistory()` threw `ArgumentException: Insufficient space in the target location to copy the information.`

**Repair:** synchronize list snapshots and mutation with the same lock, or publish immutable history snapshots atomically. Keep event callbacks outside the internal lock and define how concurrent capacity changes behave.

**Regression:** concurrent add/snapshot/clear/capacity changes; assert no exceptions, bounded history, and count consistency. Use coordinated concurrency tests plus a bounded stress test.

<a id="f11"></a>
### F11 — Expiration tombstones remove replacement index entries

**P2 · Cache policy / memory retention · Reproduced eviction error**

**Location:** [IndexCache.cs](../../src/DataLinq/Cache/IndexCache.cs), `TypedIndexCache`, lines 93–144 and 176–203.

Insertion queues `(foreignKey, timestamp)`. Removing an entry leaves that queue item behind. Expiration later removes by foreign key alone, without checking whether the current value is the generation that originally created the queued item.

**Observed:** add key K, remove K, add a new value for K after the expiration cutoff, then expire old entries: the replacement was removed despite being newer than the cutoff.

The same structure retains obsolete queue records under churn until age cleanup or a full clear drains them. That retention follows from the code; its long-running memory cost was not measured. Early eviction primarily hurts hit rate and policy correctness, rather than demonstrating wrong database results by itself.

**Repair:** associate each live entry with an insertion generation and expire only matching generations; compact stale queue records with a bounded policy. Include queue storage in policy/accounting decisions.

**Regression:** repeated replace/remove/reinsert of the same key, mixed keys, expiration boundaries, and long-running churn with age cleanup disabled.

<a id="f12"></a>
### F12 — Row-limit eviction performs a full scan for each removed row

**P2 · Performance / request latency · Measured**

**Location:** [RowStore.cs](../../src/DataLinq/Cache/RowStore.cs), lines 130–164, 185–202, and `TryFindOldestKey` around line 289; [TableCache.Maintenance.cs](../../src/DataLinq/Cache/TableCache.Maintenance.cs), lines 162–178.

Every removed row calls a dictionary-wide oldest-entry search. Removing k of n rows is approximately O(n × k), becoming quadratic when removing a fixed fraction. The row-limit loop holds the same store lock used by ordinary cache access. The byte-limit path also repeatedly removes one row.

**Diagnostic measurements, removing half the rows:**

| Initial rows | Removed | Elapsed |
| --- | --- | --- |
| 5,000 | 2,500 | 146.4 ms |
| 10,000 | 5,000 | 583.4 ms |
| 20,000 | 10,000 | 2,292.7 ms |

These are single-run Windows diagnostics using synthetic distinct keys, not isolated BenchmarkDotNet release evidence. Their roughly fourfold growth matches the implementation's complexity; absolute production timings should not be inferred.

**Repair:** maintain eviction order or batch-select victims once, avoid repeated full scans, and shorten lock hold times. Preserve the declared insertion/eviction policy when choosing a heap/queue/list structure.

**Regression:** scaling benchmarks over row and byte limits, high churn, and concurrent readers; record allocations, total eviction time, and reader tail latency.

<a id="f13"></a>
### F13 — Enabling SQL debug logging exposes complete parameter contents

**P2 · Security / operational risk · Disclosure reproduced; deployment exposure conditional**

**Location:** [Log.cs](../../src/DataLinq/Logging/Log.cs), lines 11–17 and 35–73; [DataLinqLoggingConfiguration.cs](../../src/DataLinq/Logging/DataLinqLoggingConfiguration.cs).

Debug SQL logging calls `FormatCommand`, which prints strings and binary data in full. There is no independent sensitive-value opt-in, redaction policy, or size bound. The default null logger does not emit these values.

**Observed:** a synthetic password parameter appeared completely in formatted output. No real credentials were used in the probe.

**Impact:** passwords/tokens/personal data can enter diagnostic sinks when SQL debug logging is enabled. Large binary parameters also cause substantial formatting/allocation and log volume. This is a conditional logging-policy risk, not proof of an existing credential leak.

**Repair:** log SQL shape, parameter names/types, and bounded metadata by default; require explicit sensitive-data logging opt-in with redaction/length controls. Do not rely only on names such as `password` to identify secrets.

**Regression:** string/binary secrets absent by default, deliberate opt-in behavior, size limits, nulls, and a large binary parameter allocation test.

<a id="f14"></a>
### F14 — MySQL existence helpers interpolate values and interpret names as patterns

**P2 · Security / correctness · Confirmed by code**

**Location:** [SqlProvider.cs](../../src/DataLinq.MySql/Shared/SqlProvider.cs), lines 79–101.

`DatabaseExists` builds `SHOW DATABASES LIKE '{name}'`; `TableExists` embeds both a backtick-delimited database name and `LIKE '{tableName}'`. Quotes/delimiters are not escaped. `%` and `_` also act as patterns, so an existence query is not an exact-name check even for nonmalicious input.

**Impact:** false positives and syntax errors for ordinary names; SQL structure injection if the caller lets untrusted names reach this API. No live attack was run against these helpers.

**Repair:** use parameterized equality queries against `information_schema`, with provider-appropriate name comparison. Centralize any unavoidable identifier quoting with F02.

**Regression:** names containing underscores, percent signs, quotes, delimiters, and Unicode; confirm exact identity rather than a pattern match and confirm parameterization.

<a id="f15"></a>
### F15 — Low-level membership predicates mishandle empty and null-only lists

**P2 · Query correctness · Empty case reproduced; null-only path confirmed by code**

**Location:** [Where.cs](../../src/DataLinq/Query/Where.cs), lines 206–233 and 323–336; [Operand.cs](../../src/DataLinq/Query/Operand.cs), lines 99–114.

An empty collection passed to low-level `In`/`NotIn` is rejected by `ValueOperand` before the renderer can handle it. The renderer's purported empty-list branch instead checks `IsNull`, which means one null value, and appends a boolean fragment in the operand position after the membership operator.

**Observed:** `.Where("dept_no").In(Array.Empty<string>())` threw `ArgumentException: Value cannot be null or empty. (Parameter 'values')`. Inspection shows the null-only branch can form an invalid shape such as `column IN 1=0`.

**Repair:** model empty membership explicitly and render the complete predicate as false/true. Define null membership separately. Preserve deliberate differences between SQL null semantics and LINQ semantics. Existing LINQ translation handling does not fix this low-level API.

**Regression:** empty, one-null, mixed null/non-null, normal lists, and negation on each provider, through both SQL building and execution.

<a id="f16"></a>
### F16 — Relation reads can observe a cleared/default value array

**P2 · Concurrency · Confirmed by code; interleaving not reproduced**

**Location:** [ImmutableRelation.cs](../../src/DataLinq/Instances/ImmutableRelation.cs), lines 201–219, 244–261, and 309–318.

The fast reader checks volatile `relationValuesLoaded`, then returns a separate `relationValues` field without the load lock. `Clear` resets the array to `default` and then resets the flag under the lock. A reader can observe `true`, be interrupted by `Clear`, and return the default array; callers such as `Count` then access its `Length`. A volatile flag does not make that two-field read atomic.

Loading also obtains values before subscribing for invalidation. A change between those operations can be missed; treat that related publication window alongside F03.

**Repair:** atomically publish an immutable holder representing the loaded snapshot, or consistently synchronize the fast read and clear. Couple the initial load with the invalidation generation/subscription protocol.

**Regression:** barriers around the loaded-state check, clear, and return; concurrent relation enumeration and invalidation must return a valid snapshot or reload, never a default array or permanently missed invalidation.

<a id="f17"></a>
### F17 — First subscriptions can be registered on a discarded manager

**P2 · Cache consistency / concurrency · Confirmed by code; interleaving not reproduced**

**Location:** [TableCache.Notifications.cs](../../src/DataLinq/Cache/TableCache.Notifications.cs), lines 450 and 459; [TableCache.cs](../../src/DataLinq/Cache/TableCache.cs), line 31.

Both subscription paths initialize `notificationManager` using unsynchronized `??=`. Two first subscribers can create separate managers and subscribe to different instances. Only the manager that remains in the field receives later notifications, leaving the other subscriber's cached relation uninformed.

**Repair:** create/publish one manager through `Lazy<T>`, a lock, or `Interlocked.CompareExchange`, and always subscribe to the winning instance. Coordinate with clear/discard semantics.

**Regression:** simultaneous first subscriptions on one table followed by a mutation; every live subscriber must receive invalidation. Include transaction-specific subscriptions.

<a id="f18"></a>
### F18 — Process wrappers can deadlock while capturing output

**P2 · Tooling reliability · Confirmed by code**

**Location:** [ExternalProcessRunner.cs](../../src/DataLinq.DevTools/ExternalProcessRunner.cs), lines 40–44; [PodmanCliTransport.cs](../../src/DataLinq.Testing.CLI/Infrastructure/PodmanCliTransport.cs), lines 31–33; [PodmanSocketTransport.cs](../../src/DataLinq.Testing.CLI/Infrastructure/PodmanSocketTransport.cs), around lines 348–353.

The wrappers drain stdout synchronously to completion before reading stderr. A child that fills its stderr pipe while keeping stdout open blocks waiting for the parent; the parent blocks waiting for stdout EOF. There is no effective timeout for this capture sequence.

This is the exact dependency described by [Microsoft's redirected-process-stream documentation](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.redirectstandarderror?view=net-10.0). No intentionally hanging process was launched during the review.

**Repair:** drain both streams concurrently, await process completion and both readers, and support cancellation/timeout with controlled process-tree cleanup. Apply the fix to both text and binary-output transport branches.

**Regression:** a child writes more than pipe capacity to stderr and stdout in alternating bursts; enforce a test deadline and assert complete output, exit code, and cleanup after cancellation.

<a id="f19"></a>
### F19 — The public no-overwrite option does not protect existing model files

**P2 · Tooling / data protection · Confirmed by code**

**Location:** [ModelGenerator.cs](../../src/DataLinq.Tools/ModelGenerator.cs), `OverwriteExistingModels` at line 87, `CreateModels` around line 118, and file planning around lines 223–236.

`ModelGeneratorOptions.OverwriteExistingModels` defaults to false, but no implementation reads it. `CreateModels` writes the plan and the writer replaces existing destinations. The CLI setting it to true does not remedy callers that deliberately pass false to the public Tools API.

**Impact:** a caller relying on the option can overwrite files it expected to preserve. This is independent of F04's rollback failure.

**Repair:** honor the option during planning and enforce it at write time to handle files created after the plan was produced. Define whether collisions fail the entire plan or are skipped, and make the result explicit.

**Regression:** preexisting hand-edited file with false/true settings, mixed existing/new targets, and a destination created between planning and execution.

<a id="f20"></a>
### F20 — Owned database commands rely on eventual provider/GC cleanup

**P2 · Resource ownership · Potential workload-dependent impact**

**Location:** [StateChange.cs](../../src/DataLinq/Mutation/StateChange.cs), lines 337–390; string overloads in [SqlDbAccess.cs](../../src/DataLinq.MySql/Shared/SqlDbAccess.cs), [SQLiteDbAccess.cs](../../src/DataLinq.SQLite/SQLiteDbAccess.cs), and provider transaction implementations.

Mutation execution creates a command without a surrounding `using`. Several string-based convenience overloads also allocate commands and delegate without disposing them. Some providers release most resources when the connection closes, but long-lived transactions can postpone that fallback and prepared-command resources should not depend on GC timing.

**What is established:** deterministic disposal is absent at these ownership sites. **Not established:** a measured native-resource leak or production memory-growth rate; provider cleanup behavior may reduce the impact.

**Repair:** dispose internally owned commands for scalar/non-query execution. For readers, transfer command ownership to a disposing reader/enumerator wrapper when appropriate. Do not dispose caller-owned commands indiscriminately.

**Validation:** counting/faulting command fakes and a long-running transaction workload; track active statements, managed/native memory, failures, and early reader disposal.

<a id="f21"></a>
### F21 — Development database ports are published broadly with known defaults

**P2 · Development-environment security · Potential external exposure**

**Location:** [TestInfraOrchestrator.cs](../../src/DataLinq.Testing.CLI/Infrastructure/TestInfraOrchestrator.cs), lines 145–174; [PodmanSocketTransport.cs](../../src/DataLinq.Testing.CLI/Infrastructure/PodmanSocketTransport.cs), line 254; [TestInfraCliSettings.cs](../../src/DataLinq.Testing.CLI/Infrastructure/TestInfraCliSettings.cs), lines 30–35.

Container creation publishes `{hostPort}:3306`; the socket transport explicitly uses `HostIp: "0.0.0.0"`. The default application/admin credentials are the public `datalinq` values, and the container environment permits root access from `%`.

**Impact:** reachable development/CI hosts may expose test databases to other machines. Windows/Podman forwarding, firewall policy, and network topology determine actual reachability; no external reachability scan was performed. This is test infrastructure, not a claim about production connection defaults.

**Repair:** bind to loopback by default with an explicit remote-access opt-in. Update both CLI and socket transport parsing together; the current port parser must accept the intended host-address syntax. Prefer generated credentials where persistent or shared environments require them.

**Validation:** inspect published bindings and probe from a second machine in an approved test network; verify local loopback works and remote connections fail under defaults.

<a id="f22"></a>
### F22 — Some public API members are callable stubs

**P3 · API reliability · Confirmed by code**

**Location:** [SqlQuery.cs](../../src/DataLinq/Query/SqlQuery.cs), lines 92–104; [Insert.cs](../../src/DataLinq/Query/Insert.cs), line 74; [Update.cs](../../src/DataLinq/Query/Update.cs), lines 15–36; [Delete.cs](../../src/DataLinq/Query/Delete.cs), lines 15–33; [ImmutableRelation.cs](../../src/DataLinq/Instances/ImmutableRelation.cs), `ImmutableRelationMock` at lines 87–135.

The public low-level mutation entry points dispatch to `Execute()` methods that throw `NotImplementedException`; update/delete command construction is also a stub. `ImmutableRelationMock<T>` accepts a sequence but throws from its members, including enumeration.

**Impact:** API consumers encounter unconditional runtime failure. This does not mean the normal transaction-based mutation API is unimplemented, and it is not a demand to ship unrelated roadmap features.

**Repair:** implement the promised surface or deprecate/hide it with an explicit supported alternative. A mock type should at least implement its advertised collection contract or cease to be presented as usable.

**Regression:** public-surface smoke tests and examples that exercise each retained entry point.

<a id="f23"></a>
### F23 — Normal verification paths suppress dependency advisory checks

**P3 · Dependency security · Potential future detection gap**

**Location:** [DotnetCommandRunner.cs](../../src/DataLinq.DevTools/DotnetCommandRunner.cs), lines 26–40; [DevToolPaths.cs](../../src/DataLinq.DevTools/DevToolPaths.cs), line 65; [run-test-shard/action.yml](../../.github/actions/run-test-shard/action.yml), line 54; [full-matrix.yml](../../.github/workflows/full-matrix.yml), line 103; benchmark workflow/tooling equivalents.

Common restore/build/test paths inject `NuGetAudit=false`, including the developer environment profile. No dedicated compensating advisory gate was found in the reviewed workflows. This can be sensible for deterministic/offline local work, but leaves newly disclosed package issues outside ordinary green test evidence.

**Current counterevidence:** the separately executed online direct/transitive audit reported no vulnerable packages across 27 projects. No present vulnerable dependency is alleged.

**Repair:** add a separate reliable CI advisory check, distinguish feed failures from a clean result, and retain explicit local/offline control. See [NuGet auditing documentation](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages).

**Validation:** exercise the gate with a known advisory in an isolated fixture and with an unavailable feed; neither should silently look like a successful clean scan.

<a id="f24"></a>
### F24 — Generator emission depends on the entire compilation

**P3 · Build/IDE performance · Potential scale-dependent cost**

**Location:** [ModelGenerator.cs](../../src/DataLinq.Generators/ModelGenerator.cs), lines 43–86 and `ExecuteForDatabase` from line 123.

The pipeline has useful structural comparers for model/enum declarations, but ultimately combines their metadata with `CompilationProvider` and runs every database through one source-output callback. Unrelated compilation edits therefore invalidate the final execution input even when declaration metadata is unchanged. Broad enum collection can also increase metadata invalidation.

**Not established:** unacceptable IDE latency on a large customer solution; semantic converter/type resolution legitimately needs compilation data. The concern is how much work is repeated, not the use of compilation itself.

**Repair/validation:** measure tracked incremental steps and editor latency first. Project only needed semantic facts into comparable per-database inputs so unrelated edits do not regenerate every output; preserve converter and cross-file semantic correctness.

**Regression:** large multi-database compilation with an unrelated method edit, relevant converter edit, and one-model edit; record rerun steps, allocation, and elapsed time.

<a id="f25"></a>
### F25 — Memory ordering performs full buffering and sorting before taking a small page

**P3 · Memory-backend performance · Potential scale-dependent cost**

**Location:** [MemoryRowExecutionPlan.cs](../../src/DataLinq.Memory/MemoryRowExecutionPlan.cs), lines 119–145 and 699–755; [MemoryQueryPlanBackend.cs](../../src/DataLinq.Memory/MemoryQueryPlanBackend.cs), ordered enumeration around line 379.

An ordered query creates a list with capacity equal to the full source row count, collects matches, then allocates merge-sort arrays before paging. A small `OrderBy(primaryKey).Take(k)` consequently still scans and sorts the matching set. Immutable source rows and cached model instances can coexist, increasing retained memory up to the seeded dataset size.

This is an optimization opportunity for the deliberately limited Memory backend, not evidence of an unbounded leak or a missing promised persistence feature. No large-dataset Memory benchmark was run in this review.

**Repair/validation:** benchmark intended dataset sizes. Consider an ordered primary-key structure reusable across the immutable dataset, top-k selection, and less eager list capacity. Preserve cancellation and current query capability boundaries.

**Regression:** sorted/unsorted input, highly selective predicates, tiny/large pages, cancellation, and memory allocation at increasing seed sizes.

<a id="f26"></a>
### F26 — A missing view is rendered as a new table

**P2 · Schema-tooling correctness · Confirmed by code**

**Location:** [SchemaComparer.cs](../../src/DataLinq.SharedCore/Validation/SchemaComparer.cs), lines 56–68; [SchemaDiffScriptGenerator.cs](../../src/DataLinq.Tools/SchemaDiffScriptGenerator.cs), lines 54–56 and 99–125; [TableDefinition.cs](../../src/DataLinq.SharedCore/Metadata/TableDefinition.cs), `ViewDefinition` at line 337.

The comparer reports a missing view with `MissingTable` and its `ViewDefinition`. Since `ViewDefinition` derives from `TableDefinition`, the script generator accepts it in the missing-table case and emits `CREATE TABLE`, ignoring its view definition/type.

**Impact:** executing the suggestion creates the wrong database object. The diff command does not execute it automatically, and its review header limits exposure, but the generated action is still incorrect.

**Repair:** branch explicitly on object kind. Emit a valid view definition only when supported and safe; otherwise emit an actionable manual-review comment, never a replacement table.

**Regression:** missing view with a definition and missing view without one on each dialect; assert no `CREATE TABLE` is emitted for a view.

<a id="f27"></a>
### F27 — New-table suggestions silently omit foreign-key/check review details

**P2 · Schema-tooling correctness · Confirmed by code**

**Location:** [SchemaComparer.cs](../../src/DataLinq.SharedCore/Validation/SchemaComparer.cs), missing-table early `continue` at line 68; [SchemaDiffScriptGenerator.cs](../../src/DataLinq.Tools/SchemaDiffScriptGenerator.cs), `AppendCreateTable` at lines 99–125; [Schema Validation and Diff](../Schema%20Validation%20and%20Diff.md), SQL-generation boundary.

For an entirely missing table, the comparer emits only the table difference and skips its individual constraint differences. The generator renders columns, primary key, and supported indexes, but no foreign keys/checks and no corresponding manual-action comments. The documented behavior says unsupported additive foreign keys/checks are commented for review; that works for separate differences but misses this new-table case.

**Impact:** a reviewed creation script can leave integrity constraints absent without identifying those omissions. Re-running validation afterward may reveal them, but the first script is incomplete as a review artifact.

**Repair:** include explicit constraint/manual-action diagnostics when creating a table, or safely generate the supported constraints in dependency order. Do not silently imply the table definition is complete.

**Regression:** entirely missing table with foreign key, check, and unsupported index; verify each omission is visible and follow-up validation produces no unexplained drift.

<a id="f28"></a>
### F28 — Provider registration is not safe for concurrent first use

**P2 · Startup concurrency · Potential, not stress-reproduced**

**Location:** [PluginHook.cs](../../src/DataLinq/Metadata/PluginHook.cs), lines 37–39; [MySQLProvider.cs](../../src/DataLinq.MySql/MySql/MySQLProvider.cs), lines 13–34; [SQLiteProvider.cs](../../src/DataLinq.SQLite/SQLiteProvider.cs), lines 30–39 and 62–64; MariaDB equivalent.

Three public static mutable dictionaries are populated behind unsynchronized `HasBeenRegistered` checks. Generic provider static constructors can call registration concurrently for different model types, and different provider families share the dictionaries. Publication across all three registries is not atomic.

**Impact:** concurrent first use may throw during dictionary access or expose partially installed registration. Applications that register providers serially during startup reduce the risk. No fresh-process concurrent-startup reproduction was performed, so this is retained as a potential issue.

**Repair:** centralize registration and publication under a shared synchronization protocol; expose read-only snapshots rather than replaceable dictionaries. A separate lock per provider family would not protect a shared dictionary from other families.

**Validation:** fresh processes, simultaneous first use across multiple generic models and provider families, and reads during registration; verify exactly one complete registration is visible.

<a id="f29"></a>
### F29 — SQLite WebAssembly has unresolved native-call compatibility warnings

**P2 · Platform compatibility · Warning reproduced; affected call reachability unverified**

**Location:** [DataLinq.BlazorWasm.csproj](../../src/DataLinq.BlazorWasm/DataLinq.BlazorWasm.csproj) and its SQLitePCLRaw dependency; local `solution-build.log`.

The successful solution build reported two `WASM0001` warning groups for `sqlite3_config` and `sqlite3_db_config`: their native varargs call shapes are unsupported in WebAssembly and would fail if invoked. This is an actual build warning, not a failed sandbox build being misclassified as a source defect.

**Not established:** that the demonstrated DataLinq browser path invokes those specific signatures. The browser compatibility smoke was not executed in this review.

**Repair/validation:** trace which SQLite initialization/configuration calls are reachable, execute the browser smoke with logging and actual CRUD/query coverage, and choose supported bindings/configuration for required calls. If the signatures are unreachable, document that evidence rather than simply suppressing the warnings.

## Coverage and review limits

The source inventory contains **760 authored C#/Razor/JS/TS files and 184,528 nonblank lines under `src/`**, across 29 source directories. That includes shared source and tests; the solution dependency audit covers 27 projects. Build outputs, downloaded dependencies, generated site output, and temporary probes are excluded from those source counts.

“Whole codebase” here means every subsystem was included in inventory, risk searches, and review planning, with deeper inspection of sensitive execution paths and their callers/tests. It does **not** mean every line received identical manual attention or that every interleaving/input was exercised. No static checklist can establish the absence of further defects.

| Area | Source footprint | Work performed / coverage |
| --- | --- | --- |
| Runtime `DataLinq` | 175 files / 34,127 lines | SQL building, LINQ/planning and prepared execution boundaries, materialization, key identity, mutation/transaction outcomes, cache/index/relations, diagnostics, ownership; unit and provider compliance suites |
| `DataLinq.SharedCore` | 84 / 15,706 | Shared metadata, immutable/frozen definitions, scalar/type contracts, model generation, validation/diff; exercised through runtime/generator/tooling tests |
| MySQL/MariaDB | 30 / 3,670 | Connection/transaction paths, SQL helpers, metadata/type conversion, provider registration; all six configured server targets plus targeted MySQL probes |
| SQLite | 11 / 1,849 | Connection ownership, SQL rendering, visibility policy, materialization; file/in-memory compliance plus targeted SQLite probes |
| Memory backend | 6 / 2,134 | Seed/read-source ownership, supported plan operations, cancellation/order execution and buffering; full Memory suite |
| Source generator | 10 / 1,405 | Incremental pipeline, semantic dependencies, diagnostics and rendering boundaries; full generator suite |
| Tools and public CLI | 18 / 4,707 | Config/schema/model generation, file writes, overwrite behavior, validation/diff, exit/error paths; unit/provider tooling tests and rollback probe |
| DevTools and Dev CLI | 48 / 18,673 | Process execution, command environments, build/test result interpretation, package/compatibility tooling and artifact handling; relevant unit coverage |
| Testing and Testing CLI | 56 / 6,303 | Test selection, provider state refresh, database lifecycle, Podman transports, port/credential defaults, process capture; complete functional matrix executed |
| Benchmark and Benchmark CLI | 28 / 8,410 | Harness/process integration, result/evidence paths, allocation/release workflows; tooling tests and a focused eviction measurement, not a release benchmark run |
| Blazor samples, AOT/trim/platform smoke projects | 28 / 1,438 | Startup/dependency configuration, smoke intent and build; full solution compiled, published/browser/native smoke execution not performed |
| Active tests and model fixtures | 266 / 86,106 | Generator/unit/Memory/compliance/MySQL/model sources inventoried; selected tests read to establish contracts and gaps; all five active suites executed |
| Repository support | Outside `src` totals | CI/actions, package/dependency configuration, PowerShell release/build scripts, DocFX configuration, benchmark-result JS rendering, contributor/user contracts; source inspection and live dependency audit |

Review boundaries and useful counterevidence:

- Ordinary query values are parameterized; F02/F14 concern names and SQL structure. Explicit raw-SQL APIs were not reported as vulnerabilities merely for accepting SQL.
- Existing transaction code explicitly handles poisoned mutations, ambiguous provider completion, and failed local finalization. These were not collapsed into a generic “rollback is unsafe” allegation.
- Current binary UUID ambiguity handling is intentional and tested; inferring a UUID layout for arbitrary 16-byte data would be a regression, not an automatic fix.
- The Memory backend's declared capability limits and future async/distributed-cache/persistence designs were not treated as missing shipped features.
- No production database, customer workload, deployment authorization flow, or external network exposure was tested. Tests used the repository's configured test infrastructure and disposable fixtures.
- No published package consumer, browser run, NativeAOT publish/run, trim publish/run, Linux/macOS execution, release performance campaign, or full schema fuzzing was performed.
- F20/F24/F25 require workload measurement; F21 requires network validation; F28 requires fresh-process concurrency tests; F29 requires reachable-call/browser validation.
- The report resides under `docs/dev-plans`, which `docfx.json` excludes from the public site. No docs navigation/site presentation was changed; report links/structure were checked directly instead of rebuilding an unaffected public site.

## Validation evidence

### Existing suites and build

| Check | Result |
| --- | --- |
| Solution restore | Passed after an initial no-restore attempt exposed missing local package assets |
| Generator suite | 66 passed, 0 failed, 0 skipped |
| Unit suite | 1,716 passed, 0 failed, 0 skipped |
| Memory suite | 141 passed, 0 failed, 0 skipped |
| Compliance: SQLite file + memory | 873 passed, 0 failed, 0 skipped |
| Compliance: MySQL 8.4 + 9.7 | 885 passed, 0 failed, 0 skipped |
| Compliance: MariaDB 10.11 + 11.4 | 885 passed, 0 failed, 0 skipped |
| Compliance: MariaDB 11.8 + 12.3 | 885 passed, 0 failed, 0 skipped |
| Provider-specific: MySQL 9.7 + 8.4 | 189 passed, 0 failed, 0 skipped |
| Provider-specific: MariaDB 10.11 + 11.4 | 193 passed, 0 failed, 0 skipped |
| Provider-specific: MariaDB 11.8 + 12.3 | 193 passed, 0 failed, 0 skipped |
| Full solution build | Exit 0; 2 warnings (`WASM0001`), 0 errors |
| Online dependency advisory audit | Exit 0; 27 projects; no vulnerable package entries and no audit error entries |

The **6,026** figure sums the suite rows above. It is a count of passing case executions, not distinct logical test methods: target-independent cases can repeat across batches. The initial quick plan additionally ran 502 SQLite-file compliance cases, and an initial MySQL-only run passed 127 cases; these overlapping runs are not added again to that total. This is constituent-suite coverage, not a claim that every release/compatibility prerequisite of every CLI plan was executed.

The first advisory request failed because network access to the feed was blocked; the permitted retry completed successfully. An initial full-build command needed PowerShell quoting around `'-m:1'`; the corrected command succeeded. Neither setup issue is listed as a product defect. One temporary provider probe also needed its logging-configuration constructor corrected before execution; no probe compile failure is counted as product evidence.

### Focused probes

| Probe | Observed result | Finding |
| --- | --- | --- |
| `PrimaryKeyLimitZero` | SQL 0 rows; entity 1 row | F01 |
| `PrimaryKeyInnerJoin` | SQL 0 rows; entity 1 row | F01 |
| `LinqPrimaryKeyTakeZero` | 1 row despite `Take(0)` | F01 |
| `IdentifierEscaping` | Column-name payload executed and returned both rows | F02 |
| `CacheInvalidationDuringMaterialization` | Database `new`; subsequent cache read `old` | F03 |
| `BackupCleanupFailureMustNotDeleteOriginal` | Original disappeared; result claimed restoration | F04 |
| `LoggingFailureClosesConnection` | Connection still open after logger exception | F05 |
| `ReaderFailureClosesConnection` | Passed for ordinary SQL syntax error | Narrows F05 |
| `TimeDurationIsNotSilentlyWrapped` | `25:00` → `01:00`; `-01:00` → `23:00` | F06 |
| `BitWidthIsPreserved` | `BIT(8)` mapped to `bool` | F07 |
| `EscapedEnumMembersArePreserved` | Two literals parsed as three members | F08 |
| `ServerOrderingPreserved` | SQL IDs `d001,d002`; entity IDs `d002,d001` | F09 |
| `CacheHistoryConcurrentSnapshot` | Linked-list snapshot threw `ArgumentException` | F10 |
| `IndexExpiryMustNotRemoveReplacement` | New replacement removed by old expiration record | F11 |
| `RowCacheEvictionScaling` | Approximately 4× time for each 2× input size | F12 |
| `SqlLoggingDoesNotExposeSecrets` | Complete synthetic password emitted | F13 |
| `EmptyInList` | `ArgumentException` before execution | F15 |

The retained probe set contains **17 executions: 1 passed, 16 reported failures/diagnostic signals**. The eviction probe deliberately throws to surface timing through the test reporter; it is not a failed performance threshold. Several other probes deliberately surface observed values rather than serving as finished regression tests. The defect-oriented runs were red as intended, and are separate from the green existing suites. Multiple probes map to F01, so these are not 16 independent bugs.

### Local artifacts and repeatable commands

Artifacts under the ignored directory `artifacts/codebase-review-2026-09-04/`:

- `baseline-quick.json`: structured original quick-run evidence, including raw-log locations.
- `compliance-matrix.log`, `mysql-matrix.log`: complete existing-suite matrix results.
- `solution-build.log`, `dependency-audit.json`: build and online advisory evidence.
- `CodebaseReviewProbes.cs`, `CodebaseReviewWriterProbe.cs`, `CodebaseReviewProviderProbes.cs`: temporary probe sources retained outside the active projects.
- `probes-expanded.log`, `writer-probe.log`, `provider-probes.log`: diagnostic results and raw-log locations.
- `source-inventory.csv`, `risk-pattern-index.txt`, `test-inventory.log`: scope/search inventory.

Those ignored artifacts remain local and will not be included in a normal commit of this document. The observed inputs/results above are preserved in the report so its conclusions do not depend on future availability of ignored files. A local `review-evidence.zip` bundles the report, retained probes, hashes, and supporting logs for preservation before cleaning build artifacts.

```powershell
# Existing baseline and complete provider coverage
.\scripts\dotnet-sandbox.ps1 restore src\DataLinq.sln -v minimal
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --plan quick --output failures
$env:DATALINQ_TEST_DB_HOST = '127.0.0.1'
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite compliance --alias all --output failures
.\scripts\dotnet-sandbox.ps1 run --project src\DataLinq.Testing.CLI -- run --suite mysql --alias all --output failures
.\scripts\dotnet-sandbox.ps1 build src\DataLinq.sln --no-restore '-m:1' -v minimal
.\scripts\dotnet-sandbox.ps1 list src\DataLinq.sln package --vulnerable --include-transitive --format json --output-version 1
```

To repeat a focused probe, temporarily copy its retained source into the corresponding active TUnit project, then select its class with `--filter '/*/*/CodebaseReviewProbes/*'`, `--filter '/*/*/CodebaseReviewWriterProbe/*'`, or `--filter '/*/*/CodebaseReviewProviderProbes/*'`. The first two use `--suite unit`; the provider class uses `--suite mysql --targets mysql-9.7`. Remove the copied source afterward. The writer probe uses disposable scratch files and a Windows file-attribute failure condition.

## Recommended repair sequence

1. **Release-blocking correctness and data protection:** F01–F04. Add permanent regressions at the public query/cache/file-writing boundaries before changing optimization or rollback code.
2. **Silent data changes and lifecycle faults:** F05–F11, F15–F17, F19, F26–F27. Keep provider metadata repairs separate enough to review their compatibility implications.
3. **Security exposure and operational reliability:** F13–F14, F18, F21, F28. Reachability changes priority: applications accepting external identifier choices should treat F02/F14 as urgent, and exposed shared test hosts should prioritize F21.
4. **Measured performance/resource work:** F12 first because quadratic behavior was measured; then validate F20/F24/F25 against representative workloads before larger redesigns.
5. **Release confidence and API hygiene:** F22–F23 and F29. Resolve or explicitly evidence the WebAssembly warning boundary before claiming that platform path is validated.

Each item should be closed with its stated regression/validation evidence, not merely a source edit. Concurrency findings need deterministic interleaving tests; provider type findings need live round trips; performance findings need scaling and latency measurements.
