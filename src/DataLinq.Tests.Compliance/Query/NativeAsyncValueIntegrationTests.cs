using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Attributes;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Interfaces;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Mutation;
using DataLinq.Testing;
using Microsoft.Extensions.Logging;

namespace DataLinq.Tests.Compliance;

public sealed class NativeAsyncValueIntegrationTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task GeneratedConvertedValuesHydratePrivatelyAndCommitOrRollbackTogether(TestProviderDescriptor descriptor)
    {
        using var scope = new NativeAsyncTestDatabase<NativeAsyncValueDb>(descriptor, nameof(GeneratedConvertedValuesHydratePrivatelyAndCommitOrRollbackTogether));
        var database = scope.Database;
        var table = database.Provider.Metadata.GetTableModel(typeof(NativeGeneratedValueRow)).Table;
        var cache = database.Provider.GetTableCache(table);
        var mutable = new MutableNativeGeneratedValueRow { Payload = [1, 2, 3] };
        var transaction = database.Transaction();
        NativeGeneratedValueRow inserted;
        try
        {
            inserted = await transaction.InsertAsyncCore(mutable);
            await Assert.That(inserted.Id).IsNotNull();
            await Assert.That(inserted.Id!.Value.Value).IsGreaterThan(0);
            await Assert.That(inserted.ServerValue).IsEqualTo(new ConvertedServerValue(42));
            await Assert.That(mutable.Id).IsEqualTo(inserted.Id);
            await Assert.That(mutable.ServerValue).IsEqualTo(inserted.ServerValue);
            await Assert.That(mutable.HasChanges()).IsFalse();
            await Assert.That(cache.RowCount).IsEqualTo(0);
            await Assert.That(transaction.Changes.Single().PrimaryKeys.GetValue(0)).IsTypeOf<int>();
            var local = await AsyncModelLookup.GetByModelKeyAsyncCore<NativeGeneratedValueRow>([inserted.Id], transaction);
            await Assert.That(local!.ServerValue).IsEqualTo(new ConvertedServerValue(42));
            await transaction.CommitAsyncCore();
        }
        finally { await transaction.DisposeAsyncCore(); }
        var committed = await AsyncModelLookup.GetByModelKeyAsyncCore<NativeGeneratedValueRow>([inserted.Id], database.Provider.ReadOnlyAccess);
        await Assert.That(committed!.ServerValue).IsEqualTo(new ConvertedServerValue(42));
        await Assert.That(cache.RowCount).IsEqualTo(1);
        database.Cache.Clear();
        var reloaded = (await Rows(AsyncPlan(database.Query().Generated.Where(row => row.Id == inserted.Id)))).Single();
        await Assert.That(reloaded.ServerValue).IsEqualTo(new ConvertedServerValue(42));
        await Assert.That(reloaded.Payload).IsEquivalentTo(new byte[] { 1, 2, 3 });
        var nullablePayloadCopy = reloaded.Payload!;
        nullablePayloadCopy[0] = 99;
        await Assert.That(reloaded.Payload).IsEquivalentTo(new byte[] { 1, 2, 3 });

        var rolledBack = new MutableNativeGeneratedValueRow();
        var rollback = database.Transaction();
        ConvertedAutoIncrementId? rollbackId = null;
        try
        {
            var privateRow = await rollback.SaveAsyncCore(rolledBack);
            rollbackId = privateRow.Id;
            await Assert.That(rollbackId).IsNotNull();
            await Assert.That(rolledBack.ServerValue).IsEqualTo(new ConvertedServerValue(42));
            await rollback.RollbackAsyncCore();
        }
        finally { await rollback.DisposeAsyncCore(); }
        await Assert.That(((IMutableLifecycle)rolledBack).Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(await AsyncModelLookup.GetByModelKeyAsyncCore<NativeGeneratedValueRow>([rollbackId], database.Provider.ReadOnlyAccess)).IsNull();
        // Explicit null bypasses the database default; an unset value above did not.
        var explicitNull = await database.InsertAsyncCore(new MutableNativeGeneratedValueRow { ServerValue = null });
        await Assert.That(explicitNull.ServerValue).IsNull();
        await Assert.That(explicitNull.Payload).IsNull();
        var empty = await database.InsertAsyncCore(new MutableNativeGeneratedValueRow { Payload = [] });
        await Assert.That(empty.Payload).IsNotNull();
        await Assert.That(empty.Payload!).IsEmpty();
        database.Cache.Clear();
        await Assert.That((await AsyncModelLookup.GetByModelKeyAsyncCore<NativeGeneratedValueRow>([explicitNull.Id], database.Provider.ReadOnlyAccess))!.ServerValue).IsNull();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CompositeBinaryAndConvertedKeysRetainCanonicalIdentityAndOwnedBuffers(TestProviderDescriptor descriptor)
    {
        using var scope = new NativeAsyncTestDatabase<NativeAsyncValueDb>(descriptor, nameof(CompositeBinaryAndConvertedKeysRetainCanonicalIdentityAndOwnedBuffers));
        var database = scope.Database;
        var tenant = new QueryTypedId(7);
        await database.InsertAsyncCore(new MutableNativeCompositeValueRow { Tenant = tenant, BinaryId = [0, 1, 255], Payload = [4, 5, 6] });
        database.Cache.Clear();
        var row = (await AsyncModelLookup.GetByModelKeyAsyncCore<NativeCompositeValueRow>([tenant, new byte[] { 0, 1, 255 }], database.Provider.ReadOnlyAccess))!;
        await Assert.That(row.Tenant).IsEqualTo(tenant);
        await Assert.That(row.PrimaryKeys().GetValue(0)).IsTypeOf<int>();
        await Assert.That(row.PrimaryKeys().GetValue(1)).IsEquivalentTo(new byte[] { 0, 1, 255 });
        await Assert.That(await AsyncModelLookup.GetByProviderKeyAsyncCore<NativeCompositeValueRow>(row.PrimaryKeys(), database.Provider.ReadOnlyAccess)).IsSameReferenceAs(row);
        var keyCopy = (byte[])row.PrimaryKeys().GetValue(1)!;
        keyCopy[0] = 99;
        await Assert.That(await AsyncModelLookup.GetByModelKeyAsyncCore<NativeCompositeValueRow>([tenant, new byte[] { 0, 1, 255 }], database.Provider.ReadOnlyAccess)).IsSameReferenceAs(row);
        var previousPayload = row.Payload;
        previousPayload[0] = 99;
        await Assert.That(row.Payload).IsEquivalentTo(new byte[] { 4, 5, 6 });
        var binaryIdCopy = row.BinaryId;
        binaryIdCopy[0] = 77;
        await Assert.That(row.BinaryId).IsEquivalentTo(new byte[] { 0, 1, 255 });
        var cachedSync = database.Query().Composite.Single(value => value.Tenant == tenant);
        var syncPayloadCopy = cachedSync.Payload;
        syncPayloadCopy[0] = 88;
        await Assert.That(cachedSync.Payload).IsEquivalentTo(new byte[] { 4, 5, 6 });
        database.Cache.Clear();
        row = (await AsyncModelLookup.GetByModelKeyAsyncCore<NativeCompositeValueRow>([tenant, new byte[] { 0, 1, 255 }], database.Provider.ReadOnlyAccess))!;
        // Independent materializations, reader buffers and key snapshots must
        // also preserve ownership after invalidation.
        await Assert.That(row.Payload).IsNotSameReferenceAs(previousPayload);
        previousPayload[0] = 99;
        await Assert.That(row.Payload).IsEquivalentTo(new byte[] { 4, 5, 6 });
        var projected = (await Rows(AsyncPlan(database.Query().Composite.Where(value => value.Tenant == tenant).Select(value => value.Payload)))).Single();
        projected[0] = 88;
        await Assert.That((await Rows(AsyncPlan(database.Query().Composite.Select(value => value.Payload)))).Single()).IsEquivalentTo(new byte[] { 4, 5, 6 });
        var changed = row.Mutate();
        changed.Payload = [9, 8, 7];
        var transaction = database.Transaction();
        try
        {
            var updated = await transaction.UpdateAsyncCore(changed);
            await Assert.That(updated.Payload).IsEquivalentTo(new byte[] { 9, 8, 7 });
            await Assert.That((await AsyncModelLookup.GetByProviderKeyAsyncCore<NativeCompositeValueRow>(row.PrimaryKeys(), database.Provider.ReadOnlyAccess))!.Payload)
                .IsEquivalentTo(new byte[] { 4, 5, 6 });
            await transaction.CommitAsyncCore();
        }
        finally { await transaction.DisposeAsyncCore(); }
        database.Cache.Clear();
        var after = (await AsyncModelLookup.GetByModelKeyAsyncCore<NativeCompositeValueRow>([tenant, new byte[] { 0, 1, 255 }], database.Provider.ReadOnlyAccess))!;
        await Assert.That(after.Payload).IsEquivalentTo(new byte[] { 9, 8, 7 });
        await database.DeleteAsyncCore(after);
        await Assert.That(await AsyncModelLookup.GetByProviderKeyAsyncCore<NativeCompositeValueRow>(row.PrimaryKeys(), database.Provider.ReadOnlyAccess)).IsNull();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task OrdinaryAndPreparedQueriesCaptureConvertedArgumentsAtTheirDefinedBoundaries(TestProviderDescriptor descriptor)
    {
        using var scope = new NativeAsyncTestDatabase<TypedIdPredicateDb>(descriptor, nameof(OrdinaryAndPreparedQueriesCaptureConvertedArgumentsAtTheirDefinedBoundaries));
        var database = scope.Database;
        database.Provider.DatabaseAccess.ExecuteNonQuery("INSERT INTO typedidqueryrows (id, parent_id, name) VALUES (1, NULL, 'one'), (2, 1, 'two'), (3, 1, 'three')");
        var ids = new[] { new QueryTypedId(1), new QueryTypedId(2) };
        var query = database.Query().Rows.Where(row => ids.Contains(row.Id)).OrderBy(row => row.Id).Select(row => row.Id);
        var plan = database.PrepareSequenceQuery(ids, values => database.Query().Rows.Where(row => values.Contains(row.Id)).OrderBy(row => row.Id).Select(row => row.Id));
        var ordinary = AsyncPlan(query);
        var prepared = plan.ExecuteAsyncCore(database, ids);
        ids[0] = new QueryTypedId(3);
        var actual = new List<QueryTypedId>();
        await using (var enumerator = ordinary.GetAsyncEnumerator())
        {
            ids[1] = new QueryTypedId(4);
            while (await enumerator.MoveNextAsync()) actual.Add(enumerator.Current);
        }
        await Assert.That(actual.ToArray()).IsEquivalentTo(new[] { new QueryTypedId(2), new QueryTypedId(3) });
        await Assert.That((await Rows(prepared)).ToArray()).IsEquivalentTo(new[] { new QueryTypedId(1), new QueryTypedId(2) });
        await Assert.That((await Rows(ordinary)).ToArray()).IsEquivalentTo(new[] { new QueryTypedId(3) });
        await Assert.That((await Rows(plan.ExecuteAsyncCore(database, ids))).ToArray()).IsEquivalentTo(new[] { new QueryTypedId(3) });
        var projected = await Rows(AsyncPlan(database.Query().Rows.OrderBy(row => row.Id)
            .Select(row => new { row.Id, row.ParentId, Boxed = (object)row.Id })));
        await Assert.That(projected[0].ParentId).IsNull();
        await Assert.That(projected[1].ParentId).IsEqualTo(new QueryTypedId(1));
        await Assert.That(projected[2].Boxed).IsTypeOf<QueryTypedId>();
        await Assert.That(projected[2].Boxed).IsEqualTo(new QueryTypedId(3));
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task NativeMutationEscapedArrayUsesCapturedSqlAndRequiresRecoveryAfterWrite(TestProviderDescriptor descriptor)
    {
        using var logger = new DispatchLogger();
        using var scope = new NativeAsyncTestDatabase<NativeAsyncValueDb>(descriptor, nameof(NativeMutationEscapedArrayUsesCapturedSqlAndRequiresRecoveryAfterWrite), logger);
        var database = scope.Database;
        var original = new byte[] { 1, 2, 3 };
        var mutable = new MutableNativeGeneratedValueRow { Payload = original };
        var transaction = database.Transaction();
        using var entered = new ManualResetEventSlim();
        using var released = new ManualResetEventSlim();
        logger.Callback = sql =>
        {
            if (!sql.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)) return;
            entered.Set();
            if (!released.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("The test did not release native dispatch.");
        };
        // Only the test moves work to a dedicated thread so SQLite can reach the
        // synchronous logging barrier. Production execution adds no Task.Run.
        var pending = Task.Factory.StartNew(() => transaction.InsertAsyncCore(mutable), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        try
        {
            if (!entered.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("The insert did not reach native dispatch.");
            var native = transaction.DatabaseAccess.DbTransaction!;
            original[0] = 99;
            await Assert.That(() => mutable.Payload = [8, 8, 8]).Throws<InvalidOperationException>();
            await Assert.That(async () => { await transaction.CommitAsyncCore(); }).Throws<InvalidOperationException>();
            released.Set();
            var failure = await Assert.That(async () => { await pending.WaitAsync(TimeSpan.FromSeconds(20)); }).Throws<InvalidOperationException>();
            await Assert.That(transaction.IsPoisoned).IsTrue();
            await Assert.That(((IMutableLifecycle)mutable).Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
            var context = ExecutionFailureContexts.Get(failure!)!;
            await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Insert);
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.MaterializationError);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
            // Inspect through the saved native handle only to establish which
            // bytes actually reached the server. Managed business use is denied.
            using (var probe = native.Connection!.CreateCommand())
            {
                probe.Transaction = native;
                probe.CommandText = "SELECT payload FROM native_generated_values";
                await Assert.That(probe.ExecuteScalar()).IsEquivalentTo(new byte[] { 1, 2, 3 });
            }
            await Assert.That(async () => { await transaction.CommitAsyncCore(); }).Throws<InvalidOperationException>();
            await transaction.RollbackAsyncCore();
            await Assert.That((await Rows(AsyncPlan(database.Query().Generated))).Count).IsEqualTo(0);
        }
        finally
        {
            released.Set();
            try { await pending.WaitAsync(TimeSpan.FromSeconds(20)); }
            catch (InvalidOperationException) when (pending.IsFaulted) { }
            finally { logger.Callback = null; await transaction.DisposeAsyncCore(); }
        }
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task FiniteNativeBatchCapturesMembershipAndCannotCommitAfterPartialCancellation(TestProviderDescriptor descriptor)
    {
        foreach (var cancelSecond in new[] { false, true })
        {
            using var logger = new DispatchLogger();
            using var scope = new NativeAsyncTestDatabase<NativeAsyncValueDb>(descriptor, $"w2_batch_capture_{cancelSecond}", logger);
            var database = scope.Database;
            var first = new MutableNativeGeneratedValueRow { Payload = [1] };
            var second = new MutableNativeGeneratedValueRow { Payload = [2] };
            var source = new List<Mutable<NativeGeneratedValueRow>> { first, second };
            var enumerations = 0;
            IEnumerable<Mutable<NativeGeneratedValueRow>> Input()
            {
                enumerations++;
                foreach (var item in source) yield return item;
            }
            using var cancellation = new CancellationTokenSource();
            using var entered = new ManualResetEventSlim();
            using var released = new ManualResetEventSlim();
            var inserts = 0;
            logger.Callback = sql =>
            {
                if (!sql.TrimStart().StartsWith("INSERT", StringComparison.OrdinalIgnoreCase)) return;
                if (Interlocked.Increment(ref inserts) == 1)
                {
                    entered.Set();
                    if (!released.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("The batch was not released.");
                }
                else if (cancelSecond) cancellation.Cancel();
            };
            var transaction = database.Transaction();
            var pending = Task.Factory.StartNew(() => transaction.InsertAsyncCore(Input(), cancellation.Token),
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
            try
            {
                if (!entered.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("The batch did not reach its first native command.");
                var native = transaction.DatabaseAccess.DbTransaction!;
                await Assert.That(enumerations).IsEqualTo(1);
                source.Clear();
                await Assert.That(() => second.Payload = [9]).Throws<InvalidOperationException>();
                released.Set();
                if (cancelSecond)
                {
                    var failure = await Assert.That(async () => { await pending.WaitAsync(TimeSpan.FromSeconds(20)); }).Throws<OperationCanceledException>();
                    await Assert.That(transaction.IsPoisoned).IsTrue();
                    await Assert.That(transaction.Changes.Count).IsEqualTo(1);
                    await Assert.That(((IMutableLifecycle)first).Lifecycle.BaselineKind).IsEqualTo(MutableBaselineKind.Invalid);
                    await Assert.That(ExecutionFailureContexts.Get(failure!)!.Operation).IsEqualTo(ExecutionOperationKind.Insert);
                    // The second driver call receives a canceled token. Inspect
                    // the saved native handle to prove the retained partial write.
                    using (var probe = native.Connection!.CreateCommand())
                    {
                        probe.Transaction = native;
                        probe.CommandText = "SELECT COUNT(*) FROM native_generated_values";
                        await Assert.That(Convert.ToInt64(probe.ExecuteScalar())).IsEqualTo(1L);
                    }
                    await Assert.That(async () => { await transaction.CommitAsyncCore(); }).Throws<InvalidOperationException>();
                    await transaction.RollbackAsyncCore();
                    await Assert.That((await Rows(AsyncPlan(database.Query().Generated))).Count).IsEqualTo(0);
                }
                else
                {
                    var rows = await pending.WaitAsync(TimeSpan.FromSeconds(20));
                    await Assert.That(rows.Select(row => row.Payload![0]).ToArray()).IsEquivalentTo(new byte[] { 1, 2 });
                    await Assert.That(rows[0].Id!.Value.Value).IsLessThan(rows[1].Id!.Value.Value);
                    await Assert.That(transaction.Changes.Count).IsEqualTo(2);
                    await transaction.CommitAsyncCore();
                    await Assert.That((await Rows(AsyncPlan(database.Query().Generated))).Count).IsEqualTo(2);
                }
                second.Payload = [8]; // Reservation must be released on either result.
            }
            finally
            {
                released.Set();
                try { await pending.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (OperationCanceledException) when (pending.IsCanceled) { }
                finally { logger.Callback = null; await transaction.DisposeAsyncCore(); }
            }
        }
    }

    private sealed class DispatchLogger : ILoggerFactory, ILogger
    {
        internal Action<string>? Callback;
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Callback?.Invoke(formatter(state, exception));
    }

    private static IAsyncEnumerable<T> AsyncPlan<T>(IQueryable<T> query) =>
        ((ExpressionQueryPlanProvider)query.Provider).ExecuteEnumerableAsyncCore<T>(query.Expression, default);
    private static async Task<List<T>> Rows<T>(IAsyncEnumerable<T> source)
    {
        var rows = new List<T>();
        await foreach (var row in source) rows.Add(row);
        return rows;
    }
}

[UseCache, Database("w2_native_values")]
public sealed partial class NativeAsyncValueDb(DataSourceAccess source) : IDatabaseModel
{
    public DbRead<NativeGeneratedValueRow> Generated { get; } = new(source);
    public DbRead<NativeCompositeValueRow> Composite { get; } = new(source);
}

[Table("native_generated_values")]
public abstract partial class NativeGeneratedValueRow(IRowData rowData, IDataSourceAccess source)
    : Immutable<NativeGeneratedValueRow, NativeAsyncValueDb>(rowData, source), ITableModel<NativeAsyncValueDb>
{
    [PrimaryKey, AutoIncrement, Column("id"), ScalarConverter(typeof(ConvertedAutoIncrementIdConverter))]
    [Type(DatabaseType.SQLite, "INTEGER"), Type(DatabaseType.MySQL, "int", 11), Type(DatabaseType.MariaDB, "int", 11)]
    public abstract ConvertedAutoIncrementId? Id { get; }
    [Nullable, Column("server_value"), DefaultSql(DatabaseType.Default, "42"), ScalarConverter(typeof(ConvertedServerValueConverter))]
    [Type(DatabaseType.SQLite, "INTEGER"), Type(DatabaseType.MySQL, "int", 11), Type(DatabaseType.MariaDB, "int", 11)]
    public abstract ConvertedServerValue? ServerValue { get; }
    [Nullable, Column("payload")]
    [Type(DatabaseType.SQLite, "BLOB"), Type(DatabaseType.MySQL, "varbinary", 8), Type(DatabaseType.MariaDB, "varbinary", 8)]
    public abstract byte[]? Payload { get; }
}

[Table("native_composite_values")]
public abstract partial class NativeCompositeValueRow(IRowData rowData, IDataSourceAccess source)
    : Immutable<NativeCompositeValueRow, NativeAsyncValueDb>(rowData, source), ITableModel<NativeAsyncValueDb>
{
    [PrimaryKey, Column("tenant_id"), ScalarConverter(typeof(QueryTypedIdConverter))]
    [Type(DatabaseType.SQLite, "INTEGER"), Type(DatabaseType.MySQL, "int", 11), Type(DatabaseType.MariaDB, "int", 11)]
    public abstract QueryTypedId Tenant { get; }
    [PrimaryKey, Column("binary_id")]
    [Type(DatabaseType.SQLite, "BLOB"), Type(DatabaseType.MySQL, "varbinary", 8), Type(DatabaseType.MariaDB, "varbinary", 8)]
    public abstract byte[] BinaryId { get; }
    [Column("payload")]
    [Type(DatabaseType.SQLite, "BLOB"), Type(DatabaseType.MySQL, "varbinary", 8), Type(DatabaseType.MariaDB, "varbinary", 8)]
    public abstract byte[] Payload { get; }
}
