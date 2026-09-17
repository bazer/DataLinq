> [!WARNING]
> This is a future-release planning inventory. It does not declare these async APIs implemented, compiled, or provider-verified.

# 0.10 Async Signature Inventory And Compatibility Matrix

**Status:** Consolidation of AAPI-1 through AAPI-111. G01–G03 and E01–E06 design policies are accepted; W0 capture, implementation, emitted manifests and product evidence remain open.

**Target:** 0.10 / A10, with H10, V10 and T10 integration.

**Last reviewed:** 2026-09-16.

**Source audit baseline:** `58142e7d`. The runtime sources examined below still describe the synchronous baseline. AAPI-100 through AAPI-111 were accepted on 2026-09-16; accepting them does not implement the target members.

**Authority:** [Async Public API Decisions](Async%20Public%20API%20Decisions.md) owns accepted contracts. This inventory expands and cross-checks them against source; it cannot silently approve a new API or compatibility break. [Implementation Order](Implementation%20Order%20and%20Integration%20Plan.md) owns sequencing and [Release Evidence](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md) owns exit gates.

## Reading The Inventory

- **Accepted target** means the family and stated contract follow an accepted AAPI decision. It is not evidence of implementation.
- **Expansion** means a concrete declaration or placement proposed to complete that family. Review the identified details before freezing the public API.
- **Question** means source revealed an uncovered boundary or a choice not made by the accepted decisions.
- **Excluded** means retain the existing synchronous or unsupported boundary.
- Every DataLinq consumer, ApiCompat and runtime check below is **pending**. The isolated C#/.NET 10 declaration probe in S10 is limited language/framework evidence, not product verification.

To keep tables readable, `ct` means the exact final parameter `CancellationToken cancellationToken = default`; `txType` means `TransactionType transactionType = TransactionType.ReadAndWrite`. These are documentation abbreviations, not proposed parameter names. `D` means a database model with `where D : class, IDatabaseModel<D>`. Additional model constraints are stated per family and must not be widened or tightened accidentally.

Ordinary awaitable execution validates and captures supported inputs before first suspension. Ordinary sequences capture at `GetAsyncEnumerator`; prepared sequences capture arguments at `ExecuteAsync`. Neither constructing a sequence nor its enumerator performs database I/O. Method and enumerator tokens both apply. Disposal is parameterless `ValueTask DisposeAsync()`.

## S1: Query And Prepared Execution

Accepted target: AAPI-6/AAPI-8, AAPI-17 through AAPI-22, AAPI-42 through AAPI-48.

Place query extensions on `DataLinq.Linq.DataLinqAsyncQueryableExtensions`. Every query receiver is `this IQueryable<T> source` with unconstrained `T`. Validate the actual execution provider, including a DataLinq query object constructed around a foreign provider. Available overloads do not expand supported translation or Memory query shapes.

| ID | Member after the receiver | Result | Overload expansion |
| --- | --- | --- | --- |
| Q01 | `AsAsyncEnumerable<T>(ct)` | `IAsyncEnumerable<T>` | One overload |
| Q02 | `ToListAsync<T>(ct)` | `ValueTask<List<T>>` | No predicate overload |
| Q03 | `ToArrayAsync<T>(ct)` | `ValueTask<T[]>` | No predicate overload |
| Q04 | `FirstAsync<T>`, `SingleAsync<T>`, `LastAsync<T>` | `ValueTask<T>` | Each has `(ct)` and `(Expression<Func<T, bool>> predicate, ct)` |
| Q05 | `FirstOrDefaultAsync<T>`, `SingleOrDefaultAsync<T>`, `LastOrDefaultAsync<T>` | `ValueTask<T?>` | Same two forms; no caller-supplied default |
| Q06 | `AnyAsync<T>` | `ValueTask<bool>` | Same two forms |
| Q07 | `CountAsync<T>` | `ValueTask<int>` | Same two forms |
| Q08 | `SumAsync<T>(Expression<Func<T, N>> selector, ct)` | `ValueTask<N>` | Ten numeric forms below; `N` is table notation, not a generic arithmetic type |
| Q09 | `AverageAsync<T>(Expression<Func<T, N>> selector, ct)` | `ValueTask<A>` | Ten numeric forms below |
| Q10 | `MinAsync<TSource, TResult>(Expression<Func<TSource, TResult>> selector, ct)`; corresponding `MaxAsync` | `ValueTask<TResult?>` | Exact unconstrained declaration accepted by AAPI-103/E01; receiver is `IQueryable<TSource>` |

For Q08/Q09, expand each row and its nullable counterpart into an actual overload:

| Selector `N` | Sum | Average `A` |
| --- | --- | --- |
| `int` / `int?` | `int` / `int?` | `double` / `double?` |
| `long` / `long?` | `long` / `long?` | `double` / `double?` |
| `float` / `float?` | `float` / `float?` | `float` / `float?` |
| `double` / `double?` | `double` / `double?` | `double` / `double?` |
| `decimal` / `decimal?` | `decimal` / `decimal?` | `decimal` / `decimal?` |

No selector-free aggregates, `LongCountAsync`, `AllAsync`, standalone `ContainsAsync`, `ElementAtAsync`, async predicates, or local delegate fallback enter the provider surface. Converter-backed aggregate rejection remains. For unconstrained `T`, `T?` does not make a non-nullable value result nullable: empty `int` OrDefault remains zero. Annotations, empty/null aggregate behavior, conversion and overflow require real consumer/backend evidence.

| ID | Receiver in `DataLinq.Linq` | Exact target member |
| --- | --- | --- |
| Q11 | `PreparedQuery<D, TArgument, TResult>` | `Task<TResult> ExecuteAsync(IDataSourceAccess<D> source, TArgument argument, ct)` |
| Q12 | `PreparedSequenceQuery<D, TArgument, TElement>` | `IAsyncEnumerable<TElement> ExecuteAsync(IDataSourceAccess<D> source, TArgument argument, ct)` |

Preparation remains synchronous. Prepared sources remain SQL `IDataSourceAccess<D>` sources; no Memory or neutral-read-source overload. Repeat sequence enumeration uses the captured prepared invocation, whereas a new execution call captures new arguments. No prepared `ExecuteToListAsync` or `ExecuteToArrayAsync` convenience family.

Sources: [Queryable](../../../../src/DataLinq/Linq/Queryable.cs), [terminal parser](../../../../src/DataLinq/Linq/Planning/Expressions/ExpressionQueryPlanParser.cs), [aggregate validation](../../../../src/DataLinq/Linq/Planning/QueryPlanAggregateSelectorValidator.cs), [prepared queries](../../../../src/DataLinq/Linq/PreparedQuery.cs).

## S2: Collections, References And Key Lookup

Accepted target: AAPI-5/AAPI-9/AAPI-11 through AAPI-20, AAPI-39 through AAPI-41, AAPI-49 through AAPI-55, AAPI-73/AAPI-75/AAPI-76/AAPI-89.

`DataLinq.Instances.IImmutableRelation<T>` retains `IEnumerable<T>` and `where T : IModelInstance`. It does not also inherit `IAsyncEnumerable<T>`.

| ID | Relation member | Result / placement |
| --- | --- | --- |
| R01 | `AsAsyncEnumerable(ct)` | `IAsyncEnumerable<T>`; sole required capability primitive, with unsupported default |
| R02 | `ValuesAsync(ct)` | `ValueTask<ImmutableArray<T>>` |
| R03 | `KeysAsync(ct)` | `ValueTask<ImmutableArray<DataLinqKey>>` |
| R04 | `GetAsync(DataLinqKey key, ct)` | `ValueTask<T?>`; membership in this relation |
| R05 | `ContainsKeyAsync(DataLinqKey key, ct)` | `ValueTask<bool>` |
| R06 | `ToFrozenDictionaryAsync(ct)` | `ValueTask<FrozenDictionary<DataLinqKey, T>>` |
| R07 | `ToListAsync(ct)` / `ToArrayAsync(ct)` | `ValueTask<List<T>>` / `ValueTask<T[]>` |
| R08 | Six element terminals, `AnyAsync` and `CountAsync` | Same result families as Q04–Q07; predicate forms use `Func<T, bool>` |
| R09 | Selector reductions | AAPI-104/E02: ten local `Func` Sum forms, ten Average forms, generic selector Min/Max; no provider expressions or relation-query conversion |
| R10 | `AsKeyValuePairs()` | Synchronous approved rename of keyed `AsEnumerable()`; no `AsKeyValuePairsAsync` |

