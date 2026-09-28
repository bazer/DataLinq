using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataLinq.Execution;
using DataLinq.Instances;
using DataLinq.Linq.Planning.Expressions;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class NativeAsyncReadLifetimeTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task CancellationBetweenGeneratedRowsSettlesReaderWithoutInferringTransactionIntegrity(TestProviderDescriptor descriptor)
    {
        using var scope = new NativeAsyncTestDatabase<CachePublicationDb>(descriptor, nameof(CancellationBetweenGeneratedRowsSettlesReaderWithoutInferringTransactionIntegrity));
        var database = scope.Database;
        await database.InsertAsyncCore(new MutableCachePublicationRow { Id = 1, Name = "one" });
        await database.InsertAsyncCore(new MutableCachePublicationRow { Id = 2, Name = "two" });
        database.Cache.Clear();
        var transaction = database.Transaction();
        using var cancellation = new CancellationTokenSource();
        var query = transaction.Query().Rows.OrderBy(row => row.Id);
        var rows = ((ExpressionQueryPlanProvider)query.Provider).ExecuteEnumerableAsyncCore<CachePublicationRow>(query.Expression, cancellation.Token);
        await using var enumerator = rows.GetAsyncEnumerator();
        try
        {
            await Assert.That(await enumerator.MoveNextAsync()).IsTrue();
            await Assert.That(enumerator.Current.Id).IsEqualTo(1);
            var connection = transaction.DatabaseAccess.DbTransaction!.Connection!;
            cancellation.Cancel();
            var failure = await Assert.That(async () => { await enumerator.MoveNextAsync(); }).Throws<OperationCanceledException>();
            await Assert.That(failure!.CancellationToken).IsEqualTo(cancellation.Token);
            var context = ExecutionFailureContexts.Get(failure)!;
            await Assert.That(context.Cause).IsEqualTo(ExecutionFailureCause.Cancellation);
            await Assert.That(context.Operation).IsEqualTo(ExecutionOperationKind.Query);
            await Assert.That(context.Completion).IsEqualTo(ExecutionCompletion.NotAttempted);
            await Assert.That(context.HasCleanupFailure).IsFalse();
            await Assert.That(context.TransactionId).IsEqualTo(transaction.TransactionID);
            await Assert.That(context.ProviderInstanceId).IsEqualTo(database.Provider.TelemetryInstanceId);
            await Assert.That(context.Recovery).IsEqualTo(ExecutionRecoveryActions.Rollback | ExecutionRecoveryActions.Dispose);
            await Assert.That(connection.State).IsEqualTo(ConnectionState.Open);
            await Assert.That(async () => { await AsyncModelLookup.GetByModelKeyAsyncCore<CachePublicationRow>([2], transaction); })
                .Throws<InvalidOperationException>();
            await Assert.That(async () => { await transaction.CommitAsyncCore(); }).Throws<InvalidOperationException>();
            await enumerator.DisposeAsync();
            await transaction.RollbackAsyncCore();
        }
        finally
        {
            try { await enumerator.DisposeAsync(); }
            finally { await transaction.DisposeAsyncCore(); }
        }
        await Assert.That((await AsyncModelLookup.GetByModelKeyAsyncCore<CachePublicationRow>([1], database.Provider.ReadOnlyAccess))!.Name)
            .IsEqualTo("one");
        await Assert.That((await AsyncModelLookup.GetByModelKeyAsyncCore<CachePublicationRow>([2], database.Provider.ReadOnlyAccess))!.Name)
            .IsEqualTo("two");
    }
}
