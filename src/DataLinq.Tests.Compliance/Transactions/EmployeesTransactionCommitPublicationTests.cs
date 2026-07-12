using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
using TUnit.Core;

namespace DataLinq.Tests.Compliance;

[ParallelLimiter<EmployeesTransactionLifecycleParallelLimit>]
public sealed class EmployeesTransactionCommitPublicationTests
{
    private readonly EmployeesTestData employees = new();

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task Commit_WrapperCommittedStatusObservesPublishedStateAndReusableMutable(
        TestProviderDescriptor provider)
    {
        const int employeeNumber = 995501;
        const string originalFirstName = "Original";
        const string committedFirstName = "Published";
        const string reusedFirstName = "Reused";

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            nameof(Commit_WrapperCommittedStatusObservesPublishedStateAndReusableMutable),
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        var initialMutable = employees.NewEmployee(employeeNumber);
        initialMutable.first_name = originalFirstName;
        _ = database.Insert(initialMutable);

        var cachedBeforeCommit = database.Query().Employees
            .Single(x => x.emp_no == employeeNumber);
        var mutable = cachedBeforeCommit.Mutate();
        var table = database.Provider.Metadata.GetTableModel(typeof(Employee)).Table;
        var cache = database.Provider.GetTableCache(table);
        var subscriber = new CountingCacheNotification();
        cache.SubscribeToChanges(
            subscriber,
            transaction: null,
            relationKey: null,
            loadedPrimaryKeys: [cachedBeforeCommit.PrimaryKeys()]);

        using var transaction = database.Transaction();

        mutable.first_name = committedFirstName;
        _ = transaction.Update(mutable);

        await Assert.That(subscriber.ClearCount).IsEqualTo(0);
        await Assert.That(cache.IsTransactionInCache(transaction)).IsTrue();
        await Assert.That(cache.GetTransactionRows(transaction)).IsNotEmpty();
        await Assert.That(transaction.TouchedMutables.Count).IsEqualTo(1);
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.TransactionLocal);
        await Assert.That(mutable.HasStoredCommittedBaseline).IsFalse();

        var observation = new CommitCallbackObservation();
        transaction.OnStatusChanged += (sender, args) =>
        {
            if (args.Status != DatabaseTransactionStatus.Committed)
                return;

            observation.CallbackCount++;
            observation.Sender = sender;
            observation.EventTransaction = args.Transaction;
            observation.EventStatus = args.Status;
            observation.WrapperStatus = transaction.Status;
            observation.NotificationCount = subscriber.ClearCount;
            observation.TransactionCached = cache.IsTransactionInCache(transaction);
            observation.TransactionRowCount = cache.GetTransactionRows(transaction).Count();
            observation.TouchedMutableCount = transaction.TouchedMutables.Count;
            observation.Lifecycle = mutable.Lifecycle;
            observation.OwnershipOutcome = transaction.MutableOwnership.Outcome;

            try
            {
                observation.OutsideFirstName = database.Query().Employees
                    .Single(x => x.emp_no == employeeNumber)
                    .first_name;
            }
            catch (Exception exception)
            {
                observation.OutsideReadException = exception;
            }
        };

        transaction.Commit();

        await Assert.That(observation.CallbackCount).IsEqualTo(1);
        await Assert.That(observation.Sender).IsSameReferenceAs(transaction);
        await Assert.That(observation.EventTransaction).IsSameReferenceAs(transaction);
        await Assert.That(observation.EventStatus)
            .IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(observation.WrapperStatus)
            .IsEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(observation.NotificationCount).IsEqualTo(1);
        await Assert.That(observation.TransactionCached).IsFalse();
        await Assert.That(observation.TransactionRowCount).IsEqualTo(0);
        await Assert.That(observation.TouchedMutableCount).IsEqualTo(0);
        await Assert.That(observation.OwnershipOutcome)
            .IsEqualTo(MutableTransactionOutcome.Committed);
        await Assert.That(observation.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(observation.Lifecycle.ProviderOwner)
            .IsSameReferenceAs(database.Provider);
        await Assert.That(observation.Lifecycle.TransactionOwner).IsNull();
        await Assert.That(observation.Lifecycle.InvalidationReason).IsNull();
        await Assert.That(mutable.HasStoredCommittedBaseline).IsTrue();
        await Assert.That(observation.OutsideReadException).IsNull();
        await Assert.That(observation.OutsideFirstName)
            .IsEqualTo(committedFirstName);

        mutable.first_name = reusedFirstName;
        var reused = database.Update(mutable);

        await Assert.That(reused.first_name).IsEqualTo(reusedFirstName);
        await Assert.That(mutable.GetChanges()).IsEmpty();
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Committed);
        await Assert.That(mutable.Lifecycle.ProviderOwner)
            .IsSameReferenceAs(database.Provider);
        await Assert.That(mutable.Lifecycle.TransactionOwner).IsNull();

        database.Provider.State.ClearCache();
        var persistedAfterReuse = database.Query().Employees
            .Single(x => x.emp_no == employeeNumber);

        await Assert.That(persistedAfterReuse.first_name)
            .IsEqualTo(reusedFirstName);
    }

    private sealed class CountingCacheNotification : ICacheNotification
    {
        internal int ClearCount { get; private set; }

        public void Clear() => ClearCount++;
    }

    private sealed class CommitCallbackObservation
    {
        internal int CallbackCount { get; set; }
        internal object? Sender { get; set; }
        internal Transaction? EventTransaction { get; set; }
        internal DatabaseTransactionStatus? EventStatus { get; set; }
        internal DatabaseTransactionStatus? WrapperStatus { get; set; }
        internal int NotificationCount { get; set; }
        internal bool TransactionCached { get; set; }
        internal int TransactionRowCount { get; set; }
        internal int TouchedMutableCount { get; set; }
        internal MutableLifecycleSnapshot Lifecycle { get; set; }
        internal MutableTransactionOutcome? OwnershipOutcome { get; set; }
        internal string? OutsideFirstName { get; set; }
        internal Exception? OutsideReadException { get; set; }
    }
}