R02–R09 have overridable shared defaults over genuine async execution. Dependency direction: row view → completed values → keyed result → key lookup/accessors. Row terminals/materializers/reductions consume the async row view directly. Built-ins may optimize without reading synchronous getters after awaiting.

Expose these members deliberately on built-in concrete relations and public testing helpers, including `ImmutableRelationMock<T>`. Interface defaults alone do not provide concrete member visibility; casting `this` to the interface and calling the same overridden member can recurse. Custom sync-only implementations fail explicitly for unsupported async execution. The rename remains a separate approved migration.

~~~csharp
// DataLinq.Instances; invariant capability alongside unchanged covariance.
public interface IAsyncImmutableForeignKey<T> : IImmutableForeignKey<T>
    where T : IImmutableInstance
{
    ValueTask<T?> GetAsync(CancellationToken cancellationToken = default);
}
~~~

| ID | Receiver | Member / constraint |
| --- | --- | --- |
| R11 | Built-in foreign-key holder and companion interface | `ValueTask<T?> GetAsync(ct)`; retain `where T : IImmutableInstance`, without adding `class` |
| R12 | Generated public model base | `public virtual ValueTask<TTarget> PropertyNameAsync(ct)` for required references; `ValueTask<TTarget?>` for optional references |
| K01 | `Database<D>` and `Transaction<D>` | `ValueTask<M?> GetAsync<M>(DataLinqKey key, ct) where M : IImmutableInstance` |
| K02 | Generated static model helper | `ValueTask<M?> GetAsync(<typed key components>, <source>, ct)`; components retain metadata order/names and model-side types |
| K03 | `IImmutable<T> where T : IModel` | `static ValueTask<T?> GetByProviderKeyAsync<TKey>(TKey key, IDataSourceAccess dataSource, ct) where TKey : notnull` |
| K04 | `DataLinq.Cache.TableCache` | `ValueTask<IImmutableInstance?> GetRowAsync<TKey>(TKey primaryKey, IDataSourceAccess dataSource, ct) where TKey : notnull` |
| K05 | `DataLinq.Memory.MemoryDatabase<D>` | `ValueTask<M?> FindAsync<M>(object modelPrimaryKey, ct) where M : class, IImmutableInstance, ITableModel<D>` |

K02 expands into the existing three source forms: `IDataSourceAccess dataSource`, `Database<D> database`, `Transaction<D> transaction`. It does not add members to `IDataSourceAccess` itself. Generated typed keys normalize model values exactly once; K01/K03/K04 use canonical provider keys. Preserve null-key sentinel behavior; K04 retains its existing read-only-source fallback. K05 remains single-column model-key lookup, not a composite/provider-key or SQL `FindAsync` family.

Generated navigation shares current relation state, never reads the synchronous property first, and is not automatically added to generated model interfaces. Required missing references throw `InvalidOperationException` in both sync and async paths. Duplicate targets fail cardinality; optional absence alone yields null. Async capability is invariant even when a synchronous foreign-key holder can be widened covariantly.

`DLG004` is an error titled `Async navigation member conflict`, located at the relation with an additional conflicting-member location when available. Test real binding conflicts, inherited/partial members, harmless overloads and valid overrides. Do not silently rename methods; isolate invalid generation to the affected database.

Sources: [relations](../../../../src/DataLinq/Instances/ImmutableRelation.cs), [foreign keys](../../../../src/DataLinq/Instances/ImmutableForeignKey.cs), [provider-key helper](../../../../src/DataLinq/Instances/InstanceFactory.cs), [cache lookup](../../../../src/DataLinq/Cache/TableCache.RowLookup.cs), [generator](../../../../src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs), [Memory](../../../../src/DataLinq.Memory/MemoryDatabase.cs).

## S3: Mutations, Completion And Ownership

Accepted target: AAPI-1 through AAPI-4, AAPI-8, AAPI-21 through AAPI-38, AAPI-60/AAPI-61/AAPI-68 through AAPI-72/AAPI-101.

For the core mutation table, `M : class, IImmutableInstance` and `TMutable : Mutable<M>` unless the row states otherwise. All members have the final optional `ct` parameter.

| ID | Receiver | Target signatures before `ct` |
| --- | --- | --- |
| M01 | `Database<D>` | `Task<M> InsertAsync<M>(Mutable<M> model, txType)`; corresponding `UpdateAsync` and `SaveAsync` |
| M02 | `Database<D>` | `Task DeleteAsync<M>(M model, txType) where M : IModelInstance` |
| M03 | `Transaction` | `Task<M> InsertAsync<M>(Mutable<M> model)`; `InsertAsync<M>(Mutable<M> model, Action<Mutable<M>> changes)`; `InsertAsync<M, TMutable>(TMutable model, Action<TMutable> changes)` |
| M04 | `Transaction` | `Task<List<M>> InsertAsync<M>(IEnumerable<Mutable<M>> models)` |
| M05 | `Transaction` | `Task<M> UpdateAsync<M>(Mutable<M> model)`; `UpdateAsync<M>(Mutable<M> model, Action<Mutable<M>> changes)`; `UpdateAsync<M, TMutable>(TMutable model, Action<TMutable> changes)`; `UpdateAsync<M>(M model, Action<Mutable<M>> changes)` |
| M06 | `Transaction` | `Task<M> SaveAsync<M>(Mutable<M> model)`; `SaveAsync<M>(M? model, Action<Mutable<M>> changes)`; `SaveAsync<M>(Mutable<M>? model, Action<Mutable<M>> changes)`; `SaveAsync<M, TMutable>(TMutable model, Action<TMutable> changes)` |
| M07 | `Transaction` | `Task DeleteAsync(IModelInstance model)`; nongeneric, with no transaction-type parameter |
| M08 | `DataLinq.IModelExtensions` | `Task<M> InsertAsync<M>(this Mutable<M> model, Transaction transaction)`; corresponding `UpdateAsync` and `SaveAsync` |
| M09 | Same extension class | `Task DeleteAsync<M>(this M model) where M : IImmutableInstance`; `Task DeleteAsync<M>(this M model, Transaction transaction) where M : IModelInstance` |
| T01 | `Transaction`, inherited by `Transaction<D>` | `Task CommitAsync(ct)`; `Task RollbackAsync(ct)`; `ValueTask DisposeAsync()` |
| T02 | Owning `Database<D>` / provider roots | `ValueTask DisposeAsync()` through `IAsyncDisposable`; AAPI-105/E03 fixes interface/default placement; old/custom product evidence remains pending |
| T03 | `Database<D>` | Four `CommitAsync` callback forms using `Transaction<D>` below |
| T04 | `IDatabaseProvider` / `DatabaseProvider` | Same four forms using untyped `Transaction`; no competing typed generic-provider family |
| T05 | `DataLinq.DatabaseTransaction` | Public virtual `Task CommitAsync(ct)`, `Task RollbackAsync(ct)`, `ValueTask DisposeAsync()`; unsupported legacy defaults and built-in overrides under AAPI-101 |

In M03/M05/M06, each listed overload returns `Task<M>`, including forms where the return type is not repeated. Only the two nullable editing forms in M06 mean “null starts a new mutable.” Direct mutable and typed mutable forms reject null. Generated immutable-model `SaveAsync` helpers retain their existing update-alias semantics.

For T03/T04, substitute the receiver's transaction type for `TTransaction`; this is notation, not a new public generic parameter:

~~~csharp
Task CommitAsync(
    Func<TTransaction, Task> action,
    TransactionType transactionType = TransactionType.ReadAndWrite,
    CancellationToken cancellationToken = default);
Task CommitAsync(
    Func<TTransaction, CancellationToken, Task> action,
    TransactionType transactionType = TransactionType.ReadAndWrite,
    CancellationToken cancellationToken = default);
Task<TResult> CommitAsync<TResult>(
    Func<TTransaction, Task<TResult>> action,
    TransactionType transactionType = TransactionType.ReadAndWrite,
    CancellationToken cancellationToken = default);
Task<TResult> CommitAsync<TResult>(
    Func<TTransaction, CancellationToken, Task<TResult>> action,
    TransactionType transactionType = TransactionType.ReadAndWrite,
    CancellationToken cancellationToken = default);
~~~

Callbacks execute once; helpers own completion and return results after finalization/cleanup. Tokens are explicit, not ambient. No `Action`, synchronous-result or `ValueTask` callback alternatives. Editing delegates remain synchronous local `Action` callbacks; they are not transaction callbacks.

