using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class TransactionQueryOwnershipTests
{
    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task QueryShapes_RetainAdmissionUntilOuterEnumerationEnds(TestProviderDescriptor descriptor)
    {
        using var scope = TemporaryModelTestDatabase<CacheIndexPublicationDb>.Create(descriptor, "query_ownership_shapes");
        scope.Database.Provider.State.Cache.CleanupScheduler?.Stop();
        scope.Database.Insert(new MutableCacheIndexParent { Id = 1 });
        scope.Database.Insert(new MutableCacheIndexChild { Id = 1, ParentId = 1 });
        scope.Database.Insert(new MutableCacheIndexChild { Id = 2, ParentId = 1 });
        using var transaction = scope.Database.Transaction();
        var root = transaction.Query();
        for (var pass = 0; pass < 2; pass++)
        {
            await CheckSequence(root.Children.OrderBy(row => row.Id), transaction);
            await CheckSequence(root.Children.Where(row => row.Id == 1), transaction);
            await CheckSequence(root.Children.Select(row => row.Id), transaction);
            await CheckSequence(root.Children.Select(row => new { row.Id, row.ParentId }), transaction);
            await CheckSequence(root.Children.Select(row => new OwnershipBox(row.Id)), transaction);
            await CheckSequence(root.Children.Select(row => new OwnershipBox(row.Parent.Id)), transaction);
            await CheckSequence(root.Children.GroupBy(row => row.ParentId)
                .Select(group => new { ParentId = group.Key, Count = group.Count() }), transaction);
        }
        await Assert.That(root.Children.Count()).IsEqualTo(2);
        transaction.Rollback();
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task RelationSnapshots_RetainAdmissionForWarmAndColdEnumeration(TestProviderDescriptor descriptor)
    {
        using var scope = TemporaryModelTestDatabase<CacheIndexPublicationDb>.Create(descriptor, "relation_ownership_snapshot");
        scope.Database.Provider.State.Cache.CleanupScheduler?.Stop();
        scope.Database.Insert(new MutableCacheIndexParent { Id = 1 });
        scope.Database.Insert(new MutableCacheIndexChild { Id = 1, ParentId = 1 });
        scope.Database.Insert(new MutableCacheIndexChild { Id = 2, ParentId = 1 });
        using var transaction = scope.Database.Transaction();
        var parent = transaction.Query().Parents.Single(row => row.Id == 1);
        var relation = parent.Children;
        for (var pass = 0; pass < 2; pass++)
        {
            // The second pass exercises the already published relation snapshot.
            await CheckSequence(relation, transaction);
            await CheckSequence(relation.AsEnumerable(), transaction);
            await Assert.That(relation.Values.Length).IsEqualTo(2);
            await Assert.That(relation.ToFrozenDictionary().Count).IsEqualTo(2);
        }
        var child = relation.Values[0];
        await Assert.That(child.Parent).IsSameReferenceAs(parent);
        using (transaction.ExecutionGate.Enter("another operation"))
        {
            _ = Capture<InvalidOperationException>(() => _ = relation.Values);
            _ = Capture<InvalidOperationException>(() => _ = relation.Keys);
            _ = Capture<InvalidOperationException>(() => _ = child.Parent);
            // Cold enumerator construction must not attempt admission.
            using var cold = relation.GetEnumerator();
        }
        relation.Clear();
        using var captured = relation.GetEnumerator();
        transaction.Commit();
        _ = Capture<InvalidOperationException>(() => captured.MoveNext());
        // Fresh access after confirmed completion is still allowed to change sources.
        await Assert.That(relation.Values.Length).IsEqualTo(2);
        await Assert.That(child.GetReadSource()).IsSameReferenceAs(scope.Database.Provider.ReadOnlyAccess);
    }

    [Test]
    [Property(TestProviderAffinity.PropertyName, TestProviderAffinity.EveryProvider)]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task BufferedMultiRowQuery_PreservesIdentityAndPendingTransactionValues(TestProviderDescriptor descriptor)
    {
        using var scope = TemporaryModelTestDatabase<CacheIndexPublicationDb>.Create(descriptor, "query_ownership_pending");
        scope.Database.Provider.State.Cache.CleanupScheduler?.Stop();
        scope.Database.Insert(new MutableCacheIndexParent { Id = 1 });
        scope.Database.Insert(new MutableCacheIndexChild { Id = 1, ParentId = 1 });
        using var transaction = scope.Database.Transaction();
        var inserted = transaction.Insert(new MutableCacheIndexChild { Id = 2, ParentId = 1 });
        var root = transaction.Query();
        var query = root.Children.OrderBy(row => row.Id);
        using var rows = query.GetEnumerator();
        await Assert.That(rows.MoveNext()).IsTrue();
        _ = Capture<InvalidOperationException>(() => root.Children.Count());
        await Assert.That(rows.MoveNext()).IsTrue();
        await Assert.That(rows.Current).IsSameReferenceAs(inserted);
        await Assert.That(rows.MoveNext()).IsFalse();
        await Assert.That(query.ToArray()[1]).IsSameReferenceAs(inserted);
        transaction.Rollback();
        await Assert.That(scope.Database.Query().Children.Count()).IsEqualTo(1);
    }

    private static async Task CheckSequence<T>(IEnumerable<T> sequence, Transaction transaction)
    {
        using var rows = sequence.GetEnumerator();
        using (transaction.ExecutionGate.Enter("cold sequence construction")) { }
        await Assert.That(rows.MoveNext()).IsTrue();
        _ = Capture<InvalidOperationException>(transaction.Commit);
        _ = Capture<InvalidOperationException>(transaction.Rollback);
        _ = Capture<InvalidOperationException>(transaction.Dispose);
        rows.Dispose();
        using (transaction.ExecutionGate.Enter("released after enumeration")) { }
    }

    private static TException Capture<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public sealed record OwnershipBox(int Id);
}
