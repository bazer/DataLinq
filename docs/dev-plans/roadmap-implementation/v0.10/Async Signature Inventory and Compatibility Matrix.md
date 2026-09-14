> [!WARNING]
> This is a future-release planning inventory. It does not declare these async APIs implemented, compiled, or provider-verified.

# 0.10 Async Signature Inventory And Compatibility Matrix

**Status:** First consolidation of AAPI-1 through AAPI-99; concrete audit questions remain open.

**Target:** 0.10 / A10, with H10, V10 and T10 integration.

**Last reviewed:** 2026-09-15.

**Source baseline:** `459160fd`. That commit records the latest decisions; the runtime sources examined below still describe the synchronous baseline.

**Authority:** [Async Public API Decisions](Async%20Public%20API%20Decisions.md) owns accepted contracts. This inventory expands and cross-checks them against source; it cannot silently approve a new API or compatibility break. [Implementation Order](Implementation%20Order%20and%20Integration%20Plan.md) owns sequencing and [Release Evidence](Release%20Evidence%20and%20Closeout%20Implementation%20Plan.md) owns exit gates.

## Reading The Inventory

- **Accepted target** means the family and stated contract follow an accepted AAPI decision. It is not evidence of implementation.
- **Expansion** means a concrete declaration or placement proposed to complete that family. Review the identified details before freezing the public API.
- **Question** means source revealed an uncovered boundary or a choice not made by the accepted decisions.
- **Excluded** means retain the existing synchronous or unsupported boundary.
- Every consumer, ApiCompat and runtime check below is **pending**. This consolidation performed source/document review only.

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
| Q10 | `MinAsync<T, TResult>(Expression<Func<T, TResult>> selector, ct)`; corresponding `MaxAsync` | `ValueTask<TResult?>` | Expansion: nullable annotation and generic binding require E01 |

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
| R09 | Supported selector reductions | Expansion E02 pins the local `Func` overload list; no provider expressions or relation-query conversion |
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

Accepted target: AAPI-1 through AAPI-4, AAPI-8, AAPI-21 through AAPI-38, AAPI-60/AAPI-61/AAPI-68 through AAPI-72.

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
| T02 | Owning `Database<D>` / provider roots | `ValueTask DisposeAsync()` through `IAsyncDisposable`; old/custom dispatch still needs E03 |
| T03 | `Database<D>` | Four `CommitAsync` callback forms using `Transaction<D>` below |
| T04 | `IDatabaseProvider` / `DatabaseProvider` | Same four forms using untyped `Transaction`; no competing typed generic-provider family |

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

Accepted target: AAPI-56 through AAPI-61, AAPI-82/AAPI-86 through AAPI-89. G01/G02 below identify additional public receiver questions.

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

Runtime schema APIs A05–A07 and their supporting types are planned V10 APIs, **not existing runtime APIs in this baseline**. Their full supporting type/package/namespace review belongs to D10-4; see E04. Invoke configuration once synchronously and capture its include list/options. Inspect fresh live metadata for the effective provider database; do not use a boolean probe as a substitute for actual validation. No hidden creation, repair, borrowed application transaction or atomic concurrent-DDL promise.

Provisioning is explicit and can leave partial effects. Capture registration and inputs before suspension; no automatic retry, database drop or file deletion. Current script support executes `Sql.Text` and does not establish arbitrary parameterized script support. Journal-mode completion does not verify the effective mode. Preserve SQLite shared in-memory identity and intended keeper ownership.

Sources: [provider interface](../../../../src/DataLinq/Interfaces/IDatabaseProvider.cs), [factory/forwarding contracts](../../../../src/DataLinq/Metadata/PluginHook.cs), [SQLite setup](../../../../src/DataLinq.SQLite/SQLiteProvider.cs), [schema-validation plan](../../providers-and-features/Schema%20Validation%20Hooks.md).

## S6: Diagnostics And Execution Configuration

Accepted target: AAPI-25/AAPI-26/AAPI-62/AAPI-63/AAPI-91 through AAPI-99.