### Generated Mutation Expansion

M10 covers the existing generated extension templates; retain their emitted namespace and argument names. `M` is the generated public model and `MM` its generated mutable type. Every row returns `Task<M>` and appends `ct`. Multiple entries in a cell are separate overloads; no implicit Cartesian product is intended.

| Member | Receiver and remaining existing arguments |
| --- | --- |
| `InsertAsync` | `this MM model, Database<D> database` |
| `InsertAsync` | `this MM model, Action<MM> changes, Transaction transaction`; same with `Database<D> database` |
| `InsertAsync` | `this Transaction transaction, MM model, Action<MM> changes` |
| `UpdateAsync` | `this M model, Action<MM> changes`; same with trailing `Transaction transaction` |
| `UpdateAsync` | `this Database<D> database, M model, Action<MM> changes`; same receiver form with `Transaction transaction` |
| `UpdateAsync` | `this MM model, Database<D> database` |
| `SaveAsync` | `this M model, Action<MM> changes`; same with trailing `Transaction transaction` or `Database<D> database` |
| `SaveAsync` | `this Database<D> database, M model, Action<MM> changes`; same receiver form with `Transaction transaction` |
| `SaveAsync` | `this MM model, Database<D> database`; `this MM model, Transaction transaction` |
| `SaveAsync` | `this MM model, Action<MM> changes, Transaction transaction`; same with `Database<D> database` |
| `SaveAsync` | `this Transaction transaction, MM model, Action<MM> changes` |

Source-less helpers resolve the provider through the model's existing source and own an independent transaction. Validate that source before suspension, including invalid/poisoned originating-transaction restrictions; obtaining its provider must not bypass those checks. Passing a transaction is how callers join that transaction; no ambient enlistment. Database helpers own implicit transactions, whereas transaction helpers do not complete the caller's transaction. Memory-backed and detached models gain no implicit SQL persistence.

Capture mutable identity/values and invoke local edits before suspension. Capture/preflight the entire finite collection once before any insert; reject repeated mutable-object inputs. No async-stream input, batch update/save/delete, `InsertRangeAsync` alias, automatic ordering or retry. Unchanged updates may still perform an async lookup.

A transaction has one execution owner across sync/async work, readers, finalization and cleanup. An active reader holds that ownership between moves. Reject competing operations and caller disposal without corrupting the owner. Helper recovery closes admission, settles unfinished work and cleans up without committing or abandoning live provider work.

Sources: [database root](../../../../src/DataLinq/Database.cs), [managed transaction](../../../../src/DataLinq/Mutation/Transaction.cs), [model extensions](../../../../src/DataLinq/Extensions/IModelExtensions.cs), [generated extensions](../../../../src/DataLinq.SharedCore/Factories/Generator/GeneratorFileFactory.cs).

## S4: Lower-Level Execution And Fluent Reads

Accepted target: AAPI-56 through AAPI-61, AAPI-82/AAPI-86 through AAPI-89/AAPI-100/AAPI-101. G01/G02 below retain the resolved audit findings.

On `DataLinq.Interfaces.IDatabaseAccess` and `DataLinq.DatabaseAccess`, each L01–L05 row expands into two overloads: `(string query, ct)` and `(IDbCommand command, ct)`.

| ID | Member | Result |
| --- | --- | --- |
| L01 | `ExecuteNonQueryAsync` | `Task<int>` |
| L02 | `ExecuteScalarAsync` | `Task<object?>` |
| L03 | `ExecuteScalarAsync<T>` | `Task<T>` |
| L04 | `ExecuteReaderAsync` | `Task<IDataLinqAsyncDataReader>` |
| L05 | `ReadReaderAsync` | `IAsyncEnumerable<IDataLinqDataReader>` |

New interface/default base execution reports `NotSupportedException` unless genuine async capability exists. Built-in concrete access classes implement/expose it. A `DbCommand` base type alone is not evidence of native async support; verify actual provider dispatch and reject unsupported commands before I/O.

~~~csharp
// DataLinq
public interface IDataLinqAsyncDataReader : IDataLinqDataReader, IAsyncDisposable
{
    Task<bool> ReadNextRowAsync(CancellationToken cancellationToken = default);
}
~~~

L06 is this reader capability. Existing readers remain synchronously valid. Current-row getters remain synchronous and must not hide further I/O. Borrowed row views are ephemeral: do not retain them as independent rows or advance/dispose the reader inside a helper's iteration.

| ID | Receiver | Member | Result |
| --- | --- | --- | --- |
| L07 | `DataLinq.Query.Select<T>` | `ReadReaderAsync(ct)` | `IAsyncEnumerable<IDataLinqAsyncDataReader>` |
| L08 | Same | `ReadRowsAsync(ct)` | `IAsyncEnumerable<RowData>` |
| L09 | Same | `ReadFirstRowAsync(ct)` | `Task<RowData?>` |
| L10 | Same | `ReadKeysAsync(ct)` | `IAsyncEnumerable<DataLinqKey>` |
| L11 | Same | `ReadPrimaryAndForeignKeysAsync(ColumnIndex foreignKeyIndex, ct)` | `IAsyncEnumerable<(DataLinqKey fk, DataLinqKey[] pks)>` |
| L12 | Same | `ExecuteAsync(ct)` | `IAsyncEnumerable<IImmutableInstance>` |
| L13 | Same | `ExecuteAsAsync<V>(ct)` | `IAsyncEnumerable<V>` |
| L14 | Same | `ExecuteScalarAsync<V>(ct)` / `ExecuteScalarAsync(ct)` | `Task<V>` / `Task<object?>` |
| L15 | `DataLinq.Query.SqlQuery<T>` | `SelectAsync(ct)` | `IAsyncEnumerable<T>` |
| L16 | `DataLinq.Mutation.DataSourceAccess` and built-in overrides | `GetFromQueryAsync<T>(string query, ct) where T : IModel` | `IAsyncEnumerable<T>`; public virtual unsupported base default |
| L17 | Same | `GetFromCommandAsync<T>(IDbCommand dbCommand, ct) where T : IModel` | `IAsyncEnumerable<T>`; public virtual unsupported base default |

`Select<T>` and `ExecuteAsAsync<V>` retain their existing lack of entity constraints; accepted materialization still applies. `ExecuteAs` is a cast of supported models, not a new DTO mapper. L11 may buffer. L05 deliberately exposes the synchronous current-row interface specified by AAPI-56; L07 exposes the async companion specified by AAPI-86. These are different accepted receiver contracts, not interchangeable declarations.

String execution owns its created command. Caller commands are borrowed, remain stable while active, and are not cloned. Direct readers transfer owned-reader cleanup to the caller; sequence helpers own their readers. Close only owned resources and never complete a caller-owned transaction. Raw adapter execution shares managed gates and conservative failure handling but supplies no tracked-mutation cache invalidation or SQL-effect inference. Escaped native handles remain outside those guarantees.

Capture all fluent execution/materialization state privately. Async paths must not reproduce current `Select.Execute` mutation of the caller's selected columns or read live builder state after suspension. Capturing final SQL alone is insufficient.

Sources: [access interface](../../../../src/DataLinq/Interfaces/IDatabaseAccess.cs), [access base](../../../../src/DataLinq/Database/DatabaseAccess.cs), [reader](../../../../src/DataLinq/Database/DataReader.cs), [fluent selection](../../../../src/DataLinq/Query/Select.cs), [SQL query](../../../../src/DataLinq/Query/SqlQuery.cs).

## S5: Probes, Metadata, Validation And Provisioning

Accepted target: AAPI-64 through AAPI-67, AAPI-82 through AAPI-85/AAPI-99.

