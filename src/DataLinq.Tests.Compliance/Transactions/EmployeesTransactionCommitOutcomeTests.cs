using System;
using System.Data;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DataLinq.Cache;
using DataLinq.Exceptions;
using DataLinq.Instances;
using DataLinq.Mutation;
using DataLinq.Tests.Models.Employees;
using DataLinq.Testing;
using TUnit.Core;

namespace DataLinq.Tests.Compliance;

[ParallelLimiter<EmployeesTransactionLifecycleParallelLimit>]
public sealed class EmployeesTransactionCommitOutcomeTests
{
    private readonly EmployeesTestData employees = new();

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public Task Transaction_CommitBoundaryThrowsBeforeNativeCommit_RollsBackActualOutcome(
        TestProviderDescriptor provider) =>
        AssertCommitFailureOutcome(
            provider,
            nameof(Transaction_CommitBoundaryThrowsBeforeNativeCommit_RollsBackActualOutcome),
            employeeNumber: 995801,
            commitNativeBeforeThrow: false);

    [Test]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public Task Transaction_NativeCommitCompletesThenBoundaryThrows_RematerializesActualOutcome(
        TestProviderDescriptor provider) =>
        AssertCommitFailureOutcome(
            provider,
            nameof(Transaction_NativeCommitCompletesThenBoundaryThrows_RematerializesActualOutcome),
            employeeNumber: 995802,
            commitNativeBeforeThrow: true);

    private async Task AssertCommitFailureOutcome(
        TestProviderDescriptor provider,
        string testName,
        int employeeNumber,
        bool commitNativeBeforeThrow)
    {
        const string committedFirstName = "BeforeFault";
        const string pendingFirstName = "AfterFault";

        using var databaseScope = EmployeesTestDatabase.CreateIsolated(
            provider,
            testName,
            EmployeesSeedMode.None);
        var database = databaseScope.Database;
        var seed = employees.NewEmployee(employeeNumber);
        seed.first_name = committedFirstName;
        database.Insert(seed);

        var mutable = database.Query().Employees
            .Single(x => x.emp_no == employeeNumber)
            .Mutate();
        var table = database.Provider.Metadata
            .GetTableModel(typeof(Employee))
            .Table;
        var cache = database.Provider.GetTableCache(table);

        await Assert.That(cache.RowCount).IsGreaterThan(0);

        using IDbConnection connection = database.Provider.GetDbConnection();
        connection.Open();
        using var providerTransaction = connection.BeginTransaction(
            IsolationLevel.ReadCommitted);
        using var transaction = database.AttachTransaction(providerTransaction);

        mutable.first_name = pendingFirstName;
        transaction.Update(mutable);

        var transactionRow = transaction.Query().Employees
            .Single(x => x.emp_no == employeeNumber);
        await Assert.That(transactionRow.first_name)
            .IsEqualTo(pendingFirstName);
        await Assert.That(transaction.SuccessfulChanges.Count).IsEqualTo(1);
        await Assert.That(transaction.TouchedMutables.Single())
            .IsSameReferenceAs(mutable);

        var commitFailure = new InjectedCommitBoundaryException(
            $"Injected {provider.Name} commit boundary failure.");
        using var faultTransaction = new CommitBoundaryFaultTransaction(
            providerTransaction,
            commitFailure,
            commitNativeBeforeThrow);
        ReplaceCompletionTransaction(
            transaction,
            providerTransaction,
            faultTransaction);

        var observedFailure = Capture<InjectedCommitBoundaryException>(
            transaction.Commit);

        await Assert.That(observedFailure).IsSameReferenceAs(commitFailure);
        await Assert.That(faultTransaction.CommitCalls).IsEqualTo(1);
        await Assert.That(faultTransaction.NativeCommitCalls)
            .IsEqualTo(commitNativeBeforeThrow ? 1 : 0);
        await Assert.That(transaction.Status)
            .IsNotEqualTo(DatabaseTransactionStatus.Committed);
        await Assert.That(transaction.MutableOwnership.Outcome)
            .IsEqualTo(MutableTransactionOutcome.CommitOutcomeUnknown);
        await Assert.That(transaction.SuccessfulChanges.Count).IsEqualTo(1);
        await Assert.That(transaction.SuccessfulChanges.Single().Model)
            .IsSameReferenceAs(mutable);
        await Assert.That(transaction.TouchedMutables).IsEmpty();
        await Assert.That(mutable.Lifecycle.BaselineKind)
            .IsEqualTo(MutableBaselineKind.Invalid);
        await Assert.That(mutable.Lifecycle.TransactionOwner).IsNull();
        await Assert.That(mutable.Lifecycle.InvalidationReason)
            .IsEqualTo(MutableInvalidationReason.CommitOutcomeUnknown);
        await AssertManagedCompletionContext(
            observedFailure,
            transaction,
            "Commit",
            MutableInvalidationReason.CommitOutcomeUnknown);
        await Assert.That(database.Provider.State.Cache.TableCaches.Values.All(
            IsStructurallyEmpty)).IsTrue();

        var reuseFailure = Capture<MutationGuardException>(() =>
            database.Update(mutable));
        await Assert.That(reuseFailure.Message)
            .Contains("commit outcome is unknown");

        if (commitNativeBeforeThrow)
        {
            await Assert.That(providerTransaction.Connection?.State)
                .IsNotEqualTo(ConnectionState.Open);
            await Assert.That(faultTransaction.RollbackCalls).IsEqualTo(0);
        }
        else
        {
            await Assert.That(providerTransaction.Connection?.State)
                .IsEqualTo(ConnectionState.Open);

            var rollbackDiagnostic = Capture<InvalidOperationException>(
                transaction.Rollback);
            await Assert.That(rollbackDiagnostic.Message)
                .Contains("earlier provider Commit() call failed");
            await Assert.That(faultTransaction.RollbackCalls).IsEqualTo(1);
            await Assert.That(transaction.Status)
                .IsEqualTo(DatabaseTransactionStatus.RolledBack);
            await Assert.That(transaction.MutableOwnership.Outcome)
                .IsEqualTo(MutableTransactionOutcome.CommitOutcomeUnknown);
        }

        transaction.Dispose();
        await Assert.That(transaction.IsDisposed).IsTrue();

        var persisted = database.Query().Employees
            .Single(x => x.emp_no == employeeNumber);
        await Assert.That(persisted.first_name)
            .IsEqualTo(commitNativeBeforeThrow
                ? pendingFirstName
                : committedFirstName);
    }