| ID | Public declaration | Details |
| --- | --- | --- |
| D01 | `DataLinq.Diagnostics.DataLinqFailure` | Static class; `public static DataLinqFailureContext? GetContext(Exception exception)` |
| D02 | `Transaction.FailureContext` | `public DataLinqFailureContext? FailureContext { get; }` |
| D03 | `DataLinqFailureContext` | Sealed; internal construction; public properties below have getters only |
| D04 | `DataLinqSecondaryFailure` | Sealed; getter-only `Cause`, `Operation`, `Stage`, `Exception`; internal construction |
| D05 | Five diagnostic enums | Names below; numeric assignment question G03 excludes already-fixed recovery bits |
| D06 | `DataLinqExecutionOptions` | Sealed; `public TimeSpan RecoveryRollbackTimeout { get; init; } = TimeSpan.FromSeconds(30);` |
| D07 | `IDatabaseProvider` / `DatabaseProvider` | Read-only `DataLinqExecutionOptions ExecutionOptions { get; }`; interface compatibility default uses standard settings |

D03 properties: `DataLinqFailureCause Cause`, `DataLinqOperationKind Operation`, `DataLinqFailureStage Stage`, `DataLinqCompletionOutcome CompletionOutcome`, `DataLinqRecoveryActions RecoveryActions`, `IReadOnlyList<DataLinqSecondaryFailure> SecondaryFailures`, `uint? TransactionId`, `string? ProviderInstanceId`, `DataLinqOperationKind? ActiveOperation`. D04 uses the same first three enum types and `System.Exception Exception`. These diagnostic types live in `DataLinq.Diagnostics`.

| Enum | Accepted members |
| --- | --- |
| `DataLinqFailureCause` | `Unknown, Cancellation, Timeout, ProviderError, ApplicationError, MaterializationError, LocalFinalizationError, InvalidOperation` |
| `DataLinqOperationKind` | `Unknown, Query, KeyLookup, RelationLoad, Insert, Update, Save, Delete, Commit, Rollback, Dispose, TransactionCallback, RawCommand, MetadataRead, SchemaValidation, ExistenceCheck, Provisioning, ProviderConfiguration` |
| `DataLinqFailureStage` | `Unknown, Validation, Initialization, CommandExecution, RowLoading, Callback, LocalFinalization, Commit, Rollback, CacheRecovery, Notification, Cleanup` |
| `DataLinqCompletionOutcome` | `NotApplicable, NotAttempted, Committed, RolledBack, Unknown` |
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
| B11 | Custom provider/access/factory implementations | Compile old source and run old binaries; new defaults fail without sync I/O; concrete visibility; default settings; disposal placement E03 |
| B12 | Low-level command/reader consumers | String and command overloads, existing `ToDbCommand` output, invalid command rejection, L05/L07 view difference, row lifetime and disposal |
| B13 | Diagnostic consumers | Namespace/getters/nullability, stable numbers after G03, flags, future values, immutable snapshots and original exception compatibility |
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

These items are **not newly accepted AAPI decisions**.

### G01: Raw Model Reader Counterparts

[DataSourceAccess](../../../../src/DataLinq/Mutation/DataSourceAccess.cs) publicly declares `GetFromQuery<T>(string query)` and `GetFromCommand<T>(IDbCommand dbCommand)`, both returning `IEnumerable<T> where T : IModel`. [ReadOnlyAccess](../../../../src/DataLinq/Mutation/ReadOnlyAccess.cs) and [Transaction](../../../../src/DataLinq/Mutation/Transaction.cs) execute readers and materialize models through those methods. They are distinct from IDatabaseAccess's current-row helpers and Select's fluent helpers.

**Recommendation:** include `IAsyncEnumerable<T> GetFromQueryAsync<T>(string query, ct) where T : IModel` and `IAsyncEnumerable<T> GetFromCommandAsync<T>(IDbCommand dbCommand, ct) where T : IModel` on the existing class hierarchy. Use unsupported virtual defaults for old subclasses and real built-in overrides. Preserve model/materialization limits, ordinary sequence capture, borrowed command lifetime and managed transaction gates; do not infer arbitrary raw SQL is harmless because the API returns rows. No additional `IDataSourceAccess` interface members or DTO-mapping feature.

**Why a decision is needed:** the general mirrored-execution policy points toward inclusion, but the accepted named inventories did not explicitly cover this public receiver. Record the two exact members and compatibility strategy before declaring the inventory complete.

### G02: Public Provider Transaction Completion