| ID | Receiver | Target member |
| --- | --- | --- |
| A01 | `Database<D>`, `IDatabaseProvider` / `DatabaseProvider` and built-ins | `Task<bool> FileOrServerExistsAsync(ct)` |
| A02 | Same | `Task<bool> DatabaseExistsAsync(string? databaseName = null, ct)` |
| A03 | Same | `Task<bool> TableExistsAsync(string tableName, string? databaseName = null, ct)` |
| A04 | `DataLinq.Metadata.IMetadataFromSqlFactory` and built-ins | `Task<Option<DatabaseDefinition, IDLOptionFailure>> ParseDatabaseAsync(string name, string csTypeName, string csNamespace, string dbName, string connectionString, ct)` |
| A05 | `Database<D>` | `Task<DataLinqSchemaValidationResult> ValidateSchemaAsync(Action<DataLinqSchemaValidationOptions>? configure = null, ct)` |
| A06 | `Database<D>` | `Task EnsureSchemaValidAsync(Action<DataLinqSchemaValidationOptions>? configure = null, ct)` |
| A07 | `DataLinqSchemaValidator` | `static Task<DataLinqSchemaValidationResult> ValidateAsync(IDatabaseProvider provider, DataLinqSchemaValidationOptions? options = null, ct)` |
| A08 | `SQLiteProvider<D>` | `Task SetJournalModeAsync(SQLiteJournalMode journalMode, ct)` |
| A09 | `DataLinq.Metadata.ISqlFromMetadataFactory` and built-ins | `Task<Option<int, IDLOptionFailure>> CreateDatabaseAsync(Sql sql, string databaseName, string connectionString, bool foreignKeyRestrict, ct)` |
| A10 | `DataLinq.Metadata.PluginHook` | `static Task<Option<int, IDLOptionFailure>> CreateDatabaseFromSqlAsync(this DatabaseType type, Sql sql, string databaseOrFile, string connectionString, bool foreignKeyRestrict, ct)` |
| A11 | Same | `static Task<Option<int, IDLOptionFailure>> CreateDatabaseFromMetadataAsync(this DatabaseType type, DatabaseDefinition metadata, string databaseNameOrFile, string connectionString, bool foreignKeyRestrict, ct)` |

A04/A09 retain unsupported defaults for old synchronous-only factories. Probe capability and cancellation are not false/not-found outcomes. Expected availability failures follow existing probe policy; metadata-query operational failures must not become absence. Metadata retains its non-cancellation `Option` behavior; cancellation escapes. Provisioning retains registration/generation options and original operational exceptions, rather than wrapping every failure into `Option`.

Runtime schema APIs A05–A07 and their supporting types are planned V10 APIs, **not existing runtime APIs in this baseline**. AAPI-106 through AAPI-109 settle supporting types in core, result/failure policy, comparison scope and timeout bounds; D10-4 owns implementation and evidence. Invoke configuration once synchronously and capture its include list/options. Inspect fresh live metadata for the effective provider database; do not use a boolean probe as a substitute for actual validation. No hidden creation, repair, borrowed application transaction or atomic concurrent-DDL promise.

Provisioning is explicit and can leave partial effects. Capture registration and inputs before suspension; no automatic retry, database drop or file deletion. Current script support executes `Sql.Text` and does not establish arbitrary parameterized script support. Journal-mode completion does not verify the effective mode. Preserve SQLite shared in-memory identity and intended keeper ownership.

Sources: [provider interface](../../../../src/DataLinq/Interfaces/IDatabaseProvider.cs), [factory/forwarding contracts](../../../../src/DataLinq/Metadata/PluginHook.cs), [SQLite setup](../../../../src/DataLinq.SQLite/SQLiteProvider.cs), [schema-validation plan](../../providers-and-features/Schema%20Validation%20Hooks.md).

## S6: Diagnostics And Execution Configuration

Accepted target: AAPI-25/AAPI-26/AAPI-62/AAPI-63/AAPI-91 through AAPI-99/AAPI-102.

| ID | Public declaration | Details |
| --- | --- | --- |
| D01 | `DataLinq.Diagnostics.DataLinqFailure` | Static class; `public static DataLinqFailureContext? GetContext(Exception exception)` |
| D02 | `Transaction.FailureContext` | `public DataLinqFailureContext? FailureContext { get; }` |
| D03 | `DataLinqFailureContext` | Sealed; internal construction; public properties below have getters only |
| D04 | `DataLinqSecondaryFailure` | Sealed; getter-only `Cause`, `Operation`, `Stage`, `Exception`; internal construction |
| D05 | Five diagnostic enums | Exact assignments below, accepted by AAPI-102; recovery bits retain AAPI-94 values |
| D06 | `DataLinq.DataLinqExecutionOptions` | Sealed; `public TimeSpan RecoveryRollbackTimeout { get; init; } = TimeSpan.FromSeconds(30);` |
| D07 | `IDatabaseProvider` / `DatabaseProvider` | Read-only `DataLinqExecutionOptions ExecutionOptions { get; }`; interface compatibility default uses standard settings |

D03 properties: `DataLinqFailureCause Cause`, `DataLinqOperationKind Operation`, `DataLinqFailureStage Stage`, `DataLinqCompletionOutcome CompletionOutcome`, `DataLinqRecoveryActions RecoveryActions`, `IReadOnlyList<DataLinqSecondaryFailure> SecondaryFailures`, `uint? TransactionId`, `string? ProviderInstanceId`, `DataLinqOperationKind? ActiveOperation`. D04 uses the same first three enum types and `System.Exception Exception`. These diagnostic types live in `DataLinq.Diagnostics`.

| Enum | Accepted fixed assignments |
| --- | --- |
| `DataLinqFailureCause` | `Unknown = 0, Cancellation = 1, Timeout = 2, ProviderError = 3, ApplicationError = 4, MaterializationError = 5, LocalFinalizationError = 6, InvalidOperation = 7` |
| `DataLinqOperationKind` | `Unknown = 0, Query = 1, KeyLookup = 2, RelationLoad = 3, Insert = 4, Update = 5, Save = 6, Delete = 7, Commit = 8, Rollback = 9, Dispose = 10, TransactionCallback = 11, RawCommand = 12, MetadataRead = 13, SchemaValidation = 14, ExistenceCheck = 15, Provisioning = 16, ProviderConfiguration = 17` |
| `DataLinqFailureStage` | `Unknown = 0, Validation = 1, Initialization = 2, CommandExecution = 3, RowLoading = 4, Callback = 5, LocalFinalization = 6, Commit = 7, Rollback = 8, CacheRecovery = 9, Notification = 10, Cleanup = 11` |
| `DataLinqCompletionOutcome` | `Unknown = 0, NotApplicable = 1, NotAttempted = 2, Committed = 3, RolledBack = 4` |
| `DataLinqRecoveryActions` | `[Flags]`: `None = 0, Continue = 1, Rollback = 2, Dispose = 4, FinishActiveOperation = 8` |

The accessor inspects only the supplied exception, throws for null and returns null without attached context. Preserve original exception objects/stacks; a transaction may later expose a new snapshot. Secondary entries are ordered, non-null, defensively protected against list/array mutation, and never automatically flattened or serialized. Context contains no live transaction/connection or added SQL/keys/secrets, but original exceptions are not sanitized.

Completion certainty differs from lifecycle and from local cleanup. Preserve `TransactionCommitFinalizationException`, its `TransactionId`, `InnerException` and `CleanupFailures`. `NotApplicable` does not mean no database effects. Recovery flags describe current valid actions, never retries or permission to abandon active work. Rejected overlap context must not overwrite the active operation's failure state.

### Constructor Expansion

C01 preserves every existing provider constructor, including parameter names, defaults, visibility and binary signature. The new concrete shape is fully required:

~~~csharp
// On MySqlProvider<D>, MariaDBProvider<D>, SQLiteProvider<D>.
public ProviderName(
    string connectionString,
    string? databaseName,
    DataLinqLoggingConfiguration? loggerFactory,
    DataLinqExecutionOptions executionOptions);
~~~

`ProviderName` is a constructor-name placeholder. Non-generic provider registration classes are not given these constructors.

Preserve the existing namespaces: `DataLinq.MySql.MySqlProvider<D>` and `DataLinq.MySql.SqlProvider<D>`, `DataLinq.MariaDB.MariaDBProvider<D>`, and `DataLinq.SQLite.SQLiteProvider<D>`.

C02 expands shared construction without adding ambiguous same-arity options alternatives:

| Receiver | Preserve | Proposed added forwarding overload |
| --- | --- | --- |
| `SqlProvider<D>` | Existing public three- and four-argument constructors | Public existing four arguments, in order, followed by required `executionOptions` |
| `DatabaseProvider<D>` | Existing protected three- and four-argument constructors | Protected existing four arguments, in order, followed by required `executionOptions` |
| `DatabaseProvider` | Existing protected seven-argument constructor and defaults | Protected same seven arguments, all required, followed by required `executionOptions` |

The existing four shared arguments are `string connectionString, DatabaseType databaseType, DataLinqLoggingConfiguration loggingConfiguration, string? databaseName`. The nongeneric seven are `string connectionString, Type type, DatabaseType databaseType, DataLinqLoggingConfiguration loggingConfiguration, string? databaseName, Func<Option<DatabaseDefinition, IDLOptionFailure>>? metadataFactory, bool createReadOnlyAccess`. The private-protected metadata-binder constructor remains internal implementation plumbing, not a new external extension contract. C02 is an expansion requiring E03 consumer review.

