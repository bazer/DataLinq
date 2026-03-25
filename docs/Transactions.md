# Transactions

DataLinq supports both implicit and explicit transactions.

Use implicit transactions for one-off writes.
Use explicit transactions when several writes, reads, or relation updates must happen as one unit.

The default transaction type is `TransactionType.ReadAndWrite`. There are also `ReadOnly` and `WriteOnly` modes when you want to be explicit about intent.

## Implicit Transactions

Single-operation write helpers open and complete the transaction for you.

Typical examples:

```csharp
var updated = employeesDb.Update(employeeMut);
var saved = employeesDb.Save(employeeMut);
var inserted = employeesDb.Insert(new MutableEmployee { /* ... */ });
employeesDb.Delete(existingEmployee);
```

This is the right choice when you only need one write and do not care about grouping several steps together.

## Explicit Transactions

Use an explicit transaction when you want several operations to succeed or fail together.

```csharp
using var transaction = employeesDb.Transaction();

var employee = transaction.Query().Employees.Single(x => x.emp_no == 999997).Mutate();
employee.birth_date = new DateOnly(1984, 12, 24);

transaction.Update(employee);
transaction.Commit();
```

Inside a transaction you can:

- query through `transaction.Query()`
- insert with `transaction.Insert(...)`
- update with `transaction.Update(...)`
- delete with `transaction.Delete(...)`
- save with `transaction.Save(...)`

## Convenience Transaction Callback

The test suite also uses the higher-level commit helper:

```csharp
employeesDb.Commit(transaction =>
{
    transaction.Insert(new MutableEmployee { /* ... */ });
    transaction.Insert(new MutableDepartment { /* ... */ });
});
```

That pattern is useful for short setup or maintenance operations.

## Attaching an Existing ADO.NET Transaction

If you already have a raw `IDbTransaction`, you can attach DataLinq to it:

```csharp
using IDbConnection dbConnection = employeesDb.Provider.GetDbConnection();
dbConnection.Open();

using var dbTransaction = dbConnection.BeginTransaction(IsolationLevel.ReadCommitted);
using var transaction = employeesDb.AttachTransaction(dbTransaction);

var dept = transaction.Query().Departments.Single(x => x.DeptNo == "d099");
dbTransaction.Commit();
transaction.Commit();
```

This is advanced, but it is real supported behavior and worth documenting because it gives you a bridge to lower-level ADO.NET flows.

The important subtlety is that the underlying ADO.NET transaction and the DataLinq wrapper each have work to finish. The raw transaction controls the database commit. The DataLinq wrapper also needs to complete so it can finish its own transaction bookkeeping and cache merge.

## Transaction Semantics

### Within a transaction

Within the same transaction:

- repeated reads of the same row return the same immutable instance
- transaction-local changes are visible through `transaction.Query()`
- relation updates are visible inside the transaction once inserted or updated rows exist there

### After commit

After `Commit()`:

- transaction-local cache entries are merged into the global cache
- later queries outside the transaction see the committed state
- the transaction should be treated as finished

### After rollback

After `Rollback()`:

- transaction-local changes are discarded
- later queries outside the transaction see the old committed state
- the transaction should be treated as finished

### Single-use lifecycle

The test suite explicitly covers that calling `Commit()` or `Rollback()` again after completion throws.

That is the correct behavior. A transaction object is not a reusable session object.

## Provider Caveat: SQLite vs MySQL/MariaDB

One provider-specific caveat is already visible in the tests and transaction implementations:

- SQLite transactions are opened with `ReadUncommitted`
- MySQL and MariaDB transactions are opened with `ReadCommitted`
- SQLite may therefore expose uncommitted writes to other connections in scenarios where MySQL and MariaDB do not

So if you are writing cross-provider tests around visibility of uncommitted data, do not assume identical behavior.

## Relations Inside Transactions

The transaction tests cover relation-aware inserts such as adding a salary row to an employee.

That matters because DataLinq is not just writing the row. It is also maintaining the relation view that the in-memory object graph sees.

Example pattern:

```csharp
using var transaction = employeesDb.Transaction();

var employee = transaction.Query().Employees.Single(x => x.emp_no == empNo);

var salary = transaction.Insert(new MutableSalaries
{
    emp_no = employee.emp_no.Value,
    salary = 50000,
    FromDate = new DateOnly(2020, 1, 1),
    ToDate = new DateOnly(2020, 12, 31)
});

transaction.Commit();
```

Within the transaction, relation reads such as `employee.salaries` are covered by tests and should reflect the transaction-local state.

## When to Use What

- Use implicit transactions for simple single writes.
- Use explicit transactions for multi-step workflows, relation-heavy updates, or when you need reads and writes to share one transaction scope.
- Use `AttachTransaction(...)` only when you already have a real reason to control the ADO.NET transaction yourself.
