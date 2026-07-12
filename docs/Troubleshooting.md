# Troubleshooting

This page is for the failure modes that are actually common in DataLinq, not generic "have you tried restarting your ORM" advice.

## CLI Cannot Decide Which Database or Provider to Use

If your `datalinq.json` contains more than one database entry, pass `-n`.

If the selected database contains more than one connection type, pass `-p`.

Examples:

```bash
datalinq generate models -n AppDb
datalinq generate models -n AppDb -p MariaDB
```

If you do not disambiguate, the CLI has to guess. Guessing is how bad tooling earns a reputation.

## Secret Reference Cannot Be Resolved

Secret references are resolved only when a CLI command needs the value.

Common causes:

- `${env:NAME}` fails when the environment variable is missing or empty.
- `${secret:name}` fails when the DataLinq local secret does not exist.
- `${secret:name}` also fails on platforms without a secure local backend. The current local backend is Windows Credential Manager.
- `${prompt:label}` fails in non-interactive runs such as CI.

For CI, prefer environment variables:

```json
{
  "ConnectionString": "Server=localhost;Database=appdb;User ID=app;Password=${env:DATALINQ_APPDB_PASSWORD};"
}
```

For local Windows development, store the value once:

```bash
datalinq secrets set datalinq/AppDb/password
```

then reference it:

```json
{
  "ConnectionString": "Server=localhost;Database=appdb;User ID=app;Password=${secret:datalinq/AppDb/password};"
}
```

## Generated Files Keep Getting Overwritten

That is expected.

Generated output is generated output, but CLI model declaration files have a supported edit surface.

It is OK to rename generated model classes, scalar properties, relation properties, and C# property types in `ModelDirectory`. DataLinq reads those files on the next regeneration and preserves supported edits.

Keep custom methods and behavior in separate partial classes. Do not change mapping attributes unless the database mapping itself changed. See [Model Generation](model-generation.md) for the exact rules.

Newer generated C# files also start with a DataLinq generated-file banner and an explicit `#nullable` directive. Those lines are owned by DataLinq too.

If you pass `--stamp-generated-header`, the generated header includes the CLI version and a UTC timestamp. That is useful for provenance, but it creates a source diff every time. Leave it off when you want deterministic regenerated files.

## Generated Files Suddenly Have Nullable Reference Annotations

That is intentional.

`UseNullableReferenceTypes` now defaults to enabled. Omitted config behaves like:

```json
{
  "UseNullableReferenceTypes": true
}
```

Generated files declare their own nullable context with `#nullable enable`, so they do not depend on your project-level nullable setting.

If you need the old generated shape, opt out explicitly:

```json
{
  "UseNullableReferenceTypes": false
}
```

That makes DataLinq emit `#nullable disable` in generated files and avoid nullable reference annotations.

## `validate` Reports Several Errors At Once

That is a feature, not a cascade by default.

The CLI now reports all independent validation issues it can reach. If one broken attribute prevents one table from being trustworthy, DataLinq should still report unrelated broken attributes or provider metadata issues it can honestly inspect.

The rule is still conservative: when validation issues exist, `diff` writes no SQL file and `generate models` does not replace generated model files after validation or rendering errors.

## A Query Throws `QueryTranslationException`

That usually means the LINQ translator does not support the exact expression shape you wrote.

What to do:

1. reduce the query to the documented surface in [Supported LINQ Queries](Supported%20LINQ%20Queries.md)
2. prefer `Where(...).Any()` over elaborate `Any(predicate)` shapes
3. prefer explicit ordering plus `First()` over relying on `Last()` to mean "highest"
4. if the query really should be supported, add a focused test first

The exception message should name the unsupported method, operator, selector, or predicate expression. If it does not, that is a diagnostics bug worth fixing.

## `First()` or `Last()` Returns a Surprising Row

You did not order explicitly.

Database row order is not a contract unless you make it one. Write the `OrderBy(...)` you mean.

## `Save()` Inserted When You Expected an Update