D06 accepts 1 through 4,294,967,294 milliseconds inclusive; reject zero, negative, infinite, positive sub-millisecond and oversized durations before provider setup/resource I/O. Capture a separate immutable effective value. Old constructors use standard settings; external providers keep their settings; no live hosting reconfiguration. The budget starts once at automatic rollback, not while draining unfinished work, and is not a cleanup deadline.

Sources: [provider base constructors](../../../../src/DataLinq/Database/DatabaseProvider.cs), [shared SQL provider](../../../../src/DataLinq.MySql/Shared/SqlProvider.cs), [MySQL constructors](../../../../src/DataLinq.MySql/MySql/MySqlProvider.cs), [MariaDB constructors/probe](../../../../src/DataLinq.MySql/MariaDB/MariaDBProvider.cs), [commit-finalization exception](../../../../src/DataLinq/Exceptions/TransactionCommitFinalizationException.cs).

## S7: Explicit Exclusions And Synchronous Exceptions

| ID | Boundary | Disposition |
| --- | --- | --- |
| X01 | `Transaction` / `StartTransaction` / `GetNewDatabaseTransaction` / attachment | Keep synchronous construction; lazy opening/initialization belongs to execution. Attachment consumes transaction and connection, without `leaveOpen` |
| X02 | `Query`, `From`, composition/preparation, `Mutate`, property assignment | Local synchronous operations; no decorative async variants |
| X03 | `GenerateSql`, `GetCreateTables`, `GetCreateSql`, `ToSql`, `ToDbCommand`, unopened `GetDbConnection` | Synchronous construction, not execution |
| X04 | `IDataLinqDataWriter.ConvertValue` and conversion helpers | Local encoding only in the audited contract; no database-write execution member to mirror |
| X05 | SQLite constructor setup and keeper/journal work | Preserve documented synchronous I/O timing, including new options path after validation |
| X06 | MariaDB `DetectServerVersion` and `IsMariaDbUuidSupported` | Preserve eager constructor query and catch/false fallback; later startup cancellation cannot cover it retrospectively |
| X07 | Obsolete throwing fluent `Insert` / `Update` / `Delete` and corresponding `Execute` | Remain unsupported; no async revival. SQL construction still allowed |
| X08 | Cache clearing, local lifecycle notifications and callbacks | Retain synchronous contracts; no new async event/maintenance protocol |
| X09 | Relation query composition, automatic preloading, strict sync-I/O mode | Outside 0.10; AAPI-10's relation-query proposal was withdrawn |
| X10 | Memory mutation/transactions, generated navigation/key source expansion, prepared execution | Outside current Memory scope; no `SeedAsync` or artificial task scheduling |
| X11 | Public `SupportsAsync` flags, general backend plugin protocol, raw mutation tracking | Not added; missing capability fails explicitly |
| X12 | Raw native connection/transaction handles | Escape hatch; no inferred tracking, automatic retry or managed cleanup guarantees for bypassed operations |

Source for X04: [data writer](../../../../src/DataLinq/Database/DataWriter.cs). Existing synchronous navigation can load again after invalidation; applications retain awaited values or materialized DTOs instead of assuming an earlier load permanently removes I/O.

## S8: Compatibility Matrix

Every row is **pending implementation evidence**. “Preserve” is the required result, not a completed test.

| ID | Consumer or boundary | Required evidence |
| --- | --- | --- |
| B01 | Existing sync compiled consumers | Run old binaries against candidate packages; ApiCompat separates additions from the approved keyed-enumeration rename |
| B02 | Existing relation source consumers | `AsKeyValuePairs` migration; ordinary `AsEnumerable` now yields rows; inferred element types and loading timing documented |
| B03 | Existing required-reference consumers | Explicitly disposition sync nullability correction; required missing target throws, optional missing remains null |
| B04 | Query imports and projections | Unconstrained interface/entity/scalar/nullable/anonymous/DTO receivers; EF Core import conflict and static alias escape; foreign provider rejection |
| B05 | Numeric and default overloads | Exact ten Sum/ten Average forms, integer-to-double averages, Min/Max generic inference, nullability/empty semantics; no unintended defaults or delegate fallback |
| B06 | Relation implementations | Interface/concrete calls, primitive-only custom implementation, sync-only implementation, overridden terminal, acyclic dispatch and duplicate-key behavior |
| B07 | Foreign-key implementations | Old sync covariance retained; explicit async invariance; unsupported custom holder does not access `Value` |
| B08 | Generated consumers | Scalar/composite/converted key parameters; all three sources; every M10 receiver; required/optional navigation; DLG004 binding/locations/isolation |
| B09 | Callback overloads | Typed database versus untyped provider; four delegate forms; inference with result/no result, named token/type, no async-void route; helper completion ownership |
| B10 | Provider constructors and subclasses | Preserve positional, typed-null, currently valid untyped-null and named calls; old binaries and external subclasses; options validated before setup |
| B11 | Custom provider/access/factory implementations | Compile old source and run old binaries; new defaults fail without sync I/O; concrete visibility; default settings; L16/L17 and T05 legacy subclass dispatch; root disposal placement E03 |
| B12 | Low-level command/reader consumers | String and command overloads, existing `ToDbCommand` output, invalid command rejection, L05/L07 view difference, row lifetime and disposal |
| B13 | Diagnostic consumers | Namespace/getters/nullability, exact AAPI-102 numbers/options placement, flags, future values, immutable snapshots and original exception compatibility |
| B14 | Packed .NET 8/9/10 consumers | Real NuGet dependency groups and transitive async LINQ; no mandatory extra install on 8/9; framework path on 10; no accidental overlapping imports |
| B15 | Memory-only consumer and graph doubles | No SQL dependency introduced by Memory package use; narrow actual capabilities; standalone graph doubles do not imply Memory navigation support |
| B16 | Runtime validation/hosting | V10 type/package placement; captured configuration/effective identity; owned versus externally supplied providers; explicit startup failure policy |

SQLite already has two-argument overloads whose untyped-null binding needs a recorded baseline. The goal is no **new** ambiguity, not claiming every conceivable old null call compiled. Default interface bodies also need binary verification; their existence is not a compatibility proof.

## S9: Backend And Execution Evidence Matrix

| Family | MySQL / MariaDB | SQLite | Memory | Custom synchronous-only implementation |
| --- | --- | --- | --- | --- |
| Supported queries and terminals | Native async dispatch where provided; existing translation limits | Same supported SQL contract, explicit driver blocking limits | Existing narrower query subset only | Reject without synchronous fallback |
| Key lookup | SQL canonical/generated families | Same, with file/in-memory identity semantics | K05 model-key lookup only | Capability-dependent |
| Relation loading | Shared loading, waiter cancellation and cache publication | Same contracts; blocking limit does not weaken ownership | No SQL-source/navigation expansion; graph doubles separate | Async primitive/capability required |
| Prepared/fluent/raw SQL | Audited SQL source families | Same source boundary | Unsupported | Explicit implementation required |
| Mutations and transactions | Cancellation, write/hydration poison, commit certainty, recovery | Same safety contracts; interruption/recovery limitations measured | Unsupported | Explicit implementation required |
| Metadata/probes/validation | Multi-command propagation; errors not fake absence/drift | Effective file/shared-memory identity; no creation | No SQL validation capability implied | Unsupported defaults where specified |
| Provisioning/configuration | Partial DDL and original errors; MariaDB constructor probe remains sync | Partial objects/file effects, keeper lifetime, journal-mode limits | Unsupported | Async creation must be implemented |
| Async disposal | Owned cleanup and provider trust before reuse/pooling | Keeper/reader/transaction cleanup with blocking limits | No disposal API added merely for symmetry | Placement/default behavior E03 |

For each applicable cell, record: immediate and suspended completion, pre-cancellation, interruption during execution/row loading, early iterator disposal, cleanup failure, resulting transaction usability, cache/telemetry parity and actual provider call path. A cache hit or an in-memory operation may complete synchronously; no `Task.Run` or artificial yield is required.

The Memory portion expands under AAPI-74 as follows; all existing expression restrictions still apply:

