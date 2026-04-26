# Troubleshooting

This page is for the failure modes that are actually common in DataLinq, not generic "have you tried restarting your ORM" advice.

## CLI Cannot Decide Which Database or Provider to Use

If your `datalinq.json` contains more than one database entry, pass `-n`.

If the selected database contains more than one connection type, pass `-t`.

Examples:

```bash
datalinq create-models -n AppDb
datalinq create-models -n AppDb -t MariaDB
```

If you do not disambiguate, the CLI has to guess. Guessing is how bad tooling earns a reputation.

## Generated Files Keep Getting Overwritten

That is expected.

Generated output is generated output. Do not hand-edit it and then act surprised when regeneration replaces it.

Keep hand-written changes in your source model files or partial classes, not in generated files.

## A Query Throws `NotImplementedException`

That usually means the LINQ translator does not support the exact expression shape you wrote.

What to do:

1. reduce the query to the documented surface in [Supported LINQ Queries](Supported%20LINQ%20Queries.md)
2. prefer `Where(...).Any()` over elaborate `Any(predicate)` shapes
3. prefer explicit ordering plus `First()` over relying on `Last()` to mean "highest"
4. if the query really should be supported, add a focused test first

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

The raw transaction controls the actual database commit or rollback. The DataLinq wrapper still needs to finish so its own lifecycle and cache state are completed.

## SQLite and MySQL/MariaDB Behave Differently in Transaction Visibility Tests

They do.

The current providers do not use the same isolation level defaults:

- SQLite uses `ReadUncommitted`
- MySQL and MariaDB use `ReadCommitted`

So cross-connection visibility of uncommitted writes is not identical. Write tests accordingly.

## Relation Reads Look Stale During a Complex Write Flow

If several related operations must behave as one unit, do them inside one explicit transaction and read through `transaction.Query()`.

That keeps the read and write path inside the same transaction-aware cache context.