`Save()` chooses between insert and update based on whether the mutable instance is considered new.

If you need certainty:

- use `Insert()` for new rows
- use `Update()` for existing rows
- use `IsNew()` when debugging mutable lifecycle behavior

## `Update()` Did Nothing

That may be correct.

If the mutable instance has no tracked changes, `Update()` is intentionally a no-op and returns the cached immutable row instead of issuing a meaningless write.

Check `HasChanges()` before assuming the ORM ignored you.

## Attached ADO.NET Transaction Behavior Looks Odd

When you use `AttachTransaction(...)`, you are managing two layers:

- the underlying `IDbTransaction`
- the DataLinq transaction wrapper

Once attached, finish through the DataLinq wrapper only. Calling `Commit()`, `Rollback()`, or `Dispose()` on the original handle—or completing through `transaction.DatabaseAccess`—bypasses DataLinq's mutable-lifecycle and cache coordination.

If the original handle was already completed, do not call both commits and hope they cancel out. The wrapper will report an unknown external-completion outcome, invalidate transaction-derived state, and clear caches conservatively where the provider exposes the inactive handle. Dispose the wrapper if needed, discard transaction-bound rows and mutables, and query fresh committed rows through the database.

Also remember that raw SQL writes are not reconstructed into DataLinq cache or relation publication. Explicitly invalidate affected cache entries after a lower-level write, or keep the entire mapped write flow inside the wrapper.

See [Attaching an Existing ADO.NET Transaction](Transactions.md#attaching-an-existing-adonet-transaction) for the full ownership contract.

## SQLite and MySQL/MariaDB Behave Differently in Transaction Visibility Tests

They do, but DataLinq-owned SQLite paths no longer opt into dirty reads.

Owned SQLite connections reset `PRAGMA read_uncommitted = false`, and owned transactions use deferred `Serializable` isolation. MySQL and MariaDB use `ReadCommitted`. Both give DataLinq committed visibility, but SQLite remains snapshot-oriented and single-writer rather than becoming a clone of MySQL transaction semantics.

For file-backed concurrency tests, use WAL with private/default cache. If an explicit SQLite shared-cache connection reports `SQLITE_LOCKED` while another transaction is writing, that is real table-lock behavior—not permission to enable dirty reads. Attached transactions keep the caller's SQLite pragmas, so inspect the supplied connection policy separately.

## Relation Reads Look Stale During a Complex Write Flow

If several related operations must behave as one unit, do them inside one explicit transaction and read through `transaction.Query()`.

That keeps the read and write path inside the same transaction-aware cache context.

## Byte Cache Limits Remove Rows Earlier Than Expected

Byte-based cache limits use `EstimatedCacheBytes`, not the old row-payload-only value.

That is deliberate. Row payload alone ignores row-store objects, provider keys, transaction-local caches, index caches, relation subscriptions, notification queues, and cache snapshots. If a `CacheLimitType.Megabytes` limit now removes rows sooner than an older build did, check:

- `DataLinqMetrics.Snapshot().Occupancy.RowPayloadBytes`
- `DataLinqMetrics.Snapshot().Occupancy.EstimatedCacheBytes`
- the component byte fields such as index, notification, transaction, and snapshot bytes

If `EstimatedCacheBytes` is high while `RowPayloadBytes` is modest, the cache is probably retaining memory in indexes, relation state, or transaction-local rows. That is exactly the case the newer estimate is meant to reveal.

## Memory-Pressure Cleanup Does Not Run

Memory-pressure cleanup is disabled by default and unsupported in browser/WebAssembly runtimes.

For server or desktop runtimes, configure it explicitly:

```csharp
using DataLinq.Cache;

database.Provider.State.Cache.ConfigureMemoryPressureCleanup(
    CacheMemoryPressureCleanupPolicy.Conservative);
```

It still will not run unless the runtime reports high memory load and the cache is at least `MinimumCacheBytes`. Cooldown and per-pass row/byte budgets also limit how much one cleanup pass can remove.