| Operation | Admitted Memory target |
| --- | --- |
| `ToListAsync`, `ToArrayAsync`, `AsAsyncEnumerable` | Existing entity/direct-scalar sequence shapes |
| `AnyAsync`, `CountAsync` | Existing admitted unpaged shapes |
| `SingleAsync`, `SingleOrDefaultAsync` | Existing admitted shapes and cardinality rules |
| `FirstAsync`, `FirstOrDefaultAsync` | Existing admitted shapes with supported primary-key ordering |
| Last terminals, numeric aggregates, unsupported joins/projections | Continue rejecting them; available query signatures do not grant capability |

Use existing [Memory row-plan validation](../../../../src/DataLinq.Memory/MemoryRowExecutionPlan.cs) and [backend dispatch](../../../../src/DataLinq.Memory/MemoryQueryPlanBackend.cs) as the boundary. Do not infer paging/navigation support from Q01–Q10 being callable. SQLite checks must distinguish connection/opening, busy waits, command execution, row advancement and cleanup rather than one vague “cancellation works” result.

## S10: Concrete Questions And Remaining Expansions

G01–G03 are **resolved by AAPI-100 through AAPI-102**, E01–E03's declaration policies by **AAPI-103 through AAPI-105**, and E04–E06's integration/evidence policies by **AAPI-106 through AAPI-111**, on 2026-09-16. Implementation, emitted manifests and product verification remain pending for all families.

### G01: Raw Model Reader Counterparts

[DataSourceAccess](../../../../src/DataLinq/Mutation/DataSourceAccess.cs) publicly declares `GetFromQuery<T>(string query)` and `GetFromCommand<T>(IDbCommand dbCommand)`, both returning `IEnumerable<T> where T : IModel`. [ReadOnlyAccess](../../../../src/DataLinq/Mutation/ReadOnlyAccess.cs) and [Transaction](../../../../src/DataLinq/Mutation/Transaction.cs) execute readers and materialize models through those methods. They are distinct from IDatabaseAccess's current-row helpers and Select's fluent helpers.

**Accepted: AAPI-100.** Include `IAsyncEnumerable<T> GetFromQueryAsync<T>(string query, ct) where T : IModel` and `IAsyncEnumerable<T> GetFromCommandAsync<T>(IDbCommand dbCommand, ct) where T : IModel` on the existing class hierarchy. Use unsupported public virtual defaults for old subclasses and real built-in overrides. Preserve model/materialization limits, ordinary sequence capture, borrowed command lifetime and managed transaction gates; do not infer arbitrary raw SQL is harmless because the API returns rows. No additional `IDataSourceAccess` interface members or DTO-mapping feature.

**Disposition:** L16/L17 now explicitly cover this public receiver. Legacy subclass compatibility and actual async materialization still require evidence.

### G02: Public Provider Transaction Completion

[DatabaseTransaction](../../../../src/DataLinq/Database/DatabaseTransaction.cs) is a public abstract class, returned by provider factories, with public `Commit`, `Rollback` and `Dispose`. It is different from the managed `DataLinq.Mutation.Transaction`. AAPI-1/AAPI-82 require async initialization internally, but do not explicitly settle new public completion members on this lower-level class.

**Accepted: AAPI-101.** Add public virtual `Task CommitAsync(ct)`, `Task RollbackAsync(ct)` and `ValueTask DisposeAsync()` with explicit unsupported defaults for legacy implementations and built-in overrides. Preserve constructors, old abstract synchronous members, lifecycle events and `DatabaseTransactionStatus`. Do not expose a new public eager `OpenAsync`/`BeginAsync` factory.

**Boundary:** direct provider-level completion cannot promise the managed wrapper's cache/mutable finalization. If a provider transaction is owned by a managed transaction, callers must complete through that managed wrapper; retain the existing escape-hatch warning. T05 now records this public family; E03 separately reviews provider-root/interface disposal placement.

### G03: Numeric Values And Options Namespace

AAPI-92/AAPI-93 name enums but do not assign numbers; AAPI-63/AAPI-96 do not choose an execution-options namespace.

**Accepted: AAPI-102.** Place `DataLinqExecutionOptions` in `DataLinq` beside `DatabaseProvider`, and give the diagnostic enums explicit stable values. For cause, operation and stage, use the listed order with `Unknown = 0`, then consecutive values (1–7, 1–17 and 1–11 respectively). For completion use `Unknown = 0, NotApplicable = 1, NotAttempted = 2, Committed = 3, RolledBack = 4`. Keep AAPI-94's exact flags unchanged. S6 records every exact assignment.

**Reason:** an uninitialized classification should express lack of evidence; in particular, zero completion should not accidentally assert “not applicable.” Exact assignments and namespace are now settled; consumer baselines and actual failure classification remain pending.

### Expansion Work With No New Feature Proposal

| ID | Remaining work | Recommendation / gate |
| --- | --- | --- |
| E01 | Accepted declaration; product evidence pending | AAPI-103 fixes generic Min/Max and nullable results; compile Q10 against actual reference/value/nullable consumers and verify supported runtime semantics |
| E02 | Accepted declarations; product evidence pending | AAPI-104 fixes the direct local predicate/numeric/generic extrema list; verify full emitted overloads and actual shared defaults |
| E03 | Accepted disposal declaration; product evidence pending | AAPI-105 fixes inherited `IAsyncDisposable` slot/default and public virtual base/concrete dispatch; verify old implementers/subclasses, options and C02 constructors |
| E04 | Accepted supporting API; implementation/evidence pending | AAPI-106–109 fix core types, complete results/policy, comparison scope/empty schemas and bounded timeout; D10-4 verifies actual declarations and execution |
| E05 | Accepted package version; package evidence pending | AAPI-110 selects System.Linq.AsyncEnumerable 10.0.12 for .NET 8/9 only; verify actual packed groups and consumer resolution |
| E06 | Accepted amended baseline policy; initial tooling implemented, capture/output pending | AAPI-111 fixes locked 0.9.2 coverage including Memory and compiled/generated evidence; retain 0.9.0 diagnostics historically, emit actual declarations through AAPI-111 and disposition approved breaks |

G01–G03 and E01–E06 design policies are accepted. No item is product-verified merely because this document exists. The [W0 baseline and evidence plan](W0%20Baseline%20and%20Evidence%20Plan.md) is accepted, including .NET 10 benchmark baselines/candidates; the initial tooling slice is implemented, and clean evidence capture comes next.

### Declaration Review: E01–E03

**Accepted:** 2026-09-16 under AAPI-103 through AAPI-105. The isolated checks below do not change the pending product evidence matrix.

**E01 — Generic Min/Max declarations and nullable results (AAPI-103).** Use exactly one selector-based generic signature per operator on `DataLinqAsyncQueryableExtensions`, with no `class`, `struct`, `notnull` or comparable constraint:

~~~csharp
public static ValueTask<TResult?> MinAsync<TSource, TResult>(
    this IQueryable<TSource> source,
    Expression<Func<TSource, TResult>> selector,
    CancellationToken cancellationToken = default);
// MaxAsync has the identical generic/parameter/result shape.
~~~