[DatabaseTransaction](../../../../src/DataLinq/Database/DatabaseTransaction.cs) is a public abstract class, returned by provider factories, with public `Commit`, `Rollback` and `Dispose`. It is different from the managed `DataLinq.Mutation.Transaction`. AAPI-1/AAPI-82 require async initialization internally, but do not explicitly settle new public completion members on this lower-level class.

**Recommendation:** add virtual `Task CommitAsync(ct)`, `Task RollbackAsync(ct)` and `ValueTask DisposeAsync()` with explicit unsupported defaults for legacy implementations and built-in overrides. Preserve constructors, old abstract synchronous members, lifecycle events and `DatabaseTransactionStatus`. Do not expose a new public eager `OpenAsync`/`BeginAsync` factory.

**Boundary:** direct provider-level completion cannot promise the managed wrapper's cache/mutable finalization. If a provider transaction is owned by a managed transaction, callers must complete through that managed wrapper; retain the existing escape-hatch warning. Alternatively keeping async completion internal would leave this public I/O family sync-only, which needs an explicit exclusion rather than omission.

### G03: Numeric Values And Options Namespace

AAPI-92/AAPI-93 name enums but do not assign numbers; AAPI-63/AAPI-96 do not choose an execution-options namespace.

**Recommendation:** place `DataLinqExecutionOptions` in `DataLinq` beside `DatabaseProvider`, and give the diagnostic enums explicit stable values. For cause, operation and stage, use the listed order with `Unknown = 0`, then consecutive values (1–7, 1–17 and 1–11 respectively). For completion use `Unknown = 0, NotApplicable = 1, NotAttempted = 2, Committed = 3, RolledBack = 4`. Keep AAPI-94's exact flags unchanged.

**Reason:** an uninitialized classification should express lack of evidence; in particular, zero completion should not accidentally assert “not applicable.” This numeric recommendation changes no accepted member meaning, but the exact assignments and namespace should be recorded before consumer baselines.

### Expansion Work With No New Feature Proposal

| ID | Remaining work | Recommendation / gate |
| --- | --- | --- |
| E01 | Exact generic Min/Max nullable declarations | Compile Q10 against reference/value/nullable projections and preserve supported runtime semantics; do not add numeric/custom-type translation |
| E02 | Exact relation reduction list | Expand local selector-based Sum/Average into the same ten numeric shapes, and generic Min/Max with local `Func` selectors; row predicates remain local `Func<T, bool>`. Review empty/null semantics and avoid unrelated LINQ operators |
| E03 | Interface/base disposal and options dispatch | Verify T02/C02 on old implementers/subclasses. Keep concrete `DisposeAsync` visible and never make a default call synchronous database cleanup. Settle exact `IDatabaseProvider` inheritance/member implementation before W3 |
| E04 | New runtime validation supporting API | D10-4 owns package, namespace and full options/result/exception declarations; the existing Tools/CLI validator is not proof those runtime types exist |
| E05 | Async LINQ package version | Select and pin a compatible version at implementation; verify actual packed dependency groups and consumer resolution, not only a project reference |
| E06 | Full emitted signature manifest | Expand table notation into compiled public API baselines, generated fixtures and C02 declarations after G01–G03 disposition; track every accepted/excluded receiver and approved break |

G01/G02 are concrete uncovered public I/O boundaries. G03 pins remaining public constants/placement. E01–E06 turn accepted policy into exact compilation and integration evidence; they must not be relabeled as completed because this document exists.

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

Next gates, in order:

1. **Finish the source/API planning audit:** disposition G01–G03 and expand E01–E06 with the appropriate A10/H10/V10/T10 owners. OAPI-7 remains open.
2. **W0:** capture the real before-state I/O, compatibility and performance evidence. This document's source scan is only an input, not completion of W0.
3. **W1/W2:** establish internal async contracts and prove provider feasibility, cancellation/timeout distinction, first initialization, ownership, cache publication, completion certainty and cleanup.
4. **W3:** implement the public surface against those contracts; compile the full signature/generator manifest and run B01–B16 using packed consumers and ApiCompat.
5. **Release integration:** supply deterministic and real-provider evidence from S9; reconcile hosted validation/testing integration, packaging, benchmarks and migration notes with the release evidence plan.

No runtime implementation, package publication, baseline benchmark, provider experiment or consumer compilation was performed to create this document.