    private static void ReplaceCompletionTransaction(
        Transaction transaction,
        IDbTransaction expected,
        IDbTransaction replacement)
    {
        var property = typeof(DatabaseTransaction).GetProperty(
            nameof(DatabaseTransaction.DbTransaction),
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                "Could not locate the provider transaction handle.");

        if (!ReferenceEquals(
                transaction.DatabaseAccess.DbTransaction,
                expected))
        {
            throw new InvalidOperationException(
                "The attached provider transaction was not the expected native handle.");
        }

        // Commands above ran through the concrete provider transaction. Replace only the
        // completion handle so the real provider can commit before the injected failure.
        property.SetValue(transaction.DatabaseAccess, replacement);
    }

    private static async Task AssertManagedCompletionContext(
        Exception exception,
        Transaction transaction,
        string operation,
        MutableInvalidationReason expectedReason)
    {
        await Assert.That(exception.Data["DataLinq.TransactionId"])
            .IsEqualTo(transaction.TransactionID);
        await Assert.That(exception.Data["DataLinq.CompletionOperation"])
            .IsEqualTo(operation);
        await Assert.That(
            exception.Data["DataLinq.LocalFinalizationAttempted"] is true)
            .IsTrue();
        await Assert.That(exception.Data["DataLinq.MutableInvalidationReason"])
            .IsEqualTo(expectedReason.ToString());
        await Assert.That(exception.Data.Contains(
            "DataLinq.SecondaryCompletionFailures")).IsFalse();
    }

    private static bool IsStructurallyEmpty(TableCache cache) =>
        cache.RowCount == 0 &&
        cache.TransactionRowsCount == 0 &&
        cache.IndicesCount.All(index => index.count == 0);

    private static TException Capture<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name}.");
    }

    private sealed class CommitBoundaryFaultTransaction(
        IDbTransaction transaction,
        InjectedCommitBoundaryException commitFailure,
        bool commitNativeBeforeThrow) : IDbTransaction
    {
        private bool disposed;

        public IDbConnection? Connection => transaction.Connection;
        public IsolationLevel IsolationLevel => transaction.IsolationLevel;
        public int CommitCalls { get; private set; }
        public int NativeCommitCalls { get; private set; }
        public int RollbackCalls { get; private set; }

        public void Commit()
        {
            CommitCalls++;
            if (commitNativeBeforeThrow)
            {
                NativeCommitCalls++;
                transaction.Commit();
            }

            throw commitFailure;
        }

        public void Rollback()
        {
            RollbackCalls++;
            transaction.Rollback();
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            transaction.Dispose();
        }
    }

    private sealed class InjectedCommitBoundaryException(string message)
        : Exception(message);
}