The accepted annotations follow the [Queryable selector form](https://learn.microsoft.com/en-us/dotnet/api/system.linq.queryable.min?view=net-10.0). Unconstrained `TResult?` preserves `ValueTask<int>` for an `int` selector, `ValueTask<int?>` for `int?`, and `ValueTask<string?>` for `string`. Type-checking a string selector does not grant SQL translation: the current DataLinq validator admits direct numeric columns and rejects converter-backed columns and unsupported shapes.

Do not add separate nullable-generic overloads or comparer/selector-free forms. Empty non-nullable numeric Min/Max fails; nullable empty/all-null results remain null. Product tests must verify sync/async parity and actual provider conversion/empty-result behavior, rather than routing these calls through local LINQ.

**E02 — Direct relation terminal overloads (AAPI-104).** Use R08's full two-form expansion for `FirstAsync`, `FirstOrDefaultAsync`, `SingleAsync`, `SingleOrDefaultAsync`, `LastAsync`, `LastOrDefaultAsync`, `AnyAsync` and `CountAsync`: `(ct)` and `(Func<T, bool> predicate, ct)`. Materializers remain predicate-free; compose the async row view for more elaborate local pipelines.

For R09, use ten `SumAsync(Func<T, N> selector, ct)` and ten `AverageAsync(Func<T, N> selector, ct)` overloads using S1's five numeric types plus nullable counterparts and the same result mapping. Add only these generic extrema forms:

~~~csharp
ValueTask<TResult?> MinAsync<TResult>(
    Func<T, TResult> selector,
    CancellationToken cancellationToken = default);
ValueTask<TResult?> MaxAsync<TResult>(
    Func<T, TResult> selector,
    CancellationToken cancellationToken = default);
~~~

Here `T` is the relation's existing type parameter. Generic result comparison is local framework comparison, not provider translation. Do not impose SQL's converter-backed-column rejection on already materialized model values; unsupported comparisons still fail normally. No additional relation comparer, selector-free aggregate, asynchronous selector, predicate-specific materializer, `All` or `LongCount` member is proposed.

Shared defaults select local values over the genuine async row view and call the standard numeric/Min/Max operators. The framework [MinAsync sequence API](https://learn.microsoft.com/en-us/dotnet/api/system.linq.asyncenumerable.minasync?view=net-10.0) accepts an optional comparer before cancellation; it is not a selector overload. Forward cancellation by name after projection:

~~~csharp
// Sketch of a shared local default, not a synchronous materialization.
return await relation.AsAsyncEnumerable(cancellationToken)
    .Select(selector)
    .MinAsync(cancellationToken: cancellationToken);
~~~

A nullable Sum of an empty/all-null sequence is zero; nullable Average/Min/Max return null when no non-null value exists. Non-nullable Average/Min/Max on empty input throw. Integer Average returns double. Preserve standard local comparison, numeric/overflow and floating-point behavior; do not infer SQL execution or extra provider support from the local contract. Built-ins/test helpers expose these members explicitly, while interface defaults remain acyclic under AAPI-51 through AAPI-53.

**E03 — Inherited disposal slot (AAPI-105).** Use `IDatabaseProvider : IDisposable, IAsyncDisposable` with an explicit default implementation of the inherited slot:

~~~csharp
public interface IDatabaseProvider : IDisposable, IAsyncDisposable
{
    // Existing members and the accepted ExecutionOptions default remain.
    ValueTask IAsyncDisposable.DisposeAsync() =>
        ValueTask.FromException(new NotSupportedException(
            "This provider does not implement asynchronous disposal."));
}
~~~

Do not instead declare `new ValueTask DisposeAsync()` with a default body: that creates a separate member and does not implement the inherited slot. This follows the [C# default-interface explicit-implementation rules](https://github.com/dotnet/csharplang/blob/main/proposals/csharp-8.0/default-interface-methods.md#explicit-implementation-in-interfaces).

Keep a public virtual `DisposeAsync` on `DatabaseProvider` with an unsupported legacy default, concrete built-in overrides, and the existing class-based transaction declarations. Thus concrete/base/interface callers reach implemented async cleanup, and direct synchronous-only provider implementations remain source-valid without a hidden synchronous cleanup call. Existing explicit or public class implementations of `IAsyncDisposable` keep ordinary dispatch; no recursive cast-and-forward shim.

A legacy unsupported async call must not mark the object disposed or pretend resources were released. Its synchronous disposal remains available to a caller deliberately choosing that path; DataLinq must not choose it as an automatic fallback. Purely local built-in cleanup may complete immediately. Shared disposal state, dependency lifetime ordering and failure precedence remain the already accepted contracts.

Retain D07's standard-settings interface getter and C02's fully required new base-constructor overloads. Class settings override the interface default; no new `IAsyncDatabaseProvider` interface, root draining engine or live configuration is needed. Actual old-binary compatibility still requires B01/B10/B11 and ApiCompat.

**Isolated evidence, not product verification:** a package-free C# 14 probe compiled and ran with SDK 10.0.401 / .NET 10.0.12. It checked the proposed query signatures at compile time; nullable/reference/numeric local Min/Max and Sum/Average behavior; inherited interface/base/concrete disposal dispatch; unsupported defaults without synchronous disposal; and class versus default options dispatch. Deliberately invalid variants failed with `CS0029` (numeric nullable mismatch), `CS8619` (reference nullability mismatch), and `CS0535` (unimplemented inherited disposal slot). A final positive run passed.

The probe used stand-in provider/query types, not DataLinq assemblies. It did not exercise DataLinq query execution, existing binaries, .NET 8/9, the eventual transitive package, Native AOT/trim/WebAssembly, real provider cleanup or the full relation overload manifest. E01–E06 design choices are now accepted; the probe does not complete their validation/package/manifest work.

### Integration Review: E04–E06

**Accepted:** 2026-09-16 under AAPI-106 through AAPI-111, respectively. These six decisions settle the runtime-validation contract, package selection and compatibility evidence policy. They do not reopen AAPI-66's method signatures or turn roadmap APIs into existing APIs. W0 and provider feasibility gates still apply.

**1. E04 — Runtime types, package placement and result construction.** Put `DataLinqSchemaValidator`, `DataLinqSchemaValidationOptions` and `DataLinqSchemaValidationResult` in namespace `DataLinq.Validation` in the core `DataLinq` package. Put `DataLinqSchemaValidationException` in `DataLinq.Exceptions` in that package. Hosting registration/startup belongs in the separately planned hosting package. Core validation must not acquire dependencies on Microsoft hosting, `DataLinq.Tools`, CLI configuration or Roslyn/source-file parsing.

The existing core already compiles the shared `SchemaComparer`, `SchemaDifference`, `SchemaDifferenceSeverity` and `DataLinqDiagnosticIssue` types. Reuse them. Preserve the existing Tools `SchemaValidationRunResult` and CLI behavior instead of moving or replacing that public type.

Keep sealed, publicly parameterless-constructible options with mutable setters, captured once before suspension. Accepted properties are `FailOnSeverity` (default `Error`), `TreatValidationIssuesAsFailures` (default `true`), `TimeSpan? CommandTimeout`, `IReadOnlyList<string>? Include` and `Action<string>? MetadataReaderLog`. AAPI-107 removes the old sketch's informational filtering property.

Keep the result sealed with internal production construction and getter-only `DatabaseName`, `DatabaseType`, `ModelTableCount`, `DatabaseTableCount`, `Differences`, `Issues`, `HasDifferences` and `HasFailures`, with the types shown in the [runtime validation sketch](../../providers-and-features/Schema%20Validation%20Hooks.md#31-public-surface). Copy and expose read-only collection snapshots, including issue context-message lists. Existing `SchemaDifference` references to finalized metadata are retained; do not claim a deeply cloned metadata graph. Result flags reflect the captured policy and cannot change when caller options change later.

The sealed exception has `public DataLinqSchemaValidationException(DataLinqSchemaValidationResult result)`, rejects null and exposes that same result through a getter. Do not add an arbitrary public result builder merely to support tests; use the planned T10 controlled fixtures. Do not retain a live provider, connection or mutable options in the result or add a new automatic serialization contract.

**2. E04 — Complete results and independent failure policy.** Always retain every difference in a completed structured result, including `Info`. Omit `IncludeInformationalDifferences` from the runtime options; presentation and hosting logging can filter what they display without discarding comparison evidence. AAPI-107 accepts this refinement of the earlier suggested shape.

Use these rules:

- `HasDifferences` means `Differences.Count != 0`, independent of failure severity.
- `HasFailures` means at least one difference meets `FailOnSeverity`, or `TreatValidationIssuesAsFailures` is true and there is at least one typed validation issue.
- Accept only the defined `Info`, `Warning` and `Error` threshold values. Default to `Error`.
- Keeping `TreatValidationIssuesAsFailures = false` retains issues but removes only their contribution to `HasFailures`. It cannot suppress cancellation, timeout, connection failure, failed metadata acquisition or incomplete comparison.
- `EnsureSchemaValid[Async]` throws the schema-validation exception with the complete result only when a completed comparison has failures. Operational failures retain AAPI-66's separate failure path.

Do not compare the two severity enums by cast: `SchemaDifferenceSeverity` is ordered Info/Warning/Error, whereas `DataLinqDiagnosticSeverity` is Error/Warning. Do not fabricate typed issues by parsing human-readable metadata logs or convert failed metadata Options into successful issue-only results. Where the successful reader supplies no structured issues, an empty issue list is honest. The captured synchronous log callback remains subject to the accepted application-callback exception policy.

**3. E04 — Include scopes the comparison, and empty is not unreadable.** Treat `Include` as exact model database table/view names (`Table.DbName`), not C# type names, patterns or raw SQL. Null or an empty list means the full comparison. Validate nonblank entries against the finalized model before I/O, deduplicate with the comparison's provider-aware table-name comparer and reject unknown model names. Capture the list once.

Apply the same selected scope to model and live objects; counts describe the objects actually compared. Do not mutate finalized provider metadata. Compare foreign keys owned by selected tables, preserving their referenced identity, without implicitly broadening the scope to all referenced tables. Selection does not promise fewer metadata commands or suppression of read failures elsewhere in the schema.

Do not pass runtime `Include` straight through the current import-reader option. The MySQL/MariaDB and SQLite readers reject missing requested objects, and they reject an empty schema. A runtime comparison must instead report a selected table absent from successfully read live metadata as `MissingTable`. An existing, successfully read empty database is valid comparison input and can yield missing-table differences; a missing database or an unreadable/incompletely read schema remains an operational failure.

Reuse provider metadata readers with an explicit runtime-validation path for these distinctions. Preserve legacy import/CLI behavior, never translate an arbitrary failed Option into an empty schema, and never create a missing SQLite file/database to make the comparison succeed. Exact internal reader plumbing belongs to W1/D10-4; this decision does not add a general public provider extension protocol.

**4. E04 — Bounded per-command timeout with explicit units.** Retain `TimeSpan? CommandTimeout`. Null preserves the provider's configured default; zero explicitly disables that command timeout. Positive values round up to whole seconds, so a subsecond value never truncates to unlimited. Validate before I/O and reject negative values (including `Timeout.InfiniteTimeSpan`) and values above `TimeSpan.FromSeconds(2_147_483)`. Use checked/integer normalization, not a lossy floating-point conversion.

That accepted common ceiling avoids silent shortening by the repository's pinned [MySqlConnector 2.6.2 command implementation](https://raw.githubusercontent.com/mysql-net/MySqlConnector/2.6.2/src/MySqlConnector/MySqlCommand.cs), whose effective timeout caps at `int.MaxValue / 1000` seconds. Zero-as-disabled is documented by [MySqlConnector](https://mysqlconnector.net/connection-options/#DefaultCommandTimeout) and [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/database-errors).

This is a setting applied to each metadata command, not a wall-clock deadline for the full validation operation or its connection opening. Caller cancellation remains the cooperative whole-operation mechanism, subject to the accepted provider limits; rollback recovery uses its separate timeout. Current `MetadataFromDatabaseFactoryOptions` has no command-timeout member, so implementation must wire the setting through actual metadata execution. Never silently ignore an explicitly requested setting on an unsupported custom reader. Provider coverage must verify actual propagation and failure classification, not just option validation.

**5. E05 — Pin the async LINQ dependency deliberately.** AAPI-110 selects `System.Linq.AsyncEnumerable` **10.0.12**, published 2026-09-08, as the reviewed starting version. Its [NuGet package metadata](https://packages.nuget.org/packages/System.Linq.AsyncEnumerable/10.0.12) supplies .NET 8/9/10 assets. Retain AAPI-13's policy: a normal transitive reference from the core package for .NET 8 and .NET 9, with the package reference omitted for .NET 10's framework implementation.

At implementation, pin the central `PackageVersion` in `src/Directory.Packages.props` and condition the core `PackageReference` by target framework. Do not float versions, use a prerelease 11.x package, substitute the older `System.Linq.Async` package or hide the dependency behind `PrivateAssets=all`. An exact central project version does not force every consuming application to resolve exactly that version; record the actual resolved graph.

Verify packed dependency groups, clean consumer restore and extension binding under .NET 8/9/10, including the query/relation distinction. The isolated .NET 10 framework probe does not validate the package's .NET 8/9 assets. No package files, dependency references or release artifacts are changed by this planning decision.

**6. E06 — Establish the right baseline before emitting the final manifest.** At the source audit, `ApiCompatibilityReporter` had only the historical 0.8 baseline and treated Memory as new. The initial W0 tooling slice now supplies the 0.10 release policy and compares Memory as an existing library. The accepted baseline amendment selects `v0.9.2-packages.json` by default. The old 0.8 and 0.9.0 inputs remain available for explicit historical comparisons.

AAPI-111 requires a version-specific 0.10 policy and locked published **0.9.2** baseline, with exact package bytes, hashes and provenance, including Memory as an existing library in normal compatibility comparisons. Preserve the historical 0.8-to-0.9 policy, lock and disposition evidence. Treat genuinely new 0.10 integration/test-helper packages separately only when their package identities/surfaces are settled.

Reuse the existing ApiCompat runner and metadata snapshots. Map the inventory to compiled .NET 8/9/10 declarations: overloads, namespaces, generic constraints, nullable metadata, optional values and parameter names, constructors, inherited/default-interface dispatch, and fixed enum values. Supplement assembly manifests with generated-source and positive/negative consumer fixtures for converted/composite keys, required/optional navigation, DLG004 and extension binding. A textual API diff alone cannot prove dispatch or generated-client behavior.

Disposition the already approved AAPI-11 keyed-view rename and AAPI-16 required-reference behavior explicitly; flag other changes for review instead of creating blanket suppressions. W0 captures the trustworthy before-state and package provenance; W3 emits the actual implementation's signature manifest and compatibility evidence. Do not fabricate an emitted manifest now from unimplemented signatures or call this planning review a completed compatibility check.

**Next transition:** decisions are recorded and the validation specification is aligned. Execute the accepted [bounded W0 plan](W0%20Baseline%20and%20Evidence%20Plan.md), including separate baseline identities and tooling readiness. Implementation, old-binary compatibility, packed-consumer verification and provider feasibility remain separate work.


## S11: Traceability And Next Gates

Each accepted decision is assigned below. This is traceability, not evidence that every detailed behavioral case has been tested.

| Decisions | Inventory / evidence |
| --- | --- |
| AAPI-1–AAPI-8 | S1/S2/S3, Q01–Q12, T01–T04, X01–X03, S9 |
| AAPI-9–AAPI-16 | S2, R01–R12, X09, B02/B03/B06/B07/B08/B14; AAPI-10 is withdrawn |
| AAPI-17–AAPI-20 | Capture/sequence conventions, S1/S2/S4, B12/S9 |
| AAPI-21–AAPI-26 | S3/S6/S9, diagnostics and failure/recovery evidence |
| AAPI-27–AAPI-33 | M01–M10, T03/T04, B08/B09 |
| AAPI-34–AAPI-41 | S2/S3/S4, transaction ownership and relation publication evidence |
| AAPI-42–AAPI-48 | Q01–Q12, B04/B05/B14, E01/E05 |
| AAPI-49–AAPI-55 | R01–R12/K01–K04, B06–B08 |
| AAPI-56–AAPI-63 | L01–L15, T02, D01–D07, B10–B13, E03 |
| AAPI-64–AAPI-72 | A01–A07, M01–M10, T03/T04, B08/B09/B16 |
| AAPI-73–AAPI-81 | S2/S7/S9, K05, B15, X09–X11 |
| AAPI-82–AAPI-90 | S4/S5/S7, K04, L07–L15, A08–A11 |
| AAPI-91–AAPI-99 | S6, R12/DLG004, C01/C02, D01–D07, X06, G03 |
| AAPI-100–AAPI-102 | L16/L17, T05, S6 fixed assignments/options namespace, G01–G03, B11/B13 |
| AAPI-103–AAPI-105 | Q10, R08/R09, T02, E01–E03, B05/B06/B10/B11/B14 |
| AAPI-106–AAPI-111 | A05–A07, E04–E06, B04/B14/B16, D10-4 and W0 baseline/tooling |

Next gates, in order:

1. **Complete W0:** G01–G03/E01–E06 design choices and W0-P1 through W0-P6 are accepted. Compatibility tooling and .NET 10 benchmark migration are implemented. Follow the [W0 baseline and evidence plan](W0%20Baseline%20and%20Evidence%20Plan.md) to freeze clean identities and capture the remaining baselines. OAPI-7 remains an implementation/manifest/verification gate; new design questions require concrete findings.
2. **W0:** capture the real before-state I/O, compatibility and performance evidence. This document's source scan is only an input, not completion of W0.
3. **W1/W2:** establish internal async contracts and prove provider feasibility, cancellation/timeout distinction, first initialization, ownership, cache publication, completion certainty and cleanup.
4. **W3:** implement the public surface against those contracts; compile the full signature/generator manifest and run B01–B16 using packed consumers and ApiCompat.
5. **Release integration:** supply deterministic and real-provider evidence from S9; reconcile hosted validation/testing integration, packaging, benchmarks and migration notes with the release evidence plan.

No DataLinq runtime implementation, package publication, baseline benchmark, real-provider experiment or DataLinq consumer compilation was performed. S10 separately records the isolated language/framework probe and its limitations.
