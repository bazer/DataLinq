using System;
using System.Linq;
using System.Threading.Tasks;
using DataLinq.Diagnostics;
using DataLinq.Exceptions;
using DataLinq.Testing;

namespace DataLinq.Tests.Compliance;

public sealed class QueryPlanCapabilityExecutionTests
{
    [Test]
    [NotInParallel]
    [MethodDataSource(typeof(TestProviderDataSources), nameof(TestProviderDataSources.ActiveProviders))]
    public async Task UnsupportedSqlCapability_FailsBeforeSequenceOrTerminalBackendWork(
        TestProviderDescriptor provider)
    {
        const string capturedValue = "sensitive-capability-binding-91";
        using var databaseScope = EmployeesTestDatabase.OpenSharedSeeded(
            provider,
            nameof(UnsupportedSqlCapability_FailsBeforeSequenceOrTerminalBackendWork),
            EmployeesSeedMode.Bogus);

        var employees = databaseScope.Database.Query().Employees;

        try
        {
            DataLinqMetrics.Reset();

            var sequenceFailure = Capture<QueryBackendCapabilityException>(() =>
                employees.Where(employee => employee.first_name.Substring(1) == capturedValue).ToList());
            var terminalFailure = Capture<QueryBackendCapabilityException>(() =>
                employees.Where(employee => employee.first_name.Substring(1) == capturedValue).FirstOrDefault());
            var scalarFailure = Capture<QueryBackendCapabilityException>(() =>
                employees.Where(employee => employee.first_name.Substring(1) == capturedValue).Count());
            var snapshot = DataLinqMetrics.Snapshot();

            await AssertUnsupportedSubstringDiagnostic(sequenceFailure, capturedValue);
            await AssertUnsupportedSubstringDiagnostic(terminalFailure, capturedValue);
            await AssertUnsupportedSubstringDiagnostic(scalarFailure, capturedValue);
            await Assert.That(snapshot.Commands.TotalExecutions).IsEqualTo(0);
            await Assert.That(snapshot.Queries.EntityExecutions).IsEqualTo(0);
            await Assert.That(snapshot.Queries.ScalarExecutions).IsEqualTo(0);
            await Assert.That(snapshot.RowCache.Hits).IsEqualTo(0);
            await Assert.That(snapshot.RowCache.Misses).IsEqualTo(0);
            await Assert.That(snapshot.RowCache.DatabaseRowsLoaded).IsEqualTo(0);
            await Assert.That(snapshot.RowCache.Materializations).IsEqualTo(0);
            await Assert.That(snapshot.RowCache.Stores).IsEqualTo(0);
        }
        finally
        {
            DataLinqMetrics.Reset();
        }
    }

    private static async Task AssertUnsupportedSubstringDiagnostic(
        QueryBackendCapabilityException exception,
        string capturedValue)
    {
        await Assert.That(exception.BackendName).IsEqualTo("sql");
        await Assert.That(exception.Feature).IsEqualTo("FunctionShape:SubstringWithStart");
        await Assert.That(exception.Location).IsEqualTo("operations[0].predicate.left.shape");
        await Assert.That(exception.SourceId).IsEqualTo("s0");
        await Assert.That(exception.Message).DoesNotContain(capturedValue);
    }

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

        throw new Exception($"Expected exception of type '{typeof(TException).Name}'.");
    }
}
